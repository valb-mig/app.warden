from fastapi import Header, HTTPException, Request, status

from warden.engine import Engine


def get_engine(request: Request) -> Engine:
    return request.app.state.engine


def verify_token(request: Request, authorization: str | None = Header(default=None)) -> None:
    expected = f"Bearer {request.app.state.api_token}"
    if authorization != expected:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="token inválido")
