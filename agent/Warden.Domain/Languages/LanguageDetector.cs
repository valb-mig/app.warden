namespace Warden.Domain.Languages;

/// <summary>
/// Detecção leve de linguagens do projeto — decorativo, não precisa de precisão de linguist. Mirror
/// de `languages.py` (ver NEW_CONTEXT.md §12 fase 8): manifesto primeiro (barato e confiável),
/// extensão como complemento até preencher o teto de exibição.
/// </summary>
public static class LanguageDetector
{
    private const int Limit = 3;
    private const int MaxFilesScanned = 3000;
    private const int MaxDepth = 4;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.Ordinal)
    {
        "node_modules", ".git", ".next", ".venv", "venv", "vendor",
        "dist", "build", "__pycache__", ".cache", "target", ".pytest_cache",
    };

    // Ordem importa: quando vários manifestos batem e o total ultrapassa o limite de exibição, os
    // primeiros da lista vencem — mesma ordem do `_MANIFEST_MAP` do Python.
    private static readonly (string Filename, string Lang)[] ManifestMap =
    [
        ("composer.json", "php"),
        ("pyproject.toml", "python"),
        ("requirements.txt", "python"),
        ("setup.py", "python"),
        ("Gemfile", "ruby"),
        ("go.mod", "go"),
        ("Cargo.toml", "rust"),
        ("pom.xml", "java"),
        ("build.gradle", "java"),
    ];

    private static readonly Dictionary<string, string> ExtMap = new(StringComparer.Ordinal)
    {
        [".py"] = "python",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".php"] = "php",
        [".go"] = "go",
        [".rs"] = "rust",
        [".rb"] = "ruby",
        [".java"] = "java",
        [".kt"] = "kotlin",
        [".c"] = "c",
        [".h"] = "c",
        [".cpp"] = "cpp",
        [".hpp"] = "cpp",
        [".cs"] = "csharp",
        [".sh"] = "shell",
        [".vue"] = "vue",
    };

    public static IReadOnlyList<string> Detect(string path, int limit = Limit)
    {
        var found = new List<string>();
        void Add(string lang)
        {
            if (!found.Contains(lang)) found.Add(lang);
        }

        if (File.Exists(Path.Combine(path, "tsconfig.json")))
        {
            Add("typescript");
        }
        else if (File.Exists(Path.Combine(path, "package.json")))
        {
            Add("javascript");
        }

        foreach (var (filename, lang) in ManifestMap)
        {
            if (File.Exists(Path.Combine(path, filename))) Add(lang);
        }

        if (found.Count >= limit) return found.Take(limit).ToList();

        // OrderByDescending do LINQ é stable — em empate, preserva a ordem de "primeira aparição"
        // durante o scan, igual ao `Counter.most_common()` do Python (sort estável sobre dict que
        // preserva ordem de inserção).
        foreach (var (lang, _) in ScanExtensions(path).OrderByDescending(entry => entry.Count))
        {
            if (found.Contains(lang)) continue;
            Add(lang);
            if (found.Count >= limit) break;
        }

        return found;
    }

    private static List<(string Lang, int Count)> ScanExtensions(string root)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        var scanned = 0;

        void Walk(string dir, int depth)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return;
            }

            var subdirs = new List<string>();
            foreach (var entry in entries)
            {
                if (scanned >= MaxFilesScanned) return;

                if (Directory.Exists(entry))
                {
                    var name = Path.GetFileName(entry);
                    if (SkipDirs.Contains(name) || name.StartsWith('.')) continue;
                    subdirs.Add(entry);
                    continue;
                }

                scanned++;
                var ext = Path.GetExtension(entry).ToLowerInvariant();
                if (!ExtMap.TryGetValue(ext, out var lang)) continue;
                if (!counts.ContainsKey(lang))
                {
                    counts[lang] = 0;
                    order.Add(lang);
                }
                counts[lang]++;
            }

            if (depth >= MaxDepth) return; // não desce mais — mesma poda do `dirs[:] = []` do Python
            foreach (var subdir in subdirs)
            {
                if (scanned >= MaxFilesScanned) return;
                Walk(subdir, depth + 1);
            }
        }

        Walk(root, 0);
        return order.Select(lang => (lang, counts[lang])).ToList();
    }
}
