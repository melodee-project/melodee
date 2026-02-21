using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Staging;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
/// EF Core DbContext for MusicBrainz materialized data
/// </summary>
public class MusicBrainzDbContext : DbContext
{
    public MusicBrainzDbContext(DbContextOptions<MusicBrainzDbContext> options) : base(options)
    {
    }

    // Final materialized data tables
    public DbSet<Artist> Artists { get; set; } = null!;
    public DbSet<Album> Albums { get; set; } = null!;
    public DbSet<ArtistRelation> ArtistRelations { get; set; } = null!;

    // Staging tables for streaming artist import
    public DbSet<ArtistStaging> ArtistsStaging { get; set; } = null!;
    public DbSet<ArtistAliasStaging> ArtistAliasesStaging { get; set; } = null!;
    public DbSet<LinkStaging> LinksStaging { get; set; } = null!;
    public DbSet<LinkArtistToArtistStaging> LinkArtistToArtistsStaging { get; set; } = null!;

    // Staging tables for streaming album import
    public DbSet<ArtistCreditStaging> ArtistCreditsStaging { get; set; } = null!;
    public DbSet<ArtistCreditNameStaging> ArtistCreditNamesStaging { get; set; } = null!;
    public DbSet<ReleaseCountryStaging> ReleaseCountriesStaging { get; set; } = null!;
    public DbSet<ReleaseGroupStaging> ReleaseGroupsStaging { get; set; } = null!;
    public DbSet<ReleaseGroupMetaStaging> ReleaseGroupMetasStaging { get; set; } = null!;
    public DbSet<ReleaseStaging> ReleasesStaging { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Artist entity
        modelBuilder.Entity<Artist>(entity =>
        {
            entity.ToTable("Artist");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.MusicBrainzIdRaw)
                .HasDatabaseName("IX_Artist_MusicBrainzIdRaw");
            entity.HasIndex(e => e.NameNormalized)
                .HasDatabaseName("IX_Artist_NameNormalized");
            entity.HasIndex(e => e.MusicBrainzArtistId)
                .HasDatabaseName("IX_Artist_MusicBrainzArtistId");

            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        // Configure Album entity
        modelBuilder.Entity<Album>(entity =>
        {
            entity.ToTable("Album");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.MusicBrainzIdRaw)
                .HasDatabaseName("IX_Album_MusicBrainzIdRaw");
            entity.HasIndex(e => e.MusicBrainzArtistId)
                .HasDatabaseName("IX_Album_MusicBrainzArtistId");
            entity.HasIndex(e => e.NameNormalized)
                .HasDatabaseName("IX_Album_NameNormalized");

            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.ReleaseGroupMusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        // Configure ArtistRelation entity
        modelBuilder.Entity<ArtistRelation>(entity =>
        {
            entity.ToTable("ArtistRelation");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ArtistId)
                .HasDatabaseName("IX_ArtistRelation_ArtistId");
            entity.HasIndex(e => e.RelatedArtistId)
                .HasDatabaseName("IX_ArtistRelation_RelatedArtistId");
        });

        // Configure staging tables for streaming artist import
        modelBuilder.Entity<ArtistStaging>(entity =>
        {
            entity.ToTable("ArtistStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ArtistId)
                .HasDatabaseName("IX_ArtistStaging_ArtistId");
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        modelBuilder.Entity<ArtistAliasStaging>(entity =>
        {
            entity.ToTable("ArtistAliasStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ArtistId)
                .HasDatabaseName("IX_ArtistAliasStaging_ArtistId");
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        modelBuilder.Entity<LinkStaging>(entity =>
        {
            entity.ToTable("LinkStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.LinkId)
                .HasDatabaseName("IX_LinkStaging_LinkId");
        });

        modelBuilder.Entity<LinkArtistToArtistStaging>(entity =>
        {
            entity.ToTable("LinkArtistToArtistStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.Artist0)
                .HasDatabaseName("IX_LinkArtistToArtistStaging_Artist0");
            entity.HasIndex(e => e.Artist1)
                .HasDatabaseName("IX_LinkArtistToArtistStaging_Artist1");
            entity.HasIndex(e => e.LinkId)
                .HasDatabaseName("IX_LinkArtistToArtistStaging_LinkId");
        });

        // Configure staging tables for streaming album import
        modelBuilder.Entity<ArtistCreditStaging>(entity =>
        {
            entity.ToTable("ArtistCreditStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ArtistCreditId)
                .HasDatabaseName("IX_ArtistCreditStaging_ArtistCreditId");
        });

        modelBuilder.Entity<ArtistCreditNameStaging>(entity =>
        {
            entity.ToTable("ArtistCreditNameStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ArtistCreditId)
                .HasDatabaseName("IX_ArtistCreditNameStaging_ArtistCreditId");
        });

        modelBuilder.Entity<ReleaseCountryStaging>(entity =>
        {
            entity.ToTable("ReleaseCountryStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ReleaseId)
                .HasDatabaseName("IX_ReleaseCountryStaging_ReleaseId");
        });

        modelBuilder.Entity<ReleaseGroupStaging>(entity =>
        {
            entity.ToTable("ReleaseGroupStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ReleaseGroupId)
                .HasDatabaseName("IX_ReleaseGroupStaging_ReleaseGroupId");
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });

        modelBuilder.Entity<ReleaseGroupMetaStaging>(entity =>
        {
            entity.ToTable("ReleaseGroupMetaStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ReleaseGroupId)
                .HasDatabaseName("IX_ReleaseGroupMetaStaging_ReleaseGroupId");
        });

        modelBuilder.Entity<ReleaseStaging>(entity =>
        {
            entity.ToTable("ReleaseStaging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ReleaseId)
                .HasDatabaseName("IX_ReleaseStaging_ReleaseId");
            entity.HasIndex(e => e.ReleaseGroupId)
                .HasDatabaseName("IX_ReleaseStaging_ReleaseGroupId");
            entity.HasIndex(e => e.ArtistCreditId)
                .HasDatabaseName("IX_ReleaseStaging_ArtistCreditId");
            entity.Property(e => e.MusicBrainzIdRaw)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.Name)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.NameNormalized)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
            entity.Property(e => e.SortName)
                .HasMaxLength(MusicBrainzRepositoryBase.MaxIndexSize)
                .IsRequired();
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Configuration is provided by DI registration; no fallback needed
    }
}
