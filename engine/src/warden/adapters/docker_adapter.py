"""Adapter docker — shell out pra `docker compose` (não docker-py), um stack por projeto."""

import json
import subprocess

from warden.adapters.base import Adapter, ProcessStatus
from warden.config import ProjectConfig


class DockerAdapter(Adapter):
    def __init__(self, config: ProjectConfig):
        self.config = config
        self._compose_file = config.compose_file or "docker-compose.yml"

    def _compose(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["docker", "compose", "-f", self._compose_file, *args],
            cwd=self.config.path,
            capture_output=True,
            text=True,
        )

    def start(self) -> None:
        self._compose("up", "-d")

    def stop(self) -> None:
        self._compose("stop")

    def status(self) -> ProcessStatus:
        result = self._compose("ps", "--format", "json")
        containers = _parse_ps_json(result.stdout)
        running = [c for c in containers if c.get("State") == "running"]
        if not running:
            return ProcessStatus(running=False)
        pid = self._container_pid(running[0]["ID"])
        ports = sorted(
            {
                publisher["PublishedPort"]
                for container in running
                for publisher in container.get("Publishers") or []
                if publisher.get("PublishedPort")
            }
        )
        return ProcessStatus(running=True, pid=pid, ports=ports)

    def _container_pid(self, container_id: str) -> int | None:
        result = subprocess.run(
            ["docker", "inspect", "-f", "{{.State.Pid}}", container_id],
            capture_output=True,
            text=True,
        )
        try:
            pid = int(result.stdout.strip())
        except ValueError:
            return None
        return pid or None

    def logs(self, tail: int = 100, service: str | None = None) -> list[str]:
        args = ["logs", "--no-color", "--tail", str(tail)]
        if service:
            args.append(service)
        result = self._compose(*args)
        output = result.stdout + result.stderr
        return [line for line in output.splitlines() if line.strip()]

    def services(self) -> list[str]:
        result = self._compose("config", "--services")
        return [line for line in result.stdout.splitlines() if line.strip()]


def _parse_ps_json(output: str) -> list[dict]:
    output = output.strip()
    if not output:
        return []
    try:
        data = json.loads(output)
        return data if isinstance(data, list) else [data]
    except json.JSONDecodeError:
        return [json.loads(line) for line in output.splitlines() if line.strip()]
