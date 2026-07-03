# Warden

Hub central pra monitorar e gerenciar **todos os projetos locais da máquina** (dockerizados ou não), acessível de forma segura pelo celular. Histórico de nome: MasterHub → MegaHub → **Warden**.

Status atual: **fase de design**, nenhum código ainda. Existem `TODO.md` (decisões de arquitetura, fonte da verdade), este `CONTEXT.md` (visão + design), e dois rascunhos visuais (`mainidea.png`, `fragmentando.png`).

## Problema

Valb roda vários projetos locais (robôs Python, apps web, etc), uns em Docker, outros não. Não tem visão única de status/logs, nem controle remoto (start/stop) sem abrir terminal na máquina.

## O que é

Daemon local (Python) que age como **plano de controle** pra projetos heterogêneos, com **API** e **front mobile** acessível remoto e seguro. Três papéis:

1. **Supervisor** — dono do ciclo de vida (start/stop/restart), via `subprocess.Popen` (não-docker) ou Docker SDK (docker).
2. **Observador** — status, portas, logs, detecção de erro.
3. **Executor** — roda ações nomeadas (migration, seeder, comando em container).

## Escopo core

Monitorar (status, portas, logs) + gerenciar (start/stop) + executar ações qualquer projeto local — sem forçar todo projeto a virar Docker, sem tocar no código do projeto.

---

## Arquitetura — camadas

```
[ Celular ] --Tailscale/WireGuard--> [ API (FastAPI) ] --> [ Engine daemon ]
                                          |                      |
                                     WebSocket (logs)      [ Adapters ]
                                                            /   |    \
                                                      Docker  Node  Python  PHP  Raw
                                                            |
                                                   [ ~/.warden/projects/*.toml ]
                                                   [ SQLite (histórico) ]
                                                   [ Event bus -> Notifier ]
```

- **Engine (daemon)** — processo longo. Guarda registry, estado vivo (PID, portas, ring-buffer de log), event bus. Bind só em `127.0.0.1`.
- **Adapters** — um por tipo. Interface comum: `start / stop / status / logs / exec / ports / actions`.
- **API (FastAPI)** — REST pra comandos + WebSocket pra log ao vivo.
- **Front (Next.js/PWA)** — dashboard mobile, próprio e isolado (`warden/web` ou repo separado). NÃO mora dentro de nenhum projeto que o Warden gerencia.
- **Exposição** — Tailscale (acesso remoto) **e** local (acesso na própria máquina).

**Acesso local (não só celular):** como API já faz bind em `127.0.0.1`, dá pra rodar o front na própria máquina que hospeda o Warden e abrir no navegador local (`localhost:<porta>`) sem Tailscale, sem VPN — é só loopback. Tailscale entra só quando o acesso vem de **fora** da máquina (celular, notebook remoto). Auth (bearer token) continua ativa nos dois casos — sem branch de código auth-vs-no-auth pra origem local.

---

## Passo-a-passo de estrutura (fluxo)

**Boot do daemon:**
1. Lê `~/.warden/projects/` → carrega N configs de projeto (`<id>.toml`).
2. Pra cada projeto, instancia adapter certo pelo campo `type`.
3. Reconcilia estado: docker → `docker ps`; owned → checa se PID salvo ainda vive (`psutil`).
4. Sobe API + WebSocket.

**Usuário aperta "start" no celular:**
1. Front → `POST /projects/{id}/start` (com token).
2. API valida auth → chama `engine.start(id)`.
3. Adapter roda comando; guarda PID (owned) ou sobe serviço (docker).
4. `psutil` descobre portas abertas pelo PID.
5. Engine emite evento `started` no bus → grava no SQLite.
6. Estado volta pro front (polling ou WS push).

**Log ao vivo:**
1. Front abre WS `/projects/{id}/logs`.
2. Engine faz tail da fonte (stdout capturado / arquivo / `docker logs -f`) → streama linhas.

**Ação nomeada (migration):**
1. Front lista `actions` do config.
2. `POST /projects/{id}/actions/migrate` → adapter executa comando pré-definido → streama saída.

**Descobrir e configurar projeto novo (front):**
1. Front → `GET /scan-paths` + `GET /discover` (varre subpastas ainda não registradas).
2. Usuário clica "configurar" → `POST /discover/preview` (detecta tipo, não grava) → formulário editável + TOML de preview.
3. Ajusta (grupo, start, quais log_sources/actions manter) → `POST /discover/apply` grava o `.toml` e recarrega o registry na hora.

---

## Dados importantes — modelo de config

