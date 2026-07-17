using Warden.Domain.Config;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class ConfigLoaderTests
{
    private const string DockerToml = """
        id = "leadmaster"
        name = "LeadMaster"
        group = "scrapers"
        path = "/tmp/leadmaster"
        type = "docker"
        compose_file = "docker-compose.yml"

        [notify]
        on_error = true

        [[log_sources]]
        name = "app"
        type = "docker"
        service = "app"

        [[actions]]
        name = "migrate"
        cmd = ["docker", "compose", "exec", "app", "php", "artisan", "migrate", "--force"]
        """;

    private const string PythonToml = """
        id = "caffeshop-bot"
        type = "python"
        path = "/tmp/caffeshop"

        [start]
        cmd = ["python", "main.py"]
        capture_stdout = true

        [[log_sources]]
        name = "stdout"
        type = "stdout"
        """;

    [Fact]
    public void LoadDockerProject()
    {
        var path = WriteTemp(DockerToml);

        var project = ConfigLoader.LoadProjectConfig(path);

        Assert.Equal("leadmaster", project.Id);
        Assert.Equal("LeadMaster", project.DisplayName);
        Assert.Equal("docker", project.Type);
        Assert.True(project.Notify.OnError);
    }

    [Fact]
    public void GitWatchDefaultsOff()
    {
        var path = WriteTemp(DockerToml);

        var project = ConfigLoader.LoadProjectConfig(path);

        Assert.False(project.Git.Watch);
        Assert.Equal(300, project.Git.Interval);
        Assert.Equal("origin", project.Git.Remote);
        Assert.False(project.Notify.OnGitBehind);
    }

    [Fact]
    public void GitWatchConfigured()
    {
        var path = WriteTemp(DockerToml + "\n[git]\nwatch = true\ninterval = 60\nremote = \"upstream\"\n");

        var project = ConfigLoader.LoadProjectConfig(path);

        Assert.True(project.Git.Watch);
        Assert.Equal(60, project.Git.Interval);
        Assert.Equal("upstream", project.Git.Remote);
        Assert.Equal("app", project.LogSources[0].Service);
        Assert.Equal("migrate", project.Actions[0].Name);
    }

    [Fact]
    public void LoadPythonProjectDefaults()
    {
        var path = WriteTemp(PythonToml);

        var project = ConfigLoader.LoadProjectConfig(path);

        Assert.Equal("caffeshop-bot", project.DisplayName); // sem `name`, cai pro id
        Assert.NotNull(project.Start);
        Assert.Equal(["python", "main.py"], project.Start!.Cmd);
        Assert.False(project.Notify.OnError); // default
    }

    [Fact]
    public void LoadGlobalConfigCreatesDefaultFileWhenMissing()
    {
        var dir = Directory.CreateTempSubdirectory();
        var configPath = Path.Combine(dir.FullName, "config.toml");

        var config = ConfigLoader.LoadGlobalConfig(configPath);

        Assert.Equal(new GlobalConfig(), config);
        Assert.True(File.Exists(configPath));
        Assert.Contains("api_port", File.ReadAllText(configPath));

        var reloaded = ConfigLoader.LoadGlobalConfig(configPath);
        Assert.Equal(new GlobalConfig(), reloaded);
    }

    [Fact]
    public void LoadGlobalConfigReadsExistingFile()
    {
        var dir = Directory.CreateTempSubdirectory();
        var configPath = Path.Combine(dir.FullName, "config.toml");
        File.WriteAllText(configPath, "api_port = 9000\nnotify_channel = \"ntfy\"\nntfy_topic = \"alerts\"\n");

        var config = ConfigLoader.LoadGlobalConfig(configPath);

        Assert.Equal(9000, config.ApiPort);
        Assert.Equal("ntfy", config.NotifyChannel);
        Assert.Equal("alerts", config.NtfyTopic);
    }

    [Fact]
    public void SaveGlobalConfigPersistsScanPaths()
    {
        var dir = Directory.CreateTempSubdirectory();
        var configPath = Path.Combine(dir.FullName, "config.toml");
        ConfigLoader.LoadGlobalConfig(configPath); // garante que o arquivo default existe
        var config = new GlobalConfig { ScanPaths = ["/home/valb/Projects", "/home/valb/Work"] };

        ConfigLoader.SaveGlobalConfig(configPath, config);
        var reloaded = ConfigLoader.LoadGlobalConfig(configPath);

        Assert.Equal(["/home/valb/Projects", "/home/valb/Work"], reloaded.ScanPaths);
    }

    private static string WriteTemp(string contents)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, contents);
        return path;
    }
}
