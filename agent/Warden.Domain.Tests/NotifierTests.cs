using Warden.Domain.Config;
using Warden.Domain.Events;
using Warden.Domain.Notify;
using Warden.Domain.Tests.TestSupport;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Mirror de `engine/tests/test_notifier.py` — em vez de `monkeypatch` (Python), usa um
/// <see cref="CapturingHttpServer"/> local real capturando a requisição de verdade.
/// </summary>
public sealed class NotifierTests
{
    private static ProjectConfig Project(string name = "LeadMaster") => new()
    {
        Id = "p",
        Name = name,
        Path = "/tmp/p",
        Type = "raw",
    };

    [Fact]
    public void NullNotifierIsNoop() =>
        new NullNotifier().Notify(new Event("p", EventType.Error), Project());

    [Fact]
    public void CreateNotifierNoneChannel() =>
        Assert.IsType<NullNotifier>(NotifierFactory.Create(new GlobalConfig()));

    [Fact]
    public void CreateNotifierNtfyRequiresTopic()
    {
        var config = new GlobalConfig { NotifyChannel = "ntfy" };
        var ex = Assert.Throws<InvalidOperationException>(() => NotifierFactory.Create(config));
        Assert.Contains("ntfy_topic", ex.Message);
    }

    [Fact]
    public void CreateNotifierNtfyBuildsInstance()
    {
        var config = new GlobalConfig { NotifyChannel = "ntfy", NtfyTopic = "warden-alerts" };
        var notifier = Assert.IsType<NtfyNotifier>(NotifierFactory.Create(config));
        Assert.Equal("warden-alerts", notifier.Topic);
        Assert.Equal("https://ntfy.sh", notifier.Server);
    }

    [Fact]
    public void NtfyNotifierPostsTitleAndBody()
    {
        using var server = new CapturingHttpServer();
        var notifier = new NtfyNotifier("warden-alerts", server.BaseUrl);

        notifier.Notify(new Event("p", EventType.Error, "exit=1"), Project());

        var request = server.WaitForRequest();
        Assert.Equal("/warden-alerts", request.Path);
        Assert.Equal("POST", request.Method);
        Assert.Equal("exit=1", request.Body);
        Assert.Contains("LeadMaster", request.Headers["Title"]);
    }

    [Fact]
    public void NtfyNotifierSwallowsNetworkErrors()
    {
        // porta 1 (reservada, ninguém escuta) — a chamada não deve lançar mesmo sem servidor.
        var notifier = new NtfyNotifier("warden-alerts", "http://127.0.0.1:1");
        notifier.Notify(new Event("p", EventType.Error), Project());
    }
}
