using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>Fallback: comando arbitrário, cobre projeto sem adapter dedicado.</summary>
public sealed class RawAdapter(ProjectConfig config) : ProcessAdapter(config);
