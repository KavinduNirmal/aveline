namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Raised when a privacy link is requested but <c>Privacy:LinkSigningKey</c> is not configured.
/// This is a configuration failure the disclosure path treats as "skip and retry on the next
/// inbound message": it must never produce an unsigned link and must never take the webhook down.
/// </summary>
public sealed class PrivacyLinkNotConfiguredException : InvalidOperationException
{
    public PrivacyLinkNotConfiguredException()
        : base(
            $"{Services.PrivacyLinkSigner.ConfigKey} is not configured, so no opt-out link can be "
            + "signed. Set it to a base64-encoded key of at least 32 bytes "
            + "(generate with `openssl rand -base64 32`).")
    {
    }
}
