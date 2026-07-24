using System.Collections.Concurrent;
using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>
/// Gerencia o ciclo de vida dos adapters por projeto — criação lazy, remoção no reload. Separado
/// do Engine para isolar a complexidade de "qual adapter está vivo" do resto da orquestração.
/// </summary>
public sealed class AdapterPool
{
    private readonly Registry _registry;
    private readonly ConcurrentDictionary<string, IAdapter> _adapters = new();
    private Action<string, int>? _onExit;

    public AdapterPool(Registry registry)
    {
        _registry = registry;
    }

    /// <summary>Callback invocado quando um processo termina — normalmente publica evento no Bus.</summary>
    public void SetOnExit(Action<string, int> onExit) => _onExit = onExit;

    public IAdapter Get(string projectId) =>
        _adapters.GetOrAdd(projectId, id =>
        {
            var adapter = AdapterFactory.Create(_registry.Get(id));
            adapter.SetOnExit(returnCode => _onExit?.Invoke(id, returnCode));
            return adapter;
        });

    /// <summary>
    /// Remove do cache apenas adapters parados. Adapters rodando são preservados para que o processo
    /// em execução não fique órfão (ver comentário em Engine.ReloadRegistry).
    /// </summary>
    public void PruneStopped()
    {
        foreach (var projectId in _adapters.Keys.ToList())
        {
            if (_adapters.TryGetValue(projectId, out var adapter) && !adapter.Status().Running)
                _adapters.TryRemove(projectId, out _);
        }
    }
}
