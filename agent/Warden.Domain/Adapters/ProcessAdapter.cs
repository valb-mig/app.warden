using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Porta.Pty;
using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>
/// Adapter base pra tipos owned (não-docker): o motor é dono do processo, controla PID/start/stop.
///
/// `capture_stdout=true` sobe via PTY (Porta.Pty) — igual ao fix do Python (`pty.openpty()`): com
/// PIPE puro o processo filho bufferiza em bloco porque não enxerga um terminal, e o log só chega
/// no exit. Com PTY, a maioria dos runtimes volta a fazer flush por linha sozinha.
/// `capture_stdout=false` sobe um processo comum sem redirecionamento (equivalente a `stdout=None`
/// do Python — herda a saída do processo pai, sem captura).
/// </summary>
public class ProcessAdapter : IAdapter
{
    private readonly ProjectConfig _config;
    private readonly RingBuffer _logs = new();
    private readonly VitalsSampler _vitals = new();
    private readonly IPortDiscovery _portDiscovery = PortDiscovery.ForCurrentPlatform();

    private IPtyConnection? _pty;
    private Process? _plainProcess;
    private int? _pid;
    private DateTimeOffset? _startedAt;
    private bool _stopRequested;
    private Action<int>? _onExit;

    public ProcessAdapter(ProjectConfig config)
    {
        if (config.Start is null)
        {
            throw new InvalidOperationException($"projeto \"{config.Id}\" sem [start] configurado");
        }
        _config = config;
    }

    public void SetOnExit(Action<int> callback) => _onExit = callback;

    public void Start()
    {
        if (IsRunning()) return;

        var start = _config.Start!;
        var cwd = start.Cwd ?? _config.Path;
        _stopRequested = false;

        if (start.CaptureStdout)
        {
            StartWithPty(start, cwd);
        }
        else
        {
            StartPlain(start, cwd);
        }

        _startedAt = DateTimeOffset.UtcNow;
    }

    private void StartWithPty(StartConfig start, string cwd)
    {
        var options = new PtyOptions
        {
            Cols = 200,
            Rows = 50,
            Cwd = cwd,
            App = ExecutableResolver.Resolve(start.Cmd[0]),
            CommandLine = start.Cmd.Skip(1).ToArray(),
        };

        var pty = PtyProvider.SpawnAsync(options, CancellationToken.None).GetAwaiter().GetResult();
        _pty = pty;
        _pid = pty.Pid;

        pty.ProcessExited += (_, _) =>
        {
            if (!_stopRequested) _onExit?.Invoke(pty.ExitCode);
        };

        _ = Task.Run(() => PumpPtyOutputAsync(pty));
    }

    private void StartPlain(StartConfig start, string cwd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = start.Cmd[0],
            WorkingDirectory = cwd,
            UseShellExecute = false,
        };
        foreach (var arg in start.Cmd.Skip(1)) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            if (!_stopRequested) _onExit?.Invoke(process.ExitCode);
            process.Dispose();
        };
        process.Start();

        _plainProcess = process;
        _pid = process.Id;
    }

    private async Task PumpPtyOutputAsync(IPtyConnection pty)
    {
        var reader = pty.ReaderStream;
        var pending = new List<byte>();
        var chunk = new byte[4096];

        try
        {
            int read;
            while ((read = await reader.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                pending.AddRange(chunk.AsSpan(0, read).ToArray());
                FlushCompleteLines(pending);
            }
        }
        catch (IOException)
        {
            // pty fechado quando o processo sai — leitura pode devolver erro em vez de EOF puro.
        }
        finally
        {
            if (pending.Count > 0)
            {
                _logs.Append(DecodeLine(pending.ToArray()));
            }
            pty.Dispose();
        }
    }

    private void FlushCompleteLines(List<byte> pending)
    {
        int newlineIndex;
        while ((newlineIndex = pending.IndexOf((byte)'\n')) >= 0)
        {
            var lineBytes = pending.Take(newlineIndex).ToArray();
            pending.RemoveRange(0, newlineIndex + 1);
            _logs.Append(DecodeLine(lineBytes));
        }
    }

    private static string DecodeLine(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimEnd('\r');

    public void Stop()
    {
        if (_pid is not { } pid) return;
        _stopRequested = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && UnixSignal.TryTerminate(pid))
        {
            if (WaitForExit(pid, TimeSpan.FromSeconds(10))) return;
        }

        Kill(pid);
    }

    private static bool WaitForExit(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!TryGetLiveProcess(pid, out _)) return true;
            Thread.Sleep(100);
        }
        return !TryGetLiveProcess(pid, out _);
    }

    private static void Kill(int pid)
    {
        if (!TryGetLiveProcess(pid, out var process)) return;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // já morreu entre o TryGetLiveProcess e o Kill — corrida benigna
        }
    }

    private static bool TryGetLiveProcess(int pid, out Process process)
    {
        try
        {
            process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            process = null!;
            return false;
        }
    }

    private bool IsRunning() => _pid is { } pid && TryGetLiveProcess(pid, out _);

    public ProcessStatus Status()
    {
        if (_pid is not { } pid || !TryGetLiveProcess(pid, out _))
        {
            return new ProcessStatus { Running = false };
        }

        var ports = _portDiscovery.ListeningPorts(pid);
        double? uptime = _startedAt is { } startedAt
            ? (DateTimeOffset.UtcNow - startedAt).TotalSeconds
            : null;
        var (cpuPercent, memoryMb) = _vitals.Sample(pid);

        return new ProcessStatus
        {
            Running = true,
            Pid = pid,
            Ports = ports,
            UptimeSeconds = uptime,
            CpuPercent = cpuPercent,
            MemoryMb = memoryMb,
        };
    }

    public IReadOnlyList<string> Logs(int tail = 100, string? service = null) => _logs.Tail(tail);
}
