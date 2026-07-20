# Warden

Hub local pra monitorar e gerenciar todos os projetos de uma máquina — dockerizados ou não — de forma segura pelo celular ou pela própria máquina.

## O problema

Rodar vários projetos locais (robôs Python, apps web, containers) sem visão única de status/logs, e sem controle remoto (start/stop) sem abrir terminal na máquina.

## O que é

Daemon local (.NET) que atua como plano de controle pra projetos heterogêneos, com API e front mobile-friendly acessível remoto (via Tailscale) ou localmente, mais um app de administração (tray) pra aprovar projetos e editar config. Três papéis:

- **Supervisor** — dono do ciclo de vida (start/stop/restart) de cada processo/container.
- **Observador** — status, portas, logs, detecção de erro.
- **Executor** — roda ações nomeadas (migration, seeder, comando em container).

Sem forçar todo projeto a virar Docker, sem tocar no código do projeto gerenciado.

## Arquitetura

```
[ Celular ] --Tailscale/WireGuard--> [ Warden.Agent (ASP.NET Core) ] --> [ Warden.Domain ]
                                          |                                    |
                                    SignalR Hub (logs)                  [ Adapters ]
                                          |                              /   |    \
                                  [ unix socket (Admin) ]           Docker  Raw  (node/php/just...)
                                          |                                    |
                                   [ Warden.Admin (Avalonia) ]        [ ~/.warden/*.toml ]
                                                                       [ SQLite (histórico/trust) ]
```

- **Warden.Domain** — biblioteca de domínio: registry de projetos, adapters, event bus, trust/manifests, watchers (git/arquivo de log/`.toml`).
- **Warden.Agent** — processo longo (ASP.NET Core minimal API + SignalR), REST + logs ao vivo, bind só no IP da Tailscale (nunca `0.0.0.0`/loopback), auth via bearer token. Expõe também um socket unix local só pro Admin (aprovar projetos, editar config).
- **Warden.Admin** — app desktop (Avalonia) com tray icon: aprova projetos novos/alterados, edita config, sincroniza projetos descobertos no filesystem — tudo local, fala com o Agent pelo socket unix.
- **Front** (`web/`) — Next.js (PWA), dashboard mobile-first, isolado do resto (não mora dentro de nenhum projeto gerenciado). Logs ao vivo via SignalR.

Detalhes completos de design e decisões: [NEW_CONTEXT.md](NEW_CONTEXT.md) (arquitetura corrente) e [CONTEXT.md](CONTEXT.md)/[TODO.md](TODO.md) (histórico de design que motivou essa arquitetura).

## Stack

- **Agent/Admin/Domain/Contracts** (`agent/`) — C#/.NET 10, ASP.NET Core minimal API, SignalR, Avalonia (Admin), Tomlyn (TOML), SQLite, xUnit.
- **Front** (`web/`) — Next.js (App Router) + TypeScript, pnpm, Tailwind, shadcn/ui, lucide-react.
- **Raiz** — [Justfile](justfile) orquestra os dois lados.

## Instalação rápida

```bash
curl -fsSL https://raw.githubusercontent.com/valb-mig/app.warden/main/install.sh | bash
```

Clona o repo em `~/warden`, checa se `.NET SDK`/`pnpm` estão instalados e sincroniza as deps do front. Depois é só `just boot` (mostra os comandos e o link de acesso).

## Como rodar

**0.** `just boot` mostra os comandos (um pra cada terminal) e o link de acesso (local/LAN/tailscale) — não sobe nada sozinho, só imprime:

```bash
just boot
```

```
Roda cada comando num terminal separado:

  just dotnet-agent
  just web-dev

Opcional, admin local (aprovar projetos, editar config, tray):
  just dotnet-admin

Depois acessa em:
  local:      http://localhost:3000
  tailscale:  http://100.x.x.x:3000
  rede local: http://192.168.x.x:3000
  API token:  ~/.warden/api_token
```

Passo a passo (o que os comandos acima fazem):

**1.** Config de projeto em `~/.warden/projects/<id>.toml`:

```toml
id = "meu-projeto"
name = "Meu Projeto"
type = "raw"          # ou "python" / "node" / "php" / "docker" / "just"
path = "/caminho/do/projeto"

[start]
cmd = ["python3", "seu_script.py"]
capture_stdout = true
```

Também dá pra registrar um projeto pelo Admin (`just dotnet-admin` → "Sincronizar" → escaneia pastas e detecta o tipo sozinho).

**2.** Sobe o Agent (motor + API):

```bash
just dotnet-agent
```

Gera `~/.warden/api_token` na primeira vez (permissão 600), resolve o IP da Tailscale e bind só nele (nunca localhost/0.0.0.0 — ver NEW_CONTEXT.md §10.2).

**3.** Sobe o front (outro terminal):

```bash
just web-dev
```

Abre `http://localhost:3000` — usar `localhost`, não `127.0.0.1` (Next.js bloqueia asset loading em `127.0.0.1` por proteção de dev-origin).

**4.** Conecta no front com a URL da API e o token de `~/.warden/api_token`. Antes de iniciar um projeto pela primeira vez, aprove-o no Admin (`just dotnet-admin`) — start/ações ficam bloqueados por um trust gate até a aprovação (ver NEW_CONTEXT.md §10.3).

## Rodar como serviço (Linux/systemd)

Em vez do `just dotnet-agent`/`just dotnet-admin` manual, dá pra instalar o Agent como serviço systemd de usuário (sobe sozinho no login e no boot, sobrevive a logout via `loginctl enable-linger`) e o Admin como autostart de login (XDG). **Linux/systemd only por agora** — Windows Service ainda não foi implementado (sem máquina Windows pra validar de verdade, mesma ressalva já registrada em NEW_CONTEXT.md pros paths de porta/vitals do Windows).

```bash
just service-install     # publica self-contained, instala o unit + autostart, habilita linger
just service-status       # status do serviço do Agent
just service-logs         # segue os logs (journald)
just service-uninstall    # remove serviço + autostart (não mexe em ~/.warden)
```

O front (`web/`) continua manual (`just web-dev`) — o pedido de auto-start cobre só Agent/Admin.

## Testes

```bash
just dotnet-test   # C# (xUnit)
just web-lint       # eslint
```

## Status

Migração completa do motor Python original pro .NET — todo o backend (Agent/Admin/Domain) roda em C#, sem dependência de Python/uv/FastAPI no fluxo do Warden em si (o Warden continua gerenciando projetos Python normalmente, isso é só um tipo de projeto suportado). Roadmap completo em [NEW_CONTEXT.md](NEW_CONTEXT.md).
