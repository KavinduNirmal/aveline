using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>
/// Persistence for <see cref="SystemMetricSample"/>. Unlike the tenant tables, system
/// metrics are organisation-agnostic, so no tenant filter applies.
/// </summary>
public interface ISystemMetricRepository
{
    /// <summary>Idempotent upsert keyed on <c>(MetricName, DimensionHash, WindowStart, WindowSize)</c>.</summary>
    Task<int> UpsertAsync(IReadOnlyList<SystemMetricSample> samples, CancellationToken cancellationToken = default);

    Task<int> DeleteSamplesOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemMetricSample>> QueryAsync(
        string metricName,
        DateTime from,
        DateTime to,
        string? windowSize = null,
        CancellationToken cancellationToken = default);

    Task<SystemMetricSample?> LatestAsync(string metricName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListMetricNamesAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemMetricRepository : ISystemMetricRepository
{
    private readonly AppDbContext _context;

    public SystemMetricRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> UpsertAsync(
        IReadOnlyList<SystemMetricSample> samples, CancellationToken cancellationToken = default)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        var written = 0;
        foreach (var sample in samples)
        {
            var existing = await _context.SystemMetricSamples.FirstOrDefaultAsync(
                row => row.MetricName == sample.MetricName
                       && row.DimensionHash == sample.DimensionHash
                       && row.WindowStart == sample.WindowStart
                       && row.WindowSize == sample.WindowSize,
                cancellationToken);

            if (existing is null)
            {
                _context.SystemMetricSamples.Add(sample);
            }
            else
            {
                existing.ValueDecimal = sample.ValueDecimal;
                existing.ValueBigint = sample.ValueBigint;
                existing.Unit = sample.Unit;
                existing.DimensionsJson = sample.DimensionsJson;
                existing.SampledAt = sample.SampledAt;
            }

            written++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return written;
    }

    public async Task<int> DeleteSamplesOlderThanAsync(
        DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var old = await _context.SystemMetricSamples
            .Where(sample => sample.SampledAt < cutoff)
            .ToListAsync(cancellationToken);

        if (old.Count == 0)
        {
            return 0;
        }

        _context.SystemMetricSamples.RemoveRange(old);
        await _context.SaveChangesAsync(cancellationToken);
        return old.Count;
    }

    public async Task<IReadOnlyList<SystemMetricSample>> QueryAsync(
        string metricName,
        DateTime from,
        DateTime to,
        string? windowSize = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SystemMetricSamples
            .AsNoTracking()
            .Where(sample => sample.MetricName == metricName
                             && sample.WindowStart >= from
                             && sample.WindowStart <= to);

        if (!string.IsNullOrWhiteSpace(windowSize))
        {
            query = query.Where(sample => sample.WindowSize == windowSize);
        }

        return await query.OrderBy(sample => sample.WindowStart).ToListAsync(cancellationToken);
    }

    public async Task<SystemMetricSample?> LatestAsync(
        string metricName, CancellationToken cancellationToken = default) =>
        await _context.SystemMetricSamples
            .AsNoTracking()
            .Where(sample => sample.MetricName == metricName)
            .OrderByDescending(sample => sample.SampledAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListMetricNamesAsync(
        CancellationToken cancellationToken = default) =>
        await _context.SystemMetricSamples
            .AsNoTracking()
            .Select(sample => sample.MetricName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
}
