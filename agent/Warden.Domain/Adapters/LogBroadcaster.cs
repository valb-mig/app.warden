using System.Threading.Channels;

namespace Warden.Domain.Adapters;

/// <summary>
/// Distribui cada linha de log para todos os subscribers ativos. Cada subscriber recebe um
/// <see cref="ChannelReader{T}"/> independente — múltiplas conexões SignalR recebem as mesmas linhas
/// sem polling. O Channel é bounded (descarta as mais antigas em overflow) para não acumular memória
/// quando um subscriber lento não drena a fila.
/// </summary>
public sealed class LogBroadcaster
{
    private readonly object _gate = new();
    private readonly List<Channel<string>> _subscribers = [];

    public void Write(string line)
    {
        lock (_gate)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(line);
        }
    }

    /// <summary>
    /// Retorna um reader que recebe todas as linhas futuras. Quando o <paramref name="cancellation"/>
    /// é cancelado (desconexão do Hub), o subscriber é removido automaticamente.
    /// </summary>
    public ChannelReader<string> Subscribe(CancellationToken cancellation)
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        });

        lock (_gate) _subscribers.Add(ch);

        cancellation.Register(() =>
        {
            lock (_gate) _subscribers.Remove(ch);
            ch.Writer.TryComplete();
        });

        return ch.Reader;
    }

    /// <summary>Fecha todos os subscribers — chamado quando o processo termina.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryComplete();
        }
    }
}
