using System.Text.Json;
using System.Text.Json.Serialization;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public sealed class MediaArtistExportService
{
    private const string SchemaVersion = "1.0";
    private readonly ILogger _logger;
    private readonly IDbContextFactory<ArtistSearchEngineServiceDbContext> _contextFactory;

    public MediaArtistExportService(
        ILogger logger,
        IDbContextFactory<ArtistSearchEngineServiceDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
    }

    public async Task<MediaArtistExportResult> ExportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var artists = await db.Artists
                .AsNoTracking()
                .Include(x => x.Albums)
                .Include(x => x.Aliases)
                .OrderBy(x => x.NameNormalized)
                .ToListAsync(cancellationToken);

            var exportedArtists = artists.Select(a => new ExportedMediaArtist
            {
                Name = a.Name,
                NameNormalized = a.NameNormalized,
                AlternateNames = a.AlternateNames,
                SortName = a.SortName,
                ItunesId = a.ItunesId,
                AmgId = a.AmgId,
                DiscogsId = a.DiscogsId,
                WikiDataId = a.WikiDataId,
                MusicBrainzId = a.MusicBrainzId?.ToString(),
                LastFmId = a.LastFmId,
                SpotifyId = a.SpotifyId,
                IsLocked = a.IsLocked,
                LastRefreshed = a.LastRefreshed?.ToString("O"),
                Albums = a.Albums.OrderBy(x => x.NameNormalized).Select(x => new ExportedMediaAlbum
                {
                    SortName = x.SortName,
                    AlbumType = x.AlbumType,
                    MusicBrainzId = x.MusicBrainzId?.ToString(),
                    MusicBrainzReleaseGroupId = x.MusicBrainzReleaseGroupId?.ToString(),
                    SpotifyId = x.SpotifyId,
                    CoverUrl = x.CoverUrl,
                    Name = x.Name,
                    NameNormalized = x.NameNormalized,
                    Year = x.Year
                }).ToList(),
                Aliases = a.Aliases.OrderBy(x => x.NameNormalized).Select(x => new ExportedMediaArtistAlias
                {
                    NameNormalized = x.NameNormalized
                }).ToList()
            }).ToList();

            var exportData = new MediaArtistExportData
            {
                SchemaVersion = SchemaVersion,
                ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
                Artists = exportedArtists
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return new MediaArtistExportResult
            {
                Success = true,
                Json = JsonSerializer.Serialize(exportData, jsonOptions),
                ArtistsCount = exportedArtists.Count,
                AlbumsCount = exportedArtists.Sum(x => x.Albums.Count),
                AliasesCount = exportedArtists.Sum(x => x.Aliases.Count)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export media artists");
            return new MediaArtistExportResult
            {
                Success = false,
                ErrorMessage = $"Export failed: {ex.Message}"
            };
        }
    }
}

public sealed class MediaArtistExportData
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("exportedAt")]
    public string ExportedAt { get; init; } = string.Empty;

    [JsonPropertyName("artists")]
    public List<ExportedMediaArtist> Artists { get; init; } = [];
}

public sealed class ExportedMediaArtist
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("nameNormalized")]
    public string NameNormalized { get; init; } = string.Empty;

    [JsonPropertyName("alternateNames")]
    public string? AlternateNames { get; init; }

    [JsonPropertyName("sortName")]
    public string SortName { get; init; } = string.Empty;

    [JsonPropertyName("itunesId")]
    public string? ItunesId { get; init; }

    [JsonPropertyName("amgId")]
    public string? AmgId { get; init; }

    [JsonPropertyName("discogsId")]
    public string? DiscogsId { get; init; }

    [JsonPropertyName("wikiDataId")]
    public string? WikiDataId { get; init; }

    [JsonPropertyName("musicBrainzId")]
    public string? MusicBrainzId { get; init; }

    [JsonPropertyName("lastFmId")]
    public string? LastFmId { get; init; }

    [JsonPropertyName("spotifyId")]
    public string? SpotifyId { get; init; }

    [JsonPropertyName("isLocked")]
    public bool? IsLocked { get; init; }

    [JsonPropertyName("lastRefreshed")]
    public string? LastRefreshed { get; init; }

    [JsonPropertyName("albums")]
    public List<ExportedMediaAlbum> Albums { get; init; } = [];

    [JsonPropertyName("aliases")]
    public List<ExportedMediaArtistAlias> Aliases { get; init; } = [];
}

public sealed class ExportedMediaAlbum
{
    [JsonPropertyName("sortName")]
    public string SortName { get; init; } = string.Empty;

    [JsonPropertyName("albumType")]
    public int AlbumType { get; init; }

    [JsonPropertyName("musicBrainzId")]
    public string? MusicBrainzId { get; init; }

    [JsonPropertyName("musicBrainzReleaseGroupId")]
    public string? MusicBrainzReleaseGroupId { get; init; }

    [JsonPropertyName("spotifyId")]
    public string? SpotifyId { get; init; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("nameNormalized")]
    public string NameNormalized { get; init; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; init; }
}

public sealed class ExportedMediaArtistAlias
{
    [JsonPropertyName("nameNormalized")]
    public string NameNormalized { get; init; } = string.Empty;
}

public sealed class MediaArtistExportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Json { get; init; }
    public int ArtistsCount { get; init; }
    public int AlbumsCount { get; init; }
    public int AliasesCount { get; init; }
}