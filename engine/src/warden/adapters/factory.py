"""Detecção de adapter pelo campo `type` do config."""

from warden.adapters.base import Adapter
from warden.adapters.docker_adapter import DockerAdapter
from warden.adapters.just_adapter import JustAdapter
from warden.adapters.node_adapter import NodeAdapter
from warden.adapters.php_adapter import PhpAdapter
from warden.adapters.python_adapter import PythonAdapter
from warden.adapters.raw_adapter import RawAdapter
from warden.config import ProjectConfig

_ADAPTERS: dict[str, type[Adapter]] = {
    "python": PythonAdapter,
    "raw": RawAdapter,
    "docker": DockerAdapter,
    "node": NodeAdapter,
    "php": PhpAdapter,
    "just": JustAdapter,
}


def create_adapter(config: ProjectConfig) -> Adapter:
    try:
        adapter_cls = _ADAPTERS[config.type]
    except KeyError:
        raise NotImplementedError(
            f"adapter para type={config.type!r} ainda não implementado (fase futura)"
        ) from None
    return adapter_cls(config)
