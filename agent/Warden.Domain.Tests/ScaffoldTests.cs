using Warden.Domain.Config;
using Warden.Domain.Discovery;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_scaffold.py` — cada teste escreve arquivos reais em disco, nada mockado.</summary>
public sealed class ScaffoldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "warden-scaffold-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _projectDir;

    public ScaffoldTests()
    {
        _projectDir = Path.Combine(_root, "project");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ProjectConfig WriteTomlAndReload(ProjectConfig config)
    {
        var tomlFile = Path.Combine(_root, $"{config.Id}.toml");
        File.WriteAllText(tomlFile, Scaffold.RenderToml(config));
        return ConfigLoader.LoadProjectConfig(tomlFile);
    }

    [Fact]
    public void DetectsDocker()
    {
        File.WriteAllText(Path.Combine(_projectDir, "docker-compose.yml"), "services:\n  app:\n    image: foo\n  nginx:\n    image: bar\n");
        Assert.Equal("docker", Scaffold.DetectType(_projectDir));

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("docker", config.Type);
        Assert.Equal("docker-compose.yml", config.ComposeFile);
        Assert.Equal(["app", "nginx"], config.LogSources.Select(l => l.Service).OrderBy(s => s));

        var reloaded = WriteTomlAndReload(config);
        Assert.Equal("docker-compose.yml", reloaded.ComposeFile);
        Assert.Equal(2, reloaded.LogSources.Count);
    }

    [Fact]
    public void DetectsNodeWithScripts()
    {
        File.WriteAllText(Path.Combine(_projectDir, "package.json"), """{"scripts": {"dev": "next dev", "build": "next build", "lint": "eslint ."}}""");
        File.WriteAllText(Path.Combine(_projectDir, "pnpm-lock.yaml"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("node", config.Type);
        Assert.NotNull(config.Start);
        Assert.Equal(["pnpm", "run", "dev"], config.Start!.Cmd);
        Assert.Equal(["build", "lint"], config.Actions.Select(a => a.Name).OrderBy(n => n));

        var reloaded = WriteTomlAndReload(config);
        Assert.Equal(["pnpm", "run", "dev"], reloaded.Start!.Cmd);
    }

    [Fact]
    public void DetectsPhpLaravel()
    {
        File.WriteAllText(Path.Combine(_projectDir, "composer.json"), "{}");
        File.WriteAllText(Path.Combine(_projectDir, "artisan"), "#!/usr/bin/env php\n");
        var logDir = Path.Combine(_projectDir, "storage", "logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "laravel.log"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("php", config.Type);
        Assert.Equal(["migrate", "seed", "tinker"], config.Actions.Select(a => a.Name).OrderBy(n => n));
        var tinker = config.Actions.Single(a => a.Name == "tinker");
        Assert.True(tinker.Interactive);
        Assert.Equal("./storage/logs/laravel.log", config.LogSources[0].Path);

        WriteTomlAndReload(config);
    }

    [Fact]
    public void DetectsPythonDjango()
    {
        File.WriteAllText(Path.Combine(_projectDir, "manage.py"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("python", config.Type);
        Assert.Equal(["python", "manage.py", "runserver"], config.Start!.Cmd);

        WriteTomlAndReload(config);
    }

    [Fact]
    public void DetectsPythonWithUv()
    {
        File.WriteAllText(Path.Combine(_projectDir, "main.py"), "");
        File.WriteAllText(Path.Combine(_projectDir, "uv.lock"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal(["uv", "run", "python", "main.py"], config.Start!.Cmd);
    }

    [Fact]
    public void DetectsPythonWithVenv()
    {
        File.WriteAllText(Path.Combine(_projectDir, "run.py"), "");
        var venvBin = Path.Combine(_projectDir, "venv", "bin");
        Directory.CreateDirectory(venvBin);
        File.WriteAllText(Path.Combine(venvBin, "python"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal([Path.Combine(venvBin, "python"), "run.py"], config.Start!.Cmd);
    }

    [Fact]
    public void PythonVenvIgnoredWhenUvLockPresent()
    {
        File.WriteAllText(Path.Combine(_projectDir, "main.py"), "");
        File.WriteAllText(Path.Combine(_projectDir, "uv.lock"), "");
        var venvBin = Path.Combine(_projectDir, "venv", "bin");
        Directory.CreateDirectory(venvBin);
        File.WriteAllText(Path.Combine(venvBin, "python"), "");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal(["uv", "run", "python", "main.py"], config.Start!.Cmd);
    }

    [Fact]
    public void DetectsJust()
    {
        File.WriteAllText(Path.Combine(_projectDir, "Justfile"), "default: dev\n\ndev:\n    echo hi\n\ntest:\n    pytest\n");

        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("just", config.Type);
        Assert.Equal(["just", "dev"], config.Start!.Cmd);
        Assert.Equal(["test"], config.Actions.Select(a => a.Name));

        WriteTomlAndReload(config);
    }

    [Fact]
    public void DetectsRawFallback()
    {
        var config = Scaffold.BuildConfig(_projectDir);
        Assert.Equal("raw", config.Type);
        Assert.Null(config.Start);
        Assert.Empty(config.Actions);

        WriteTomlAndReload(config);
    }

    [Fact]
    public void BuildConfigCustomId()
    {
        var namedDir = Path.Combine(_root, "My Cool Project");
        Directory.CreateDirectory(namedDir);

        var config = Scaffold.BuildConfig(namedDir);
        Assert.Equal("my-cool-project", config.Id);
        Assert.Equal("My Cool Project", config.Name);

        var withId = Scaffold.BuildConfig(namedDir, "custom-id");
        Assert.Equal("custom-id", withId.Id);
    }

    [Fact]
    public void BuildConfigMissingPathThrows() =>
        Assert.Throws<DirectoryNotFoundException>(() => Scaffold.BuildConfig("/path/definitely/does/not/exist"));
}
