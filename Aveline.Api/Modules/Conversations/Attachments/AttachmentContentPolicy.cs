using Aveline.Api.Common.Media;

namespace Aveline.Api.Modules.Conversations.Attachments;

/// <summary>
/// The conversations write paths' content-type decision: the allow-list resolution plus the
/// magic-byte sniff (strategy §3.6, salon §7.2). Both the staff-upload route and the inbound
/// webhook call this one method, so a mislabelled body is refused identically on both paths.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MediaContentTypes.Resolve"/> trusts the declared type and then the file extension, so
/// a <c>.png</c> claimed over a non-image body is admitted by resolution alone. The sniff closes
/// that: the bytes are authoritative, and the sniffed value is what gets stored, because the vision
/// provider detects format from bytes as well.
/// </para>
/// <para>
/// There is no excepted arm. <see cref="MediaContentTypes.Sniff"/> recognises the image allow-list
/// <em>and</em> <c>application/pdf</c>, and everything it returns is a member of
/// <see cref="MediaContentTypes.Allowed"/>. A real PDF is confirmed by its <c>%PDF-</c> header; a
/// non-PDF body claiming <c>application/pdf</c> is refused, and a real image claiming
/// <c>application/pdf</c> is stored as the image its bytes say it is.
/// </para>
/// </remarks>
public static class AttachmentContentPolicy
{
    /// <summary>
    /// The content type to store for an uploaded body, or <c>null</c> when it may not be stored:
    /// a type outside the allow-list, or an image/PDF claim the bytes do not support.
    /// </summary>
    /// <param name="declaredType">The uploader's declared media type, when there is one.</param>
    /// <param name="fileName">The caller's file name, used only to rescue a generic declared type.</param>
    /// <param name="bytes">The bytes as received; read for their signature and never mutated.</param>
    public static string? ResolveForStorage(string? declaredType, string? fileName, byte[]? bytes)
    {
        // The allow-list gate is unchanged: an uploader who declared a disallowed type is refused
        // even when the bytes look storable. What the bytes then say the file is decides the rest.
        if (MediaContentTypes.Resolve(declaredType, fileName) is null)
        {
            return null;
        }

        return MediaContentTypes.Sniff(bytes);
    }
}
