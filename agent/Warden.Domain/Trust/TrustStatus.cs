namespace Warden.Domain.Trust;

public enum TrustStatus
{
    /// <summary>Projeto nunca foi aprovado pelo Admin — botões existem no manifesto mas ficam desabilitados.</summary>
    NeverApproved,

    /// <summary>Já foi aprovado antes, mas o conteúdo resolvido mudou desde então — precisa de reaprovação.</summary>
    PendingReview,

    /// <summary>Digest atual bate com o último aprovado — botões liberados pra execução.</summary>
    Approved,
}
