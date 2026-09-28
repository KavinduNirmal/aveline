using System.IO;
using System.Runtime.CompilerServices;
using Aveline.Api.Configurations;

namespace Aveline.Api.Tests;

/// <summary>
/// Names the EF Core in-memory store that a test class reads and writes.
///
/// EF Core caches its internal service provider against the options fingerprint, and the in-memory
/// store name is part of that fingerprint. Two contexts built with the same name therefore share
/// ONE store for the life of the process, even when they come from different service providers.
///
/// That is what made the suite order-dependent: dozens of integration classes all named their store
/// <c>AvelineInMemoryDb</c>, so they shared a single set of tables, and nothing ever cleared it
/// between them. A test asserting a <em>global</em> aggregate (platform MRR, total tenant count)
/// then saw every other class's seeded rows and failed deterministically rather than occasionally.
///
/// The name is derived from the calling file, so the two places that must agree - the test's own
/// <c>AppDbContext</c>, used to seed, and the API host it starts, configured through
/// <see cref="DatabaseConfiguration.InMemoryNameKey"/> - agree automatically without every class
/// having to declare and thread through a field of its own.
/// </summary>
internal static class TestDatabase
{
    /// <summary>
    /// A store name unique to the calling file. Deliberately derived from the file rather than
    /// randomised per call: the seed context and the host under test must resolve the same store.
    /// </summary>
    public static string Name([CallerFilePath] string path = "")
    {
        var file = Path.GetFileNameWithoutExtension(path);

        // CallerFilePath is always supplied by the compiler; the guard only covers a direct call
        // from a context where it cannot be resolved, which must still produce a usable name.
        return string.IsNullOrEmpty(file)
            ? $"{DatabaseConfiguration.DefaultInMemoryName}-{Guid.NewGuid():N}"
            : $"{DatabaseConfiguration.DefaultInMemoryName}-{file}";
    }
}
