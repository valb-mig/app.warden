"""Adapter base pra tipos owned (não-docker): Popen direto, motor dono do PID."""

import os
import pty
import subprocess
import threading
import time
from collections.abc import Callable

import psutil

from warden.adapters.base import Adapter, ProcessStatus
from warden.config import ProjectConfig
from warden.logbuffer import RingBuffer
from warden.vitals import VitalsSampler


class ProcessAdapter(Adapter):
    def __init__(self, config: ProjectConfig):
        if config.start is None:
            raise ValueError(f"projeto {config.id!r} sem [start] configurado")
        self.config = config
        self._process: subprocess.Popen | None = None
        self._master_fd: int | None = None
        self._logs = RingBuffer()
        self._started_at: float | None = None
        self._stop_requested = False
        self._on_exit: Callable[[int], None] | None = None
        self._vitals = VitalsSampler()

    def set_on_exit(self, callback: Callable[[int], None]) -> None:
        self._on_exit = callback

    def start(self) -> None:
        if self._process is not None and self._process.poll() is None:
            return
        start = self.config.start
        capture = start.capture_stdout
        self._stop_requested = False

        slave_fd: int | None = None
        if capture:
            # PTY em vez de PIPE: o filho enxerga um terminal e a maioria dos
            # runtimes (python, node, php...) volta a fazer flush por linha —
            # com PIPE puro eles bufferizam em bloco e os logs só chegam no exit.
            self._master_fd, slave_fd = pty.openpty()

        self._process = subprocess.Popen(
            start.cmd,
            cwd=start.cwd or self.config.path,
            stdout=slave_fd if capture else None,
            stderr=subprocess.STDOUT if capture else None,
        )
        if capture:
            assert slave_fd is not None
            os.close(slave_fd)
        self._started_at = time.time()
        if capture:
            threading.Thread(target=self._pump_stdout, daemon=True).start()
        threading.Thread(target=self._watch_exit, args=(self._process,), daemon=True).start()

    def _pump_stdout(self) -> None:
        assert self._master_fd is not None
        buffer = b""
        while True:
            try:
                chunk = os.read(self._master_fd, 4096)
            except OSError:
                # slave fechado (processo saiu) — pty devolve EIO nesse caso, não EOF
                break
            if not chunk:
                break
            buffer += chunk
            while b"\n" in buffer:
                line, buffer = buffer.split(b"\n", 1)
                self._logs.append(line.decode(errors="replace").rstrip("\r"))
        if buffer:
            self._logs.append(buffer.decode(errors="replace").rstrip("\r"))
        os.close(self._master_fd)
        self._master_fd = None

    def _watch_exit(self, process: subprocess.Popen) -> None:
        returncode = process.wait()
        if self._on_exit is not None and not self._stop_requested:
            self._on_exit(returncode)

    def stop(self) -> None:
        if self._process is None:
            return
        self._stop_requested = True
        self._process.terminate()
        try:
            self._process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self._process.kill()

    def status(self) -> ProcessStatus:
        if self._process is None or self._process.poll() is not None:
            return ProcessStatus(running=False)
        try:
            proc = psutil.Process(self._process.pid)
        except psutil.NoSuchProcess:
            return ProcessStatus(running=False)
        ports = sorted(
            {
                conn.laddr.port
                for conn in proc.net_connections(kind="inet")
                if conn.status == psutil.CONN_LISTEN
            }
        )
        uptime = time.time() - self._started_at if self._started_at else None
        vitals = self._vitals.sample(proc.pid)
        cpu_percent, memory_mb = vitals if vitals else (None, None)
        return ProcessStatus(
            running=True,
            pid=proc.pid,
            ports=ports,
            uptime_seconds=uptime,
            cpu_percent=cpu_percent,
            memory_mb=memory_mb,
        )

    def logs(self, tail: int = 100, service: str | None = None) -> list[str]:
        return self._logs.tail(tail)
