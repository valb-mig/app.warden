set shell := ["bash", "-cu"]

# instala deps do engine (python)
sync:
    cd engine && uv sync

# roda testes do engine
test:
    cd engine && uv run pytest

# lint + format check do engine
lint:
    cd engine && uv run ruff check .

# format do engine
fmt:
    cd engine && uv run ruff format .

# CLI do warden (fase 2 em diante)
cli *args:
    cd engine && uv run warden {{args}}

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
