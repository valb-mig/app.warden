from pathlib import Path

from warden.registry import Registry

PROJECT_A = """
id = "a"
type = "raw"
path = "/tmp/a"

[start]
cmd = ["true"]
"""

PROJECT_B = """
id = "b"
type = "raw"
path = "/tmp/b"

[start]
cmd = ["true"]
"""


def test_registry_loads_all_tomls_and_skips_global_config(tmp_path: Path) -> None:
    (tmp_path / "a.toml").write_text(PROJECT_A)
    (tmp_path / "b.toml").write_text(PROJECT_B)
    (tmp_path / "config.toml").write_text("api_port = 9000\n")

    registry = Registry(tmp_path)
    registry.load()

    ids = {p.id for p in registry.all()}
    assert ids == {"a", "b"}
    assert registry.get("a").path == "/tmp/a"


def test_registry_empty_when_dir_missing(tmp_path: Path) -> None:
    registry = Registry(tmp_path / "does-not-exist")
    registry.load()
    assert registry.all() == []
