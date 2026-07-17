using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>Projeto orquestrado por Justfile. Mesmo comportamento de <see cref="ProcessAdapter"/> — [start].cmd já vem resolvido (`just &lt;recipe&gt;`) pelo scaffold.</summary>
public sealed class JustAdapter(ProjectConfig config) : ProcessAdapter(config);
