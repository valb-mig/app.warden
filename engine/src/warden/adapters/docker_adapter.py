"""Adapter docker — shell out pra `docker compose` (não docker-py), um stack por projeto."""

import json
import subprocess

from warden.adapters.base import Adapter, ProcessStatus
from warden.config import ProjectConfig
from warden.vitals import VitalsSampler


class DockerAdapter(Adapter):
    """`compose_services` vazio = compose file é só desse projeto, comando afeta o
    arquivo inteiro (comportamento antigo). Preenchido = compose file compartilhado
    por vários projetos (stack único por workspace) — todo comando fica restrito
    aos serviços listados, pra não subir/derrubar/misturar log dos vizinhos."""

    def __init__(self, config: ProjectConfig):
        self.config = config
        self._compose_file = config.compose_file or "docker-compose.yml"
        self._services = config.compose_services
        self._vitals = VitalsSampler()

    def _compose(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["docker", "compose", "-f", self._compose_file, *args],
            cwd=self.config.path,
            capture_output=True,
            text=True,
        )

    def start(self) -> None:
        self._compose("up", "-d", *self._services)

    def stop(self) -> None:
        self._compose("stop", *self._services)

    def status(self) -> ProcessStatus:
        result = self._compose("ps", "--format", "json")
        containers = _parse_ps_json(result.stdout)
        if self._services:
            containers = [c for c in containers if c.get("Service") in self._services]
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
        cpu_percent, memory_mb = None, None
        if pid:
            vitals = self._vitals.sample(pid)
            if vitals:
                cpu_percent, memory_mb = vitals
        return ProcessStatus(
            running=True, pid=pid, ports=ports, cpu_percent=cpu_percent, memory_mb=memory_mb
        )

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
        elif self._services:
            args.extend(self._services)
        result = self._compose(*args)
        output = result.stdout + result.stderr
        return [line for line in output.splitlines() if line.strip()]

    def services(self) -> list[str]:
        if self._services:
            return self._services
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
