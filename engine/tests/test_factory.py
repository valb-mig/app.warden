import pytest

from warden.adapters.docker_adapter import DockerAdapter
from warden.adapters.factory import create_adapter
from warden.adapters.just_adapter import JustAdapter
from warden.adapters.node_adapter import NodeAdapter
from warden.adapters.php_adapter import PhpAdapter
from warden.adapters.python_adapter import PythonAdapter
from warden.adapters.raw_adapter import RawAdapter
from warden.config import ProjectConfig, StartConfig

_OWNED_TYPES = [
    ("python", PythonAdapter),
    ("raw", RawAdapter),
    ("node", NodeAdapter),
    ("php", PhpAdapter),
    ("just", JustAdapter),
]


@pytest.mark.parametrize(("project_type", "adapter_cls"), _OWNED_TYPES)
def test_create_adapter_for_owned_types(project_type, adapter_cls) -> None:
    config = ProjectConfig(
        id="p",
        path="/tmp/p",
        type=project_type,
        start=StartConfig(cmd=["true"]),
    )
    adapter = create_adapter(config)
    assert isinstance(adapter, adapter_cls)


def test_create_adapter_for_docker() -> None:
    config = ProjectConfig(id="p", path="/tmp/p", type="docker", compose_file="docker-compose.yml")
    adapter = create_adapter(config)
    assert isinstance(adapter, DockerAdapter)
