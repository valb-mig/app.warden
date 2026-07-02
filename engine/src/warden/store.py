"""SQLite — histórico de eventos estruturados. Log bruto não entra aqui (fica no ring buffer)."""

import json
import sqlite3
from pathlib import Path

from warden.events import Event


class EventStore:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(db_path, check_same_thread=False)
        self._init_schema()

    def _init_schema(self) -> None:
        self._conn.execute(
            """
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                type TEXT NOT NULL,
                message TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            )
            """
        )
        self._conn.execute(
            """
            CREATE TABLE IF NOT EXISTS action_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                action_name TEXT NOT NULL,
                cmd TEXT NOT NULL,
                confirmed INTEGER NOT NULL,
                exit_code INTEGER NOT NULL,
                created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            )
            """
        )
        self._conn.commit()

    def record(self, event: Event) -> None:
        self._conn.execute(
            "INSERT INTO events (project_id, type, message) VALUES (?, ?, ?)",
            (event.project_id, event.type.value, event.message),
        )
        self._conn.commit()

    def history(self, project_id: str, limit: int = 50) -> list[dict]:
        cursor = self._conn.execute(
            "SELECT project_id, type, message, created_at FROM events "
            "WHERE project_id = ? ORDER BY id DESC LIMIT ?",
            (project_id, limit),
        )
        columns = [c[0] for c in cursor.description]
        return [dict(zip(columns, row, strict=True)) for row in cursor.fetchall()]

    def record_action(
        self, project_id: str, action_name: str, cmd: list[str], confirmed: bool, exit_code: int
    ) -> None:
        self._conn.execute(
            "INSERT INTO action_audit (project_id, action_name, cmd, confirmed, exit_code) "
            "VALUES (?, ?, ?, ?, ?)",
            (project_id, action_name, json.dumps(cmd), int(confirmed), exit_code),
        )
        self._conn.commit()

    def action_audit(self, project_id: str, limit: int = 50) -> list[dict]:
        cursor = self._conn.execute(
            "SELECT project_id, action_name, cmd, confirmed, exit_code, created_at "
            "FROM action_audit WHERE project_id = ? ORDER BY id DESC LIMIT ?",
            (project_id, limit),
        )
        columns = [c[0] for c in cursor.description]
        rows = [dict(zip(columns, row, strict=True)) for row in cursor.fetchall()]
        for row in rows:
            row["cmd"] = json.loads(row["cmd"])
            row["confirmed"] = bool(row["confirmed"])
        return rows

    def close(self) -> None:
        self._conn.close()
