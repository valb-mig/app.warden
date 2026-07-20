using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Warden.Admin.Ipc;

namespace Warden.Admin.ViewModels;

/// <summary>Uma linha renderizada no terminal de logs, já com a classificação de erro resolvida (pra template não precisar chamar de volta na ViewModel).</summary>
public sealed record LogLineViewModel(string Text, bool IsError);

/// <summary>
/// Espelha `log-viewer.tsx`, mas por polling em vez de push (SignalR/WS) — o Admin fala com o Agent
/// só pelo socket unix local, então um `GET /projects/{id}/logs?tail=` a cada
/// <see cref="PollInterval"/> é a via mais simples e confiável dado esse transporte; a diferença de
/// latência (~1.5s vs. instantâneo) não importa pra uma ferramenta de administração local. Cada poll
/// devolve as últimas N linhas do ring buffer do Agent — a view sempre mostra o snapshot mais recente,
/// não acumula por conta própria (ring buffer do servidor já é a fonte da verdade).
/// </summary>
public sealed partial class LogViewerViewModel : ObservableObject
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly Regex[] DefaultErrorPatterns =
    [
        new(@"\bERROR\b", RegexOptions.IgnoreCase),
        new(@"\bEXCEPTION\b", RegexOptions.IgnoreCase),
        new(@"\bFATAL\b", RegexOptions.IgnoreCase),
        new(@"\bTRACEBACK\b", RegexOptions.IgnoreCase),
        new(@"\bFAIL(?:ED)?\b", RegexOptions.IgnoreCase),
    ];

    private readonly AgentApiClient _client;
    private readonly string _projectId;
    private DispatcherTimer? _timer;
    private IReadOnlyList<Regex> _errorPatterns = DefaultErrorPatterns;
    private IReadOnlyList<string> _rawLines = [];

    [ObservableProperty]
    private ObservableCollection<string> _services = [];

    public bool HasServices => Services.Count > 0;

    partial void OnServicesChanged(ObservableCollection<string> value) => OnPropertyChanged(nameof(HasServices));

    [ObservableProperty]
    private string? _selectedService;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _onlyErrors;

    [ObservableProperty]
    private bool _connected;

    [ObservableProperty]
    private ObservableCollection<LogLineViewModel> _visibleLines = [];

    public LogViewerViewModel(AgentApiClient client, string projectId)
    {
        _client = client;
        _projectId = projectId;
    }

    public async Task StartAsync()
    {
        try
        {
            var services = await _client.GetServicesAsync(_projectId);
            Services = new ObservableCollection<string>(services.Services);
            if (services.ErrorPatterns.Count > 0)
            {
                _errorPatterns = CompilePatterns(services.ErrorPatterns);
            }
        }
        catch (AgentApiException)
        {
            // sem /services (projeto sem log_sources) — mantém padrões default, sem abas de serviço
        }

        await PollAsync();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static IReadOnlyList<Regex> CompilePatterns(IReadOnlyList<string> patterns)
    {
        var compiled = new List<Regex>();
        foreach (var pattern in patterns)
        {
            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase));
            }
            catch (ArgumentException)
            {
                // regex do Python que não é válida em .NET (ex: lookbehind exótico) — ignora, mesma tolerância do front
            }
        }
        return compiled;
    }

    private async Task PollAsync()
    {
        try
        {
            var logs = await _client.GetLogsAsync(_projectId, tail: 300, SelectedService);
            _rawLines = logs.Lines;
            Connected = true;
            ApplyFilter();
        }
        catch (AgentApiException)
        {
            Connected = false;
        }
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnOnlyErrorsChanged(bool value) => ApplyFilter();

    partial void OnSelectedServiceChanged(string? value) => _ = PollAsync();

    private bool IsError(string line) => _errorPatterns.Any(p => p.IsMatch(line));

    private void ApplyFilter()
    {
        var q = Query.Trim();
        IEnumerable<string> lines = _rawLines;
        if (OnlyErrors) lines = lines.Where(IsError);
        if (q.Length > 0) lines = lines.Where(l => l.Contains(q, StringComparison.OrdinalIgnoreCase));
        VisibleLines = new ObservableCollection<LogLineViewModel>(lines.Select(l => new LogLineViewModel(l, IsError(l))));
    }
}
