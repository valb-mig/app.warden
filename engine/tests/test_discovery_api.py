from pathlib import Path

from fastapi.testclient import TestClient

from warden.api.app import create_app


def _client(tmp_path: Path) -> tuple[TestClient, str]:
    app = create_app(tmp_path)
    return TestClient(app), app.state.api_token


def test_scan_paths_crud(tmp_path: Path) -> None:
    scan_root = tmp_path / "myprojects"
    scan_root.mkdir()
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.get("/scan-paths", headers=headers)
    assert resp.json() == {"scan_paths": []}

    resp = client.post("/scan-paths", json={"path": str(scan_root)}, headers=headers)
    assert resp.status_code == 200
    assert resp.json() == {"scan_paths": [str(scan_root)]}

    resp = client.request("DELETE", "/scan-paths", json={"path": str(scan_root)}, headers=headers)
    assert resp.json() == {"scan_paths": []}


def test_add_scan_path_rejects_missing_dir(tmp_path: Path) -> None:
    client, token = _client(tmp_path)
    resp = client.post(
        "/scan-paths",
        json={"path": str(tmp_path / "nope")},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 400


def test_scan_paths_require_auth(tmp_path: Path) -> None:
    client, _ = _client(tmp_path)
    assert client.get("/scan-paths").status_code == 401


def test_discover_lists_unregistered_projects(tmp_path: Path) -> None:
    scan_root = tmp_path / "myprojects"
    scan_root.mkdir()
    (scan_root / "new-node").mkdir()
    (scan_root / "new-node" / "package.json").write_text("{}")

    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}
    client.post("/scan-paths", json={"path": str(scan_root)}, headers=headers)

    resp = client.get("/discover", headers=headers)
    assert resp.status_code == 200
    assert resp.json()["projects"] == [
        {
            "name": "new-node",
            "path": str((scan_root / "new-node").resolve()),
            "type": "node",
        }
    ]


def test_preview_and_apply_writes_config_and_reloads_registry(tmp_path: Path) -> None:
    project_dir = tmp_path / "myprojects" / "demo"
    project_dir.mkdir(parents=True)
    (project_dir / "main.py").write_text("print('hi')")

    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.post("/discover/preview", json={"path": str(project_dir)}, headers=headers)
    assert resp.status_code == 200
    config = resp.json()["config"]
    assert config["type"] == "python"
    assert config["id"] == "demo"

    resp = client.post("/discover/apply", json=config, headers=headers)
    assert resp.status_code == 200

    resp = client.get("/projects", headers=headers)
    assert "demo" in {p["id"] for p in resp.json()}

    resp = client.get(f"/projects/{config['id']}/config", headers=headers)
    assert resp.status_code == 200
    assert resp.json()["id"] == "demo"


def test_get_project_config_404_when_missing(tmp_path: Path) -> None:
    client, token = _client(tmp_path)
    resp = client.get("/projects/nope/config", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 404


def test_browse_lists_subdirectories(tmp_path: Path) -> None:
    browse_root = tmp_path / "browseroot"
    browse_root.mkdir()
    (browse_root / "sub").mkdir()

    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.get("/browse", params={"path": str(browse_root)}, headers=headers)
    assert resp.status_code == 200
    body = resp.json()
    assert body["path"] == str(browse_root)
    assert body["parent"] == str(browse_root.parent)
    assert body["entries"] == [{"name": "sub", "path": str(browse_root / "sub")}]


def test_browse_rejects_non_directory(tmp_path: Path) -> None:
    client, token = _client(tmp_path)
    resp = client.get(
        "/browse",
        params={"path": str(tmp_path / "nope")},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 400
