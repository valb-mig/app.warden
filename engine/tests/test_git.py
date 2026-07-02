import subprocess
from pathlib import Path

import pytest

from warden.git import git_command, git_info, is_git_repo


def _git(path: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(path), *args], check=True, capture_output=True, text=True)


def _init_repo(path: Path) -> None:
    _git(path, "init", "-b", "main")
    _git(path, "config", "user.email", "test@warden.local")
    _git(path, "config", "user.name", "Test")


def _commit(path: Path, filename: str, message: str) -> None:
    (path / filename).write_text("x")
    _git(path, "add", filename)
    _git(path, "commit", "-m", message)


def test_not_a_repo_returns_none(tmp_path: Path) -> None:
    assert is_git_repo(tmp_path) is False
    assert git_info(tmp_path) is None


def test_clean_repo_with_commit(tmp_path: Path) -> None:
    _init_repo(tmp_path)
    _commit(tmp_path, "a.txt", "primeiro commit")

    info = git_info(tmp_path)

    assert info is not None
    assert info.branch == "main"
    assert info.dirty is False
    assert info.dirty_count == 0
    assert info.has_remote is False
    assert info.last_commit is not None
    assert info.last_commit.subject == "primeiro commit"
    assert info.last_commit.author == "Test"
    assert info.last_commit.hash  # não-vazio


def test_dirty_tree_counts_changes(tmp_path: Path) -> None:
    _init_repo(tmp_path)
    _commit(tmp_path, "a.txt", "commit")
    (tmp_path / "b.txt").write_text("novo")  # untracked
    (tmp_path / "a.txt").write_text("mudou")  # modified

    info = git_info(tmp_path)

    assert info is not None
    assert info.dirty is True
    assert info.dirty_count == 2


def test_repo_without_commits_has_no_last_commit(tmp_path: Path) -> None:
    _init_repo(tmp_path)

    info = git_info(tmp_path)

    assert info is not None
    assert info.last_commit is None
    assert info.ahead is None
    assert info.behind is None


def test_ahead_behind_against_upstream(tmp_path: Path) -> None:
    # remote bare + clone → mexe no clone pra gerar ahead/behind reais.
    remote = tmp_path / "remote.git"
    remote.mkdir()
    _git(remote, "init", "--bare", "-b", "main")

    work = tmp_path / "work"
    work.mkdir()
    _init_repo(work)
    _commit(work, "a.txt", "base")
    _git(work, "remote", "add", "origin", str(remote))
    _git(work, "push", "-u", "origin", "main")

    # 2 commits locais não empurrados → ahead=2, behind=0
    _commit(work, "b.txt", "local 1")
    _commit(work, "c.txt", "local 2")

    info = git_info(work)

    assert info is not None
    assert info.has_remote is True
    assert info.ahead == 2
    assert info.behind == 0


def _clone_pair(tmp_path: Path) -> tuple[Path, Path]:
    """Retorna (upstream_work, downstream_work) apontando pro mesmo remote bare."""
    remote = tmp_path / "remote.git"
    remote.mkdir()
    _git(remote, "init", "--bare", "-b", "main")

    up = tmp_path / "up"
    up.mkdir()
    _init_repo(up)
    _commit(up, "a.txt", "base")
    _git(up, "remote", "add", "origin", str(remote))
    _git(up, "push", "-u", "origin", "main")

    down = tmp_path / "down"
    subprocess.run(["git", "clone", str(remote), str(down)], check=True, capture_output=True)
    _git(down, "config", "user.email", "down@warden.local")
    _git(down, "config", "user.name", "Down")
    return up, down


def test_command_unknown_verb_raises(tmp_path: Path) -> None:
    _init_repo(tmp_path)
    with pytest.raises(ValueError):
        git_command(tmp_path, "clone")


def test_command_on_non_repo_is_refused(tmp_path: Path) -> None:
    res = git_command(tmp_path, "fetch")
    assert res.refused is True
    assert res.ok is False


def test_pull_refused_when_dirty(tmp_path: Path) -> None:
    up, down = _clone_pair(tmp_path)
    (down / "a.txt").write_text("mexido local")  # suja o tree

    res = git_command(down, "pull")

    assert res.refused is True
    assert "sujo" in res.output


def test_sync_fast_forwards_when_behind_and_clean(tmp_path: Path) -> None:
    up, down = _clone_pair(tmp_path)
    # upstream avança e empurra → down fica behind
    _commit(up, "b.txt", "novo no origin")
    _git(up, "push", "origin", "main")

    res = git_command(down, "sync")

    assert res.ok is True
    assert res.refused is False
    # down agora tem o commit novo
    info = git_info(down)
    assert info is not None
    assert info.behind == 0
    assert (down / "b.txt").exists()


def test_sync_noop_when_up_to_date(tmp_path: Path) -> None:
    _up, down = _clone_pair(tmp_path)

    res = git_command(down, "sync")

    assert res.ok is True
    assert res.output == "já atualizado"


def test_fetch_updates_refs_without_touching_tree(tmp_path: Path) -> None:
    up, down = _clone_pair(tmp_path)
    _commit(up, "b.txt", "novo no origin")
    _git(up, "push", "origin", "main")

    res = git_command(down, "fetch")

    assert res.ok is True
    # fetch atualiza behind mas NÃO traz o arquivo pro working tree
    info = git_info(down)
    assert info is not None
    assert info.behind == 1
    assert not (down / "b.txt").exists()
