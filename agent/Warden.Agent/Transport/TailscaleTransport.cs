using System.Diagnostics;
using System.Net;

namespace Warden.Agent.Transport;

/// <summary>
/// Resolve o IP Tailscale via `tailscale ip -4` (shell out — mesma filosofia já usada pro Docker
/// CLI, sem SDK/`tsnet` embutido; ver NEW_CONTEXT.md §7). Sem tailscaled rodando ou fora da tailnet,
/// o Agent não sobe — não existe fallback pra bind wildcard/loopback.
/// </summary>
public sealed class TailscaleTransport(string tailscaleCommand = "tailscale") : IConsoleTransport
{
    public IPEndPoint ResolveEndpoint(int port) => new(ResolveTailscaleIp(), port);

    private IPAddress ResolveTailscaleIp()
    {
        var psi = new ProcessStartInfo
        {
            FileName = tailscaleCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("ip");
        psi.ArgumentList.Add("-4");

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new TailscaleUnavailableException(
                $"comando \"{tailscaleCommand}\" não encontrado — o Agent não sobe sem o Tailscale ativo", ex);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TailscaleUnavailableException($"\"{tailscaleCommand} ip -4\" não respondeu em 5s");
        }

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new TailscaleUnavailableException(
                $"\"{tailscaleCommand} ip -4\" falhou (exit={process.ExitCode}): {stderr.Trim()}");
        }

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (firstLine is null || !IPAddress.TryParse(firstLine, out var ip))
        {
            throw new TailscaleUnavailableException(
                $"\"{tailscaleCommand} ip -4\" devolveu algo que não é um IP: \"{stdout.Trim()}\"");
        }

        if (!IsInTailscaleCgnatRange(ip))
        {
            throw new TailscaleUnavailableException(
                $"\"{ip}\" não está na faixa CGNAT do Tailscale (100.64.0.0/10) — resolução suspeita, recusando bind");
        }

        return ip;
    }

    private static bool IsInTailscaleCgnatRange(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
