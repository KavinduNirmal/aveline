namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Signs and verifies the permanent opt-out link (plan §5.1, DR-2). The link is stateless: there is
/// no per-customer row and no timestamp in the signed payload, so it never expires and cannot be
/// enumerated.
/// </summary>
/// <remarks>
/// <para>
/// <b>The canonical payload is <c>v{version}|{organizationId}</c></b> (for the shipped version,
/// <c>v1|{organizationId}</c>). The version is inside the signature, so changing <c>v</c> in the
/// URL invalidates the link rather than reinterpreting it under a new contract.
/// </para>
/// <para>
/// <b>No phone number.</b> The link proves <i>which boutique</i>; the OTP proof in the opt-out flow
/// proves <i>which number</i> (DR-2). Putting an E.164 number in a URL that WhatsApp may cache and
/// the customer may forward would leak PII for no security gain.
/// </para>
/// </remarks>
public interface IPrivacyLinkSigner
{
    /// <summary>
    /// False when <c>Privacy:LinkSigningKey</c> is not configured. A caller must then skip the
    /// disclosure rather than build an unsigned link.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Returns the URL-safe base64 HMAC-SHA256 signature for the organization.</summary>
    /// <exception cref="PrivacyLinkNotConfiguredException">The signing key is not configured.</exception>
    string Sign(Guid organizationId, string? version = null);

    /// <summary>
    /// True only when <paramref name="signature"/> is the HMAC of the canonical payload for the
    /// supplied organization and version. A tampered <c>o</c>, a tampered <c>v</c>, a tampered
    /// signature or a missing signature all answer false.
    /// </summary>
    bool Verify(Guid organizationId, string? version, string? signature);

    /// <summary>
    /// Builds <c>{web_host}/privacy/opt-out?o={org}&amp;v=1&amp;s={hmac}</c>. Returns a relative URL
    /// when <c>App:BaseUrl</c> is not configured, which is what a Development host without a web
    /// origin would do; the signature is still present.
    /// </summary>
    string BuildOptOutUrl(Guid organizationId);

    /// <summary>Builds the data-policy URL the disclosure names: <c>{web_host}/privacy?org={slug}</c>.</summary>
    string BuildDataPolicyUrl(string organizationSlug);
}
