using CloudinaryDotNet.Actions;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The strategy's resource-type token — <c>image</c> for a photo, <c>raw</c> for a PDF — and its
/// SDK enum. Shared by the storage adapter (which writes the storage key) and the gateway (which
/// reads a key back), so the two can never disagree about the encoding.
/// </summary>
internal static class CloudinaryResourceTypes
{
    public const string Image = "image";
    public const string Raw = "raw";

    /// <summary>The SDK enum for a stored key's resource type.</summary>
    public static ResourceType Parse(string resourceType) => resourceType switch
    {
        Image => ResourceType.Image,
        Raw => ResourceType.Raw,
        _ => throw new MediaStorageException($"Unsupported Cloudinary resource type '{resourceType}'."),
    };
}
