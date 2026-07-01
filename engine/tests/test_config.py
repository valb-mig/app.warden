from pathlib import Path

from warden.config import load_project_config

DOCKER_TOML = """
id = "leadmaster"
name = "LeadMaster"
group = "scrapers"
path = "/tmp/leadmaster"
type = "docker"
compose_file = "docker-compose.yml"

[notify]
on_error = true

[[log_sources]]
name = "app"
type = "docker"
service = "app"

[[actions]]
name = "migrate"
cmd = ["docker", "compose", "exec", "app", "php", "artisan", "migrate", "--force"]
"""

PYTHON_TOML = """
id = "caffeshop-bot"
type = "python"
path = "/tmp/caffeshop"

[start]
cmd = ["python", "main.py"]
capture_stdout = true

[[log_sources]]
name = "stdout"
type = "stdout"
"""


def test_load_docker_project(tmp_path: Path) -> None:
    toml_file = tmp_path / "leadmaster.toml"
    toml_file.write_text(DOCKER_TOML)

    project = load_project_config(toml_file)

    assert project.id == "leadmaster"
    assert project.display_name == "LeadMaster"
    assert project.type == "docker"
    assert project.notify.on_error is True
    assert project.log_sources[0].service == "app"
    assert project.actions[0].name == "migrate"


def test_load_python_project_defaults(tmp_path: Path) -> None:
    toml_file = tmp_path / "caffeshop-bot.toml"
    toml_file.write_text(PYTHON_TOML)

    project = load_project_config(toml_file)

    assert project.display_name == "caffeshop-bot"  # sem `name`, cai pro id
    assert project.start is not None
    assert project.start.cmd == ["python", "main.py"]
    assert project.notify.on_error is False  # default
