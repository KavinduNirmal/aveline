namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// The SignOff message (or its conversation) is not in this organization, or is not visible to
/// the caller.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing callers that treat any
/// service refusal as one still work, while the endpoints can distinguish it and answer the
/// <c>404</c> their route already advertises instead of a <c>400</c>.
/// </remarks>
public sealed class SignOffNotFoundException : InvalidOperationException
{
    public SignOffNotFoundException(string message) : base(message)
    {
    }
}
