using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;

namespace Melodee.Common.Data.Configurations;

public class LibraryConfiguration : IEntityTypeConfiguration<Library>
{
    public void Configure(EntityTypeBuilder<Library> builder)
    {
        builder.HasIndex(e => e.Type)
            .IsUnique()
            .HasFilter("\"Type\" != 3");

        var seedDataTimestamp = Instant.FromUnixTimeSeconds(0);

        builder.HasData(
            new Library
            {
                Id = 1,
                ApiKey = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                Name = "Inbound",
                Description = "Files in this directory are scanned and Album information is gathered via processing.",
                Path = "/app/inbound/",
                Type = (int)LibraryType.Inbound,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 2,
                ApiKey = new Guid("b2c3d4e5-f678-9012-bcde-f12345678901"),
                Name = "Staging",
                Description = "The staging directory to place processed files into (Inbound -> Staging -> Library).",
                Path = "/app/staging/",
                Type = (int)LibraryType.Staging,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 3,
                ApiKey = new Guid("c3d4e5f6-7890-1234-defa-234567890123"),
                Name = "Storage",
                Description = "The library directory to place processed, reviewed and ready to use music files into.",
                Path = "/app/storage/",
                Type = (int)LibraryType.Storage,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 4,
                ApiKey = new Guid("d4e5f678-9012-3456-efab-345678901234"),
                Name = "User Images",
                Description = "Library where user images are stored.",
                Path = "/app/user-images/",
                Type = (int)LibraryType.UserImages,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 5,
                ApiKey = new Guid("e5f67890-1234-4567-fabc-456789012345"),
                Name = "Playlist Data",
                Description = "Library where playlist data is stored.",
                Path = "/app/playlists/",
                Type = (int)LibraryType.Playlist,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 6,
                ApiKey = new Guid("f6789012-3456-7890-bcde-567890123456"),
                Name = "Templates",
                Description = "Library where templates are stored, organized by language code.",
                Path = "/app/templates/",
                Type = (int)LibraryType.Templates,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 7,
                ApiKey = new Guid("67890123-4567-8901-cdef-678901234567"),
                Name = "Podcasts",
                Description = "Library where podcast media files are stored.",
                Path = "/app/podcasts/",
                Type = (int)LibraryType.Podcast,
                CreatedAt = seedDataTimestamp
            },
            new Library
            {
                Id = 8,
                ApiKey = new Guid("78901234-5678-9012-defa-789012345678"),
                Name = "Themes",
                Description = "Library where custom theme packs are stored.",
                Path = "/app/themes/",
                Type = (int)LibraryType.Theme,
                CreatedAt = seedDataTimestamp
            });
    }
}
