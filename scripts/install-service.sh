#!/usr/bin/env bash
# Instala o Warden Agent como serviço systemd de usuário (sobe sozinho no login e no boot, via
# `loginctl enable-linger`) e o Warden Admin como autostart de login (XDG). Linux/systemd only —
# ver NEW_CONTEXT.md pra Windows Service (ainda não implementado, sem máquina pra validar).
#
# Uso: ./scripts/install-service.sh (ou `just service-install`)

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_DIR="$HOME/.local/share/warden"
UNIT_DIR="$HOME/.config/systemd/user"
AUTOSTART_DIR="$HOME/.config/autostart"

g=$'\033[0;32m'; r=$'\033[0m'
info() { echo -e "${g}==>${r} $*"; }
die()  { echo -e "\033[1;31merro:\033[0m $*" >&2; exit 1; }

command -v dotnet >/dev/null 2>&1 || die ".NET SDK não encontrado."
command -v systemctl >/dev/null 2>&1 || die "systemctl não encontrado — este instalador é só pra Linux/systemd."
command -v loginctl >/dev/null 2>&1 || die "loginctl não encontrado — este instalador é só pra Linux/systemd."

info "publicando Warden.Agent (self-contained, linux-x64)..."
dotnet publish "$REPO_DIR/agent/Warden.Agent" -c Release -r linux-x64 --self-contained true -o "$INSTALL_DIR/agent"

info "publicando Warden.Admin (self-contained, linux-x64)..."
dotnet publish "$REPO_DIR/agent/Warden.Admin" -c Release -r linux-x64 --self-contained true -o "$INSTALL_DIR/admin"

mkdir -p "$UNIT_DIR" "$AUTOSTART_DIR"
cp "$REPO_DIR/scripts/warden-agent.service" "$UNIT_DIR/warden-agent.service"
sed "s#@HOME@#$HOME#g" "$REPO_DIR/scripts/warden-admin.desktop.template" > "$AUTOSTART_DIR/warden-admin.desktop"

info "habilitando serviço do Agent..."
systemctl --user daemon-reload
systemctl --user enable --now warden-agent.service

info "habilitando linger (sobrevive a logout/boot sem sessão ativa)..."
loginctl enable-linger "$USER"

echo
info "instalado. status do Agent:"
systemctl --user status warden-agent.service --no-pager || true
echo
info "o Admin abre sozinho no próximo login — pra abrir agora: $INSTALL_DIR/admin/Warden.Admin"
info "desinstalar: just service-uninstall"
