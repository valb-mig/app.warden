import json
from pathlib import Path

from warden.config import load_project_config
from warden.scaffold import build_config, detect_type, render_toml


def _write_toml_and_reload(config, tmp_path: Path):
    toml_file = tmp_path / f"{config.id}.toml"
    toml_file.write_text(render_toml(config))
    return load_project_config(toml_file)


def test_detect_docker(tmp_path: Path) -> None:
    (tmp_path / "docker-compose.yml").write_text(
        "services:\n  app:\n    image: foo\n  nginx:\n    image: bar\n"
    )
    assert detect_type(tmp_path) == "docker"

    config = build_config(tmp_path)
    assert config.type == "docker"
    assert config.compose_file == "docker-compose.yml"
    assert {ls.service for ls in config.log_sources} == {"app", "nginx"}

    reloaded = _write_toml_and_reload(config, tmp_path.parent)
    assert reloaded.compose_file == "docker-compose.yml"
    assert len(reloaded.log_sources) == 2


def test_detect_node_with_scripts(tmp_path: Path) -> None:
    (tmp_path / "package.json").write_text(
        json.dumps({"scripts": {"dev": "next dev", "build": "next build", "lint": "eslint ."}})
    )
    (tmp_path / "pnpm-lock.yaml").write_text("")

    config = build_config(tmp_path)
    assert config.type == "node"
    assert config.start is not None
    assert config.start.cmd == ["pnpm", "run", "dev"]
    action_names = {a.name for a in config.actions}
    assert action_names == {"build", "lint"}

    reloaded = _write_toml_and_reload(config, tmp_path.parent)
    assert reloaded.start.cmd == ["pnpm", "run", "dev"]


def test_detect_php_laravel(tmp_path: Path) -> None:
    (tmp_path / "composer.json").write_text("{}")
    (tmp_path / "artisan").write_text("#!/usr/bin/env php\n")
    log_dir = tmp_path / "storage" / "logs"
    log_dir.mkdir(parents=True)
    (log_dir / "laravel.log").write_text("")

    config = build_config(tmp_path)
    assert config.type == "php"
    action_names = {a.name for a in config.actions}
    assert action_names == {"migrate", "seed", "tinker"}
    tinker = next(a for a in config.actions if a.name == "tinker")
    assert tinker.interactive is True
    assert config.log_sources[0].path == "./storage/logs/laravel.log"

    _write_toml_and_reload(config, tmp_path.parent)


def test_detect_python_django(tmp_path: Path) -> None:
    (tmp_path / "manage.py").write_text("")

    config = build_config(tmp_path)
    assert config.type == "python"
    assert config.start.cmd == ["python", "manage.py", "runserver"]

    _write_toml_and_reload(config, tmp_path.parent)


def test_detect_python_with_uv(tmp_path: Path) -> None:
    (tmp_path / "main.py").write_text("")
    (tmp_path / "uv.lock").write_text("")

    config = build_config(tmp_path)
    assert config.start.cmd == ["uv", "run", "python", "main.py"]


def test_detect_python_with_venv(tmp_path: Path) -> None:
    (tmp_path / "run.py").write_text("")
    venv_bin = tmp_path / "venv" / "bin"
    venv_bin.mkdir(parents=True)
    (venv_bin / "python").write_text("")

    config = build_config(tmp_path)
    assert config.start.cmd == [str(venv_bin / "python"), "run.py"]


def test_detect_python_venv_ignored_when_uv_lock_present(tmp_path: Path) -> None:
    (tmp_path / "main.py").write_text("")
    (tmp_path / "uv.lock").write_text("")
    venv_bin = tmp_path / "venv" / "bin"
    venv_bin.mkdir(parents=True)
    (venv_bin / "python").write_text("")

    config = build_config(tmp_path)
    assert config.start.cmd == ["uv", "run", "python", "main.py"]


def test_detect_just(tmp_path: Path) -> None:
    (tmp_path / "Justfile").write_text("default: dev\n\ndev:\n    echo hi\n\ntest:\n    pytest\n")

    config = build_config(tmp_path)
    assert config.type == "just"
    assert config.start.cmd == ["just", "dev"]
    assert {a.name for a in config.actions} == {"test"}

    _write_toml_and_reload(config, tmp_path.parent)


def test_detect_raw_fallback(tmp_path: Path) -> None:
    config = build_config(tmp_path)
    assert config.type == "raw"
    assert config.start is None
    assert config.actions == []

    _write_toml_and_reload(config, tmp_path.parent)


def test_build_config_custom_id(tmp_path: Path) -> None:
    project_dir = tmp_path / "My Cool Project"
    project_dir.mkdir()

    config = build_config(project_dir)
    assert config.id == "my-cool-project"
    assert config.name == "My Cool Project"

    config_with_id = build_config(project_dir, project_id="custom-id")
    assert config_with_id.id == "custom-id"


def test_build_config_missing_path_raises() -> None:
    try:
        build_config("/path/definitely/does/not/exist")
        raise AssertionError("expected FileNotFoundError")
    except FileNotFoundError:
        pass
