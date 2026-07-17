using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>App/script Node. Mesmo comportamento de <see cref="ProcessAdapter"/> — [start].cmd já vem resolvido (npm/pnpm/yarn run &lt;script&gt;) pelo scaffold.</summary>
public sealed class NodeAdapter(ProjectConfig config) : ProcessAdapter(config);
