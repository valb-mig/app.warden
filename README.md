# Warden

Hub local pra monitorar e gerenciar todos os projetos de uma máquina — dockerizados ou não — de forma segura pelo celular ou pela própria máquina.

## O problema

Rodar vários projetos locais (robôs Python, apps web, containers) sem visão única de status/logs, e sem controle remoto (start/stop) sem abrir terminal na máquina.

## O que é

Daemon local (Python) que atua como plano de controle pra projetos heterogêneos, com API e front mobile-friendly acessível remoto (via Tailscale) ou localmente. Três papéis:

- **Supervisor** — dono do ciclo de vida (start/stop/restart) de cada processo/container.
- **Observador** — status, portas, logs, detecção de erro.
- **Executor** — roda ações nomeadas (migration, seeder, comando em container).

Sem forçar todo projeto a virar Docker, sem tocar no código do projeto gerenciado.

## Arquitetura

```
[ Celular ] --Tailscale/WireGuard--> [ API (FastAPI) ] --> [ Engine daemon ]
                                          |                      |
                                     WebSocket (logs)      [ Adapters ]
                                                            /   |    \
                                                      Docker  Python  Raw
                                                            |
                                                   [ ~/.warden/*.toml ]
                                                   [ SQLite (histórico) ]
```

- **Engine** — processo longo, registry de projetos, estado vivo (PID, portas, ring buffer de log), event bus.
- **Adapters** — um por tipo de projeto (`docker`, `python`, `raw` hoje; `node`/`php`/`just` planejados), interface comum start/stop/status/logs.
- **API** — FastAPI, REST + WebSocket, bind só em `127.0.0.1`, auth via bearer token.
- **Front** — Next.js (PWA), dashboard mobile-first, isolado do resto (não mora dentro de nenhum projeto gerenciado).

Detalhes completos de design e decisões: [CONTEXT.md](CONTEXT.md) e [TODO.md](TODO.md).

## Stack

- **Engine** (`engine/`) — Python, [uv](https://github.com/astral-sh/uv), FastAPI, Pydantic v2, psutil, SQLite, ruff, pytest.
- **Front** (`web/`) — Next.js (App Router) + TypeScript, pnpm, Tailwind, shadcn/ui, lucide-react.
- **Raiz** — [Justfile](justfile) orquestra os dois lados.

## Como rodar

**1.** Config de projeto em `~/.warden/<id>.toml`:

```toml
id = "meu-projeto"
name = "Meu Projeto"
type = "raw"          # ou "python" / "docker"
path = "/caminho/do/projeto"

[start]
cmd = ["python3", "seu_script.py"]
capture_stdout = true
```

**2.** Sobe a API (motor + FastAPI):

```bash
just cli serve
```

Gera `~/.warden/api_token` na primeira vez (permissão 600), sobe em `127.0.0.1:8420`.

**3.** Sobe o front (outro terminal):

```bash
just web-dev
```

Abre `http://localhost:3000` — usar `localhost`, não `127.0.0.1` (Next.js bloqueia asset loading em `127.0.0.1` por proteção de dev-origin).

**4.** Conecta no front com a URL da API e o token de `~/.warden/api_token`.

## Testes

```bash
just test        # engine (pytest)
just lint         # ruff
just web-lint      # eslint
```

## Status

Fases 1-6 concluídas (motor, persistência, API, adapter docker, front). Próxima fase: exposição remota via Tailscale. Roadmap completo em [TODO.md](TODO.md).
