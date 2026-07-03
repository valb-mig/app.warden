"""CLI de debug do motor — usa antes de API/front existirem (fase 2)."""

import shlex
from pathlib import Path

import typer

from warden.config import ProjectConfig, load_global_config
from warden.engine import Engine
from warden.notifier import create_notifier
from warden.registry import PROJECTS_DIRNAME
from warden.scaffold import build_config, render_toml
from warden.store import EventStore

app = typer.Typer(help="Warden — CLI de debug do motor")

DEFAULT_CONFIG_DIR = Path.home() / ".warden"
DEFAULT_DB_PATH = DEFAULT_CONFIG_DIR / "warden.db"

BANNER = [
    "░█░█░█▀█░█▀▄░█▀▄░█▀▀░█▀█",
    "░█▄█░█▀█░█▀▄░█░█░█▀▀░█░█",
    "░▀░▀░▀░▀░▀░▀░▀▀░░▀▀▀░▀░▀",
]


def _print_banner() -> None:
    for line in BANNER:
        typer.secho(line, fg=typer.colors.BRIGHT_GREEN, bold=True)
    typer.secho("─" * 24, fg=typer.colors.GREEN)


def _engine() -> Engine:
    store = EventStore(DEFAULT_DB_PATH)
    global_config = load_global_config(DEFAULT_CONFIG_DIR / "config.toml")
    notifier = create_notifier(global_config)
    engine = Engine(DEFAULT_CONFIG_DIR, store=store, notifier=notifier)
    engine.boot()
    return engine


@app.command()
def start(project_id: str) -> None:
    _engine().start(project_id)
    typer.echo(f"{project_id}: start disparado")


@app.command()
def stop(project_id: str) -> None:
    _engine().stop(project_id)
    typer.echo(f"{project_id}: stop disparado")


@app.command()
def status(project_id: str) -> None:
    typer.echo(_engine().status(project_id))


@app.command()
def logs(project_id: str, tail: int = 100) -> None:
    for line in _engine().logs(project_id, tail):
        typer.echo(line)


@app.command()
def history(project_id: str, limit: int = 50) -> None:
    for event in _engine().history(project_id, limit):
        typer.echo(f"{event['created_at']}\t{event['type']}\t{event['message']}")


@app.command(name="list")
def list_projects() -> None:
    engine = _engine()
    for project in engine.registry.all():
        typer.echo(f"{project.id}\t{project.type}\t{project.display_name}")


def _label(text: str) -> str:
    return typer.style(text, fg=typer.colors.YELLOW)


def _highlight(text: str) -> str:
    return typer.style(text, fg=typer.colors.CYAN, bold=True)


def _prompt_group(config: ProjectConfig) -> None:
    group = typer.prompt(_label("Grupo (opcional)"), default=config.group or "", show_default=False)
    config.group = group.strip() or None


def _prompt_start(config: ProjectConfig) -> None:
    if config.start is None:
        return
    current = shlex.join(config.start.cmd)
    prompt = f"{_label('Start detectado:')} {_highlight(current)}\n{_label('Manter?')}"
    if typer.confirm(prompt, default=True):
        return
    edited = typer.prompt(_label("Novo comando de start"), default=current)
    config.start.cmd = shlex.split(edited)


def _prompt_log_sources(config: ProjectConfig) -> None:
    if not config.log_sources:
        return
    kept = []
    for source in config.log_sources:
        label = _highlight(f"{source.name} ({source.type})")
        if typer.confirm(_label(f"Manter log source '{label}'?"), default=True):
            kept.append(source)
    config.log_sources = kept


def _prompt_actions(config: ProjectConfig) -> None:
    if not config.actions:
        return
    kept = []
    for action in config.actions:
        label = _highlight(f"{action.name}: {shlex.join(action.cmd)}")
        if typer.confirm(_label(f"Manter action '{label}'?"), default=True):
            kept.append(action)
    config.actions = kept


@app.command()
def init(
    path: str,
    id: str | None = typer.Option(None, "--id", help="id do projeto (default: slug da pasta)"),
    yes: bool = typer.Option(False, "--yes", "-y", help="pula prompts e confirmação, grava direto"),
) -> None:
    """Detecta tipo de projeto na pasta e gera ~/.warden/<id>.toml."""
    try:
        config = build_config(path, project_id=id)
    except FileNotFoundError as exc:
        typer.secho(str(exc), fg=typer.colors.RED, err=True)
        raise typer.Exit(1) from exc

    typer.secho("tipo detectado: ", fg=typer.colors.GREEN, bold=True, nl=False)
    typer.secho(config.type, fg=typer.colors.CYAN, bold=True)

    if not yes:
        _prompt_group(config)
        _prompt_start(config)
        _prompt_log_sources(config)
        _prompt_actions(config)

    toml_text = render_toml(config)
    typer.secho("─" * 40, fg=typer.colors.BRIGHT_BLACK)
    typer.secho(toml_text, fg=typer.colors.WHITE)
    typer.secho("─" * 40, fg=typer.colors.BRIGHT_BLACK)

    target = DEFAULT_CONFIG_DIR / PROJECTS_DIRNAME / f"{config.id}.toml"
    if target.exists():
        typer.secho(
            f"aviso: {target} já existe, será sobrescrito", fg=typer.colors.YELLOW, bold=True
        )

    if not yes and not typer.confirm(_label(f"Gravar em {_highlight(str(target))}?")):
        typer.secho("cancelado", fg=typer.colors.RED)
        raise typer.Exit(0)

    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(toml_text)
    typer.secho("gravado: ", fg=typer.colors.GREEN, bold=True, nl=False)
    typer.secho(str(target), fg=typer.colors.CYAN, bold=True)


@app.command()
def serve(host: str = "127.0.0.1", port: int | None = None) -> None:
    import uvicorn

    from warden.api.app import create_app

    global_config = load_global_config(DEFAULT_CONFIG_DIR / "config.toml")
    resolved_port = port if port is not None else global_config.api_port

    _print_banner()
    typer.secho(f"  api    http://{host}:{resolved_port}", fg=typer.colors.CYAN, bold=True)
    typer.secho(f"  token  {DEFAULT_CONFIG_DIR / 'api_token'}", dim=True)
    typer.echo()

    uvicorn.run(create_app(DEFAULT_CONFIG_DIR), host=host, port=resolved_port)


def main() -> None:
    app()


if __name__ == "__main__":
    main()
