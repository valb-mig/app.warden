namespace Warden.Domain.Trust;

/// <summary>Ação/start bloqueado porque o manifesto do projeto não está com status <see cref="TrustStatus.Approved"/>.</summary>
public sealed class ManifestNotApprovedException(string message) : Exception(message);

/// <summary>Ação destrutiva chamada sem <c>confirm=true</c> — mesma semântica do `ConfirmationRequired` do engine Python.</summary>
public sealed class ConfirmationRequiredException(string message) : Exception(message);

/// <summary>Ação marcada como interativa, que não é suportada via execução síncrona da API.</summary>
public sealed class ActionInteractiveException(string message) : Exception(message);
