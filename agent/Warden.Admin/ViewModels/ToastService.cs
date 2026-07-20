namespace Warden.Admin.ViewModels;

public sealed record ToastMessage(string Text, bool IsError);

/// <summary>
/// Barramento global mínimo pra notificações transitórias (equivalente ao `sonner` do front Next.js).
/// Estático de propósito: o único assinante real é o `ShellViewModel`, que vive por toda a sessão da
/// janela — não há risco de vazamento de assinatura nem motivo pra um messenger com escopo/DI aqui.
/// </summary>
public static class ToastService
{
    public static event Action<ToastMessage>? Raised;

    public static void Show(string text, bool isError = false) => Raised?.Invoke(new ToastMessage(text, isError));
}
