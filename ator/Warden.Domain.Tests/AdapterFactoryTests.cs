using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class AdapterFactoryTests
{
    public static TheoryData<string, Type> OwnedTypes => new()
    {
        { "python", typeof(PythonAdapter) },
        { "raw", typeof(RawAdapter) },
        { "node", typeof(NodeAdapter) },
        { "php", typeof(PhpAdapter) },
        { "just", typeof(JustAdapter) },
    };

    [Theory]
    [MemberData(nameof(OwnedTypes))]
    public void CreateAdapterForOwnedTypes(string projectType, Type adapterType)
    {
        var config = new ProjectConfig
        {
            Id = "p",
            Path = "/tmp/p",
            Type = projectType,
            Start = new StartConfig { Cmd = ["true"] },
        };

        var adapter = AdapterFactory.Create(config);

        Assert.IsType(adapterType, adapter);
    }

    [Fact]
    public void CreateAdapterForDocker()
    {
        var config = new ProjectConfig
        {
            Id = "p",
            Path = "/tmp/p",
            Type = "docker",
            ComposeFile = "docker-compose.yml",
        };

        var adapter = AdapterFactory.Create(config);

        Assert.IsType<DockerAdapter>(adapter);
    }
}
