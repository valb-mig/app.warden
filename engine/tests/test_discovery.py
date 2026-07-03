from pathlib import Path

import pytest

from warden.config import load_global_config
from warden.discovery import add_scan_path, browse_directory, discover_projects, remove_scan_path
from warden.registry import Registry


def test_add_scan_path_persists_and_dedupes(tmp_path: Path) -> None:
    scan_root = tmp_path / "myprojects"
    scan_root.mkdir()

    config = add_scan_path(tmp_path, str(scan_root))
    assert config.scan_paths == [str(scan_root)]

    config = add_scan_path(tmp_path, str(scan_root))
    assert config.scan_paths == [str(scan_root)]

    reloaded = load_global_config(tmp_path / "config.toml")
    assert reloaded.scan_paths == [str(scan_root)]


def test_add_scan_path_rejects_non_directory(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        add_scan_path(tmp_path, str(tmp_path / "nope"))


def test_remove_scan_path(tmp_path: Path) -> None:
    scan_root = tmp_path / "myprojects"
    scan_root.mkdir()
    add_scan_path(tmp_path, str(scan_root))

    config = remove_scan_path(tmp_path, str(scan_root))
    assert config.scan_paths == []


def test_discover_projects_lists_new_and_skips_registered_and_hidden(tmp_path: Path) -> None:
    scan_root = tmp_path / "myprojects"
    scan_root.mkdir()
    (scan_root / "already-registered").mkdir()
    (scan_root / "new-python").mkdir()
    (scan_root / "new-python" / "requirements.txt").write_text("")
    (scan_root / ".hidden").mkdir()

    projects_dir = tmp_path / "projects"
    projects_dir.mkdir()
    (projects_dir / "already.toml").write_text(
        f'id = "already"\ntype = "raw"\npath = "{scan_root / "already-registered"}"\n'
    )
    registry = Registry(tmp_path)
    registry.load()

    config = add_scan_path(tmp_path, str(scan_root))
    discovered = discover_projects(config, registry)

    assert {d.name for d in discovered} == {"new-python"}
    assert discovered[0].type == "python"


def test_discover_projects_ignores_missing_scan_path(tmp_path: Path) -> None:
    config = add_scan_path(tmp_path, str(tmp_path))
    config.scan_paths.append(str(tmp_path / "gone"))

    registry = Registry(tmp_path)
    registry.load()

    discovered = discover_projects(config, registry)
    assert discovered == []


def test_browse_defaults_to_home() -> None:
    result = browse_directory(None)
    assert result.path == str(Path.home())


def test_browse_lists_subdirectories_and_parent(tmp_path: Path) -> None:
    (tmp_path / "sub-a").mkdir()
    (tmp_path / "sub-b").mkdir()
    (tmp_path / ".hidden").mkdir()
    (tmp_path / "a-file.txt").write_text("")

    result = browse_directory(str(tmp_path))

    assert result.path == str(tmp_path)
    assert result.parent == str(tmp_path.parent)
    assert {e.name for e in result.entries} == {"sub-a", "sub-b"}


def test_browse_root_has_no_parent() -> None:
    result = browse_directory("/")
    assert result.parent is None


def test_browse_rejects_non_directory(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        browse_directory(str(tmp_path / "nope"))
