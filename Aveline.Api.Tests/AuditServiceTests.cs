using System.Reflection;
using Aveline.Api.Common.Middleware;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #177 — audit write path. Entries are persisted, secrets are redacted, and a
/// failure to write an audit row never fails the caller's non-critical request.
/// </summary>
public class AuditServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Audit_{Guid.NewGuid()}")
            .Options);

    private static AuditService CreateService(
        AppDbContext context,
        HttpContext? httpContext = null,
        IAuditRepository? repository = null,
        ILogger<AuditService>? logger = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new AuditService(
            repository ?? new AuditRepository(context),
            new AuditRedactor(),
            accessor,
            logger ?? new TestLogger<AuditService>());
    }

    [Fact]
    public async Task RecordAsync_PersistsEntry_WithRedactedPayloads()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new DefaultHttpContext());

        await service.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.PricingRuleCreated,
            EntityType: "BlossomConversionRule",
            EntityId: "rule-1",
            Before: null,
            After: new { unitsPerBlossom = 1000, secretToken = "leak-me" }));

        var entry = await context.AuditLogEntries.SingleAsync();
        Assert.Equal(AuditAction.PricingRuleCreated, entry.Action);
        Assert.Equal("BlossomConversionRule", entry.EntityType);
        Assert.Equal("rule-1", entry.EntityId);
        Assert.NotNull(entry.AfterJson);
        Assert.DoesNotContain("leak-me", entry.AfterJson);
        Assert.Contains("[REDACTED]", entry.AfterJson);
        Assert.Contains("1000", entry.AfterJson);
    }

    [Fact]
    public async Task RecordAsync_EnrichesCorrelationId_FromHttpContext()
    {
        await using var context = CreateContext();
        var http = new DefaultHttpContext();
        http.Items[CorrelationIdMiddleware.RequestIdItemKey] = "req-abc";
        var service = CreateService(context, http);

        await service.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.PricingRuleActivated,
            EntityType: "BlossomConversionRule",
            EntityId: "rule-1"));

        var entry = await context.AuditLogEntries.SingleAsync();
        Assert.Equal("req-abc", entry.RequestId);
    }

    [Fact]
    public async Task RecordAsync_WhenRepositoryThrows_DoesNotThrow()
    {
        await using var context = CreateContext();
        var logger = new TestLogger<AuditService>();
        var service = CreateService(
            context,
            new DefaultHttpContext(),
            repository: new ThrowingAuditRepository(),
            logger: logger);

        var exception = await Record.ExceptionAsync(() => service.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.PricingRuleCancelled,
            EntityType: "BlossomConversionRule",
            EntityId: "rule-1")));

        Assert.Null(exception);
        Assert.Contains(logger.Logs, log => log.Level == LogLevel.Error);
    }

    [Fact]
    public void AuditRepository_IsAppendOnly()
    {
        var methods = typeof(IAuditRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(methods, name =>
            name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ThrowingAuditRepository : IAuditRepository
    {
        public Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store unavailable");

        public Task<AuditLogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store unavailable");

        public Task<(IReadOnlyList<AuditLogEntry> Items, int Total)> QueryAsync(
            string? action,
            string? entityType,
            string? entityId,
            Guid? organizationId,
            Guid? actorUserId,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store unavailable");
    }
}
