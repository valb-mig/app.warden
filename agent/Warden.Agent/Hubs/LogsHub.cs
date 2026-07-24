using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Warden.Agent.Auth;
using Warden.Domain;

namespace Warden.Agent.Hubs;

/// <summary>
/// Hub de log ao vivo — substitui o WebSocket cru do engine Python (ver NEW_CONTEXT.md §10.1). Auth
/// via `access_token` na query string: o SignalR client oficial já suporta isso nativamente
/// (`HttpConnectionOptions.AccessTokenProvider`), e é a mesma solução que o WS puro do Python usava
/// pra contornar "browser não deixa setar header custom em WebSocket".
///
/// Para adapters baseados em processo (ProcessAdapter e subclasses), o streaming é real via
/// Channel&lt;string&gt; — cada linha chega ao Hub imediatamente sem polling. Para adapters sem broadcaster
/// (ex: Docker), mantém o fallback de polling a cada 500ms.
/// </summary>
public sealed class LogsHub(Engine engine, ChildProcessRegistry childProcesses, ApiTokenProvider tokenProvider) : Hub
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Streams = new();
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int MaxTail = 10_000;

    public override Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
        if (token != tokenProvider.Token)
        {
            Context.Abort();
            return Task.CompletedTask;
        }
        return base.OnConnectedAsync();
    }

    public void Subscribe(string projectId, string? service)
    {
        var cts = new CancellationTokenSource();
        Streams[Context.ConnectionId] = cts;

        var reader = engine.SubscribeLogs(projectId, cts.Token);
        if (reader is not null)
        {
            _ = StreamAsync(Context.ConnectionId, Clients.Caller, projectId, service, reader, cts.Token);
        }
        else
        {
            _ = PollAsync(Context.ConnectionId, Clients.Caller, projectId, service, cts.Token);
        }
    }

    /// <summary>Streaming real: envia o histórico inicial e depois cada linha nova via Channel.</summary>
    private async Task StreamAsync(
        string connectionId, ISingleClientProxy caller, string projectId, string? service,
        System.Threading.Channels.ChannelReader<string> reader, CancellationToken token)
    {
        try
        {
            // Histórico já capturado antes da subscrição
            var history = engine.Logs(projectId, tail: MaxTail, service);
            if (history.Count > 0)
                await caller.SendAsync("LogLines", history, token);

            // Linhas novas chegam pelo Channel
            await foreach (var line in reader.ReadAllAsync(token))
            {
                await caller.SendAsync("LogLines", new[] { line }, token);
            }
        }
        catch (OperationCanceledException)
        {
            // subscrição cancelada no disconnect — encerramento normal
        }
    }

    /// <summary>Fallback de polling para adapters sem broadcaster (ex: Docker).</summary>
    private async Task PollAsync(
        string connectionId, ISingleClientProxy caller, string projectId, string? service, CancellationToken token)
    {
        var sent = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var lines = engine.Logs(projectId, tail: MaxTail, service: service);
                if (lines.Count > sent)
                {
                    await caller.SendAsync("LogLines", lines.Skip(sent).ToList(), token);
                    sent = lines.Count;
                }
                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // subscrição cancelada no disconnect — encerramento normal
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Streams.TryRemove(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        childProcesses.KillAll(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
