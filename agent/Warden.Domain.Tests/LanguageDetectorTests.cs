using Warden.Domain.Languages;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_languages.py`.</summary>
public sealed class LanguageDetectorTests
{
    [Fact]
    public void EmptyProjectDetectsNothing()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;

        Assert.Empty(LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void PythonManifestDetected()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "pyproject.toml"), "[project]\n");

        Assert.Equal(["python"], LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void TypescriptPreferredOverJavascriptWhenTsconfigPresent()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(tmpDir, "tsconfig.json"), "{}");

        Assert.Equal(["typescript"], LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void JavascriptWithoutTsconfig()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "package.json"), "{}");

        Assert.Equal(["javascript"], LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void MultipleManifestsCombineUpToLimit()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "composer.json"), "{}");
        File.WriteAllText(Path.Combine(tmpDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(tmpDir, "go.mod"), "module demo\n");

        var langs = LanguageDetector.Detect(tmpDir);

        Assert.Equal(new HashSet<string> { "php", "javascript", "go" }, langs.ToHashSet());
        Assert.Equal(3, langs.Count);
    }

    [Fact]
    public void ExtensionScanFillsGapWhenNoManifest()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "main.rs"), "fn main() {}");
        File.WriteAllText(Path.Combine(tmpDir, "lib.rs"), "");
        File.WriteAllText(Path.Combine(tmpDir, "notes.txt"), "");

        Assert.Equal(["rust"], LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void ExtensionScanSkipsIgnoredDirs()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        var vendor = Path.Combine(tmpDir, "node_modules", "pkg");
        Directory.CreateDirectory(vendor);
        File.WriteAllText(Path.Combine(vendor, "index.js"), "");

        Assert.Empty(LanguageDetector.Detect(tmpDir));
    }

    [Fact]
    public void LimitCapsResultCount()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(tmpDir, "composer.json"), "{}");
        File.WriteAllText(Path.Combine(tmpDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(tmpDir, "go.mod"), "module demo\n");

        Assert.Equal(2, LanguageDetector.Detect(tmpDir, limit: 2).Count);
    }
}
