using Warden.Domain.Config;
using Warden.Domain.Discovery;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class EnvScaffoldTests
{
    [Fact]
    public void RenderToml_ComEnv_SerializaSecaoEnv()
    {
        var config = new ProjectConfig
        {
            Id = "p",
            Path = "/tmp/p",
            Type = "raw",
            Env = new Dictionary<string, string>
            {
                ["DATABASE_URL"] = "postgres://localhost/db",
                ["API_KEY"] = "secret123",
            },
        };

        var toml = Scaffold.RenderToml(config);

        Assert.Contains("[env]", toml);
        Assert.Contains("DATABASE_URL = \"postgres://localhost/db\"", toml);
        Assert.Contains("API_KEY = \"secret123\"", toml);
    }

    [Fact]
    public void RenderToml_SemEnv_NaoIncluiSecaoEnv()
    {
        var config = new ProjectConfig
        {
            Id = "p",
            Path = "/tmp/p",
            Type = "raw",
        };

        var toml = Scaffold.RenderToml(config);

        Assert.DoesNotContain("[env]", toml);
    }

    [Fact]
    public void RoundTrip_Env_PreservaChavesEValores()
    {
        var config = new ProjectConfig
        {
            Id = "p",
            Path = "/tmp/p",
            Type = "raw",
            Env = new Dictionary<string, string>
            {
                ["MY_VAR"] = "hello",
                ["ANOTHER"] = "world",
            },
        };

        var toml = Scaffold.RenderToml(config);
        var tmpPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpPath, toml);
            var reloaded = ConfigLoader.LoadProjectConfig(tmpPath);
            Assert.Equal(2, reloaded.Env.Count);
            Assert.Equal("hello", reloaded.Env["MY_VAR"]);
            Assert.Equal("world", reloaded.Env["ANOTHER"]);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }
}
