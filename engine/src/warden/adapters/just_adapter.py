from warden.adapters.process import ProcessAdapter


class JustAdapter(ProcessAdapter):
    """Projeto orquestrado por Justfile. Mesmo comportamento de ProcessAdapter —
    [start].cmd já vem resolvido (`just <recipe>`) pelo scaffold."""
