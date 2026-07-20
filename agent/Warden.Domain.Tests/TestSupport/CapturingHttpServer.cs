using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;

namespace Warden.Domain.Tests.TestSupport;

public sealed record CapturedRequest(string Path, string Method, string Body, NameValueCollection Headers);

/// <summary>
/// Servidor HTTP local real (não mock) pra capturar a requisição que o `NtfyNotifier` manda — mesmo
/// papel do `monkeypatch` do `test_notifier.py`, só que exercitando uma requisição de verdade pela
/// stack de rede em vez de substituir a função de baixo nível.
/// </summary>
public sealed class CapturingHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly BlockingCollection<CapturedRequest> _requests = new();

    public string BaseUrl { get; }

    public CapturingHttpServer()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{BaseUrl}/");
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    private static int GetFreePort()
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                _requests.Add(new CapturedRequest(context.Request.Url!.AbsolutePath, context.Request.HttpMethod, body, context.Request.Headers));
                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }
        catch (HttpListenerException)
        {
            // listener fechado no Dispose — encerra o loop
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public CapturedRequest WaitForRequest(TimeSpan? timeout = null) =>
        _requests.TryTake(out var request, (int)(timeout ?? TimeSpan.FromSeconds(5)).TotalMilliseconds)
            ? request
            : throw new TimeoutException("nenhuma requisição recebida a tempo");

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        _requests.Dispose();
    }
}
