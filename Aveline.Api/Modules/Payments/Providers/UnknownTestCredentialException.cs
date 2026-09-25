using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Providers;

/// <summary>
/// A mock test credential outside the closed table of plan §7.2. Mock-only: a real adapter never
/// accepts a client-supplied credential string, so this failure exists nowhere else.
/// </summary>
/// <remarks>
/// Maps to <c>400 unknown-test-credential</c>. It derives from <see cref="PaymentDomainException"/>
/// so the endpoint layer's single switch maps the whole payment family and this member needs no
/// special case.
/// </remarks>
internal sealed class UnknownTestCredentialException(string credential)
    : PaymentDomainException(
        $"'{credential}' is not a recognised mock test credential. Use one of the documented "
        + "Stripe-compatible test cards or an 'tok_aveline_*' scenario token.")
{
    public override int? StatusCode => 400;

    public override string? ErrorCode => "unknown-test-credential";
}
