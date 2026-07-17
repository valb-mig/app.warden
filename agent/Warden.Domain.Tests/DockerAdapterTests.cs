using System.Diagnostics;
using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class DockerAdapterTests : IDisposable
{
    private const int Port = 18199;

    private const string ComposeYamlTemplate = """
        services:
          app:
            image: busybox:stable
            command: ["sh", "-c", "i=0; while true; do echo tick $i; i=$((i+1)); sleep 1; done"]
            ports:
              - "{PORT}:80"
          worker:
            image: busybox:stable
            command: ["sh", "-c", "i=0; while true; do echo worker-tick $i; i=$((i+1)); sleep 1; done"]
        """;

    private readonly string _tmpDir = Directory.CreateTempSubdirectory().FullName;
    private readonly DockerAdapter _adapter;

    public DockerAdapterTests()
    {
        File.WriteAllText(
            Path.Combine(_tmpDir, "docker-compose.yml"),
            ComposeYamlTemplate.Replace("{PORT}", Port.ToString()));

        var config = new ProjectConfig { Id = "docker-demo", Path = _tmpDir, Type = "docker" };
        _adapter = new DockerAdapter(config);
    }

    public void Dispose()
    {
        RunDockerComposeDown();
    }

    private void RunDockerComposeDown()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                WorkingDirectory = _tmpDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("compose");
            psi.ArgumentList.Add("down");
            using var process = Process.Start(psi)!;
            process.WaitForExit();
        }
        catch (Exception)
        {
            // best-effort cleanup
        }
    }

    private static bool DockerAvailable() => IsOnPath("docker");

    private static bool IsOnPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        return pathEnv.Split(Path.PathSeparator).Any(dir => File.Exists(Path.Combine(dir, command)));
    }

    private static void WaitUntil(Func<bool> predicate, double timeoutSeconds = 30.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            Thread.Sleep(300);
        }
        Assert.Fail("condição não satisfeita dentro do timeout");
    }

    [SkippableFact]
    public void StatusBeforeStartIsNotRunning()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        Assert.False(_adapter.Status().Running);
    }

    [SkippableFact]
    public void StartReportsRunningPidAndPort()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        _adapter.Start();
        WaitUntil(() => _adapter.Status().Running);

        var status = _adapter.Status();
        Assert.NotNull(status.Pid);
        Assert.True(status.Pid > 0);
        Assert.Contains(Port, status.Ports);
    }

    [SkippableFact]
    public void LogsCaptureContainerOutput()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        _adapter.Start();
        WaitUntil(() => _adapter.Logs().Any(line => line.Contains("tick")));
    }

    [SkippableFact]
    public void StopMarksNotRunning()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        _adapter.Start();
        WaitUntil(() => _adapter.Status().Running);

        _adapter.Stop();

        WaitUntil(() => !_adapter.Status().Running);
    }

    [SkippableFact]
    public void ServicesListsAllComposeServices()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        Assert.Equal(new HashSet<string> { "app", "worker" }, _adapter.Services().ToHashSet());
    }

    [SkippableFact]
    public void LogsFilteredByServiceExcludesOtherServices()
    {
        Skip.IfNot(DockerAvailable(), "requer docker instalado");

        _adapter.Start();
        WaitUntil(() => _adapter.Logs().Any(line => line.Contains("worker-tick")));

        var appLogs = _adapter.Logs(service: "app");
        Assert.Contains(appLogs, line => line.Contains("tick"));
        Assert.DoesNotContain(appLogs, line => line.Contains("worker-tick"));

        var workerLogs = _adapter.Logs(service: "worker");
        Assert.Contains(workerLogs, line => line.Contains("worker-tick"));
    }
}