Um arquivo TOML por projeto em `~/.warden/projects/<id>.toml`. Config **fora do repo** do projeto (não vaza pro GitHub). TOML em vez de JSON: permite comentário e é menos chato de editar à mão. `config.toml`/`api_token`/`warden.db` (arquivos únicos do daemon) ficam na raiz de `~/.warden/`, fora da pasta `projects/` — separa "N configs de projeto" de "estado do daemon". Exemplo docker cobrindo todos os casos:

```toml
id = "leadmaster"
name = "LeadMaster"
group = "scrapers"
path = "/home/valb/Projects/leadmaster"
type = "docker"
compose_file = "docker-compose.yml"

[notify]
on_error = true
on_finished = false
on_git_behind = false

[git]
watch = true
interval = 300
remote = "origin"

[[log_sources]]
name = "app"
type = "docker"
service = "app"

[[log_sources]]
name = "nginx"
type = "file"
path = "./docker/nginx/error.log"

[[log_sources]]
name = "php"
type = "file"
path = "./storage/logs/laravel.log"
error_patterns = ["ERROR", "\\bException\\b", "PHP Fatal"]

[[actions]]
name = "migrate"
cmd = ["docker", "compose", "exec", "app", "php", "artisan", "migrate", "--force"]

[[actions]]
name = "seed"
cmd = ["docker", "compose", "exec", "app", "php", "artisan", "db:seed", "--force"]

[[actions]]
name = "tinker"
cmd = ["docker", "compose", "exec", "app", "php", "artisan", "tinker"]
interactive = true
```

Robô Python sem docker:

```toml
id = "caffeshop-bot"
type = "python"
path = "/home/valb/Projects/caffeshop"

[start]
cmd = ["python", "main.py"]
capture_stdout = true

[[log_sources]]
name = "stdout"
type = "stdout"

[notify]
on_error = true
on_finished = true
```

`[git]` é opt-in — sem essa seção o projeto não ganha fetch periódico nenhum, mas leitura de estado git (`GET /projects/{id}/git`) funciona sempre que `path` é um repo, watch ligado ou não.

Config global do daemon em `~/.warden/config.toml` (porta da API, canal de notificação default, etc).

**Estado em memória no engine** (não no config): PID atual, estado (running/stopped/errored), portas descobertas, últimas N linhas de log (ring buffer), timestamp de início (uptime).

**Persistência (SQLite):** histórico de eventos estruturados (`started/stopped/finished/error` + timestamp). Responde "quantas vezes caiu essa semana", uptime histórico. Log bruto NÃO vai pro SQLite — fica em arquivo/ring buffer.

---

## Lógica de monitoramento

| Coisa | Como |
|---|---|
| **Status (owned)** | `psutil.pid_exists(pid)` + processo não-zumbi |
| **Status (docker)** | `docker ps` / SDK → state do container |
| **Portas** | `psutil.Process(pid).net_connections()` → filtra LISTEN |
| **Uptime** | agora − `create_time()` do processo |
| **Logs python print** | `capture_stdout=true` → Popen com `stdout=PIPE`, thread lê linha-a-linha → ring buffer + arquivo |
| **Logs docker** | `docker logs -f <container>` (ou SDK stream) |
| **Logs nginx** | tail do arquivo declarado em `log_sources` |
| **Erro PHP** | tail de `laravel.log`/`error_log` + regex `error_patterns` → casou → emite evento `error` → Notifier |
| **Drift git** | `[git] watch=true` → `GitWatcher` faz `fetch` periódico → `behind>0` na transição → emite `git_behind` → Notifier |
| **Eventos** | `started / stopped / finished(exit=0) / error(exit≠0 ou pattern) / git_behind` → grava SQLite |

**Chave da detecção de erro sem tocar no projeto:** Warden não instrumenta código nenhum. Só observa PID (morreu?) + exit code + tail de log com regex. Zero-fricção — o projeto não sabe que o Warden existe.

---

## Git — monitoramento e comandos

Dimensão própria, **ortogonal ao adapter**: git é propriedade do `path` no disco, não do ciclo de vida do processo. Motor em `git.py`, sem estado, dois modos:

