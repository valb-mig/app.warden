using CommunityToolkit.Mvvm.ComponentModel;

namespace Warden.Admin.ViewModels;

/// <summary>
/// Base de toda página/card navegável. `OnActivated`/`OnDeactivated` dão um ciclo de vida simples
/// pra polling (status/git/vitals) — só a página visível no momento deve estar com timers rodando,
/// evitar que o dashboard e o detalhe de projeto fiquem batendo no socket ao mesmo tempo por trás
/// das cortinas quando só um dos dois está na tela.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    public virtual void OnActivated()
    {
    }

    public virtual void OnDeactivated()
    {
    }
}
