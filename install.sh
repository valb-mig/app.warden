#!/usr/bin/env bash
# Instala o Warden numa máquina nova: clona o repo, instala uv/pnpm se faltar,
# sincroniza as deps do engine e do front.
#
# Uso:
#   curl -fsSL https://raw.githubusercontent.com/valb-mig/app.warden/main/install.sh | bash
#
# Variáveis opcionais:
#   WARDEN_INSTALL_DIR   pasta de destino (default: ~/warden)

set -euo pipefail

REPO_URL="https://github.com/valb-mig/app.warden.git"
INSTALL_DIR="${WARDEN_INSTALL_DIR:-$HOME/warden}"

bg=$'\033[1;32m'; g=$'\033[0;32m'; r=$'\033[0m'

info() { echo -e "${g}==>${r} $*"; }
die()  { echo -e "\033[1;31merro:\033[0m $*" >&2; exit 1; }

printf '%s\n' "${bg}░█░█░█▀█░█▀▄░█▀▄░█▀▀░█▀█${r}"
printf '%s\n' "${bg}░█▄█░█▀█░█▀▄░█░█░█▀▀░█░█${r}"
printf '%s\n' "${bg}░▀░▀░▀░▀░▀░▀░▀▀░░▀▀▀░▀░▀${r}"
printf '%s\n' "${g}────────────────────────${r}"
echo

command -v git >/dev/null 2>&1 || die "git não encontrado. Instale git e rode de novo."

if [ -d "$INSTALL_DIR/.git" ]; then
    if [ -n "$(git -C "$INSTALL_DIR" status --porcelain)" ]; then
        info "Warden já existe em $INSTALL_DIR com mudanças locais, pulando git pull"
    else
        info "Warden já existe em $INSTALL_DIR, atualizando..."
        git -C "$INSTALL_DIR" pull --ff-only
    fi
else
    info "Clonando Warden em $INSTALL_DIR..."
    git clone "$REPO_URL" "$INSTALL_DIR"
fi

cd "$INSTALL_DIR"

if ! command -v uv >/dev/null 2>&1; then
    info "uv não encontrado, instalando..."
    curl -LsSf https://astral.sh/uv/install.sh | sh
    export PATH="$HOME/.local/bin:$PATH"
fi

if ! command -v pnpm >/dev/null 2>&1; then
    info "pnpm não encontrado, instalando..."
    curl -fsSL https://get.pnpm.io/install.sh | sh -
    export PATH="$HOME/.local/share/pnpm:$PATH"
fi

command -v just >/dev/null 2>&1 || die "just não encontrado. Instale antes de continuar: https://github.com/casey/just#installation"

info "Instalando deps do engine (uv sync)..."
(cd engine && uv sync)

info "Instalando deps do front (pnpm install)..."
(cd web && pnpm install)

printf '%s\n' "${g}────────────────────────${r}"
info "instalado. próximo passo:"
echo
printf '%s\n' "  ${g}\$${r} cd $INSTALL_DIR"
printf '%s\n' "  ${g}\$${r} just boot"
echo
