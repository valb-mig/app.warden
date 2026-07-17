# Warden (do Valb) — TODO / Arquitetura

> Histórico de nome: MasterHub → MegaHub → **Warden**.

Hub central pra monitorar e gerenciar **todos os projetos da máquina** (dockerizados ou não), acessível de forma segura pelo celular.

## Escopo core
Monitorar (status, portas, logs) + gerenciar (start/stop) + executar ações nomeadas em qualquer projeto local, sem exigir que todo projeto vire Docker e sem tocar no código do projeto.

## Decisões fechadas

1. **Motor local é dono do processo.** Pra projetos sem Docker, o Warden dá `subprocess.Popen` nele mesmo (em vez de só chamar `npm run dev` e sair). Isso garante PID, controle de start/stop, e acesso a logs — vira tipo um supervisor caseiro.
2. **Detecção de tipo de projeto (adapter por tipo).** Motor olha a pasta e decide como rodar: Docker (`docker compose`), `package.json`/`composer.json` (scripts), Justfile, ou comando cru. Cada tipo tem seu próprio adaptador de start/stop/logs.
3. **Portas descobertas, não fixas.** Como o motor tem o PID, usa algo tipo `psutil` pra perguntar ao SO quais portas aquele processo abriu. Usuário fica livre pra abrir qualquer porta.
4. **Config do Warden fica fora do repo do projeto.** Não pode ir pro GitHub de cada projeto. Vive centralizada em `~/.warden/`, **um arquivo TOML por projeto** nomeado pelo id, em `~/.warden/projects/<id>.toml`, referenciando o path. Essa pasta também resolve identidade/instrução do projeto, não precisa de arquivo extra dentro do repo. `~/.warden/projects/` fica separado da raiz de `~/.warden/` (que guarda só os arquivos únicos do daemon: `config.toml`, `api_token`, `warden.db`) — evita misturar N tomls de projeto com o resto conforme a lista de projetos cresce. Extra: `~/.warden/config.toml` global (settings do daemon: porta da API, canal de notificação default). TOML escolhido em vez de JSON por permitir comentário inline e ser menos chato de editar à mão.
5. **Eventos: lifecycle primeiro, tráfego depois.** MVP = evento de processo (subiu/caiu/erro). Ver cada request GET/POST que chega no serviço é ambição futura — exige proxy reverso ou parse de log de acesso, não é trivial, fica pra depois.
6. **Notificação: canal fica atrás de interface, decide depois.** Motor só dispara evento (`finished`, `error`) num barramento interno. Canal de envio (`Notifier`) é strategy plugável — email, `ntfy`, Telegram, o que for — implementa depois sem tocar no motor. Toggle de notificação por projeto = flag no arquivo de config (`~/.warden/`).
7. **Diagrama de execução passo-a-passo — fora do core.** Visualizar grafo/sequência de execução de script (ex: scraping) não entra no MVP, exigiria instrumentar cada script e contraria a filosofia zero-fricção do motor. Revisitar só se aparecer necessidade concreta.
8. **Exposição remota: Tailscale.** VPN WireGuard, device-level auth, sem superfície pública. Cloudflare Tunnel descartado pro caso single-user (só revisitar se precisar compartilhar com terceiro sem device na tailnet).
9. **Sem shell livre remoto.** Celular só dispara `actions` pré-definidas no config (allowlist). Terminal livre é risco grande demais; fora do MVP. Meio-termo futuro (se necessário): shell atrás de confirmação + escopo por projeto/container + expira em N min.
10. **Persistência: SQLite pequeno.** Estado vivo (PID, portas, ring buffer de log) fica em memória (efêmero). Histórico de eventos + timestamps (uptime, quantas vezes caiu) vai pra SQLite — 1 arquivo, zero servidor. Log inteiro NÃO vai pro SQLite (fica em arquivo/ring buffer); SQLite só evento estruturado.
11. **Nome do projeto: Warden.** Fechado.
12. **Git é dimensão própria, ortogonal ao adapter.** Adapter cuida do ciclo de vida do processo (start/stop/status/logs); git é propriedade do `path` no disco, motor novo (`git.py`) desacoplado. Leitura (`git_info`) sempre disponível se `path` for repo; comandos (`git_command`) via **allowlist embutida** (`fetch/sync/pull/push`, não `git <qualquer coisa>` — mesma filosofia da decisão #9). `pull`/`push` exigem `?confirm=true` (mesmo padrão de `ActionConfig.destructive`); `pull`/`sync` recusam se working tree sujo (evita merge-conflito no escuro via API); `sync` = fetch + fast-forward automático só se limpo e atrás — botão único pro caso comum no celular. `GIT_TERMINAL_PROMPT=0` em todo comando git — falha rápido em vez de pendurar pedindo credencial num tty inexistente.
13. **Watch de drift é opt-in por projeto, não automático.** `[git] watch = true` no TOML liga um `GitWatcher` (thread própria, mesmo padrão do `FileErrorWatcher`) que faz `fetch` periódico (`interval`, default 300s) e emite evento `git_behind` só na **transição** pra atrás do origin (0→N), não a cada poll — evita spam de notificação enquanto o usuário não agiu. Reusa bus/notifier existentes (`notify.on_git_behind`), zero infra nova. Sem watch ligado, `ahead/behind` mostrado no front é o último estado conhecido (calculado on-demand, sem fetch).
14. **Linguagens do projeto: decorativo, não linguist.** Detecção barata em duas camadas — manifesto primeiro (`pyproject.toml`, `package.json`+`tsconfig.json`, `composer.json`, `go.mod`, `Cargo.toml`, `Gemfile`, `pom.xml`...), scan raso de extensão como complemento até completar teto de 3 linguagens exibidas. Sem precisão de detecção de % de código, é só ícone pequeno no header do projeto — over-engineering aqui não compensa.
15. **Filtro de log por tipo reusa `error_patterns` já existente na config**, não cria taxonomia nova. Endpoint `/services` passou a devolver também os `error_patterns` (merge dedup de todos os `log_sources` do projeto); front compila como regex JS e classifica linha por linha. Sem patterns configurados, cai num fallback genérico (`ERROR`/`Exception`/`Fatal`/`Traceback`/`Fail`). Busca por substring é client-side puro, sem tocar backend.
16. **Multi-máquina é conceito só do front, não do engine.** Cada engine Warden continua single-machine (bind `127.0.0.1`/tailscale IP, dono só dos próprios projetos). O front (`lib/settings.tsx`) passa a guardar N conexões nomeadas (`Machine{id,name,baseUrl,token}`) no localStorage em vez de 1 objeto só, com uma "máquina ativa" por vez — troca via `machine-switcher.tsx` (dialog no header), sem endpoint novo no engine, sem lista unificada entre máquinas. Migração automática do formato antigo (`warden.settings`) na primeira carga.
17. **Descoberta de projetos via `scan_paths`, não só `warden init` manual.** `GlobalConfig.scan_paths: list[str]` (novo campo em `~/.warden/config.toml`) guarda pastas onde procurar projetos — front ("Sincronizar" no header) lista subpastas diretas de cada scan_path ainda não registradas em `~/.warden/projects/` (compara path resolvido contra `registry.all()`, não recursivo). Endpoints novos: `GET/POST/DELETE /scan-paths`, `GET /discover` (roda o scan), `GET /browse` (navegador de pasta pro front — browser não entrega path absoluto real de `<input type=file>`/File System Access API por segurança, então o engine, que tem acesso real ao filesystem da máquina, expõe listagem de subpastas pro front navegar). `warden init` ganhou equivalente web: `POST /discover/preview` (detecta, não grava) + `POST /discover/apply` (grava toml + `registry.load()` na hora, sem restart) — mesmo endpoint serve criar projeto novo e editar um já existente. Fora do escopo por ora: auto-iniciar file/git watcher pro projeto novo sem restart do daemon, adicionar log_source/action custom do zero pelo modal (só keep/remove dos detectados), paths remotos/rede.
18. **Captura de log usa PTY, não PIPE puro.** `ProcessAdapter` rodava o filho com `stdout=PIPE` — thread lê linha a linha, mas na prática o processo filho bufferiza em bloco quando stdout não é tty, então log só aparecia quando o processo terminava ou dava flush manual. Fix: `pty.openpty()`, filho roda com stdout = slave do pty — enxerga terminal, maioria das linguagens volta a ser line-buffered sozinha, sem precisar `PYTHONUNBUFFERED`/`-u` por comando.
19. **API externa pra apps do próprio usuário (ex: leadmaster) é token escopado, não broker de mensagens.** App terceira (mesmo dono, não é caso multiusuário) chama a mesma API REST já existente (`GET /projects`, `POST /projects/{id}/actions/...`, start/stop) usando token próprio — não o `api_token` global de admin. Token é escopado a uma lista de `project_id` permitidos, não chave mestra. Ações destrutivas continuam exigindo `?confirm=true` (decisão #12) — não existe passo de "mostrar comando antes de rodar" pro caller, esse gate é exclusivo do fluxo humano/front; pra app o gate real já é confirm explícito no payload + token escopado. Emissão/revogação de token escopado é ação local (CLI ou edição direta de config), nunca endpoint HTTP — mesma lógica de não expor ação de confiança pela rede.

## Stack técnica (monorepo)

Repo único, dois mundos (Python + JS), sem tool tipo Nx/Turborepo — overkill com 1 pacote por linguagem. Orquestração via Justfile na raiz (consistência com o próprio adapter `just` do Warden).

```
warden/
├── justfile              # dev, lint, test, build — atalhos cross-linguagem
├── engine/                # daemon Python: core, adapters, API, event bus
│   ├── pyproject.toml
│   └── src/warden/
├── web/                   # front Next.js (PWA)
│   ├── package.json
│   └── src/
├── CONTEXT.md
└── TODO.md
```

**Python (`engine/`):**
- **uv** — gerenciador de pacote/venv/lock. Escolhido por velocidade e suporte nativo a workspace (útil se `engine` precisar virar múltiplos pacotes internos no futuro — ex: `warden-core` + `warden-adapters`).
- **FastAPI** — REST + WebSocket (logs ao vivo), já decidido no CONTEXT.md.
- **Pydantic v2** — validação de config TOML e schemas da API.
- **tomllib** (stdlib, 3.11+) — parse TOML, sem dependência extra.
- **psutil** — status/portas/uptime de processo (já decidido).
- **sqlite3** (stdlib) — persistência de eventos. Se WS/API precisar não bloquear em query, considerar `aiosqlite` depois; não é decisão de dia 1.
- **ruff** — lint + format (substitui black+flake8+isort, um binário só).
- **pytest** — testes.

**JS (`web/`):**
- **pnpm** — package manager, workspace nativo caso `web` vire múltiplos pacotes (ex: extrair design system).
- **Next.js (App Router) + TypeScript** — front PWA mobile-first.
- **Tailwind + shadcn/ui** — componentes copy-in, fácil customizar pra mobile, sem dependência opaca de UI kit.

**Raiz:**
- **Justfile** — comandos tipo `just dev`, `just lint`, `just test` chamando as ferramentas de cada lado (`uv run` / `pnpm run`).
- **Git** — repo ainda não iniciado; init fica pro começo do MVP (motor local).
- **CI** — fora de escopo até repo existir remotamente; quando vier, GitHub Actions com dois jobs (python: `uv sync` + `ruff` + `pytest`; node: `pnpm install` + `pnpm build`).

## Fases de desenvolvimento

Ordem por dependência real (motor antes de API, API antes de front). Cada fase produz algo testável sozinho antes de acoplar a próxima camada.

1. ~~**Scaffolding**~~ ✅ — `git init`, estrutura `engine/` + `web/` + `justfile` raiz, `uv init` no engine (ruff+pytest).
2. ~~**Núcleo do motor**~~ ✅ — config loader (TOML→pydantic), registry de projetos, interface `Adapter`, adapters `python`+`raw`, psutil (status/portas/uptime), ring buffer de log em memória, CLI (`typer`).
3. ~~**Persistência + eventos**~~ ✅ — event bus interno, eventos `started/stopped/finished/error` → SQLite, `warden history <id>`.
4. ~~**API REST + WebSocket**~~ ✅ — FastAPI: rotas de projeto/actions, WS `/projects/{id}/logs`, auth bearer token, CORS aberto (auth é o gate real, não cookie).
5. ~~**Adapter docker**~~ ✅ — shell out `docker compose` (não SDK), validado contra stack real (via Docker local, já que `leadmaster` não existe nesta máquina).
6. ~~**Front Next.js (PWA)**~~ ✅ — pnpm+Tailwind+shadcn (Base UI)+lucide, dashboard (lista/status/start-stop) + página de detalhe (logs ao vivo via WS + histórico). Validado num browser real (Playwright), não só build.
7. ~~**Exposição remota**~~ ✅ — Tailscale, front+API validados pelo celular real (`moto-g24`), mesmo token bearer nos dois cenários. Máquinas na tailnet hoje: `valb` (esta, roda o Warden) + `moto-g24` (celular) + `arch` (casa, offline, não é alvo de teste). Engine sobe com `just cli serve --host 0.0.0.0`, front com `just web-dev` (`next dev` já bind `0.0.0.0` por padrão). Validação: `curl` no IP tailscale da máquina (`/projects` → 200 c/ token, 401 sem); WS de logs (`/ws/projects/{id}/logs?token=`) conecta c/ token, rejeita com 403 sem; front carregado no celular via `http://<tailscale-ip>:3000`, máquina cadastrada em Settings (baseUrl+token), lista/detalhe/logs ao vivo 100% funcional sem erro.
8. **Adapters restantes + polish** ✅ — `node`/`php`/`just` (subclasses finas de `ProcessAdapter`, mesmo padrão do `python`); Notifier plugável (`Notifier` strategy em `notifier.py`, canal `ntfy` via `urllib` stdlib, `NullNotifier` default; `GlobalConfig.notify_channel` em `~/.warden/config.toml`; email/telegram ficam pra quando precisar, interface já pronta); detecção erro PHP (`FileErrorWatcher` em `file_error_watcher.py` — construído do zero, não existia leitura nenhuma de `log_sources` tipo `file` antes: tail por polling 1s, rotação via inode+tamanho, truncamento copytruncate, agrupamento de stacktrace multi-linha por heurística `^\[` + flush por idle de 2s); confirmação + audit log pra ações destrutivas (`ActionConfig.destructive`, `Engine.run_action(confirmed=...)` levanta `ConfirmationRequired`, API responde 409 sem `?confirm=true`, audit vai pra tabela `action_audit` no SQLite, endpoint `GET /projects/{id}/actions/audit`; scaffold já marca `migrate`/`seed` do Laravel como destrutivos).
9. **Git + linguagens + filtro de log** ✅ — validado num projeto real (`leadmaster-scraper`) rodando via Tailscale.
   - **Git leitura** (`git.py`, decisão #12): `git_info(path)` → branch, dirty+contagem, ahead/behind vs upstream, último commit, `None` se não é repo. `GET /projects/{id}/git`. Card dedicado no front (`git-card.tsx`), colapsável, some se não é repo git. Badge "desatualizado" no status quando `running && behind>0` — sinal que nenhum git status puro te dá de graça.
   - **Git comandos** (decisão #12): `git_command(path, verb)` com allowlist `fetch/sync/pull/push`. `POST /projects/{id}/git/{verb}?confirm=`. Front: botões com `AlertDialog` de confirmação em pull/push, resultado (exit+output) em dialog.
   - **Git watch periódico** (decisão #13): `git_watcher.py` (`GitWatcher`, thread própria), evento `EventType.GIT_BEHIND`, `notify.on_git_behind`. Opt-in via `[git] watch = true` no TOML do projeto.
   - **Linguagens** (decisão #14): `languages.py` (`detect_languages`), `GET /projects/{id}/languages`. Front: `language-icons.tsx` com `react-icons/si` (única dependência JS nova desta fase), ícones pequenos sem destaque no header do projeto e no início da linha na lista.
   - **Filtro de log** (decisão #15): `/services` passou a incluir `error_patterns`. Front (`log-viewer.tsx`): busca por substring com highlight, toggle "só erros", linha de erro em vermelho, auto-scroll pausa durante busca ativa.
   - **Achado de sessão, não é bug:** engine sem `--reload` não pega rota nova em código editado com o processo já rodando — mesma classe de limitação do hot-reload de TOML (linha abaixo), só que pra código Python. Reiniciar `just cli serve` depois de qualquer mudança no `engine/`.
10. **Descoberta de projetos + polish de UI** ✅ — `scan_paths`/`discover`/`browse`/`preview`/`apply` (decisão #17); fix de detecção de venv no `warden init` (`scaffold.py` ignorava `venv/bin/python`, sempre sugeria `python` bare); CLI `warden init` ganhou prompts interativos (grupo/start/log_sources/actions) + cores; fix de buffering de log via PTY (decisão #18); front: `log-viewer.tsx` virou estilo terminal (fundo escuro, header tipo terminal, cursor piscando, botão limpar, fullscreen); `history-table.tsx` com data formatada e mensagem longa truncável; dashboard trocou N tabelas por grupo (header duplicado, layout quebrava com muitos projetos) por uma tabela só com header sticky + busca.

## Em aberto

- **Curl de instalação** ✅ — `install.sh` na raiz: clona repo em `~/warden` (ou `$WARDEN_INSTALL_DIR`), instala `uv`/`pnpm` se faltarem, roda `uv sync` + `pnpm install`. Reexecução segura: pula `git pull` se há mudanças locais na pasta (evita crash em `--ff-only`), instaladores de `uv`/`pnpm` só rodam se o comando não existir. Exposto via `curl -fsSL https://raw.githubusercontent.com/valb-mig/app.warden/main/install.sh | bash` (documentado no README). Exige `git` e `just` já instalados na máquina; `just` não tem instalador universal simples então só falha com link de instrução. **Validado em container Ubuntu 24.04 limpo de verdade** (Docker, usuário sem privilégio, `just` instalado à parte pra simular pré-requisito) — 2 bugs reais achados e corrigidos:
  1. `export PATH="$HOME/.local/share/pnpm:$PATH"` faltava o `/bin` do binário recém-instalado — `pnpm install` quebrava (`command not found`) na mesma execução logo depois de instalar o pnpm do zero.
  2. `web/pnpm-workspace.yaml` usava `ignoredBuiltDependencies` (campo removido no pnpm v11, virou `allowBuilds: {pkg: false}`) — máquina de dev nunca pegou isso porque tinha os builds já ignorados via config **global** do usuário (`~/.config/pnpm/rc`), fora do repo; instalador sempre baixa o pnpm mais recente (`get.pnpm.io`), então todo usuário novo bateria nisso com `pnpm install` saindo com exit 1 e o script morrendo por `set -e` sem nem imprimir erro claro.
  Depois do fix: `install.sh` completo (clone→uv sync→pnpm install→`just boot`→`just cli --help`) roda limpo, exit 0, ambiente funcional.
- **`just boot`** ✅ — **não sobe processo nenhum**, só imprime os dois comandos (`just cli serve --host 0.0.0.0` + `just web-dev`, um pra cada terminal) e o link de acesso (local, LAN, tailscale se disponível) + onde fica o token da API. Decisão de design: primeira versão tentava orquestrar os dois processos num script só (background jobs + `wait`), mas deu problema — Next dev (Turbopack) lê stdin bruto pra atalhos de teclado (`o`/`q`/etc.), e sem separação de process group qualquer tecla no terminal matava os dois processos. `</dev/null` resolveria isso, mas complexidade (trap, `kill 0`, checagem de porta ocupada) não compensava pra um script que só existe pra facilitar onboarding — usuário decidiu trocar por só imprimir os comandos, cada um roda no seu terminal (padrão que já era o fluxo manual documentado no README). `lan_ip` cai pra `ip route get` se `hostname -I` não existir (Arch usa `inetutils`, sem `-I`; GNU coreutils/`net-tools` tem). Não resolve a Fase 7 (ainda não valida Tailscale de verdade), só documenta o link que ela vai gerar.
- **Detecção de erro em app web (ex: PHP) ✅ resolvido — item estava desatualizado.** `file_error_watcher.py` (fase 8) já cobre os edge cases listados: rotação por inode novo (`test_rotation_by_new_inode`), truncamento copytruncate mesmo inode (`test_truncation_same_inode_copytruncate`), agrupamento de stacktrace multi-linha por heurística `^\[` + flush por idle 2s (`test_multiline_stacktrace_grouped_as_one_entry`, `test_new_entry_start_flushes_previous_pending`), arquivo ausente sem crash (`test_missing_file_does_not_crash`). 7 testes cobrindo os cenários, nada pendente aqui.
- **Hot-reload de `~/.warden/projects/*.toml` ✅ resolvido** — `ProjectsWatcher` (`projects_watcher.py`, novo, mesmo padrão de thread do `GitWatcher`/`FileErrorWatcher`) faz poll de mtime dos `.toml` a cada 2s (default, configurável via `Engine.boot(projects_watch_interval=...)`) e chama `Engine.reload_registry()` (já existente, decisão do fix de cache de adapter) na primeira mudança detectada — criar, editar ou remover projeto aparece sozinho, sem restart do daemon nem endpoint manual. `/discover/apply` continua chamando `reload_registry()` direto pra resposta da API já vir consistente, sem esperar o poll. 2 testes novos (`test_projects_watcher_picks_up_new_toml_without_manual_reload`, `test_projects_watcher_picks_up_edit_to_stopped_project`).
- **Token escopado por app externa** — decisão #19 fechada. Formato de armazenamento resolvido em `NEW_CONTEXT.md` §10.6 (tabela SQLite `scoped_tokens`, hash do token, nunca valor cru): se implementar ainda no engine Python, seguir esse mesmo formato (tabela nova no `warden.db` já existente) em vez de arquivo TOML novo — evita divergir do que a migração C# já vai usar. Falta: comando CLI de emissão/revogação, validação de escopo no middleware de auth da API.

## Backlog de ideias (brainstorm 2026-07-03, nada decidido ainda)

Ideias levantadas em sessão de brainstorm, sem priorização nem compromisso de escopo. Revisitar quando fase 7 fechar.

- **Warden Gateway (proxy unificado)** — reverse proxy path-based (`warden.host/p/<id>` → porta interna do projeto), um único endpoint exposto via Tailscale em vez de decorar porta por projeto. Risco: websocket passthrough, path rewrite, acopla disponibilidade dos projetos ao proxy.
- **Editor de `.env`/secrets** — ver/editar env var de projeto direto no front, valor mascarado, sem SSH. Mata caso comum (trocar API key + restart). Aumenta superfície de segurança — configs sensíveis passando pela API.
- **Cron Actions (scheduler)** — `actions` existentes ganham trigger por tempo além de manual (ex: `backup` toda meia-noite via TOML). Reusa infra de actions já pronta; diferencial vs crontab puro é histórico/log centralizado no Warden.
- **Busca global de log** — grep cross-projeto ("onde apareceu esse erro essa semana, em qual bot"), hoje log é isolado por projeto. Precisa indexar (SQLite FTS ou grep on-demand nos arquivos, mais simples).
- **`depends_on` (dependência entre projetos)** — projeto declara depender de outro (bot depende de DB), start respeita ordem, status mostra "degradado" se dependência caiu. Risco de escopo: vira orquestrador tipo docker-compose entre projetos, foge do core atual.
- **TUI tipo `htop`** — `warden top` no terminal, dashboard live sem browser, usa engine local direto (sem precisar API rodando). Mais uma superfície de UI pra manter (3ª, depois web).
- **Assistente NL (ideia especulativa)** — chat tipo "reinicia o bot que tá comendo CPU" resolve pra action concreta + pede confirm, reusando allowlist/`confirm=true` já existente (LLM só propõe, nunca executa direto). Acessibilidade alta pelo celular; maior desconforto de segurança por rodar LLM em cima de infra que mexe em processo real.

## Sessão de design — Painel de monitoramento na home (2026-07-03) ✅ implementado

**Problema:** home hoje só tem tabela de projetos + contador "X de Y rodando". Falta visão rápida de "tem algo rodando" + "tem algo com git modificado/sujo" sem abrir projeto por projeto. Git hoje só é buscado na página de detalhe (`/projects/{id}/git`), nunca na home.

**Opções levantadas (brainstorm), com score Necessidade×0.7 + Inovação×0.3 (1-5 cada, escala doméstica/uso pessoal — necessidade pesa mais que novidade):**

| # | Ideia | Necessidade | Inovação | Score | Ordem |
|---|---|---|---|---|---|
| D | Lista "precisa de atenção" — texto puro, só exceções (ex: "bot-x parado há 2 dias", "scraper com 4 arquivos não commitados") | 5 | 3 | **4.4** | 1º |
| E | Heartbeat único — indicador pulsante verde/vermelho, clica pra expandir | 3 | 5 | 3.6 | 2º |
| A | Banner silencioso — só aparece se tem problema, some se tudo normal | 4 | 2 | 3.4 | 3º |
| B | Stat tiles KPI — números grandes ("2 rodando", "3 sujos") no topo | 4 | 2 | 3.4 | 3º |
| C | Coluna Git na tabela existente — badge limpo/sujo/atrás por linha, zero componente novo | 4 | 1 | 3.1 | 5º |
| F | Git status board separado — lista só de git, fora da tabela de processo | 3 | 2 | 2.7 | 6º |
| G | Agregação por grupo — resumo tipo "robo: 2/3 rodando, 1 sujo" | 2 | 3 | 2.3 | 7º |

**Direção escolhida:** D (lista de atenção) como base, exibida como A (banner que só aparece se a lista não tá vazia — sem alerta visível quando tudo normal). C entrou junto (coluna Git na tabela, badge `limpo`/`sujo (N)`/`atrás (N)`), zero fetch extra por reusar o mesmo `gitInfo` do banner. G descartado — só 1 projeto registrado (`leadmaster-scraper`), problema de escala não existe ainda.

**Heurística fechada (resolvia o "em aberto"):** "parado" sozinho **não** entra na lista — falso alarme certo pra bot sob demanda. Sinal usado: **último evento do histórico == `error`** (`/projects/{id}/history?limit=1`, já existente — grava exit≠0 ou pattern de erro casado, distinto de `finished` com exit=0). Git entra sempre que `dirty` ou `behind>0`, independente de tá rodando.

**Custo técnico:** `/projects/{id}/git` + `/projects/{id}/history?limit=1` buscados de todos os projetos na home, em poll próprio de 15s (separado do poll de status de 3s, que é mais barato/frequente). N requests extras, aceitável na escala doméstica atual — revisitar (G ou paginação) se a lista de projetos crescer muito.

**Implementação:** `web/src/app/page.tsx` — estado `gitInfo`/`lastEvents`, `attentionItems` (useMemo), `Alert` condicional, coluna Git na tabela (`colSpan` do header de grupo ajustado de 5→6). Validado no browser real (Playwright): dirty aparece no banner + badge, limpo some por completo, parado sozinho não aparece.

**Follow-up — dispensar alerta ✅:** botão X por item do banner, persistido em `localStorage` (`warden.dismissedAttention`). Chave do item inclui o valor (`{id}-dirty-{dirty_count}`, `{id}-behind-{behind}`, `{id}-error-{created_at}`) — dispensar "sujo (1)" não esconde "sujo (2)" se piorar, reaparece automaticamente. Sem auto-expirar por tempo (decisão: alerta real não devia sumir sozinho, só ação explícita do usuário).

## Vitals (métricas de CPU/RAM) ✅ implementado (2026-07-03)

Item que estava só no backlog. Decisões de escopo:

- **Sem persistência** — client-side only, front acumula ~100 amostras (≈5min a 3s/poll) em memória, reseta ao fechar a página. Sem SQLite/thread nova no engine.
- **Owned + docker** — mesmo `VitalsSampler` (`engine/src/warden/vitals.py`, cacheia `psutil.Process` por PID pra `cpu_percent()` dar delta real em vez de 0.0) usado pelos dois adapters. Docker reusa o PID que o `DockerAdapter._container_pid` já resolvia.
- **Só página de detalhe do projeto** — sem sparkline na home.
- **Visual monocromático** — segue skill `dataviz`: sparkline em `currentColor` (tema claro/escuro automático via CSS), sem hue nova introduzida (app inteiro já é grayscale + vermelho só pra erro/destrutivo). Crosshair + tooltip on hover (valor + "Ns atrás").

**Implementação:** `engine/src/warden/vitals.py` (novo), `adapters/base.py` (+`cpu_percent`/`memory_mb` em `ProcessStatus`), `adapters/process.py` + `adapters/docker_adapter.py` (integração), `api/schemas.py` + `api/routes.py` (repassa os campos). Front: `lib/api.ts` (+campos), `app/projects/[id]/page.tsx` (acumula amostras), `components/vitals-card.tsx` (novo, 2 sparklines SVG). `pytest` 122/122, `ruff`/`tsc`/`eslint` limpos. Validado no browser real com projeto descartável (loop Python inofensivo) — CPU variando, RAM estável, tooltip funcionando.

**Achado de sessão — bug de cache de adapter ✅ corrigido:** `Engine._adapter()` cacheia a instância por `project_id` na primeira chamada e nunca recriava, mesmo depois de `registry.load()`. Editar `[start].cmd` (ou `compose_file`) via `/discover/apply` de um projeto **que já rodou uma vez** não pegava — continuava usando o adapter velho com o comando antigo, só resolvia com restart do daemon. Fix: `Engine.reload_registry()` (`engine.py`) recarrega config, invalida (`del`) só os adapters de projetos **parados** (preserva o de projeto rodando, não quebra processo em execução), e reinicia file/git watchers pra pegar `[git] watch`/`log_sources` novos. `discovery_routes.py` (`/discover/apply`) chama `reload_registry()` em vez de `registry.load()` puro. 2 testes novos (`test_reload_registry_picks_up_new_cmd_for_stopped_project`, `test_reload_registry_keeps_adapter_for_running_project`).

## Ideia de MVP (rascunho, não iniciar sem validar)

1. Motor local (Python): detecta projetos configurados, sabe start/stop/logs por adapter.
2. API REST local + WebSocket expondo isso (lista projetos, status, start/stop, logs ao vivo, portas, actions).
3. Front próprio e isolado (`warden/web` ou repo separado), mobile-friendly (PWA). NÃO morar dentro de `leadmaster` nem de nenhum projeto — Warden gerencia esses projetos, o front dele não pode cair junto quando eles caem, nem viver num repo que ele liga/desliga. Reaproveitar só stack/componentes de UI (Next.js/design system), não o repo.
4. Exposição: Tailscale primeiro pra validar o fluxo ponta a ponta.
