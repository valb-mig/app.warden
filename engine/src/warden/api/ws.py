"""WS de log ao vivo. Token via query param — WebSocket nativo do browser não seta headers."""

import asyncio

from fastapi import APIRouter, Query, WebSocket, WebSocketDisconnect

router = APIRouter()

POLL_INTERVAL_SECONDS = 0.5
MAX_TAIL = 10_000


@router.websocket("/ws/projects/{project_id}/logs")
async def stream_logs(
    websocket: WebSocket,
    project_id: str,
    token: str = Query(default=""),
    service: str | None = Query(default=None),
) -> None:
    if token != websocket.app.state.api_token:
        await websocket.close(code=4401)
        return

    engine = websocket.app.state.engine
    await websocket.accept()

    sent = 0
    try:
        while True:
            lines = engine.logs(project_id, tail=MAX_TAIL, service=service)
            for line in lines[sent:]:
                await websocket.send_text(line)
            sent = len(lines)
            await asyncio.sleep(POLL_INTERVAL_SECONDS)
    except WebSocketDisconnect:
        return
