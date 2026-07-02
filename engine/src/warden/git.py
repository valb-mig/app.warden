"""Leitura de estado git local do projeto — read-only, sem tocar working tree.

Ortogonal ao adapter: git é propriedade do `path` no disco, não do ciclo de
vida do processo. Serviço próprio, consumido pelo Engine.
"""

import subprocess
from dataclasses import dataclass
from pathlib import Path

# GIT_TERMINAL_PROMPT=0 garante que nenhum comando git pendure pedindo
# credencial num tty inexistente — falha rápido em vez de travar o daemon.
_GIT_ENV = {"GIT_TERMINAL_PROMPT": "0"}

# Separador de campos no formato de log (\x1f = unit separator) — não colide
# com nada que apareça numa mensagem de commit.
_FIELD_SEP = "\x1f"


@dataclass
class GitCommit:
    hash: str
    subject: str
    author: str
    relative: str  # "há 4 minutos" — cru do git (%cr), respeita locale


@dataclass
class GitInfo:
    branch: str
    dirty: bool
    dirty_count: int
    ahead: int | None  # None = sem upstream configurado
    behind: int | None
    has_remote: bool
    last_commit: GitCommit | None  # None = repo sem commits


@dataclass
class GitCommandResult:
    ok: bool
    exit_code: int
    output: str
    refused: bool = False  # bloqueado por guarda (tree sujo etc) — nem chegou a rodar


# Allowlist embutida (decisão #9: sem shell livre). Verbos bounded, não `git <qualquer>`.
# pull/push mutam estado → exigem confirmação na camada da API.
ALLOWED_VERBS = ("fetch", "sync", "pull", "push")
CONFIRM_VERBS = ("pull", "push")


def _run_git(path: Path, args: list[str], timeout: float = 10.0) -> subprocess.CompletedProcess:
    return subprocess.run(
        ["git", "-C", str(path), *args],
        capture_output=True,
        text=True,
        timeout=timeout,
        env=_GIT_ENV,
    )


def is_git_repo(path: Path) -> bool:
    try:
        result = _run_git(path, ["rev-parse", "--is-inside-work-tree"])
    except (OSError, subprocess.TimeoutExpired):
        return False
    return result.returncode == 0 and result.stdout.strip() == "true"


def git_info(path: Path) -> GitInfo | None:
    """Estado git do projeto, ou None se `path` não é repo git."""
    if not is_git_repo(path):
        return None

    branch = _branch(path)
    dirty_count = _dirty_count(path)
    ahead, behind = _ahead_behind(path)
    return GitInfo(
        branch=branch,
        dirty=dirty_count > 0,
        dirty_count=dirty_count,
        ahead=ahead,
        behind=behind,
        has_remote=_has_remote(path),
        last_commit=_last_commit(path),
    )


def _branch(path: Path) -> str:
    result = _run_git(path, ["rev-parse", "--abbrev-ref", "HEAD"])
    return result.stdout.strip() or "HEAD"


def _dirty_count(path: Path) -> int:
    result = _run_git(path, ["status", "--porcelain"])
    if result.returncode != 0:
        return 0
    return sum(1 for line in result.stdout.splitlines() if line.strip())


def _ahead_behind(path: Path) -> tuple[int | None, int | None]:
    # left-right count contra o upstream (@{u}): "<behind>\t<ahead>".
    # Sem upstream configurado o comando falha → (None, None).
    result = _run_git(path, ["rev-list", "--left-right", "--count", "@{u}...HEAD"])
    if result.returncode != 0:
        return None, None
    parts = result.stdout.split()
    if len(parts) != 2:
        return None, None
    behind, ahead = parts
    return int(ahead), int(behind)


def _has_remote(path: Path) -> bool:
    result = _run_git(path, ["remote"])
    return result.returncode == 0 and bool(result.stdout.strip())


def _cmd_result(result: subprocess.CompletedProcess) -> GitCommandResult:
    return GitCommandResult(
        ok=result.returncode == 0,
        exit_code=result.returncode,
        output=(result.stdout + result.stderr).strip(),
    )


def _refused(reason: str) -> GitCommandResult:
    return GitCommandResult(ok=False, exit_code=-1, output=reason, refused=True)


def git_command(path: Path, verb: str, remote: str = "origin") -> GitCommandResult:
    """Roda um verbo git da allowlist. Guardas evitam merge-conflito e travas no escuro.

    Não checa confirmação — isso é responsabilidade da camada da API (409).
    """
    if verb not in ALLOWED_VERBS:
        raise ValueError(f"verbo git {verb!r} não suportado")
    if not is_git_repo(path):
        return _refused("não é um repositório git")

    if verb == "fetch":
        # fetch é só atualização de refs — não toca working tree.
        return _cmd_result(_run_git(path, ["fetch", remote], timeout=30))

    if verb == "pull":
        # Guarda dirty: pull com tree sujo pode gerar merge-conflito sem ninguém pra
        # resolver via API. --ff-only recusa limpo se divergiu (ahead E behind).
        if _dirty_count(path) > 0:
            return _refused("working tree sujo — commite ou stash antes de pull")
        return _cmd_result(_run_git(path, ["pull", "--ff-only"], timeout=60))

    if verb == "push":
        # GIT_TERMINAL_PROMPT=0 (em _GIT_ENV) faz push falhar rápido se faltar
        # credencial, em vez de pendurar num prompt de senha inexistente.
        return _cmd_result(_run_git(path, ["push"], timeout=60))

    # verb == "sync": one-tap. fetch, e só faz fast-forward se limpo e atrás.
    if _dirty_count(path) > 0:
        return _refused("working tree sujo — commite ou stash antes de sync")
    fetch = _run_git(path, ["fetch", remote], timeout=30)
    if fetch.returncode != 0:
        return _cmd_result(fetch)
    _, behind = _ahead_behind(path)
    if not behind:  # None (sem upstream) ou 0 (já no topo)
        return GitCommandResult(ok=True, exit_code=0, output="já atualizado")
    return _cmd_result(_run_git(path, ["merge", "--ff-only", "@{u}"], timeout=60))


def _last_commit(path: Path) -> GitCommit | None:
    fmt = _FIELD_SEP.join(["%h", "%s", "%an", "%cr"])
    result = _run_git(path, ["log", "-1", f"--format={fmt}"])
    if result.returncode != 0:
        return None  # repo sem commits ainda
    fields = result.stdout.strip().split(_FIELD_SEP)
    if len(fields) != 4:
        return None
    hash_, subject, author, relative = fields
    return GitCommit(hash=hash_, subject=subject, author=author, relative=relative)
