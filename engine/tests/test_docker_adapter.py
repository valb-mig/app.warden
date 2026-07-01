import shutil
import subprocess
import time
from pathlib import Path

import pytest

from warden.adapters.docker_adapter import DockerAdapter
from warden.config import ProjectConfig

pytestmark = pytest.mark.skipif(shutil.which("docker") is None, reason="requer docker instalado")

COMPOSE_YAML = """
services:
  app:
    image: busybox:stable
    command: ["sh", "-c", "i=0; while true; do echo tick $$i; i=$$((i+1)); sleep 1; done"]
    ports:
      - "{port}:80"
"""

PORT = 18199


def _wait_until(predicate, timeout: float = 30.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.3)
    raise AssertionError("condição não satisfeita dentro do timeout")


@pytest.fixture
def docker_adapter(tmp_path: Path):
    (tmp_path / "docker-compose.yml").write_text(COMPOSE_YAML.format(port=PORT))
    config = ProjectConfig(id="docker-demo", path=str(tmp_path), type="docker")
    adapter = DockerAdapter(config)
    yield adapter
    subprocess.run(["docker", "compose", "down"], cwd=tmp_path, capture_output=True)


def test_status_before_start_is_not_running(docker_adapter: DockerAdapter) -> None:
    assert docker_adapter.status().running is False


def test_start_reports_running_pid_and_port(docker_adapter: DockerAdapter) -> None:
    docker_adapter.start()

    _wait_until(lambda: docker_adapter.status().running is True)

    status = docker_adapter.status()
    assert status.pid is not None and status.pid > 0
    assert PORT in status.ports


def test_logs_capture_container_output(docker_adapter: DockerAdapter) -> None:
    docker_adapter.start()

    _wait_until(lambda: any("tick" in line for line in docker_adapter.logs()))


def test_stop_marks_not_running(docker_adapter: DockerAdapter) -> None:
    docker_adapter.start()
    _wait_until(lambda: docker_adapter.status().running is True)

    docker_adapter.stop()

    _wait_until(lambda: docker_adapter.status().running is False)
