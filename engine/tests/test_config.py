from pathlib import Path

from warden.config import GlobalConfig, load_global_config, load_project_config

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


def test_git_watch_defaults_off(tmp_path: Path) -> None:
    toml_file = tmp_path / "leadmaster.toml"
    toml_file.write_text(DOCKER_TOML)

    project = load_project_config(toml_file)

    assert project.git.watch is False
    assert project.git.interval == 300
    assert project.git.remote == "origin"
    assert project.notify.on_git_behind is False


def test_git_watch_configured(tmp_path: Path) -> None:
    toml_file = tmp_path / "leadmaster.toml"
    toml_file.write_text(
        DOCKER_TOML + "\n[git]\nwatch = true\ninterval = 60\nremote = \"upstream\"\n"
    )

    project = load_project_config(toml_file)

    assert project.git.watch is True
    assert project.git.interval == 60
    assert project.git.remote == "upstream"
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


def test_load_global_config_creates_default_file_when_missing(tmp_path: Path) -> None:
    config_path = tmp_path / "config.toml"

    config = load_global_config(config_path)

    assert config == GlobalConfig()
    assert config_path.exists()
    assert "api_port" in config_path.read_text()

    # segunda leitura carrega o arquivo já criado, não sobrescreve
    reloaded = load_global_config(config_path)
    assert reloaded == GlobalConfig()


def test_load_global_config_reads_existing_file(tmp_path: Path) -> None:
    config_path = tmp_path / "config.toml"
    config_path.write_text('api_port = 9000\nnotify_channel = "ntfy"\nntfy_topic = "alerts"\n')

    config = load_global_config(config_path)

    assert config.api_port == 9000
    assert config.notify_channel == "ntfy"
    assert config.ntfy_topic == "alerts"
