using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>App PHP sem docker. Mesmo comportamento de <see cref="ProcessAdapter"/>; ponto de extensão futuro pra detecção de erro via log_sources (laravel.log).</summary>
public sealed class PhpAdapter(ProjectConfig config) : ProcessAdapter(config);
