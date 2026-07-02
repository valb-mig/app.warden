from pathlib import Path

from warden.languages import detect_languages


def test_empty_project_detects_nothing(tmp_path: Path) -> None:
    assert detect_languages(tmp_path) == []


def test_python_manifest_detected(tmp_path: Path) -> None:
    (tmp_path / "pyproject.toml").write_text("[project]\n")
    assert detect_languages(tmp_path) == ["python"]


def test_typescript_preferred_over_javascript_when_tsconfig_present(tmp_path: Path) -> None:
    (tmp_path / "package.json").write_text("{}")
    (tmp_path / "tsconfig.json").write_text("{}")
    assert detect_languages(tmp_path) == ["typescript"]


def test_javascript_without_tsconfig(tmp_path: Path) -> None:
    (tmp_path / "package.json").write_text("{}")
    assert detect_languages(tmp_path) == ["javascript"]


def test_multiple_manifests_combine_up_to_limit(tmp_path: Path) -> None:
    (tmp_path / "composer.json").write_text("{}")
    (tmp_path / "package.json").write_text("{}")
    (tmp_path / "go.mod").write_text("module demo\n")

    langs = detect_languages(tmp_path)

    assert set(langs) == {"php", "javascript", "go"}
    assert len(langs) == 3


def test_extension_scan_fills_gap_when_no_manifest(tmp_path: Path) -> None:
    (tmp_path / "main.rs").write_text("fn main() {}")
    (tmp_path / "lib.rs").write_text("")
    (tmp_path / "notes.txt").write_text("")

    assert detect_languages(tmp_path) == ["rust"]


def test_extension_scan_skips_ignored_dirs(tmp_path: Path) -> None:
    vendor = tmp_path / "node_modules" / "pkg"
    vendor.mkdir(parents=True)
    (vendor / "index.js").write_text("")

    assert detect_languages(tmp_path) == []


def test_limit_caps_result_count(tmp_path: Path) -> None:
    (tmp_path / "composer.json").write_text("{}")
    (tmp_path / "package.json").write_text("{}")
    (tmp_path / "go.mod").write_text("module demo\n")

    assert len(detect_languages(tmp_path, limit=2)) == 2
