using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Warden.Domain.Config;

namespace Warden.Domain.Discovery;

/// <summary>
/// Mirror 1:1 de `scaffold.py` — detecta o tipo de um projeto pela presença de arquivos
/// característicos e monta um <see cref="ProjectConfig"/> pré-populado com heurísticas simples.
/// Não roda nada, só monta o config; quem chama decide se grava (via <see cref="RenderToml"/>).
/// </summary>
public static class Scaffold
{
    private static readonly string[] ComposeCandidates = ["docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"];
    private static readonly string[] JustfileCandidates = ["Justfile", "justfile"];
    private static readonly string[] VenvCandidates = ["venv", ".venv", "env"];
    private static readonly Regex JustRecipeRegex = new(@"^([A-Za-z0-9_-]+)(?:\s+[^:]*)?:(?!=)", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumRegex = new("[^a-z0-9]+", RegexOptions.Compiled);

    private static readonly Dictionary<string, Func<string, ScaffoldExtras>> Builders = new()
    {
        ["node"] = BuildNode,
        ["php"] = BuildPhp,
        ["python"] = BuildPython,
        ["just"] = BuildJust,
        ["raw"] = _ => new ScaffoldExtras(),
    };

    /// <summary>Expande `~` no início do path — .NET não faz isso sozinho como o `Path.expanduser()` do Python.</summary>
    public static string ExpandHome(string path) =>
        path.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'))
            : path;

    public static string DetectType(string path)
    {
        if (FindComposeFile(path) is not null) return "docker";
        if (File.Exists(Path.Combine(path, "package.json"))) return "node";
        if (File.Exists(Path.Combine(path, "composer.json"))) return "php";
        if (LooksLikePython(path)) return "python";
        if (FindJustfile(path) is not null) return "just";
        return "raw";
    }

    public static ProjectConfig BuildConfig(string path, string? projectId = null)
    {
        var resolved = Path.GetFullPath(ExpandHome(path));
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"path não é diretório: {resolved}");
        }

        var name = Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var ptype = DetectType(resolved);
        var pid = projectId ?? Slugify(name);
        var extras = ptype == "docker" ? BuildDocker(resolved, pid) : Builders[ptype](resolved);

