using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;

public class ArtistSearchEngineServiceDbContext(DbContextOptions<ArtistSearchEngineServiceDbContext> options)
    : DbContext(options)
{
    public DbSet<Artist> Artists { get; set; }

    public DbSet<ArtistAlias> ArtistAliases { get; set; }

    public DbSet<Album> Albums { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateTimeOffset)
                                                                            || p.PropertyType ==
                                                                            typeof(DateTimeOffset?));
            foreach (var property in properties)
            {
                modelBuilder
                    .Entity(entityType.Name)
                    .Property(property.Name)
                    .HasConversion(new DateTimeOffsetToBinaryConverter());
            }
        }

        modelBuilder.Entity<ArtistAlias>(entity =>
        {
            entity.ToTable("ArtistAliases");
            entity.HasOne(x => x.Artist)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Artist>(entity =>
        {
            entity.Property(x => x.IsLocked)
                .HasConversion<int?>(
                    v => v.HasValue ? (v.Value ? 1 : 0) : null,
                    v => v.HasValue ? (v.Value != 0) : null)
                .HasColumnType("INTEGER");
        });
    }
}
