using Aveline.Api.Modules.Payments.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the webhook inbox (plan §6.3). The unique <c>(Provider, ProviderEventId)</c>
/// index is the replay guard, and the filtered <c>ReceivedAt</c> index is the unprocessed backlog.
/// </summary>
public class PaymentProviderEventConfiguration : IEntityTypeConfiguration<PaymentProviderEvent>
{
    public void Configure(EntityTypeBuilder<PaymentProviderEvent> builder)
    {
        builder.ToTable("PaymentProviderEvents", table =>
        {
            // Mirroring the intent's amount rule: an amount is either absent or non-negative.
            table.HasCheckConstraint(
                "CK_PaymentProviderEvents_Amount",
                "(\"AmountMinor\" IS NULL OR \"AmountMinor\" >= 0)");
        });

        builder.HasKey(providerEvent => providerEvent.Id);

        builder.Property(providerEvent => providerEvent.Provider)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProviderEventId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.EventType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProviderIntentId)
            .HasMaxLength(128);

        builder.Property(providerEvent => providerEvent.Currency)
            .HasMaxLength(3);

        builder.Property(providerEvent => providerEvent.OccurredAt)
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ReceivedAt)
            .IsRequired();

        // jsonb keeps the raw payload queryable for forensics without re-parsing free text, matching
        // IdempotencyRecords.ResponseBodyJson.
        builder.Property(providerEvent => providerEvent.RawPayload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(providerEvent => providerEvent.ProcessingError)
            .HasMaxLength(500);

        // The replay guard: one row per provider event id.
        builder.HasIndex(providerEvent => new { providerEvent.Provider, providerEvent.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("IX_PaymentProviderEvents_Provider_EventId");

        // Plan §6.3 marks ProviderIntentId as indexed; the provider-event index above does not lead
        // with it, so settlement needs its own.
        builder.HasIndex(providerEvent => providerEvent.ProviderIntentId)
            .HasDatabaseName("IX_PaymentProviderEvents_ProviderIntentId");

        // The unprocessed backlog: partial, because processed events are not the queue.
        builder.HasIndex(providerEvent => providerEvent.ReceivedAt)
            .HasFilter("\"ProcessedAt\" IS NULL")
            .HasDatabaseName("IX_PaymentProviderEvents_Unprocessed");
    }
}