        return new ProjectConfig
        {
            Id = pid,
            Name = name,
            Path = resolved,
            Type = ptype,
            ComposeFile = extras.ComposeFile,
            ComposeServices = extras.ComposeServices ?? [],
            Start = extras.Start,
            LogSources = extras.LogSources ?? [],
            Actions = extras.Actions ?? [],
        };
    }

    /// <summary>Serializa pra TOML — não precisa bater byte-a-byte com `tomli_w.dumps`, só ser TOML válido que o `ConfigLoader` (Tomlyn) releia de volta pro mesmo <see cref="ProjectConfig"/>.</summary>
    public static string RenderToml(ProjectConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("id = ").Append(TomlString(config.Id)).Append('\n');
        if (!string.IsNullOrEmpty(config.Name) && config.Name != config.Id)
        {
            sb.Append("name = ").Append(TomlString(config.Name)).Append('\n');
        }
        if (!string.IsNullOrEmpty(config.Group))
        {
            sb.Append("group = ").Append(TomlString(config.Group)).Append('\n');
        }
        sb.Append("path = ").Append(TomlString(config.Path)).Append('\n');
        sb.Append("type = ").Append(TomlString(config.Type)).Append('\n');
        if (!string.IsNullOrEmpty(config.ComposeFile))
        {
            sb.Append("compose_file = ").Append(TomlString(config.ComposeFile)).Append('\n');
        }
        if (config.ComposeServices.Count > 0)
        {
            sb.Append("compose_services = ").Append(TomlArray(config.ComposeServices)).Append('\n');
        }

        if (config.Start is { } start)
        {
            sb.Append("\n[start]\n");
            sb.Append("cmd = ").Append(TomlArray(start.Cmd)).Append('\n');
            if (!string.IsNullOrEmpty(start.Cwd)) sb.Append("cwd = ").Append(TomlString(start.Cwd)).Append('\n');
            if (start.CaptureStdout) sb.Append("capture_stdout = true\n");
        }

        var notify = config.Notify;
        if (notify.OnError || notify.OnFinished || notify.OnGitBehind)
        {
            sb.Append("\n[notify]\n");
            if (notify.OnError) sb.Append("on_error = true\n");
            if (notify.OnFinished) sb.Append("on_finished = true\n");
            if (notify.OnGitBehind) sb.Append("on_git_behind = true\n");
        }

        foreach (var ls in config.LogSources)
        {
            sb.Append("\n[[log_sources]]\n");
            sb.Append("name = ").Append(TomlString(ls.Name)).Append('\n');
            sb.Append("type = ").Append(TomlString(ls.Type)).Append('\n');
            if (!string.IsNullOrEmpty(ls.Path)) sb.Append("path = ").Append(TomlString(ls.Path)).Append('\n');
            if (!string.IsNullOrEmpty(ls.Service)) sb.Append("service = ").Append(TomlString(ls.Service)).Append('\n');
            if (ls.ErrorPatterns.Count > 0) sb.Append("error_patterns = ").Append(TomlArray(ls.ErrorPatterns)).Append('\n');
        }

        foreach (var action in config.Actions)
        {
            sb.Append("\n[[actions]]\n");
            sb.Append("name = ").Append(TomlString(action.Name)).Append('\n');
            sb.Append("cmd = ").Append(TomlArray(action.Cmd)).Append('\n');
            if (action.Interactive) sb.Append("interactive = true\n");
            if (action.Destructive) sb.Append("destructive = true\n");
        }

        return sb.ToString();
    }

    private static string TomlArray(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(TomlString)) + "]";

    private static string TomlString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Slugify(string name)
    {
        var slug = NonAlphaNumRegex.Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "project" : slug;
    }

    private static string? FindComposeFile(string path)
    {
        var parent = Directory.GetParent(path)?.FullName;
        foreach (var basePath in new[] { path, parent })
        {
            if (basePath is null) continue;
            foreach (var candidate in ComposeCandidates)
            {
                var found = Path.Combine(basePath, candidate);
                if (File.Exists(found)) return found;
            }
        }
        return null;
    }

    private static string? FindJustfile(string path)
    {
        foreach (var candidate in JustfileCandidates)
        {
            var found = Path.Combine(path, candidate);
            if (File.Exists(found)) return found;
        }
        return null;
    }

    private static bool LooksLikePython(string path)
    {
        if (File.Exists(Path.Combine(path, "pyproject.toml"))) return true;
        if (File.Exists(Path.Combine(path, "requirements.txt"))) return true;
        if (File.Exists(Path.Combine(path, "manage.py"))) return true;
        return Directory.EnumerateFiles(path, "*.py").Any();
    }

    private static List<string> MatchPrefixedServices(List<string> services, string projectId)
    {
        var prefixes = new[] { $"{projectId}_", $"{projectId}-" };
        return services.Where(s => prefixes.Any(p => s.StartsWith(p, StringComparison.Ordinal))).ToList();
    }

    private static List<string> ParseComposeServices(string composeFile)
    {
        var services = new List<string>();
        var inServices = false;
        int? childIndent = null;
        foreach (var rawLine in File.ReadAllLines(composeFile))
        {
            var stripped = rawLine.Trim();
            if (stripped.Length == 0 || stripped.StartsWith('#')) continue;
            var indent = rawLine.Length - rawLine.TrimStart(' ').Length;
            if (!inServices)
            {
                if (stripped == "services:") inServices = true;
                continue;
            }
            childIndent ??= indent;
            if (indent < childIndent) break;
            if (indent == childIndent && stripped.EndsWith(':'))
            {
                services.Add(stripped[..^1].Trim('"', '\''));
            }
        }
        return services;
    }

    private static ScaffoldExtras BuildDocker(string path, string projectId)
    {
        var composeFile = FindComposeFile(path)
            ?? throw new InvalidOperationException("compose file esperado mas não encontrado — chame só depois de DetectType == docker");
        var allServices = ParseComposeServices(composeFile);
        var composeDir = Path.GetDirectoryName(composeFile);

        var ownServices = allServices;
        if (composeDir != path)
        {
            var matched = MatchPrefixedServices(allServices, projectId);
            if (matched.Count > 0) ownServices = matched;
        }

        var logSources = ownServices.Select(s => new LogSource { Name = s, Type = "docker", Service = s }).ToList();
        if (logSources.Count == 0)
        {
            logSources = [new LogSource { Name = "app", Type = "docker", Service = "app" }];
        }

        var composeRef = composeDir == path ? Path.GetFileName(composeFile) : composeFile;
        var extras = new ScaffoldExtras { ComposeFile = composeRef, LogSources = logSources };
        if (!ownServices.SequenceEqual(allServices))
        {
            extras = extras with { ComposeServices = ownServices };
        }

        var justfile = FindJustfile(path);
        if (justfile is not null)
        {
            var actions = ParseJustfileRecipes(justfile).Select(r => new ActionConfig { Name = r, Cmd = ["just", r] }).ToList();
            if (actions.Count > 0) extras = extras with { Actions = actions };
        }
        return extras;
    }

    private static string DetectNodeRunner(string path)
    {
        if (File.Exists(Path.Combine(path, "pnpm-lock.yaml"))) return "pnpm";
        if (File.Exists(Path.Combine(path, "yarn.lock"))) return "yarn";
        return "npm";
    }

    private static ScaffoldExtras BuildNode(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "package.json")));
        var scriptNames = new List<string>();
        if (doc.RootElement.TryGetProperty("scripts", out var scriptsEl) && scriptsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in scriptsEl.EnumerateObject()) scriptNames.Add(prop.Name);
        }

        var runner = DetectNodeRunner(path);
        var startScript = new[] { "dev", "start" }.FirstOrDefault(scriptNames.Contains);

        var extras = new ScaffoldExtras();
        if (startScript is not null)
        {
            extras = extras with
            {
                Start = new StartConfig { Cmd = [runner, "run", startScript], CaptureStdout = true },
                LogSources = [new LogSource { Name = "stdout", Type = "stdout" }],
            };
        }

        var actions = scriptNames.Where(n => n != startScript)
            .Select(n => new ActionConfig { Name = n, Cmd = [runner, "run", n] }).ToList();
        if (actions.Count > 0) extras = extras with { Actions = actions };
        return extras;
    }

    private static ScaffoldExtras BuildLaravel(string path)
    {
        var extras = new ScaffoldExtras
        {
            Start = new StartConfig { Cmd = ["php", "artisan", "serve"], CaptureStdout = true },
            Actions =
            [
                new ActionConfig { Name = "migrate", Cmd = ["php", "artisan", "migrate", "--force"], Destructive = true },
                new ActionConfig { Name = "seed", Cmd = ["php", "artisan", "db:seed", "--force"], Destructive = true },
                new ActionConfig { Name = "tinker", Cmd = ["php", "artisan", "tinker"], Interactive = true },
            ],
        };

        var logFile = Path.Combine(path, "storage", "logs", "laravel.log");
        if (File.Exists(logFile))
        {
            extras = extras with
            {
                LogSources =
                [
                    new LogSource
                    {
                        Name = "laravel",
                        Type = "file",
                        Path = "./storage/logs/laravel.log",
                        ErrorPatterns = ["ERROR", @"\bException\b", "PHP Fatal"],
                    },
                ],
            };
        }
        return extras;
    }

    private static ScaffoldExtras BuildPlainPhp(string path)
    {
        var docroot = Directory.Exists(Path.Combine(path, "public")) ? "public" : ".";
        var extras = new ScaffoldExtras
        {
            Start = new StartConfig { Cmd = ["php", "-S", "0.0.0.0:8000", "-t", docroot], CaptureStdout = true },
            LogSources = [new LogSource { Name = "stdout", Type = "stdout" }],
        };

        var justfile = FindJustfile(path);
        if (justfile is not null)
        {
            var actions = ParseJustfileRecipes(justfile).Select(r => new ActionConfig { Name = r, Cmd = ["just", r] }).ToList();
            if (actions.Count > 0) extras = extras with { Actions = actions };
        }
        return extras;
    }

    private static ScaffoldExtras BuildPhp(string path) =>
        File.Exists(Path.Combine(path, "artisan")) ? BuildLaravel(path) : BuildPlainPhp(path);

    private static (string File, string[] ExtraArgs)? DetectPythonEntry(string path)
    {
        if (File.Exists(Path.Combine(path, "manage.py"))) return ("manage.py", ["runserver"]);
        foreach (var candidate in new[] { "main.py", "app.py" })
        {
            if (File.Exists(Path.Combine(path, candidate))) return (candidate, []);
        }
        var pyFiles = Directory.GetFiles(path, "*.py");
        return pyFiles.Length == 1 ? (Path.GetFileName(pyFiles[0]), []) : null;
    }

    private static string? DetectVenvPython(string path)
    {
        foreach (var candidate in VenvCandidates)
        {
            var venvPython = Path.Combine(path, candidate, "bin", "python");
            if (File.Exists(venvPython)) return venvPython;
        }
        return null;
    }

    private static ScaffoldExtras BuildPython(string path)
    {
        var entry = DetectPythonEntry(path);
        if (entry is null) return new ScaffoldExtras();
        var (filename, extraArgs) = entry.Value;

        List<string> cmd;
        if (File.Exists(Path.Combine(path, "uv.lock")))
        {
            cmd = ["uv", "run", "python", filename, .. extraArgs];
        }
        else
        {
            var interpreter = DetectVenvPython(path) ?? "python";
            cmd = [interpreter, filename, .. extraArgs];
        }

        return new ScaffoldExtras
        {
            Start = new StartConfig { Cmd = cmd, CaptureStdout = true },
            LogSources = [new LogSource { Name = "stdout", Type = "stdout" }],
        };
    }

    private static List<string> ParseJustfileRecipes(string justfile)
    {
        var recipes = new List<string>();
        foreach (var line in File.ReadAllLines(justfile))
        {
            if (line.Length == 0 || " \t#@".Contains(line[0])) continue;
            var match = JustRecipeRegex.Match(line);
            if (match.Success && match.Groups[1].Value != "default") recipes.Add(match.Groups[1].Value);
        }
        return recipes;
    }

    private static ScaffoldExtras BuildJust(string path)
    {
        var justfile = FindJustfile(path)
            ?? throw new InvalidOperationException("justfile esperado mas não encontrado — chame só depois de DetectType == just");
        var recipes = ParseJustfileRecipes(justfile);
        var startRecipe = new[] { "dev", "serve", "start" }.FirstOrDefault(recipes.Contains);

        var extras = new ScaffoldExtras();
        if (startRecipe is not null)
        {
            extras = extras with
            {
                Start = new StartConfig { Cmd = ["just", startRecipe], CaptureStdout = true },
                LogSources = [new LogSource { Name = "stdout", Type = "stdout" }],
            };
        }

        var actions = recipes.Where(r => r != startRecipe).Select(r => new ActionConfig { Name = r, Cmd = ["just", r] }).ToList();
        if (actions.Count > 0) extras = extras with { Actions = actions };
        return extras;
    }

    private sealed record ScaffoldExtras
    {
        public string? ComposeFile { get; init; }
        public List<string>? ComposeServices { get; init; }
        public StartConfig? Start { get; init; }
        public List<LogSource>? LogSources { get; init; }
        public List<ActionConfig>? Actions { get; init; }
    }
}
