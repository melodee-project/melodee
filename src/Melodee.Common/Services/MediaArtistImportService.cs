using System.Text.Json;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public sealed class MediaArtistImportService
{
    private const string ExpectedSchemaVersion = "1.0";
    private readonly ILogger _logger;
    private readonly IDbContextFactory<ArtistSearchEngineServiceDbContext> _contextFactory;

    public MediaArtistImportService(
        ILogger logger,
        IDbContextFactory<ArtistSearchEngineServiceDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
    }

    public async Task<MediaArtistImportResult> ImportAsync(
        string jsonContent,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        var result = new MediaArtistImportResult();

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var importData = JsonSerializer.Deserialize<MediaArtistExportData>(jsonContent, jsonOptions);
            if (importData == null)
            {
                return result.WithError("Invalid or empty import file");
            }

            if (importData.SchemaVersion != ExpectedSchemaVersion)
            {
                return result.WithError($"Schema version mismatch. Expected {ExpectedSchemaVersion}, got {importData.SchemaVersion}");
            }

            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var importedArtist in importData.Artists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ImportArtistAsync(db, importedArtist, overwriteExisting, result, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            result.Success = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to import media artists");
            return result.WithError($"Import failed: {ex.Message}");
        }
    }

    private static async Task ImportArtistAsync(
        ArtistSearchEngineServiceDbContext db,
        ExportedMediaArtist importedArtist,
        bool overwriteExisting,
        MediaArtistImportResult result,
        CancellationToken cancellationToken)
    {
        Artist? existingArtist = null;

        if (!string.IsNullOrEmpty(importedArtist.MusicBrainzId) &&
            Guid.TryParse(importedArtist.MusicBrainzId, out var mbId))
        {
            existingArtist = await db.Artists
                .Include(x => x.Albums)
                .Include(x => x.Aliases)
                .FirstOrDefaultAsync(x => x.MusicBrainzId == mbId, cancellationToken);
        }

        if (existingArtist == null)
        {
            existingArtist = await db.Artists
                .Include(x => x.Albums)
                .Include(x => x.Aliases)
                .FirstOrDefaultAsync(x => x.NameNormalized == importedArtist.NameNormalized, cancellationToken);
        }

        if (existingArtist != null)
        {
            if (!overwriteExisting)
            {
                result.ArtistsSkipped++;
                return;
            }

            UpdateExistingArtist(existingArtist, importedArtist);

            ImportAlbumsIntoExisting(existingArtist, importedArtist, overwriteExisting, result);
            ImportAliasesIntoExisting(existingArtist, importedArtist, overwriteExisting, result);

            result.ArtistsUpdated++;
        }
        else
        {
            var newArtist = CreateNewArtist(importedArtist);
            db.Artists.Add(newArtist);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var album in importedArtist.Albums)
            {
                db.Albums.Add(new Album
                {
                    Artist = newArtist,
                    ArtistId = newArtist.Id,
                    SortName = album.SortName,
                    AlbumType = album.AlbumType,
                    MusicBrainzId = TryParseGuid(album.MusicBrainzId),
                    MusicBrainzReleaseGroupId = TryParseGuid(album.MusicBrainzReleaseGroupId),
                    SpotifyId = album.SpotifyId,
                    CoverUrl = album.CoverUrl,
                    Name = album.Name,
                    NameNormalized = album.NameNormalized,
                    Year = album.Year
                });
            }

            foreach (var alias in importedArtist.Aliases)
            {
                db.ArtistAliases.Add(new ArtistAlias
                {
                    ArtistId = newArtist.Id,
                    NameNormalized = alias.NameNormalized
                });
            }

            result.ArtistsImported++;
            result.AlbumsImported += importedArtist.Albums.Count;
            result.AliasesImported += importedArtist.Aliases.Count;
        }
    }

    private static void UpdateExistingArtist(Artist existing, ExportedMediaArtist imported)
    {
        existing.Name = imported.Name;
        existing.NameNormalized = imported.NameNormalized;
        existing.AlternateNames = imported.AlternateNames;
        existing.SortName = imported.SortName;
        existing.ItunesId = imported.ItunesId;
        existing.AmgId = imported.AmgId;
        existing.DiscogsId = imported.DiscogsId;
        existing.WikiDataId = imported.WikiDataId;
        existing.MusicBrainzId = TryParseGuid(imported.MusicBrainzId);
        existing.LastFmId = imported.LastFmId;
        existing.SpotifyId = imported.SpotifyId;
        existing.IsLocked = imported.IsLocked;

        if (DateTimeOffset.TryParse(imported.LastRefreshed, out var lastRefreshed))
        {
            existing.LastRefreshed = lastRefreshed;
        }
    }

    private static Artist CreateNewArtist(ExportedMediaArtist imported)
    {
        var artist = new Artist
        {
            Name = imported.Name,
            NameNormalized = imported.NameNormalized,
            AlternateNames = imported.AlternateNames,
            SortName = imported.SortName,
            ItunesId = imported.ItunesId,
            AmgId = imported.AmgId,
            DiscogsId = imported.DiscogsId,
            WikiDataId = imported.WikiDataId,
            MusicBrainzId = TryParseGuid(imported.MusicBrainzId),
            LastFmId = imported.LastFmId,
            SpotifyId = imported.SpotifyId,
            IsLocked = imported.IsLocked
        };

        if (DateTimeOffset.TryParse(imported.LastRefreshed, out var lastRefreshed))
        {
            artist.LastRefreshed = lastRefreshed;
        }

        return artist;
    }

    private static void ImportAlbumsIntoExisting(
        Artist existingArtist,
        ExportedMediaArtist importedArtist,
        bool overwriteExisting,
        MediaArtistImportResult result)
    {
        foreach (var importedAlbum in importedArtist.Albums)
        {
            var existingAlbum = existingArtist.Albums
                .FirstOrDefault(x => x.NameNormalized == importedAlbum.NameNormalized
                                     && x.Year == importedAlbum.Year);

            if (existingAlbum != null)
            {
                if (!overwriteExisting)
                {
                    continue;
                }

                existingAlbum.MusicBrainzId = TryParseGuid(importedAlbum.MusicBrainzId);
                existingAlbum.MusicBrainzReleaseGroupId = TryParseGuid(importedAlbum.MusicBrainzReleaseGroupId);
                existingAlbum.SpotifyId = importedAlbum.SpotifyId;
                result.AlbumsUpdated++;
            }
            else
            {
                existingArtist.Albums.Add(new Album
                {
                    Artist = existingArtist,
                    ArtistId = existingArtist.Id,
                    SortName = importedAlbum.SortName,
                    AlbumType = importedAlbum.AlbumType,
                    MusicBrainzId = TryParseGuid(importedAlbum.MusicBrainzId),
                    MusicBrainzReleaseGroupId = TryParseGuid(importedAlbum.MusicBrainzReleaseGroupId),
                    SpotifyId = importedAlbum.SpotifyId,
                    CoverUrl = importedAlbum.CoverUrl,
                    Name = importedAlbum.Name,
                    NameNormalized = importedAlbum.NameNormalized,
                    Year = importedAlbum.Year
                });
                result.AlbumsImported++;
            }
        }
    }

    private static void ImportAliasesIntoExisting(
        Artist existingArtist,
        ExportedMediaArtist importedArtist,
        bool overwriteExisting,
        MediaArtistImportResult result)
    {
        foreach (var importedAlias in importedArtist.Aliases)
        {
            var existingAlias = existingArtist.Aliases
                .FirstOrDefault(x => x.NameNormalized == importedAlias.NameNormalized);

            if (existingAlias == null)
            {
                existingArtist.Aliases.Add(new ArtistAlias
                {
                    ArtistId = existingArtist.Id,
                    NameNormalized = importedAlias.NameNormalized
                });
                result.AliasesImported++;
            }
        }
    }

    private static Guid? TryParseGuid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}

public sealed class MediaArtistImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int ArtistsImported { get; set; }
    public int ArtistsUpdated { get; set; }
    public int ArtistsSkipped { get; set; }
    public int AlbumsImported { get; set; }
    public int AlbumsUpdated { get; set; }
    public int AliasesImported { get; set; }

    public MediaArtistImportResult WithError(string error)
    {
        Success = false;
        ErrorMessage = error;
        return this;
    }
}
