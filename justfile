set shell := ["bash", "-cu"]

# instala deps do front
web-sync:
    cd web && pnpm install

# dev server do front (Next.js)
web-dev:
    cd web && pnpm dev

# lint do front
web-lint:
    cd web && pnpm lint

# build de produção do front
web-build:
    cd web && pnpm build

# build da solução .NET (Agent/Admin/Domain/Contracts — migração C#, ver NEW_CONTEXT.md)
dotnet-build:
    cd agent && dotnet build

# testes do lado .NET
dotnet-test:
    cd agent && dotnet test

# roda o Agent (API/daemon) em dev
dotnet-agent:
    cd agent && dotnet run --project Warden.Agent

# roda o Admin (tray Avalonia) em dev
dotnet-admin:
    cd agent && dotnet run --project Warden.Admin

# instala Agent (systemd --user, sobe sozinho) + Admin (autostart de login) — Linux/systemd only
service-install:
    ./scripts/install-service.sh

# remove o serviço do Agent e o autostart do Admin (não mexe em ~/.warden)
service-uninstall:
    ./scripts/uninstall-service.sh

# status do serviço do Agent
service-status:
    systemctl --user status warden-agent.service

# segue os logs do Agent (journald)
service-logs:
    journalctl --user -u warden-agent.service -f

# imprime os comandos pra subir Agent + front (um em cada terminal) e o link de acesso (local/LAN/tailscale)
boot:
    #!/usr/bin/env bash
    set -euo pipefail

    web_port=3000

    bg=$'\033[1;32m'; g=$'\033[0;32m'; d=$'\033[2;32m'; c=$'\033[1;36m'; r=$'\033[0m'

    printf '%s\n' "${bg}░█░█░█▀█░█▀▄░█▀▄░█▀▀░█▀█${r}"
    printf '%s\n' "${bg}░█▄█░█▀█░█▀▄░█░█░█▀▀░█░█${r}"
    printf '%s\n' "${bg}░▀░▀░▀░▀░▀░▀░▀▀░░▀▀▀░▀░▀${r}"
    printf '%s\n' "${g}────────────────────────${r}"
    echo
    printf '%s\n' "${d}roda cada comando num terminal separado${r}"
    echo
    printf '%s\n' "  ${g}\$${r} just dotnet-agent"
    printf '%s\n' "  ${g}\$${r} just web-dev"
    echo
    printf '%s\n' "${d}opcional, admin local (aprovar projetos, editar config, tray)${r}"
    printf '%s\n' "  ${g}\$${r} just dotnet-admin"
    echo
    printf '%s\n' "${d}acesso${r}"
    printf '%s\n' "  ${d}local       ${r}${c}http://localhost:${web_port}${r}"
    if command -v tailscale >/dev/null 2>&1; then
        ts_ip="$(tailscale ip -4 2>/dev/null || true)"
        [ -n "$ts_ip" ] && printf '%s\n' "  ${d}tailscale   ${r}${c}http://$ts_ip:${web_port}${r}"
    fi
    lan_ip="$(hostname -I 2>/dev/null | awk '{print $1}')" || true
    if [ -z "$lan_ip" ]; then
        lan_ip="$(ip route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if ($i=="src") print $(i+1)}')" || true
    fi
    [ -n "$lan_ip" ] && printf '%s\n' "  ${d}rede local  ${r}${c}http://$lan_ip:${web_port}${r}"
    printf '%s\n' "  ${d}token       ${r}~/.warden/api_token"
