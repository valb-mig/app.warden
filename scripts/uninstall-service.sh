#!/usr/bin/env bash
# Remove o serviço systemd do Agent e o autostart do Admin instalados por install-service.sh.
# Nunca toca em ~/.warden (dados/config de projeto) — só desinstala o mecanismo de auto-start.
#
# Uso: ./scripts/uninstall-service.sh (ou `just service-uninstall`)

set -euo pipefail

INSTALL_DIR="$HOME/.local/share/warden"
UNIT_DIR="$HOME/.config/systemd/user"
AUTOSTART_DIR="$HOME/.config/autostart"

g=$'\033[0;32m'; r=$'\033[0m'
info() { echo -e "${g}==>${r} $*"; }

info "desabilitando e parando o serviço do Agent..."
systemctl --user disable --now warden-agent.service 2>/dev/null || true

rm -f "$UNIT_DIR/warden-agent.service" "$AUTOSTART_DIR/warden-admin.desktop"
systemctl --user daemon-reload

info "removendo binários publicados ($INSTALL_DIR)..."
rm -rf "$INSTALL_DIR"

echo
info "removido. ~/.warden (dados/config) não foi tocado."
info "linger continua habilitado pro seu usuário — rode 'loginctl disable-linger \$USER' se quiser desligar (não fiz isso automaticamente, pode ser usado por outra coisa)."
