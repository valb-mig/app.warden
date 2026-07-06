"""Gera ~/.warden/<id>.toml a partir da pasta de um projeto — `warden init <path>`.

Detecta o tipo pela presença de arquivos característicos (docker-compose, package.json,
composer.json, Justfile, *.py) e pré-popula start/log_sources/actions com heurísticas
simples. Não roda nada, só monta o ProjectConfig — usuário confirma antes de gravar.
"""

import json
import re
from pathlib import Path

import tomli_w

from warden.config import (
    ActionConfig,
    LogSource,
    NotifyConfig,
    ProjectConfig,
    ProjectType,
    StartConfig,
)

_COMPOSE_CANDIDATES = ("docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml")
_JUSTFILE_CANDIDATES = ("Justfile", "justfile")
_JUST_RECIPE_RE = re.compile(r"^([A-Za-z0-9_-]+)(?:\s+[^:]*)?:(?!=)")


def _slugify(name: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")
    return slug or "project"


def _find_compose_file(path: Path) -> Path | None:
    # checa a própria pasta e um nível acima — cobre o caso comum de workspace com
    # vários projetos client (zarb1/zarb2/...) compartilhando um compose na raiz.
    for base in (path, path.parent):
        for candidate in _COMPOSE_CANDIDATES:
            found = base / candidate
            if found.exists():
                return found
    return None


def _find_justfile(path: Path) -> Path | None:
    for candidate in _JUSTFILE_CANDIDATES:
        found = path / candidate
        if found.exists():
            return found
    return None


def _looks_like_python(path: Path) -> bool:
    if (path / "pyproject.toml").exists():
        return True
    if (path / "requirements.txt").exists():
        return True
    if (path / "manage.py").exists():
        return True
    return any(path.glob("*.py"))


def detect_type(path: Path) -> ProjectType:
    if _find_compose_file(path) is not None:
        return "docker"
    if (path / "package.json").exists():
        return "node"
    if (path / "composer.json").exists():
        return "php"
    if _looks_like_python(path):
        return "python"
    if _find_justfile(path) is not None:
        return "just"
    return "raw"


def _match_prefixed_services(services: list[str], project_id: str) -> list[str]:
    prefixes = (f"{project_id}_", f"{project_id}-")
    return [s for s in services if s.startswith(prefixes)]


def _parse_compose_services(compose_file: Path) -> list[str]:
    """Heurística por indentação, sem parser YAML — cobre o caso comum de 2 espaços."""
    services: list[str] = []
    in_services = False
    child_indent: int | None = None
    for line in compose_file.read_text().splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = len(line) - len(line.lstrip(" "))
        if not in_services:
            if stripped == "services:":
                in_services = True
            continue
        if child_indent is None:
            child_indent = indent
        if indent < child_indent:
            break
        if indent == child_indent and stripped.endswith(":"):
            services.append(stripped[:-1].strip("\"'"))
    return services


def _build_docker(path: Path, project_id: str) -> dict:
    compose_file = _find_compose_file(path)
    assert compose_file is not None
    all_services = _parse_compose_services(compose_file)

    # compose fora da própria pasta (workspace com várias apps client) quase sempre
    # é compartilhado — restringe pelos serviços prefixados com o id do projeto pra
    # não fazer start/stop/logs vazar pros vizinhos no mesmo arquivo.
    own_services = all_services
    if compose_file.parent != path:
        matched = _match_prefixed_services(all_services, project_id)
        if matched:
            own_services = matched

    log_sources = [LogSource(name=s, type="docker", service=s) for s in own_services]
    if not log_sources:
        log_sources = [LogSource(name="app", type="docker", service="app")]

    compose_ref = compose_file.name if compose_file.parent == path else str(compose_file)
    kwargs: dict = {"compose_file": compose_ref, "log_sources": log_sources}
    if own_services != all_services:
        kwargs["compose_services"] = own_services

    justfile = _find_justfile(path)
    if justfile is not None:
        actions = [ActionConfig(name=r, cmd=["just", r]) for r in _parse_justfile_recipes(justfile)]
        if actions:
            kwargs["actions"] = actions
    return kwargs


def _detect_node_runner(path: Path) -> str:
    if (path / "pnpm-lock.yaml").exists():
        return "pnpm"
    if (path / "yarn.lock").exists():
        return "yarn"
    return "npm"


def _node_run_cmd(runner: str, script: str) -> list[str]:
    return [runner, "run", script]


def _build_node(path: Path) -> dict:
    package = json.loads((path / "package.json").read_text())
    scripts: dict[str, str] = package.get("scripts") or {}
    runner = _detect_node_runner(path)

    start_script = next((s for s in ("dev", "start") if s in scripts), None)
    kwargs: dict = {}
    if start_script:
        kwargs["start"] = StartConfig(cmd=_node_run_cmd(runner, start_script), capture_stdout=True)
        kwargs["log_sources"] = [LogSource(name="stdout", type="stdout")]

    actions = [
        ActionConfig(name=name, cmd=_node_run_cmd(runner, name))
        for name in scripts
        if name != start_script
    ]
    if actions:
        kwargs["actions"] = actions
    return kwargs


def _build_laravel(path: Path) -> dict:
    kwargs: dict = {
        "start": StartConfig(cmd=["php", "artisan", "serve"], capture_stdout=True),
        "actions": [
            ActionConfig(
                name="migrate",
                cmd=["php", "artisan", "migrate", "--force"],
                destructive=True,
            ),
            ActionConfig(
                name="seed", cmd=["php", "artisan", "db:seed", "--force"], destructive=True
            ),
            ActionConfig(name="tinker", cmd=["php", "artisan", "tinker"], interactive=True),
        ],
    }
    log_file = path / "storage" / "logs" / "laravel.log"
    if log_file.exists():
        kwargs["log_sources"] = [
            LogSource(
                name="laravel",
                type="file",
                path="./storage/logs/laravel.log",
                error_patterns=["ERROR", r"\bException\b", "PHP Fatal"],
            )
        ]
    return kwargs


def _build_plain_php(path: Path) -> dict:
    """PHP sem artisan (Symfony/ZF/Slim/legado) — sobe com o server embutido do PHP
    servindo `public/` (padrão de quase todo framework) e importa ações do Justfile
    quando existir, já que esse tipo de projeto costuma driblar tudo via `just`."""
    docroot = "public" if (path / "public").exists() else "."
    kwargs: dict = {
        "start": StartConfig(cmd=["php", "-S", "0.0.0.0:8000", "-t", docroot], capture_stdout=True),
        "log_sources": [LogSource(name="stdout", type="stdout")],
    }
    justfile = _find_justfile(path)
    if justfile is not None:
        recipes = _parse_justfile_recipes(justfile)
        actions = [ActionConfig(name=r, cmd=["just", r]) for r in recipes]
        if actions:
            kwargs["actions"] = actions
    return kwargs


def _build_php(path: Path) -> dict:
    if (path / "artisan").exists():
        return _build_laravel(path)
    return _build_plain_php(path)


def _detect_python_entry(path: Path) -> tuple[str, list[str]] | None:
    if (path / "manage.py").exists():
        return "manage.py", ["runserver"]
    for candidate in ("main.py", "app.py"):
        if (path / candidate).exists():
            return candidate, []
    py_files = list(path.glob("*.py"))
    if len(py_files) == 1:
        return py_files[0].name, []
    return None


_VENV_CANDIDATES = ("venv", ".venv", "env")


def _detect_venv_python(path: Path) -> str | None:
    for candidate in _VENV_CANDIDATES:
        venv_python = path / candidate / "bin" / "python"
        if venv_python.exists():
            return str(venv_python)
    return None


def _build_python(path: Path) -> dict:
    entry = _detect_python_entry(path)
    if entry is None:
        return {}
    filename, extra_args = entry

    if (path / "uv.lock").exists():
        cmd = ["uv", "run", "python", filename, *extra_args]
    else:
        interpreter = _detect_venv_python(path) or "python"
        cmd = [interpreter, filename, *extra_args]

    return {
        "start": StartConfig(cmd=cmd, capture_stdout=True),
        "log_sources": [LogSource(name="stdout", type="stdout")],
    }


def _parse_justfile_recipes(justfile: Path) -> list[str]:
    recipes = []
    for line in justfile.read_text().splitlines():
        if not line or line[0] in " \t#@":
            continue
        match = _JUST_RECIPE_RE.match(line)
        if match and match.group(1) != "default":
            recipes.append(match.group(1))
    return recipes


def _build_just(path: Path) -> dict:
    justfile = _find_justfile(path)
    assert justfile is not None
    recipes = _parse_justfile_recipes(justfile)
    start_recipe = next((r for r in ("dev", "serve", "start") if r in recipes), None)

    kwargs: dict = {}
    if start_recipe:
        kwargs["start"] = StartConfig(cmd=["just", start_recipe], capture_stdout=True)
        kwargs["log_sources"] = [LogSource(name="stdout", type="stdout")]
    actions = [ActionConfig(name=r, cmd=["just", r]) for r in recipes if r != start_recipe]
    if actions:
        kwargs["actions"] = actions
    return kwargs


def _build_raw(path: Path) -> dict:
    return {}


_BUILDERS = {
    "node": _build_node,
    "php": _build_php,
    "python": _build_python,
    "just": _build_just,
    "raw": _build_raw,
}


def build_config(path: str | Path, project_id: str | None = None) -> ProjectConfig:
    resolved = Path(path).expanduser().resolve()
    if not resolved.is_dir():
        raise FileNotFoundError(f"path não é diretório: {resolved}")

    ptype = detect_type(resolved)
    pid = project_id or _slugify(resolved.name)
    extra = _build_docker(resolved, pid) if ptype == "docker" else _BUILDERS[ptype](resolved)
    return ProjectConfig(id=pid, name=resolved.name, path=str(resolved), type=ptype, **extra)


def render_toml(config: ProjectConfig) -> str:
    data: dict = {"id": config.id}
    if config.name and config.name != config.id:
        data["name"] = config.name
    if config.group:
        data["group"] = config.group
    data["path"] = config.path
    data["type"] = config.type
    if config.compose_file:
        data["compose_file"] = config.compose_file
    if config.compose_services:
        data["compose_services"] = config.compose_services
    if config.start:
        data["start"] = config.start.model_dump(exclude_none=True, exclude_defaults=True)
    if config.notify != NotifyConfig():
        data["notify"] = config.notify.model_dump(exclude_defaults=True)
    if config.log_sources:
        data["log_sources"] = [
            ls.model_dump(exclude_none=True, exclude_defaults=True) for ls in config.log_sources
        ]
    if config.actions:
        data["actions"] = [
            a.model_dump(exclude_none=True, exclude_defaults=True) for a in config.actions
        ]
    return tomli_w.dumps(data)
