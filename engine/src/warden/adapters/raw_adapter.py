from warden.adapters.process import ProcessAdapter


class RawAdapter(ProcessAdapter):
    """Fallback: comando arbitrário, cobre projeto sem adapter dedicado."""
