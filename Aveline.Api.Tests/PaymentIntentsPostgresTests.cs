using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// The PostgreSQL-backed invariants of the two payment tables (plan §6.3). The in-memory provider
/// cannot exercise a named CHECK, a filtered unique index, or the <c>xmin</c> row version, so this
/// class applies the real migration to a real container and asserts each one by exercising the
/// violation rather than by reading the EF model.
/// </summary>
public class PaymentIntentsPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
        _orgId = await SeedOrganizationAsync(context);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task<Guid> SeedOrganizationAsync(AppDbContext context)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"pay_{ownerId:N}", Email = "pay@aveline.lk",
            FirstName = "Pay", LastName = "Owner", Username = $"pay_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Payments Pg Boutique", Slug = $"pay-pg-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private PaymentIntent Intent(
        string? providerIntentId = null,
        string? idempotencyKey = null,
        PaymentPurpose purpose = PaymentPurpose.BlossomTopUp,
        PaymentProviderStatus status = PaymentProviderStatus.RequiresAction,
        long amountMinor = 350000,
        string? skuCode = "blossom_pack_100",
        decimal? blossomQuantity = 100m,
        string provider = "manual",
        DateTime? settledAt = null,
        Guid? organizationId = null) => new()
        {
            OrganizationId = organizationId ?? _orgId,
            Provider = provider,
            ProviderIntentId = providerIntentId,
            Purpose = purpose,
            Status = status,
            AmountMinor = amountMinor,
            Currency = "LKR",
            PriceLkr = 3500m,
            SkuCode = skuCode,
            BlossomQuantity = blossomQuantity,
            IdempotencyKey = idempotencyKey ?? $"idem-{Guid.NewGuid():N}",
            Description = "Migration constraint test intent.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SettledAt = settledAt,
        };

    private PaymentProviderEvent ProviderEvent(
        string provider = "manual",
        string? providerEventId = null,
        long? amountMinor = 350000,
        DateTime? processedAt = null) => new()
        {
            Provider = provider,
            ProviderEventId = providerEventId ?? $"evt-{Guid.NewGuid():N}",
            EventType = PaymentWebhookEventType.IntentSucceeded,
            ProviderIntentId = "manual_intent",
            AmountMinor = amountMinor,
            Currency = "LKR",
            OccurredAt = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow,
            RawPayload = "{}",
            ProcessedAt = processedAt,
        };

    [Fact]
    public async Task TheMigrationAppliesAndBothTablesExist()
    {
        await using var context = new AppDbContext(_options);

        var intents = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"PaymentIntents\"")
            .SingleAsync();
        var events = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"PaymentProviderEvents\"")
            .SingleAsync();

        Assert.Equal(0, intents);
        Assert.Equal(0, events);
    }

    /// <summary>
    /// Every named index from plan §6.3 is present in PostgreSQL under its exact name. The two
    /// non-unique indexes cannot be exercised by a violation, so they are asserted against the
    /// database catalog rather than the EF model.
    /// </summary>
    [Fact]
    public async Task EveryNamedIndexExists()
    {
        await using var context = new AppDbContext(_options);

        var names = await context.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public'")
            .ToListAsync();

        names.Should().Contain(new[]
        {
            "IX_PaymentIntents_Org",
            "IX_PaymentIntents_Provider_IntentId",
            "IX_PaymentIntents_Org_Purpose_IdempotencyKey",
            "IX_PaymentProviderEvents_Provider_EventId",
            "IX_PaymentProviderEvents_ProviderIntentId",
            "IX_PaymentProviderEvents_Unprocessed",
        });
    }

    [Fact]
    public async Task TheUnprocessedIndex_IsPartialOnANullProcessedAt()
    {
        await using var context = new AppDbContext(_options);

        var definition = await context.Database
            .SqlQuery<string>(
                $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'IX_PaymentProviderEvents_Unprocessed'")
            .SingleAsync();

        definition.Should().Contain("ProcessedAt");
        definition.Should().Contain("IS NULL");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TheAmountCheckConstraint_RejectsANonPositiveAmount(long amountMinor)
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(providerIntentId: $"pi_{Guid.NewGuid():N}", amountMinor: amountMinor));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertConstraint(exception, "23514", "CK_PaymentIntents_Amount");
    }

    [Fact]
    public async Task TheTopUpShapeCheckConstraint_RejectsATopUpWithoutASkuCode()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}", skuCode: null, blossomQuantity: 100m));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertConstraint(exception, "23514", "CK_PaymentIntents_TopUpShape");
    }

    [Fact]
    public async Task TheTopUpShapeCheckConstraint_RejectsATopUpWithoutABlossomQuantity()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}", skuCode: "blossom_pack_100", blossomQuantity: null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertConstraint(exception, "23514", "CK_PaymentIntents_TopUpShape");
    }

    [Fact]
    public async Task TheTopUpShapeCheckConstraint_AllowsAPurposeThatIsNotATopUp()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}",
            purpose: PaymentPurpose.SubscriptionInitial,
            skuCode: null,
            blossomQuantity: null));

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.PaymentIntents.CountAsync());
    }

    /// <summary>
    /// The constraint that prevents the class of bug this plan exists to remove: an intent that says
    /// it succeeded with no settlement timestamp.
    /// </summary>
    [Fact]
    public async Task TheSettledCheckConstraint_RejectsSucceededWithoutASettledAt()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}",
            status: PaymentProviderStatus.Succeeded,
            settledAt: null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertConstraint(exception, "23514", "CK_PaymentIntents_Settled");
    }

    [Fact]
    public async Task TheSettledCheckConstraint_AllowsSucceededWithASettledAt()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}",
            status: PaymentProviderStatus.Succeeded,
            settledAt: DateTime.UtcNow));

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.PaymentIntents.CountAsync());
    }

    [Fact]
    public async Task TheProviderIntentIndex_RejectsASecondIntentForTheSameProviderAndProviderIntentId()
    {
        var providerIntentId = $"pi_{Guid.NewGuid():N}";
        await using (var first = new AppDbContext(_options))
        {
            first.PaymentIntents.Add(Intent(providerIntentId: providerIntentId));
            await first.SaveChangesAsync();
        }

        await using var second = new AppDbContext(_options);
        second.PaymentIntents.Add(Intent(providerIntentId: providerIntentId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        AssertConstraint(exception, "23505", "IX_PaymentIntents_Provider_IntentId");
    }

    [Fact]
    public async Task TheProviderIntentIndex_AllowsTheSameProviderIntentIdUnderADifferentProvider()
    {
        var providerIntentId = $"pi_{Guid.NewGuid():N}";
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(providerIntentId: providerIntentId, provider: "manual"));
        context.PaymentIntents.Add(Intent(providerIntentId: providerIntentId, provider: "mock"));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentIntents.CountAsync());
    }

    /// <summary>The index is partial on a non-null provider intent id, so two unsent intents do not
    /// collide with each other.</summary>
    [Fact]
    public async Task TheProviderIntentIndex_IsPartialOnANullProviderIntentId()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(providerIntentId: null));
        context.PaymentIntents.Add(Intent(providerIntentId: null));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentIntents.CountAsync());
    }

    [Fact]
    public async Task TheIdempotencyKeyIndex_RejectsASecondIntentForTheSameOrgPurposeAndKey()
    {
        var key = $"idem-{Guid.NewGuid():N}";
        await using (var first = new AppDbContext(_options))
        {
            first.PaymentIntents.Add(Intent(
                providerIntentId: $"pi_{Guid.NewGuid():N}", idempotencyKey: key));
            await first.SaveChangesAsync();
        }

        await using var second = new AppDbContext(_options);
        second.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}", idempotencyKey: key));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        AssertConstraint(exception, "23505", "IX_PaymentIntents_Org_Purpose_IdempotencyKey");
    }

    [Fact]
    public async Task TheIdempotencyKeyIndex_AllowsTheSameKeyForADifferentPurpose()
    {
        var key = $"idem-{Guid.NewGuid():N}";
        await using var context = new AppDbContext(_options);
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}", idempotencyKey: key, purpose: PaymentPurpose.BlossomTopUp));
        context.PaymentIntents.Add(Intent(
            providerIntentId: $"pi_{Guid.NewGuid():N}", idempotencyKey: key,
            purpose: PaymentPurpose.SubscriptionInitial, skuCode: null, blossomQuantity: null));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentIntents.CountAsync());
    }

    [Fact]
    public async Task TheIdempotencyKeyIndex_IsPartialOnANullKey()
    {
        await using var context = new AppDbContext(_options);
        var first = Intent(providerIntentId: $"pi_{Guid.NewGuid():N}");
        first.IdempotencyKey = null;
        var second = Intent(providerIntentId: $"pi_{Guid.NewGuid():N}");
        second.IdempotencyKey = null;
        context.PaymentIntents.AddRange(first, second);

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentIntents.CountAsync());
    }

    [Fact]
    public async Task TheProviderEventIndex_RejectsAReplayedEvent()
    {
        var eventId = $"evt-{Guid.NewGuid():N}";
        await using (var first = new AppDbContext(_options))
        {
            first.PaymentProviderEvents.Add(ProviderEvent(providerEventId: eventId));
            await first.SaveChangesAsync();
        }

        await using var second = new AppDbContext(_options);
        second.PaymentProviderEvents.Add(ProviderEvent(providerEventId: eventId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        AssertConstraint(exception, "23505", "IX_PaymentProviderEvents_Provider_EventId");
    }

    [Fact]
    public async Task TheProviderEventIndex_AllowsTheSameEventIdForADifferentProvider()
    {
        var eventId = $"evt-{Guid.NewGuid():N}";
        await using var context = new AppDbContext(_options);
        context.PaymentProviderEvents.Add(ProviderEvent(provider: "manual", providerEventId: eventId));
        context.PaymentProviderEvents.Add(ProviderEvent(provider: "mock", providerEventId: eventId));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.PaymentProviderEvents.CountAsync());
    }

    [Fact]
    public async Task TheProviderEventAmountCheckConstraint_RejectsANegativeAmount()
    {
        await using var context = new AppDbContext(_options);
        context.PaymentProviderEvents.Add(ProviderEvent(amountMinor: -1));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertConstraint(exception, "23514", "CK_PaymentProviderEvents_Amount");
    }

    /// <summary>
    /// The intent's <c>xmin</c> row version is what makes two concurrent confirms lose one rather
    /// than double-settle (plan §6.6).
    /// </summary>
    [Fact]
    public async Task XminConcurrencyToken_RaisesOnAStaleUpdate()
    {
        var intentId = Guid.CreateVersion7();
        await using (var seed = new AppDbContext(_options))
        {
            var intent = Intent(providerIntentId: $"pi_{Guid.NewGuid():N}");
            intent.Id = intentId;
            seed.PaymentIntents.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using var first = new AppDbContext(_options);
        await using var second = new AppDbContext(_options);

        var firstIntent = await first.PaymentIntents.SingleAsync(i => i.Id == intentId);
        var secondIntent = await second.PaymentIntents.SingleAsync(i => i.Id == intentId);

        firstIntent.Description = "First writer.";
        firstIntent.UpdatedAt = DateTime.UtcNow;
        await first.SaveChangesAsync();

        secondIntent.Description = "Second writer.";
        secondIntent.UpdatedAt = DateTime.UtcNow;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static void AssertConstraint(Exception exception, string sqlState, string constraintName)
    {
        var postgres = FindPostgresException(exception);
        Assert.NotNull(postgres);
        Assert.Equal(sqlState, postgres!.SqlState);
        Assert.Equal(constraintName, postgres.ConstraintName);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }
        return null;
    }
}
