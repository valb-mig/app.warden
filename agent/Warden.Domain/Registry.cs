using Warden.Domain.Config;

namespace Warden.Domain;

/// <summary>
/// Carrega e indexa configs de projeto de <c>&lt;configDir&gt;/projects/*.toml</c>. Separado de
/// config.toml/api_token/warden.db (arquivos únicos, ficam na raiz de configDir) pra não misturar
/// N tomls de projeto com o resto.
/// </summary>
public sealed class Registry
{
    private const string ProjectsDirName = "projects";

    public string ConfigDir { get; }
    public string ProjectsDir { get; }

    private readonly string _projectsDir;
    private readonly Dictionary<string, ProjectConfig> _projects = new();

    public Registry(string configDir)
    {
        ConfigDir = configDir;
        _projectsDir = Path.Combine(configDir, ProjectsDirName);
        ProjectsDir = _projectsDir;
    }

    public void Load()
    {
        _projects.Clear();
        if (!Directory.Exists(_projectsDir)) return;

        foreach (var tomlFile in Directory.GetFiles(_projectsDir, "*.toml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var project = ConfigLoader.LoadProjectConfig(tomlFile);
            _projects[project.Id] = project;
        }
    }

    public ProjectConfig Get(string projectId) => _projects[projectId];

    public IReadOnlyList<ProjectConfig> All() => _projects.Values.ToList();
}
