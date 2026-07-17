using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>
/// Adapter docker — shell out pra `docker compose` (não a Docker Engine API), um stack por projeto.
///
/// `ComposeServices` vazio = compose file é só desse projeto, comando afeta o arquivo inteiro
/// (comportamento antigo). Preenchido = compose file compartilhado por vários projetos (stack único
/// por workspace) — todo comando fica restrito aos serviços listados, pra não subir/derrubar/misturar
/// log dos vizinhos.
/// </summary>
public sealed class DockerAdapter : IAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ProjectConfig _config;
    private readonly string _composeFile;
    private readonly IReadOnlyList<string> _services;
    private readonly VitalsSampler _vitals = new();

    public DockerAdapter(ProjectConfig config)
    {
        _config = config;
        _composeFile = config.ComposeFile ?? "docker-compose.yml";
        _services = config.ComposeServices;
    }

    public void Start() => Compose(["up", "-d", .._services]);

    public void Stop() => Compose(["stop", .._services]);

    public ProcessStatus Status()
    {
        var (stdout, _, _) = Compose("ps", "--format", "json");
        var containers = ParsePsJson(stdout);
        if (_services.Count > 0)
        {
            containers = containers.Where(c => c.Service is not null && _services.Contains(c.Service)).ToList();
        }

        var running = containers.Where(c => c.State == "running").ToList();
        if (running.Count == 0) return new ProcessStatus { Running = false };

        var pid = running[0].Id is { } id ? ContainerPid(id) : null;
        var ports = running
            .SelectMany(c => c.Publishers ?? [])
            .Where(p => p.PublishedPort is not null)
            .Select(p => p.PublishedPort!.Value)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        (double? cpuPercent, double? memoryMb) vitals = pid is { } p ? _vitals.Sample(p) : (null, null);

        return new ProcessStatus
        {
            Running = true,
            Pid = pid,
            Ports = ports,
            CpuPercent = vitals.cpuPercent,
            MemoryMb = vitals.memoryMb,
        };
    }

    public IReadOnlyList<string> Logs(int tail = 100, string? service = null)
    {
        var args = new List<string> { "logs", "--no-color", "--tail", tail.ToString() };
        if (service is not null) args.Add(service);
        else if (_services.Count > 0) args.AddRange(_services);

        var (stdout, stderr, _) = Compose(args.ToArray());
        return (stdout + stderr)
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    public IReadOnlyList<string> Services()
    {
        if (_services.Count > 0) return _services;
        var (stdout, _, _) = Compose("config", "--services");
        return stdout.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }

    private int? ContainerPid(string containerId)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("inspect");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("{{.State.Pid}}");
        psi.ArgumentList.Add(containerId);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return int.TryParse(output, out var pid) && pid != 0 ? pid : null;
    }

    private (string Stdout, string Stderr, int ExitCode) Compose(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = _config.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("compose");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(_composeFile);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (stdout, stderr, process.ExitCode);
    }

    private static List<ComposeContainer> ParsePsJson(string output)
    {
        output = output.Trim();
        if (output.Length == 0) return [];

        try
        {
            if (output.StartsWith('['))
            {
                return JsonSerializer.Deserialize<List<ComposeContainer>>(output, JsonOptions) ?? [];
            }
            return [JsonSerializer.Deserialize<ComposeContainer>(output, JsonOptions)!];
        }
        catch (JsonException)
        {
            // `docker compose ps --format json` às vezes devolve um array, às vezes NDJSON
            // (um objeto por linha) dependendo da versão instalada — mesma ambiguidade que o
            // adapter Python já tratava.
            return output.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonSerializer.Deserialize<ComposeContainer>(l, JsonOptions)!)
                .ToList();
        }
    }
}

internal sealed class ComposeContainer
{
    [JsonPropertyName("ID")]
    public string? Id { get; set; }
    public string? Service { get; set; }
    public string? State { get; set; }
    public List<ComposePublisher>? Publishers { get; set; }
}

internal sealed class ComposePublisher
{
    public int? PublishedPort { get; set; }
}
