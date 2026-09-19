namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// A send named an attachment it may not bind.
/// </summary>
/// <remarks>
/// Raised when an id is foreign to the conversation, already bound to another message, unknown,
/// or when more than the per-message cap was named. The whole send is refused rather than
/// half-bound, so the uploads stay unbound and the sweep can collect them.
///
/// Derives from <see cref="Exception"/> rather than <see cref="InvalidOperationException"/>
/// because the send route already maps that to a `404`; this is a `400`.
/// </remarks>
public sealed class AttachmentBindingException : Exception
{
    public AttachmentBindingException(string message) : base(message)
    {
    }
}