- **Leitura** (`git_info`) — sempre disponível se `path` é repo. Branch, dirty+contagem de arquivos, ahead/behind vs upstream, último commit. `GET /projects/{id}/git` → `null` se não é repo.
- **Comandos** (`git_command`) — allowlist embutida `fetch / sync / pull / push`, não shell livre (mesma filosofia de `actions`). `fetch` é read-only sem confirmação; `pull`/`push` exigem `?confirm=true`; `pull`/`sync` recusam se working tree sujo (evita merge-conflito no escuro via API); `sync` = fetch + fast-forward automático só se limpo e atrás — botão único pro caso comum no celular. Todo comando roda com `GIT_TERMINAL_PROMPT=0`, então falta de credencial falha rápido em vez de pendurar pedindo senha num tty que não existe.
- **Watch periódico** (`GitWatcher`, opt-in via `[git] watch = true`) — mesma forma do `FileErrorWatcher`: thread própria, `fetch` a cada `interval` segundos, evento `git_behind` só na transição (0→N atrás do origin), não a cada poll. Sem watch ligado, o `ahead/behind` mostrado é o último estado conhecido (calculado on-demand, sem fetch de fundo).

No front, card Git dedicado — colapsável, só aparece se o projeto é repo — fica separado do card de Actions do projeto (migrate/seed) pra não misturar as duas coisas. Badge "desatualizado" no status quando `running && behind>0`.

---

## Linguagens e filtro de log

**Linguagens:** decorativo, não linguist. `languages.py` detecta manifesto primeiro (`pyproject.toml`, `package.json`+`tsconfig.json`, `composer.json`, `go.mod`, `Cargo.toml`, `Gemfile`, `pom.xml`), extensão como complemento até completar 3. `GET /projects/{id}/languages`. Front mostra ícones pequenos (`react-icons/si`) tanto na lista de projetos (início da linha) quanto no header do detalhe, sem badge, sem destaque.

**Filtro de log:** reusa `error_patterns` já configurado em `log_sources` — `GET /projects/{id}/services` passou a devolver também os patterns (merge dedup). Front classifica linha por linha (regex JS), toggle "só erros" + busca por substring com highlight, tudo client-side. Sem patterns configurados no projeto, cai num fallback genérico.

---

## Funciona pra todo tipo de projeto?

**Sim, com ressalva.** Funciona pra tudo que reduz a **(comando pra rodar) + (fonte de log)**. Adapters cobrem o comum:

- `docker` → compose up/down, logs, exec.
- `node` → lê `package.json` scripts.
- `python` → cmd cru + venv.
- `php` → composer/artisan.
- `just` → Justfile.
- `raw` → **fallback**: qualquer comando arbitrário. Cobre o resto.

Não é mágica: cada projeto precisa de **um** arquivo de config pequeno, centralizado em `~/.warden/projects/`, não no repo.

---

## Segurança + acesso mobile

Parte sensível — comandos remotos numa máquina que roda tudo. Defesa em camadas:

1. **Engine nunca exposto direto.** API faz bind só em `127.0.0.1`. Nada escuta em `0.0.0.0`. Sem port-forward no roteador.
2. **Transporte: Tailscale.** VPN WireGuard, device-level auth. Celular entra na tailnet; só dispositivos autenticados na conta alcançam a API. Sem URL pública.
3. **Auth na própria API** (mesmo dentro da tailnet, defesa em profundidade): token bearer ou sessão.
4. **Comandos = allowlist, não shell livre.** Celular NÃO manda shell arbitrário. Só dispara `actions` pré-definidas no config (migrate, seed, restart) ou verbos git de uma allowlist embutida (`fetch/sync/pull/push` — nunca `git <qualquer coisa>`). Rodar comando em container = ação nomeada declarada, não caixa de texto que executa qualquer coisa.
5. **Ações destrutivas com confirmação** (`migrate --force`, `down`, `shutdown`) + log de auditoria.
6. **Segredos.** Configs em `~/.warden/projects/` com permissão `600`. Token de API só no device (secure storage), não hardcoded, não commitado.

Casos concretos cobertos:

| Quer | Como |
|---|---|
| On/off script python | supervisor owned, Popen + kill por PID |
| PHP dando erro / ver log | tail `laravel.log` + regex → evento + stream |
| Parar/iniciar container | docker adapter → compose up/down/stop |
| Rodar comando no container | action nomeada → `docker compose exec` |
| Migration / seeder | actions `migrate`/`seed` |
| Logs nginx | log_source type=file |
| Logs docker | `docker logs -f` via WS |
| Print do python | capture stdout → ring buffer + stream |
| Ver drift de git / sincronizar | card Git → `sync` (fetch+FF) ou `pull`/`push` com confirmação |
| Saber se código rodando é o do topo do origin | badge "desatualizado" (`running && behind>0`) |
| Achar linha de erro específica no log | busca por substring + toggle "só erros" no log viewer |

---

