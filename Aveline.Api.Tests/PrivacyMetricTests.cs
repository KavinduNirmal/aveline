using System.Diagnostics.Metrics;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Jobs;
using Aveline.Api.Modules.CustomerConcierge.Metrics;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Privacy.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Privacy plan §11 Phase 6 item 6.3: the rights and opt-out/OTP counters the plan's §9.1 table
/// names, registered in the one catalog that authors a Prometheus name
/// (<c>MetricsCatalog</c>, plan §6.1) and exercised so the declaration is not vacuous.
///
/// The processing counter (<c>aveline_message_skip_total{reason}</c>) and the outbound family
/// already exist from Phases 1 and 2; the tests here only assert that phase 6 registered them, so
/// a name cannot be authored twice.
/// </summary>
public class PrivacyMetricTests
{
    // ----- rights family ----------------------------------------------------------------------

    [Fact]
    public void ExportRequestsEmitsTheCounterLabelledByStatus()
    {
        var observed = ObserveLong(
            RightsMetrics.MeterName,
            RightsMetrics.ExportRequestsMetricName,
            out var listener);

        using (listener)
        using (var metrics = new RightsMetrics())
        {
            metrics.RecordExportRequest(DataSubjectOutcomes.Completed);
            metrics.RecordExportRequest(DataSubjectOutcomes.NotFound);
            metrics.RecordExportRequest(DataSubjectOutcomes.VerificationFailed);
            listener.RecordObservableInstruments();

            Assert.Contains(observed, o => o is { Status: "completed", Value: 1 });
            Assert.Contains(observed, o => o is { Status: "not_found", Value: 1 });
            Assert.Contains(observed, o => o is { Status: "verification_failed", Value: 1 });
        }
    }

    [Fact]
    public void DeleteRequestsEmitsTheCounterLabelledByStatus()
    {
        var observed = ObserveLong(
            RightsMetrics.MeterName,
            RightsMetrics.DeleteRequestsMetricName,
            out var listener);

        using (listener)
        using (var metrics = new RightsMetrics())
        {
            metrics.RecordDeleteRequest(DataSubjectOutcomes.Completed);
            metrics.RecordDeleteRequest(DataSubjectOutcomes.Conflict);
            listener.RecordObservableInstruments();

            Assert.Contains(observed, o => o is { Status: "completed", Value: 1 });
            Assert.Contains(observed, o => o is { Status: "conflict", Value: 1 });
        }
    }

    [Fact]
    public void TimeToCompleteIsRecordedInSeconds()
    {
        var observed = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RightsMetrics.MeterName
                && instrument.Name == RightsMetrics.TimeToCompleteMetricName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => observed.Add(value));
        listener.Start();

