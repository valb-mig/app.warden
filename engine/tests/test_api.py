import sys
import time
from pathlib import Path

from fastapi.testclient import TestClient

from warden.api.app import create_app


def _write_project(config_dir: Path, project_id: str, cmd: list[str], extra: str = "") -> None:
    projects_dir = config_dir / "projects"
    projects_dir.mkdir(parents=True, exist_ok=True)
    (projects_dir / f"{project_id}.toml").write_text(
        f'id = "{project_id}"\ntype = "raw"\npath = "{config_dir}"\n\n'
        f"[start]\ncmd = {cmd!r}\n{extra}"
    )


def _client(tmp_path: Path) -> tuple[TestClient, str]:
    app = create_app(tmp_path)
    return TestClient(app), app.state.api_token


def test_cors_allows_browser_origin(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)

    resp = client.get(
        "/projects",
        headers={"Authorization": f"Bearer {token}", "Origin": "http://localhost:3000"},
    )

    assert resp.headers["access-control-allow-origin"] == "*"


def test_requires_auth(tmp_path: Path) -> None:
    client, _ = _client(tmp_path)
    resp = client.get("/projects")
    assert resp.status_code == 401


def test_list_projects(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)

    resp = client.get("/projects", headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    assert resp.json() == [{"id": "demo", "name": "demo", "type": "raw", "group": None}]


def test_unknown_project_returns_404(tmp_path: Path) -> None:
    client, token = _client(tmp_path)
    resp = client.get("/projects/nope/status", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 404


def test_start_status_stop_lifecycle(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", [sys.executable, "-c", "import time; time.sleep(5)"])
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    assert client.post("/projects/demo/start", headers=headers).status_code == 200

    deadline = time.time() + 3
    running = False
    while time.time() < deadline:
        if client.get("/projects/demo/status", headers=headers).json()["running"]:
            running = True
            break
        time.sleep(0.05)
    assert running

    assert client.post("/projects/demo/stop", headers=headers).status_code == 200


def test_list_actions_returns_configured_actions(tmp_path: Path) -> None:
    _write_project(
        tmp_path,
        "demo",
        ["true"],
        extra=(
            '\n[[actions]]\nname = "hello"\ncmd = ["echo", "hi"]\n'
            '\n[[actions]]\nname = "shell"\ncmd = ["bash"]\ninteractive = true\n'
        ),
    )
    client, token = _client(tmp_path)

    resp = client.get("/projects/demo/actions", headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    assert resp.json() == [
        {"name": "hello", "interactive": False},
        {"name": "shell", "interactive": True},
    ]


def test_action_runs_and_returns_output(tmp_path: Path) -> None:
    _write_project(
        tmp_path,
        "demo",
        ["true"],
        extra='\n[[actions]]\nname = "hello"\ncmd = ["echo", "hi"]\n',
    )
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.post("/projects/demo/actions/hello", headers=headers)

    assert resp.status_code == 200
    body = resp.json()
    assert body["exit_code"] == 0
    assert "hi" in body["output"]


def test_services_empty_for_single_process_project(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)

    resp = client.get("/projects/demo/services", headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    assert resp.json() == {"services": []}


def test_languages_endpoint_detects_python_manifest(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    (tmp_path / "pyproject.toml").write_text("[project]\nname = 'demo'\n")
    client, token = _client(tmp_path)

    resp = client.get("/projects/demo/languages", headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    assert "python" in resp.json()["languages"]


def test_unknown_action_returns_404(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)
    resp = client.post(
        "/projects/demo/actions/nope", headers={"Authorization": f"Bearer {token}"}
    )
    assert resp.status_code == 404


def test_ws_logs_requires_token(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, _ = _client(tmp_path)

    try:
        with client.websocket_connect("/ws/projects/demo/logs?token=errado"):
            raise AssertionError("deveria ter fechado a conexão")
    except Exception:
        pass


def test_git_endpoint_returns_null_for_non_repo(tmp_path: Path) -> None:
    # path do projeto (config_dir) não é repo git → endpoint devolve null, não 404.
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)

    resp = client.get("/projects/demo/git", headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    assert resp.json() is None


def test_git_pull_requires_confirmation(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    # sem confirm → 409 antes mesmo de tentar rodar
    assert client.post("/projects/demo/git/pull", headers=headers).status_code == 409


def test_git_unknown_verb_is_400(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.post("/projects/demo/git/clone", headers=headers)
    assert resp.status_code == 400


def test_git_fetch_on_non_repo_returns_refused(tmp_path: Path) -> None:
    _write_project(tmp_path, "demo", ["true"])
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}

    resp = client.post("/projects/demo/git/fetch", headers=headers)
    assert resp.status_code == 200
    body = resp.json()
    assert body["refused"] is True
    assert body["ok"] is False


def test_ws_logs_streams_output(tmp_path: Path) -> None:
    _write_project(
        tmp_path,
        "demo",
        [sys.executable, "-u", "-c", "print('hello')"],
        extra="capture_stdout = true\n",
    )
    client, token = _client(tmp_path)
    headers = {"Authorization": f"Bearer {token}"}
    client.post("/projects/demo/start", headers=headers)

    with client.websocket_connect(f"/ws/projects/demo/logs?token={token}") as ws:
        message = ws.receive_text()
        assert "hello" in message
