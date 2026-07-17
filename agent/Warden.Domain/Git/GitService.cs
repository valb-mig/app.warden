using System.ComponentModel;
using System.Diagnostics;

namespace Warden.Domain.Git;

/// <summary>
/// Leitura de estado git local do projeto — read-only, sem tocar working tree. Ortogonal ao adapter:
/// git é propriedade do <c>path</c> no disco, não do ciclo de vida do processo (mirror de `git.py`,
/// ver NEW_CONTEXT.md §12 fase 8).
/// </summary>
public static class GitService
{
    // Separador de campos no formato de log (\x1f = unit separator) — não colide com nada que
    // apareça numa mensagem de commit.
    private const string FieldSep = "\x1f";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MutatingTimeout = TimeSpan.FromSeconds(60);

    // Allowlist embutida (decisão #9 do TODO.md: sem shell livre). Verbos bounded, não `git <qualquer>`.
    // pull/push mutam estado → exigem confirmação na camada da API/Engine.
    public static readonly IReadOnlyList<string> AllowedVerbs = ["fetch", "sync", "pull", "push"];
    public static readonly IReadOnlyList<string> ConfirmVerbs = ["pull", "push"];

    public static bool IsGitRepo(string path)
    {
        try
        {
            var result = RunGit(path, ["rev-parse", "--is-inside-work-tree"], DefaultTimeout);
            return !result.TimedOut && result.ExitCode == 0 && result.Stdout.Trim() == "true";
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Estado git do projeto, ou null se <c>path</c> não é repo git.</summary>
    public static GitInfo? Info(string path)
    {
        if (!IsGitRepo(path)) return null;

        var dirtyCount = DirtyCount(path);
        var (ahead, behind) = AheadBehind(path);
        return new GitInfo
        {
            Branch = Branch(path),
            Dirty = dirtyCount > 0,
            DirtyCount = dirtyCount,
            Ahead = ahead,
            Behind = behind,
            HasRemote = HasRemote(path),
            LastCommit = LastCommit(path),
        };
    }

    /// <summary>
    /// Roda um verbo git da allowlist. Guardas evitam merge-conflito e travas no escuro. Não checa
    /// confirmação — isso é responsabilidade da camada acima (Engine/API → 409).
    /// </summary>
    public static GitCommandResult Command(string path, string verb, string remote = "origin")
    {
        if (!AllowedVerbs.Contains(verb))
        {
            throw new GitVerbNotSupportedException($"verbo git \"{verb}\" não suportado");
        }
        if (!IsGitRepo(path))
        {
            return Refused("não é um repositório git");
        }

        switch (verb)
        {
            case "fetch":
                // fetch é só atualização de refs — não toca working tree.
                return ToResult(RunGit(path, ["fetch", remote], FetchTimeout));

            case "pull":
                // Guarda dirty: pull com tree sujo pode gerar merge-conflito sem ninguém pra resolver
                // via API. --ff-only recusa limpo se divergiu (ahead E behind).
                if (DirtyCount(path) > 0)
                {
                    return Refused("working tree sujo — commite ou stash antes de pull");
                }
                return ToResult(RunGit(path, ["pull", "--ff-only"], MutatingTimeout));

            case "push":
                // GIT_TERMINAL_PROMPT=0 faz push falhar rápido se faltar credencial, em vez de
                // pendurar num prompt de senha inexistente.
                return ToResult(RunGit(path, ["push"], MutatingTimeout));

            default: // "sync": one-tap. fetch, e só faz fast-forward se limpo e atrás.
                if (DirtyCount(path) > 0)
                {
                    return Refused("working tree sujo — commite ou stash antes de sync");
                }
                var fetch = RunGit(path, ["fetch", remote], FetchTimeout);
                if (fetch.TimedOut || fetch.ExitCode != 0)
                {
                    return ToResult(fetch);
                }
                var (_, behind) = AheadBehind(path);
                if (behind is null or 0) // null (sem upstream) ou 0 (já no topo)
                {
                    return new GitCommandResult { Ok = true, ExitCode = 0, Output = "já atualizado" };
                }
                return ToResult(RunGit(path, ["merge", "--ff-only", "@{u}"], MutatingTimeout));
        }
    }

    private static string Branch(string path)
    {
        var result = RunGit(path, ["rev-parse", "--abbrev-ref", "HEAD"], DefaultTimeout);
        var trimmed = result.Stdout.Trim();
        return trimmed.Length > 0 ? trimmed : "HEAD";
    }

    private static int DirtyCount(string path)
    {
        var result = RunGit(path, ["status", "--porcelain"], DefaultTimeout);
        if (result.TimedOut || result.ExitCode != 0) return 0;
        return result.Stdout.Split('\n').Count(line => line.Trim().Length > 0);
    }

    private static (int? Ahead, int? Behind) AheadBehind(string path)
    {
        // left-right count contra o upstream (@{u}): "<behind>\t<ahead>". Sem upstream configurado
        // o comando falha → (null, null).
        var result = RunGit(path, ["rev-list", "--left-right", "--count", "@{u}...HEAD"], DefaultTimeout);
        if (result.TimedOut || result.ExitCode != 0) return (null, null);
        var parts = result.Stdout.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (null, null);
        return (int.Parse(parts[1]), int.Parse(parts[0]));
    }

    private static bool HasRemote(string path)
    {
        var result = RunGit(path, ["remote"], DefaultTimeout);
        return !result.TimedOut && result.ExitCode == 0 && result.Stdout.Trim().Length > 0;
    }

    private static GitCommit? LastCommit(string path)
    {
        var format = string.Join(FieldSep, "%h", "%s", "%an", "%cr");
        var result = RunGit(path, ["log", "-1", $"--format={format}"], DefaultTimeout);
        if (result.TimedOut || result.ExitCode != 0) return null; // repo sem commits ainda

        var fields = result.Stdout.Trim().Split(FieldSep);
        if (fields.Length != 4) return null;
        return new GitCommit { Hash = fields[0], Subject = fields[1], Author = fields[2], Relative = fields[3] };
    }

    private static GitCommandResult ToResult(GitProcessResult result) => new()
    {
        Ok = !result.TimedOut && result.ExitCode == 0,
        ExitCode = result.TimedOut ? -1 : result.ExitCode,
        Output = (result.Stdout + result.Stderr).Trim(),
    };

    private static GitCommandResult Refused(string reason) =>
        new() { Ok = false, ExitCode = -1, Output = reason, Refused = true };

    private readonly record struct GitProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    private static GitProcessResult RunGit(string path, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(path);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        // Garante que nenhum comando git pendure pedindo credencial num tty inexistente — falha
        // rápido em vez de travar o daemon.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return new GitProcessResult(-1, "", "", TimedOut: true);
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return new GitProcessResult(process.ExitCode, stdout, stderr, TimedOut: false);
    }
}