        using var metrics = new RightsMetrics();
        metrics.RecordTimeToComplete(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));

        Assert.Equal([150d], observed);
    }

    [Fact]
    public void EveryRightsMetricIsInTheAuthoredCatalog()
    {
        // The single-authoring-place rule with one caveat worth stating: `MetricsCatalog` is
        // `internal` to the API assembly, so the test project sees it through the same
        // InternalsVisibleTo that the existing naming tests rely on.
        var names = CatalogPrometheusNames();

        Assert.Contains("aveline_data_export_requests_total", names);
        Assert.Contains("aveline_data_delete_requests_total", names);
        Assert.Contains("aveline_data_delete_time_to_complete_seconds", names);
    }

    // ----- opt-out / OTP family ---------------------------------------------------------------

    [Fact]
    public void TheRateLimitCounterIsLabelledByTheBoundedReasonVocabulary()
    {
        var observed = ObserveLong(
            OtpMetrics.MeterName,
            OtpMetrics.EndpointRateLimitedMetricName,
            out var listener);

        using (listener)
        using (var metrics = new OtpMetrics())
        {
            metrics.RecordRateLimited(PrivacyRateLimitReasons.PhoneBudget);
            metrics.RecordRateLimited(PrivacyRateLimitReasons.IpBudget);
            metrics.RecordRateLimited(PrivacyRateLimitReasons.StoreUnavailable);
            listener.RecordObservableInstruments();

            Assert.Contains(observed, o => o is { Status: "phone_budget", Value: 1 });
            Assert.Contains(observed, o => o is { Status: "ip_budget", Value: 1 });
            Assert.Contains(observed, o => o is { Status: "store_unavailable", Value: 1 });
        }
    }

    [Fact]
    public void EveryOptOutMetricIsInTheAuthoredCatalog()
    {
        var names = CatalogPrometheusNames();

        Assert.Contains("aveline_otp_issued_total", names);
        Assert.Contains("aveline_otp_verified_total", names);
        Assert.Contains("aveline_otp_failed_total", names);
        Assert.Contains("aveline_privacy_endpoint_rate_limited_total", names);
    }

    // ----- consent / disclosure families already authored -------------------------------------

    [Fact]
    public void TheConsentAndDisclosureFamiliesAreInTheAuthoredCatalog()
    {
        var names = CatalogPrometheusNames();

        foreach (var name in new[]
                 {
                     "aveline_message_skip_total",
                     "aveline_consent_state",
                     "aveline_disclosure_shown_total",
                     "aveline_disclosure_unshown_total",
                     "aveline_privacy_delivery_delivered_total",
                     "aveline_privacy_delivery_failed_total",
                     "aveline_outbound_message_result_total",
                     "aveline_notification_delivery_total",
                 })
        {
            Assert.Contains(name, names);
        }
    }

    // ----- the consent-state collector --------------------------------------------------------

    [Fact]
    public async Task TheConsentCollectorPublishesAGaugeSamplePerStatus()
    {
        var storeName = $"ConsentCollector_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(storeName)
            .Options;

        await using (var seed = new AppDbContext(options))
        {
            var orgId = Guid.NewGuid();
            seed.CustomerConsents.AddRange(
                Consent(orgId, ConsentStatuses.Pending),
                Consent(orgId, ConsentStatuses.Pending),
                Consent(orgId, ConsentStatuses.Revoked));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(storeName));
        await using var provider = services.BuildServiceProvider();

        using var metrics = new ConsentMetrics();
        var collected = new List<(string? Status, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ConsentMetrics.MeterName
                && instrument.Name == ConsentMetrics.StateMetricName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? status = null;
            foreach (var tag in tags)
            {
                status = tag.Value?.ToString();
            }

            collected.Add((status, value));
        });
        listener.Start();

        var collector = new ConsentMetricCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            metrics,
            NullLogger<ConsentMetricCollector>.Instance);

        var snapshot = await collector.RunAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.Equal(2, snapshot[ConsentStatuses.Pending]);
        Assert.Equal(1, snapshot[ConsentStatuses.Revoked]);
        Assert.Contains(collected, o => o is { Status: "pending", Value: 2 });
        Assert.Contains(collected, o => o is { Status: "revoked", Value: 1 });
    }

    private static CustomerConsent Consent(Guid orgId, string status)
        => new()
        {
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            ConsentStatus = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private static IReadOnlyList<string> CatalogPrometheusNames()
    {
        var field = typeof(AvelineMetrics).Assembly
            .GetType("Aveline.Api.Configurations.MetricsCatalog")!
            .GetField("All", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var all = (System.Collections.IEnumerable)field.GetValue(null)!;

        var names = new List<string>();
        foreach (var metric in all)
        {
            names.Add((string)metric!.GetType().GetProperty("PrometheusName")!.GetValue(metric)!);
        }

        return names;
    }

    /// <summary>
    /// Records every long measurement on one instrument. Both new counters carry a single bounded
    /// label, so reading the tag value positionally is enough and keeps the assertion readable.
    /// </summary>
    private static List<(string? Status, long Value)> ObserveLong(
        string meterName, string instrumentName, out MeterListener listener)
    {
        var observed = new List<(string? Status, long Value)>();
        var probe = new MeterListener();
        probe.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        probe.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? status = null;
            foreach (var tag in tags)
            {
                status = tag.Value?.ToString();
            }

            observed.Add((status, value));
        });
        probe.Start();
        listener = probe;
        return observed;
    }
}
