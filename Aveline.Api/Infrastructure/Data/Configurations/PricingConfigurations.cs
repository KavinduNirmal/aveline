using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aveline.Api.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for the effective-dated conversion rules (domain-model.md §3.1).
/// The GiST exclusion constraint that enforces non-overlap is added by migration M2,
/// because EF cannot express an exclusion constraint.
/// </summary>
public class BlossomConversionRuleConfiguration : IEntityTypeConfiguration<BlossomConversionRule>
{
    public void Configure(EntityTypeBuilder<BlossomConversionRule> builder)
    {
        builder.ToTable("BlossomConversionRules", table =>
        {
            table.HasCheckConstraint(
                "CK_BlossomConversionRules_Scope",
                "(\"ScopeKind\" = 'Global' AND \"Provider\" IS NULL AND \"Model\" IS NULL) OR " +
                "(\"ScopeKind\" = 'Provider' AND \"Provider\" IS NOT NULL AND \"Model\" IS NULL) OR " +
                "(\"ScopeKind\" = 'ProviderModel' AND \"Provider\" IS NOT NULL AND \"Model\" IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_BlossomConversionRules_Range",
                "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");

            table.HasCheckConstraint(
                "CK_BlossomConversionRules_Units",
                "\"UnitsPerBlossom\" > 0");

            table.HasCheckConstraint(
                "CK_BlossomConversionRules_Minimum",
                "\"MinimumChargeBlossoms\" >= 0");

            table.HasCheckConstraint(
                "CK_BlossomConversionRules_Decimals",
                "\"RoundingDecimals\" BETWEEN 0 AND 6");
        });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.ScopeKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(rule => rule.Provider)
            .HasMaxLength(64);

        builder.Property(rule => rule.Model)
            .HasMaxLength(128);

        builder.Property(rule => rule.MinimumChargeBlossoms)
            .HasPrecision(18, 4);

        builder.Property(rule => rule.RoundingMode)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(rule => rule.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(rule => rule.ChangeReason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(rule => rule.EffectiveFrom)
            .IsRequired();

        builder.Property(rule => rule.CreatedAt)
            .IsRequired();

        builder.Property(rule => rule.UpdatedAt)
            .IsRequired();

        // One rule per (scope, window start); NULLS NOT DISTINCT makes the nullable
        // Provider/Model columns participate in uniqueness (PostgreSQL 15+).
        builder.HasIndex(rule => new { rule.ScopeKind, rule.Provider, rule.Model, rule.EffectiveFrom })
            .IsUnique()
            .AreNullsDistinct(false);

        // Lookup path for resolving the active rule at a timestamp.
        builder.HasIndex(rule => new { rule.Status, rule.EffectiveFrom, rule.EffectiveTo })
            .HasFilter("\"Status\" = 'Active'");

        // No concurrency token exists anywhere else in the repository; the uint row-version
        // property maps to PostgreSQL's xmin system column (domain-model.md C-2).
        builder.Property(rule => rule.ConcurrencyToken)
            .IsRowVersion();
    }
}

/// <summary>EF Core mapping for the commercial price book (domain-model.md §3.2).</summary>
public class BlossomPriceEntryConfiguration : IEntityTypeConfiguration<BlossomPriceEntry>
{
    public void Configure(EntityTypeBuilder<BlossomPriceEntry> builder)
    {
        builder.ToTable("BlossomPriceEntries", table =>
        {
            table.HasCheckConstraint(
                "CK_BlossomPriceEntries_Quantity",
                "\"BlossomQuantity\" > 0");

            table.HasCheckConstraint(
                "CK_BlossomPriceEntries_Price",
                "\"PriceLkr\" >= 0");

            table.HasCheckConstraint(
                "CK_BlossomPriceEntries_Range",
                "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.PlanTier)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(entry => entry.SkuKind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(entry => entry.SkuCode)
            .HasMaxLength(64);

        builder.Property(entry => entry.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.BlossomQuantity)
            .HasPrecision(18, 4);

        builder.Property(entry => entry.PriceLkr)
            .HasPrecision(18, 2);

        builder.Property(entry => entry.ChangeReason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(entry => entry.EffectiveFrom)
            .IsRequired();

        builder.Property(entry => entry.CreatedAt)
            .IsRequired();

        builder.Property(entry => entry.UpdatedAt)
            .IsRequired();

        builder.HasIndex(entry => new
            {
                entry.PlanTier,
                entry.OrganizationId,
                entry.SkuKind,
                entry.SkuCode,
                entry.EffectiveFrom,
            })
            .IsUnique()
            .AreNullsDistinct(false);
    }
}
