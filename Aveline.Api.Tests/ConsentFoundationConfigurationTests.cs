using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 0 EF Core model configuration for the consent foundation: the plaintext
/// <c>RevokeToken</c> removal, the additive privacy columns, the status index (0.4/0.5), and
/// the append-only <c>ConsentAuditEntry</c> table (0.6).
/// </summary>
public class ConsentFoundationConfigurationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConsentFoundationConfig_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// The read-optimized runtime model drops some relational metadata (notably
    /// <see cref="IIndex.IsDescending"/>), so the design-time model is the one to inspect.
    /// </summary>
    private static IModel DesignModel(AppDbContext context)
        => context.GetService<IDesignTimeModel>().Model;

    private static IReadOnlyList<IIndex> IndexesFor(AppDbContext context, Type entityType)
        => DesignModel(context).FindEntityType(entityType)!.GetIndexes().ToList();

    [Fact]
    public void CustomerConsent_NoLongerMapsThePlaintextRevokeToken()
    {
        // D-1 / 0.4: the column was declared, configured and never read or written.
        using var context = CreateContext();

        var property = DesignModel(context).FindEntityType(typeof(CustomerConsent))!
            .FindProperty("RevokeToken");

        Assert.Null(property);
    }

    [Theory]
    [InlineData("ConsentSource")]
    [InlineData("GlobalSubjectId")]
    [InlineData("DisclosureShownAt")]
    [InlineData("DisclosureVersion")]
    public void CustomerConsent_MapsThePrivacyColumns(string propertyName)
    {
        // 0.4: additive columns the disclosure flow and a future cross-org opt-out need.
        using var context = CreateContext();

        var property = DesignModel(context).FindEntityType(typeof(CustomerConsent))!
            .FindProperty(propertyName);

        Assert.NotNull(property);
    }

    [Fact]
    public void CustomerConsent_HasTheStatusIndex()
    {
        // 0.5: every metrics query is "count by status within an org".
        using var context = CreateContext();

        var indexes = IndexesFor(context, typeof(CustomerConsent));

        var statusIndex = indexes.SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "ConsentStatus" }));
        Assert.NotNull(statusIndex);
        Assert.False(statusIndex!.IsUnique);
    }

    [Fact]
    public void CustomerConsent_KeepsTheUniqueRowIndex()
    {
        // The `(OrganizationId, CustomerId)` unique index is the lookup path - it must survive.
        using var context = CreateContext();

        var indexes = IndexesFor(context, typeof(CustomerConsent));
        var unique = indexes.Single(i => i.IsUnique);

        Assert.Equal(new[] { "OrganizationId", "CustomerId" }, unique.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ConsentAuditEntry_MapsToTheExpectedTable()
    {
        // 0.6: the append-only consent history the single status row cannot express.
        using var context = CreateContext();

        var tableName = DesignModel(context).FindEntityType(typeof(ConsentAuditEntry))!.GetTableName();

        Assert.Equal("ConsentAuditEntries", tableName);
    }

    [Fact]
    public void ConsentAuditEntry_HasTheCustomerTimelineIndex()
    {
        using var context = CreateContext();

        var indexes = IndexesFor(context, typeof(ConsentAuditEntry));

        var timeline = indexes.SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "CustomerId", "CreatedAt" }));
        Assert.NotNull(timeline);
        Assert.Equal(new[] { false, false, true }, timeline!.IsDescending);
    }

    [Fact]
    public void ConsentAuditEntry_HasTheCrossOrgCustomerIndex()
    {
        // "Every consent event for this customer across orgs" is the erasure question.
        using var context = CreateContext();

        var indexes = IndexesFor(context, typeof(ConsentAuditEntry));

        var crossOrg = indexes.SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CustomerId", "CreatedAt" }));
        Assert.NotNull(crossOrg);
        Assert.Equal(new[] { false, true }, crossOrg!.IsDescending);
    }

    [Fact]
    public void ConsentAuditEntry_EvidenceJson_IsJsonbWithAnEmptyObjectDefault()
    {
        // Identifiers only, never message content - and an empty object rather than null so a
        // reader does not have to distinguish "no evidence" from "not an object".
        using var context = CreateContext();

        var property = DesignModel(context).FindEntityType(typeof(ConsentAuditEntry))!
            .FindProperty(nameof(ConsentAuditEntry.EvidenceJson));

        Assert.NotNull(property);
        Assert.Equal("jsonb", property!.FindAnnotation("Relational:ColumnType")?.Value?.ToString());
        Assert.Equal("{}", property.FindAnnotation("Relational:DefaultValue")?.Value?.ToString());
        Assert.True(property.IsNullable == false);
    }

    [Fact]
    public void ConsentAuditEntry_ForeignKeys_AreRestrictForTheOrgAndCascadeForTheCustomer()
    {
        // The audit row is *about* the customer, so erasure cascades; the organisation is a hard
        // boundary and can never drag consent history with it.
        using var context = CreateContext();
        var entity = DesignModel(context).FindEntityType(typeof(ConsentAuditEntry))!;

        var toOrganization = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Organization));
        var toCustomer = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Customer));

        Assert.Equal(DeleteBehavior.Restrict, toOrganization.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, toCustomer.DeleteBehavior);
    }

    [Theory]
    [InlineData(nameof(ConsentAuditEntry.Action), 32)]
    [InlineData(nameof(ConsentAuditEntry.PreviousStatus), 16)]
    [InlineData(nameof(ConsentAuditEntry.NewStatus), 16)]
    [InlineData(nameof(ConsentAuditEntry.Source), 16)]
    [InlineData(nameof(ConsentAuditEntry.ActorKind), 16)]
    [InlineData(nameof(ConsentAuditEntry.ActorRef), 128)]
    [InlineData(nameof(ConsentAuditEntry.IpHash), 64)]
    [InlineData(nameof(ConsentAuditEntry.UserAgent), 256)]
    public void ConsentAuditEntry_TextColumns_AreBounded(string propertyName, int maxLength)
    {
        using var context = CreateContext();

        var property = DesignModel(context).FindEntityType(typeof(ConsentAuditEntry))!
            .FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(maxLength, property!.GetMaxLength());
    }

    [Theory]
    [InlineData(AuditAction.ConsentRowCreated)]
    [InlineData(AuditAction.ConsentGranted)]
    [InlineData(AuditAction.ConsentRevoked)]
    [InlineData(AuditAction.ConsentRegranted)]
    [InlineData(AuditAction.DisclosureShown)]
    [InlineData(AuditAction.DataExportRequested)]
    [InlineData(AuditAction.DataExportCompleted)]
    [InlineData(AuditAction.DataDeletionRequested)]
    [InlineData(AuditAction.DataDeletionCompleted)]
    [InlineData(AuditAction.OtpIssued)]
    [InlineData(AuditAction.OtpVerified)]
    [InlineData(AuditAction.OtpFailed)]
    public void AuditAction_HasTheConsentAndPrivacyConstants(string action)
    {
        // 0.7: extend the canonical action list rather than inventing names at the call site.
        Assert.False(string.IsNullOrWhiteSpace(action));
        Assert.Contains(".", action);
        Assert.True(action.Length <= 100, "AuditLogEntry.Action is varchar(100).");
    }
}
