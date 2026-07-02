from warden.adapters.process import ProcessAdapter


class PhpAdapter(ProcessAdapter):
    """App PHP sem docker. Mesmo comportamento de ProcessAdapter; ponto de
    extensão futuro pra detecção de erro via log_sources (laravel.log)."""
