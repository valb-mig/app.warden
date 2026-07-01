# Warden (do Valb) — TODO / Arquitetura

> Histórico de nome: MasterHub → MegaHub → **Warden**.

Hub central pra monitorar e gerenciar **todos os projetos da máquina** (dockerizados ou não), acessível de forma segura pelo celular.

## Escopo core
Monitorar (status, portas, logs) + gerenciar (start/stop) + executar ações nomeadas em qualquer projeto local, sem exigir que todo projeto vire Docker e sem tocar no código do projeto.

## Decisões fechadas

1. **Motor local é dono do processo.** Pra projetos sem Docker, o Warden dá `subprocess.Popen` nele mesmo (em vez de só chamar `npm run dev` e sair). Isso garante PID, controle de start/stop, e acesso a logs — vira tipo um supervisor caseiro.
2. **Detecção de tipo de projeto (adapter por tipo).** Motor olha a pasta e decide como rodar: Docker (`docker compose`), `package.json`/`composer.json` (scripts), Justfile, ou comando cru. Cada tipo tem seu próprio adaptador de start/stop/logs.
3. **Portas descobertas, não fixas.** Como o motor tem o PID, usa algo tipo `psutil` pra perguntar ao SO quais portas aquele processo abriu. Usuário fica livre pra abrir qualquer porta.
4. **Config do Warden fica fora do repo do projeto.** Não pode ir pro GitHub de cada projeto. Vive centralizada em `~/.warden/`, **um arquivo TOML por projeto** nomeado pelo id (`~/.warden/<id>.toml`), referenciando o path. Essa pasta também resolve identidade/instrução do projeto, não precisa de arquivo extra dentro do repo. Extra: `~/.warden/config.toml` global (settings do daemon: porta da API, canal de notificação default). TOML escolhido em vez de JSON por permitir comentário inline e ser menos chato de editar à mão.
5. **Eventos: lifecycle primeiro, tráfego depois.** MVP = evento de processo (subiu/caiu/erro). Ver cada request GET/POST que chega no serviço é ambição futura — exige proxy reverso ou parse de log de acesso, não é trivial, fica pra depois.
6. **Notificação: canal fica atrás de interface, decide depois.** Motor só dispara evento (`finished`, `error`) num barramento interno. Canal de envio (`Notifier`) é strategy plugável — email, `ntfy`, Telegram, o que for — implementa depois sem tocar no motor. Toggle de notificação por projeto = flag no arquivo de config (`~/.warden/`).
7. **Diagrama de execução passo-a-passo — fora do core.** Visualizar grafo/sequência de execução de script (ex: scraping) não entra no MVP, exigiria instrumentar cada script e contraria a filosofia zero-fricção do motor. Revisitar só se aparecer necessidade concreta.
8. **Exposição remota: Tailscale.** VPN WireGuard, device-level auth, sem superfície pública. Cloudflare Tunnel descartado pro caso single-user (só revisitar se precisar compartilhar com terceiro sem device na tailnet).
9. **Sem shell livre remoto.** Celular só dispara `actions` pré-definidas no config (allowlist). Terminal livre é risco grande demais; fora do MVP. Meio-termo futuro (se necessário): shell atrás de confirmação + escopo por projeto/container + expira em N min.
10. **Persistência: SQLite pequeno.** Estado vivo (PID, portas, ring buffer de log) fica em memória (efêmero). Histórico de eventos + timestamps (uptime, quantas vezes caiu) vai pra SQLite — 1 arquivo, zero servidor. Log inteiro NÃO vai pro SQLite (fica em arquivo/ring buffer); SQLite só evento estruturado.
11. **Nome do projeto: Warden.** Fechado.

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
7. **Exposição remota** — Tailscale, valida front+API pelo celular. Confirma auth bearer nos dois casos (local/remoto). **Próxima fase, retomar daqui.**
8. **Adapters restantes + polish** — `node`, `php`, `just`; detecção erro PHP (edge cases de rotação/multi-linha); Notifier plugável (ntfy/email/telegram); confirmação pra ações destrutivas + audit log.

## Em aberto

- **Fase 7 (Tailscale/exposição remota) — pendente, retomar na próxima sessão.** Contexto: fases 1-6 fechadas e validadas (testes automatizados + smoke manual + browser real via Playwright). Front hoje só roda local (`127.0.0.1`); falta validar acesso remoto pelo celular via Tailscale e confirmar que o mesmo token bearer funciona nos dois cenários (local e remoto), conforme já decidido no CONTEXT.md.
- Detecção de erro em app web (ex: PHP) sem tocar no projeto: desenho via tail de log (`laravel.log`/`error_log`) + regex `error_patterns` por adapter — falta detalhar edge cases (rotação de log, multi-linha de stacktrace).

## Ideia de MVP (rascunho, não iniciar sem validar)

1. Motor local (Python): detecta projetos configurados, sabe start/stop/logs por adapter.
2. API REST local + WebSocket expondo isso (lista projetos, status, start/stop, logs ao vivo, portas, actions).
3. Front próprio e isolado (`warden/web` ou repo separado), mobile-friendly (PWA). NÃO morar dentro de `leadmaster` nem de nenhum projeto — Warden gerencia esses projetos, o front dele não pode cair junto quando eles caem, nem viver num repo que ele liga/desliga. Reaproveitar só stack/componentes de UI (Next.js/design system), não o repo.
4. Exposição: Tailscale primeiro pra validar o fluxo ponta a ponta.
