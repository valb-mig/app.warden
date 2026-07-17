namespace Warden.Domain.Git;

public sealed record GitCommit
{
    public required string Hash { get; init; }
    public required string Subject { get; init; }
    public required string Author { get; init; }

    /// <summary>"há 4 minutos" — cru do `git log --format=%cr`, respeita locale.</summary>
    public required string Relative { get; init; }
}

public sealed record GitInfo
{
    public required string Branch { get; init; }
    public required bool Dirty { get; init; }
    public required int DirtyCount { get; init; }

    /// <summary>null = sem upstream configurado.</summary>
    public int? Ahead { get; init; }
    public int? Behind { get; init; }
    public required bool HasRemote { get; init; }

    /// <summary>null = repo sem commits.</summary>
    public GitCommit? LastCommit { get; init; }
}

public sealed record GitCommandResult
{
    public required bool Ok { get; init; }
    public required int ExitCode { get; init; }
    public required string Output { get; init; }

    /// <summary>Bloqueado por guarda (tree sujo etc) — nem chegou a rodar.</summary>
    public bool Refused { get; init; }
}

/// <summary>Verbo git fora da allowlist embutida (decisão #9 do TODO.md: sem shell livre).</summary>
public sealed class GitVerbNotSupportedException(string message) : Exception(message);
