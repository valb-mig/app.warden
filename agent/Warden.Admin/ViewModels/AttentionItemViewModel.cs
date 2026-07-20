using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Warden.Admin.ViewModels;

/// <summary>Item do banner "Precisa de atenção" do dashboard — espelha o `attentionItems` do `page.tsx`, mais um caso próprio do Admin (projeto aguardando aprovação).</summary>
public sealed partial class AttentionItemViewModel(string key, string projectId, string label, Action<string> openProject, Action<string> dismiss) : ObservableObject
{
    public string Key { get; } = key;
    public string ProjectId { get; } = projectId;
    public string Label { get; } = label;

    [RelayCommand]
    private void Open() => openProject(ProjectId);

    [RelayCommand]
    private void Dismiss() => dismiss(Key);
}
