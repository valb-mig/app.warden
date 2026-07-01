"""Token bearer da API — arquivo local em ~/.warden/, permissão 600."""

import secrets
from pathlib import Path


def load_or_create_token(path: Path) -> str:
    if path.exists():
        return path.read_text().strip()
    token = secrets.token_urlsafe(32)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(token)
    path.chmod(0o600)
    return token
