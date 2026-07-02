from warden.adapters.process import ProcessAdapter


class NodeAdapter(ProcessAdapter):
    """App/script Node. Mesmo comportamento de ProcessAdapter — [start].cmd
    já vem resolvido (npm/pnpm/yarn run <script>) pelo scaffold."""
