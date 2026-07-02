import pytest

from warden.config import GlobalConfig, ProjectConfig
from warden.events import Event, EventType
from warden.notifier import NtfyNotifier, NullNotifier, create_notifier


def _project(**overrides) -> ProjectConfig:
    return ProjectConfig(id="p", name="LeadMaster", path="/tmp/p", type="raw", **overrides)


def test_null_notifier_is_noop() -> None:
    NullNotifier().notify(Event(project_id="p", type=EventType.ERROR), _project())


def test_create_notifier_none_channel() -> None:
    notifier = create_notifier(GlobalConfig())
    assert isinstance(notifier, NullNotifier)


def test_create_notifier_ntfy_requires_topic() -> None:
    with pytest.raises(ValueError, match="ntfy_topic"):
        create_notifier(GlobalConfig(notify_channel="ntfy"))


def test_create_notifier_ntfy_builds_instance() -> None:
    notifier = create_notifier(
        GlobalConfig(notify_channel="ntfy", ntfy_topic="warden-alerts")
    )
    assert isinstance(notifier, NtfyNotifier)
    assert notifier.topic == "warden-alerts"
    assert notifier.server == "https://ntfy.sh"


def test_ntfy_notifier_posts_title_and_body(monkeypatch) -> None:
    captured = {}

    def fake_urlopen(request, timeout=5):
        captured["url"] = request.full_url
        captured["headers"] = dict(request.header_items())
        captured["data"] = request.data
        captured["method"] = request.get_method()

        class _Resp:
            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        return _Resp()

    monkeypatch.setattr("warden.notifier.urlopen", fake_urlopen)

    notifier = NtfyNotifier("warden-alerts", server="https://ntfy.example.com")
    event = Event(project_id="p", type=EventType.ERROR, message="exit=1")
    notifier.notify(event, _project())

    assert captured["url"] == "https://ntfy.example.com/warden-alerts"
    assert captured["method"] == "POST"
    assert captured["data"] == b"exit=1"
    assert "LeadMaster" in captured["headers"]["Title"]


def test_ntfy_notifier_swallows_network_errors(monkeypatch) -> None:
    def raise_oserror(request, timeout=5):
        raise OSError("unreachable")

    monkeypatch.setattr("warden.notifier.urlopen", raise_oserror)

    notifier = NtfyNotifier("warden-alerts")
    notifier.notify(Event(project_id="p", type=EventType.ERROR), _project())
