using Aveline.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Infrastructure.Persistence;

public class AvelineDbContext : DbContext
{
    public AvelineDbContext(DbContextOptions<AvelineDbContext> options) : base(options)
    {
    }

    // Visual Intelligence & Inventory Module (Slice 2)
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvelineDbContext).Assembly);
    }
}
