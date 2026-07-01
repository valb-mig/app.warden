from warden.adapters.process import ProcessAdapter


class PythonAdapter(ProcessAdapter):
    """Robô/script Python. Mesmo comportamento de ProcessAdapter por enquanto;
    ponto de extensão futuro pra detecção de venv."""
