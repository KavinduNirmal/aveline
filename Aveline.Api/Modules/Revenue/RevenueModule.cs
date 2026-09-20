using Aveline.Api.Modules.Revenue.Services;

namespace Aveline.Api.Modules.Revenue;

/// <summary>
/// Dependency injection registration for the revenue module.
/// </summary>
/// <remarks>
/// There is no <c>MapRevenueEndpoints</c> yet: R1 lands the schema and its write rules only, and
/// the read and write routes arrive in R2 and R3. Registration is here from the start so those two
/// phases add endpoints to an already-wired module rather than touching <c>Program.cs</c> twice.
/// </remarks>
public static class RevenueModule
{
    public static IServiceCollection AddRevenueModule(this IServiceCollection services)
    {
        services.AddScoped<IIncomeLedgerService, IncomeLedgerService>();
        return services;
    }
}
