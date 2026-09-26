using Aveline.Api.Modules.Payments.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the provider-neutral payment intent (plan §6.3). Modelled on
/// <see cref="IncomeLedgerEntryConfiguration"/>: enum-as-string, an explicit precision, named check
/// constraints declared in the model, and **filtered unique** indexes as the dedup identities.
/// </summary>
public class PaymentIntentConfiguration : IEntityTypeConfiguration<PaymentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentIntent> builder)
    {
        builder.ToTable("PaymentIntents", table =>
        {
            // Amount is integer minor units and a charge is always positive.
            table.HasCheckConstraint("CK_PaymentIntents_Amount", "\"AmountMinor\" > 0");

            // A top-up intent must carry the SKU and quantity its settlement will grant, so a
            // settlement can never have to guess what was bought.
            table.HasCheckConstraint(
                "CK_PaymentIntents_TopUpShape",
                "(\"Purpose\" <> 'BlossomTopUp' OR (\"SkuCode\" IS NOT NULL AND \"BlossomQuantity\" IS NOT NULL))");

            // The constraint that prevents the class of bug this abstraction exists to remove: an
            // intent that says it succeeded with no settlement timestamp. This is the Commerce
            // `Status = "confirmed"` problem expressed as a schema rule.
            table.HasCheckConstraint(
                "CK_PaymentIntents_Settled",
                "(\"Status\" <> 'Succeeded' OR \"SettledAt\" IS NOT NULL)");
        });

        builder.HasKey(intent => intent.Id);

        builder.Property(intent => intent.Provider)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(intent => intent.ProviderIntentId)
            .HasMaxLength(128);

        builder.Property(intent => intent.Purpose)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(intent => intent.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(intent => intent.AmountMinor)
            .IsRequired();

        builder.Property(intent => intent.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(intent => intent.PriceLkr)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(intent => intent.SkuCode)
            .HasMaxLength(64);

        builder.Property(intent => intent.BlossomQuantity)
            .HasPrecision(18, 4);

        builder.Property(intent => intent.PlanTier)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(intent => intent.ProviderSubscriptionId)
            .HasMaxLength(128);

        builder.Property(intent => intent.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(intent => intent.Description)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(intent => intent.FailureCode)
            .HasMaxLength(64);

        builder.Property(intent => intent.FailureMessage)
            .HasMaxLength(500);

        builder.Property(intent => intent.CreatedAt)
            .IsRequired();

        builder.Property(intent => intent.UpdatedAt)
            .IsRequired();

        builder.Property(intent => intent.ExternalRef)
            .HasMaxLength(128);

        builder.Property(intent => intent.ConcurrencyToken)
            .IsRowVersion();

        builder.HasOne<Modules.Organizations.Models.Organization>()
            .WithMany()
            .HasForeignKey(intent => intent.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(intent => intent.OrganizationId)
            .HasDatabaseName("IX_PaymentIntents_Org");

        // The provider intent identity. Partial on a non-null provider id, because an intent that has
        // not reached the provider yet has no provider identity to dedupe on. The index also serves
        // lookups by `Provider` alone, since it is the leading column.
        builder.HasIndex(intent => new { intent.Provider, intent.ProviderIntentId })
            .IsUnique()
            .HasFilter("\"ProviderIntentId\" IS NOT NULL")
            .HasDatabaseName("IX_PaymentIntents_Provider_IntentId");

        // The request dedup identity: one intent per organisation, purpose, and client key.
        builder.HasIndex(intent => new { intent.OrganizationId, intent.Purpose, intent.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("IX_PaymentIntents_Org_Purpose_IdempotencyKey");
    }
}
