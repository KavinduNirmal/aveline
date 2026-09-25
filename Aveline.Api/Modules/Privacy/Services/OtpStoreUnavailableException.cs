namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The distributed store that backs the OTP proof is unreachable. Raised instead of returning a
/// verdict because the correct response for every OTP operation is "refuse", never "allow"
/// (plan §9.2, DR-6). The endpoint maps it to <c>503</c>.
/// </summary>
public sealed class OtpStoreUnavailableException : Exception
{
    public OtpStoreUnavailableException(Exception inner)
        : base("The OTP store is unavailable, so the request was refused (fail closed).", inner)
    {
    }
}
