from pathlib import Path

from fastapi import Depends, FastAPI
from fastapi.middleware.cors import CORSMiddleware

from warden.api.deps import verify_token
from warden.api.routes import router
from warden.api.ws import router as ws_router
from warden.auth import load_or_create_token
from warden.engine import Engine
from warden.store import EventStore


def create_app(config_dir: Path) -> FastAPI:
    app = FastAPI(title="Warden API")

    store = EventStore(config_dir / "warden.db")
    engine = Engine(config_dir, store=store)
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
    app.include_router(ws_router)

    return app
