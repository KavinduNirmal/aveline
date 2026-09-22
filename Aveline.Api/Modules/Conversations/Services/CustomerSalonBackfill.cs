using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Creates the missing client Salon for every client that predates eager Salon creation.
/// </summary>
/// <remarks>
/// A client is visible in the Salon list only once its thread exists, because that list is
/// conversations. Clients created before the create path minted one are therefore present in the
/// client book and absent from the concierge view, with nothing in the UI to explain the gap.
///
/// This is a one-shot, idempotent repair rather than a schema migration: creating a Salon means
/// inserting the Aveline greeting as well, and that greeting is C# domain text. Duplicating it in
/// SQL would give repaired Salons a different opening message from new ones, which is the kind of
/// divergence a later edit silently widens.
/// </remarks>
public static class CustomerSalonBackfill
{
    /// <summary>How many clients one run will repair. A ceiling, not a target, so a large tenant cannot stall boot.</summary>
    public const int DefaultMaxCustomers = 500;

    /// <summary>
    /// Repairs up to <paramref name="maxCustomers"/> clients that have no Salon.
    /// </summary>
    /// <returns>The number of Salons created.</returns>
    public static async Task<int> RunAsync(
        AppDbContext db,
        IConversationService conversations,
        int maxCustomers,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Max(1, maxCustomers);

        // Soft-deleted clients are excluded by the global query filter on `Customer`, so a removed
        // client is never resurrected as a thread.
        var customers = await db.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.CreatedAt)
            .Select(customer => new { customer.Id, customer.OrganizationId })
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
        {
            return 0;
        }

        var covered = await db.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.Kind == ConversationKind.Salon
                                   && conversation.CustomerId != null)
            .Select(conversation => conversation.CustomerId!.Value)
            .ToListAsync(cancellationToken);
        var coveredIds = covered.ToHashSet();

        var missing = customers.Where(c => !coveredIds.Contains(c.Id)).Take(limit).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var created = 0;
        foreach (var customer in missing)
        {
            // The Salon carries the client's own organization; `EnsureCustomerSalonAsync` matches the
            // existing thread first, so a concurrent insert or a second boot cannot mint a duplicate.
            if (await conversations.EnsureCustomerSalonAsync(
                    customer.OrganizationId, customer.Id, cancellationToken))
            {
                created++;
            }
        }

        logger.LogInformation(
            "Client Salon repair created {Created} of {Missing} missing Salons.",
            created,
            missing.Count);

        return created;
    }
}

/// <summary>
/// Runs <see cref="CustomerSalonBackfill"/> once at startup and logs the outcome. Failures are
/// logged rather than thrown: a repair that cannot reach the database must not stop the API from
/// serving the rest of its surface.
/// </summary>
public sealed class CustomerSalonBackfillJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CustomerSalonBackfillJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var maxCustomers = configuration.GetValue(
                "Conversations:CustomerSalonBackfillMaxCustomers",
                CustomerSalonBackfill.DefaultMaxCustomers);

            var created = await CustomerSalonBackfill.RunAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<IConversationService>(),
                maxCustomers,
                logger,
                stoppingToken);

            if (created > 0)
            {
                logger.LogInformation("Repaired {Count} client Salons on startup.", created);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The client Salon repair did not complete.");
        }
    }
}
