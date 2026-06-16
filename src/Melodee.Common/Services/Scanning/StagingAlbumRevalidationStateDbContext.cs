using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Melodee.Common.Services.Scanning;

public sealed class StagingAlbumRevalidationStateDbContext(
    DbContextOptions<StagingAlbumRevalidationStateDbContext> options) : DbContext(options)
{
    public DbSet<StagingAlbumRevalidationState> AlbumRevalidationStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StagingAlbumRevalidationState>(entity =>
        {
            entity.HasKey(x => x.AlbumKey);
            entity.HasIndex(x => x.NextAttemptAt);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateTimeOffset) ||
                                                                           p.PropertyType == typeof(DateTimeOffset?));
            foreach (var property in properties)
            {
                modelBuilder
                    .Entity(entityType.Name)
                    .Property(property.Name)
                    .HasConversion(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
