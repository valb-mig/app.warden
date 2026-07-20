using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Warden.Admin.ViewModels;

namespace Warden.Admin;

/// <summary>Convenção padrão do template MVVM da Avalonia: `Warden.Admin.ViewModels.FooViewModel` → `Warden.Admin.Views.FooView`.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "(sem conteúdo)" };

        var name = data.GetType().FullName!
            .Replace("ViewModels", "Views")
            .Replace("ViewModel", "View");
        var type = Type.GetType(name);

        if (type is not null && Activator.CreateInstance(type) is Control control) return control;
        return new TextBlock { Text = $"View não encontrada: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
