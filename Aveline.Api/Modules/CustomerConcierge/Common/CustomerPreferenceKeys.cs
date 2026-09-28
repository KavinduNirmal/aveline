namespace Aveline.Api.Modules.CustomerConcierge.Common;

/// <summary>
/// The canonical <c>CustomerPreference.PreferenceKey</c> values this module derives and reads back.
/// </summary>
/// <remarks>
/// A key is written into the row, so a second literal is a second thing to keep in step: the
/// nickname key was declared privately in <c>CustomerTenantService</c> and the interaction brief
/// built its summary without knowing about it, which put the internal key on the staff screen as
/// though the customer had stated it. One definition, shared by every reader and writer.
/// </remarks>
internal static class CustomerPreferenceKeys
{
    /// <summary>
    /// The nickname mirror. <c>Customer.Nickname</c> is the field of record; this preference row is
    /// the derived copy the book and brief read back. It is an internal key and must never be
    /// rendered as a customer-stated preference.
    /// </summary>
    internal const string Nickname = "nickname";
}
