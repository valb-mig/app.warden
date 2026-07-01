import sys
import time

from warden.adapters.raw_adapter import RawAdapter
from warden.config import ProjectConfig, StartConfig


def _wait_until(predicate, timeout: float = 3.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError("condição não satisfeita dentro do timeout")


def _project(tmp_path, cmd: list[str]) -> ProjectConfig:
    return ProjectConfig(
        id="fake",
        path=str(tmp_path),
        type="raw",
        start=StartConfig(cmd=cmd, capture_stdout=True),
    )


def test_start_captures_stdout_and_reports_running(tmp_path) -> None:
    script = "import sys, time; print('hello'); sys.stdout.flush(); time.sleep(2)"
    adapter = RawAdapter(_project(tmp_path, [sys.executable, "-u", "-c", script]))

    adapter.start()
    try:
        _wait_until(lambda: "hello" in adapter.logs())
        status = adapter.status()
        assert status.running is True
        assert status.pid is not None
    finally:
        adapter.stop()

    _wait_until(lambda: adapter.status().running is False)


def test_stop_terminates_process(tmp_path) -> None:
    adapter = RawAdapter(_project(tmp_path, [sys.executable, "-c", "import time; time.sleep(30)"]))

    adapter.start()
    _wait_until(lambda: adapter.status().running is True)

    adapter.stop()

    _wait_until(lambda: adapter.status().running is False)


def test_status_before_start_is_not_running(tmp_path) -> None:
    adapter = RawAdapter(_project(tmp_path, [sys.executable, "-c", "pass"]))
    assert adapter.status().running is False