## Decisões fechadas (resumo do TODO.md)

1. **Motor local é dono do processo** — `subprocess.Popen` direto, pra ter PID/controle/logs.
2. **Adapter por tipo de projeto** — detecção pela pasta.
3. **Portas descobertas via `psutil`**, não fixas.
4. **Config fora do repo** — `~/.warden/projects/<id>.toml` (TOML), + `config.toml` global na raiz.
5. **MVP = eventos de lifecycle** (subiu/caiu/erro). Tráfego request-a-request fica pra depois.
6. **Notificação plugável** — `Notifier` strategy (email, ntfy, Telegram...), toggle por projeto.
7. **Fora do core**: diagrama de execução passo-a-passo de scripts.
8. **Exposição: Tailscale.** Cloudflare Tunnel descartado pro caso single-user.
9. **Sem shell livre remoto** — só actions em allowlist.
10. **Persistência: SQLite** pra histórico de eventos; estado vivo em memória.
11. **Nome: Warden.**
12. **Git é dimensão própria, ortogonal ao adapter** — leitura sempre disponível, comandos via allowlist embutida (`fetch/sync/pull/push`), `GIT_TERMINAL_PROMPT=0` pra nunca pendurar pedindo credencial.
13. **Watch de drift git é opt-in por projeto** (`[git] watch=true`) — notifica só na transição pra "atrás do origin", não a cada poll.
14. **Linguagens do projeto: decorativo, não linguist** — manifesto + extensão, teto de 3 ícones.
15. **Filtro de log reusa `error_patterns` já existente** — sem taxonomia nova, sem endpoint dedicado além de estender `/services`.
16. **Multi-máquina é conceito só do front.** Engine continua single-machine; front guarda N conexões nomeadas (localStorage) e troca a ativa via switcher no header, sem endpoint novo no engine.
17. **Descoberta de projetos via `scan_paths`** — front lista pastas candidatas ainda não configuradas (subpastas diretas, não recursivo) e usa `/discover/preview`+`/apply` pra gerar e gravar o `.toml`, mesmo fluxo do `warden init` só que pela web; mesmo endpoint serve criar e editar.
18. **Log capturado via PTY, não PIPE puro** — processo filho enxerga terminal, evita buffering em bloco que atrasava log até o processo terminar.

## Em aberto

- Detecção de erro PHP: falta detalhar edge cases (rotação de log, stacktrace multi-linha).
- Engine sem `--reload`: mudança de código em `engine/` só aparece depois de reiniciar `just cli serve` — mesma classe de limitação do hot-reload de TOML, mas pra código Python.

## Rascunho de MVP

1. Motor local (Python): detecta projetos configurados, start/stop/logs por adapter.
2. API REST local + WebSocket expondo isso.
3. Front próprio isolado (`warden/web`, Next.js/PWA), mobile-friendly.
4. Exposição: Tailscale primeiro pra validar fluxo ponta a ponta.

## Como rodar (dev, fases 1-6 concluídas)

**1. Config de projeto** — `~/.warden/projects/<id>.toml`. Exemplo mínimo:

```toml
id = "meu-projeto"
name = "Meu Projeto"
type = "raw"
path = "/caminho/do/projeto"

[start]
cmd = ["python3", "seu_script.py"]
capture_stdout = true
```

Tipos disponíveis: `raw`, `python`, `node`, `php`, `just`, `docker` (`docker` usa `compose_file = "docker-compose.yml"` em vez de `[start]`).

**2. Sobe a API** (motor + FastAPI):
```bash
just cli serve
```
Lê `~/.warden/projects/*.toml`, gera `~/.warden/api_token` na primeira vez (permissão 600), sobe em `127.0.0.1:8420`.

**3. Pega o token:**
```bash
cat ~/.warden/api_token
```

**4. Sobe o front** (outro terminal):
```bash
just web-dev
```
Abre `http://localhost:3000` — **usar `localhost`, não `127.0.0.1`** (Next.js bloqueia asset loading em `127.0.0.1` por proteção de dev-origin).

**5. Conecta** no front: URL da API `http://127.0.0.1:8420` + token do passo 3.

**Testes automatizados** (sem precisar subir nada):
```bash
just test        # engine (pytest)
just lint         # ruff
just web-lint      # eslint
```

## Arquivos

- [TODO.md](TODO.md) — decisões e arquitetura, fonte da verdade.
- `mainidea.png` — rascunho inicial (nome do app, agrupamento de robôs, visualização de fluxo).
- `fragmentando.png` — rascunho de detecção de tipo de projeto e eventos.
