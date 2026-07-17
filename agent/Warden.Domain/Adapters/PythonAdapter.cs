using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>Robô/script Python. Mesmo comportamento de <see cref="ProcessAdapter"/> por enquanto; ponto de extensão futuro pra detecção de venv.</summary>
public sealed class PythonAdapter(ProjectConfig config) : ProcessAdapter(config);
