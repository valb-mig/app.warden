"""SQLite — histórico de eventos estruturados. Log bruto não entra aqui (fica no ring buffer)."""

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

    def close(self) -> None:
        self._conn.close()
