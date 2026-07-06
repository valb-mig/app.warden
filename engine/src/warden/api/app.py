from pathlib import Path

from fastapi import Depends, FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from warden.api.deps import verify_token
from warden.api.discovery_routes import router as discovery_router
from warden.api.routes import router
from warden.api.system_routes import router as system_router
from warden.api.ws import router as ws_router
from warden.auth import load_or_create_token
from warden.config import load_global_config
from warden.engine import Engine
from warden.notifier import create_notifier
from warden.store import EventStore


def create_app(config_dir: Path) -> FastAPI:
    app = FastAPI(title="Warden API")

    store = EventStore(config_dir / "warden.db")
    global_config = load_global_config(config_dir / "config.toml")
    notifier = create_notifier(global_config)
    engine = Engine(config_dir, store=store, notifier=notifier)
    engine.boot()

    app.state.engine = engine
    app.state.api_token = load_or_create_token(config_dir / "api_token")

    # Auth é bearer token manual (não cookie), então CORS aberto não abre brecha real:
    # um site de terceiro não tem como adivinhar o token pra montar o header Authorization.
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_methods=["*"],
        allow_headers=["*"],
    )

    app.include_router(router, dependencies=[Depends(verify_token)])
    app.include_router(discovery_router, dependencies=[Depends(verify_token)])
    app.include_router(system_router, dependencies=[Depends(verify_token)])
    app.include_router(ws_router)

    @app.exception_handler(ValueError)
    @app.exception_handler(NotImplementedError)
    async def config_error_handler(request: Request, exc: Exception) -> JSONResponse:
        # config de projeto invalida (ex: sem [start], adapter não implementado) —
        # erro de usuário, não bug do servidor: 422 com mensagem, sem traceback.
        return JSONResponse(status_code=422, content={"detail": str(exc)})

    return app
