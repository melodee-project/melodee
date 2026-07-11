namespace Melodee.Common.Services.Models.ArtistDuplicate;

/// <summary>
/// A lightweight read model for artist data used in duplicate detection.
/// Contains only fields relevant for matching.
/// </summary>
/// <param name="ArtistId">The internal database ID of the artist.</param>
/// <param name="ApiKey">The API key (GUID) of the artist.</param>
/// <param name="Name">The display name of the artist.</param>
/// <param name="NameNormalized">The normalized form of the artist name.</param>
/// <param name="SortName">The sort name of the artist (e.g., "John, Elton").</param>
/// <param name="AlbumCount">Number of albums for this artist.</param>
/// <param name="SongCount">Number of songs for this artist.</param>
/// <param name="SpotifyId">External Spotify ID.</param>
/// <param name="MusicBrainzId">External MusicBrainz GUID.</param>
/// <param name="DiscogsId">External Discogs ID.</param>
/// <param name="ItunesId">External iTunes ID.</param>
/// <param name="DeezerId">External Deezer ID.</param>
/// <param name="LastFmId">External Last.fm ID.</param>
/// <param name="AmgId">External AMG ID.</param>
/// <param name="WikiDataId">External WikiData ID.</param>
/// <param name="Albums">Lightweight album stubs for album overlap calculation.</param>
/// <param name="CreatedAtTicks">The database creation timestamp represented as ticks.</param>
public sealed record ArtistReadModel(
    int ArtistId,
    Guid ApiKey,
    string Name,
    string NameNormalized,
    string? SortName,
    int AlbumCount,
    int SongCount,
    string? SpotifyId,
    Guid? MusicBrainzId,
    string? DiscogsId,
    string? ItunesId,
    int? DeezerId,
    string? LastFmId,
    string? AmgId,
    string? WikiDataId,
    IReadOnlyCollection<AlbumStub> Albums,
    long CreatedAtTicks = 0)
{
    private static readonly HashSet<string> InvalidSentinelValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "0", "-1", "unknown", "null", "none", "n/a", "na"
    };

    /// <summary>
    /// Compute a "quality score" for this artist to determine if it should be the primary in a merge.
    /// Higher score = better candidate to keep.
    /// </summary>
    public int ComputePrimaryScore()
    {
        var score = 0;

        var externalIds = GetExternalIds();
        score += externalIds.Count * 100;

        if (externalIds.ContainsKey("musicbrainz"))
        {
            score += 50;
        }

        if (externalIds.ContainsKey("spotify"))
        {
            score += 30;
        }

        score += AlbumCount * 10;
        score += SongCount;

        if (HasProperCase(Name))
        {
            score += 25;
        }

        if (!IsCommaInvertedName(Name))
        {
            score += 20;
        }

        if (!HasLeadingArticle(Name))
        {
            score += 5;
        }

        if (CreatedAtTicks > 0)
        {
            score += 10;
        }

        return score;
    }

    private static bool HasProperCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name == name.ToUpperInvariant())
        {
            return false;
        }

        if (name == name.ToLowerInvariant())
        {
            return false;
        }

        return true;
    }

    private static bool IsCommaInvertedName(string name)
    {
        return name.Contains(',') && name.Split(',').Length == 2;
    }

    private static bool HasLeadingArticle(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.StartsWith("the ") || lower.StartsWith("a ") || lower.StartsWith("an ");
    }

    /// <summary>
    /// Get all valid external IDs as a dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetExternalIds()
    {
        var ids = new Dictionary<string, string>();

        if (IsValidExternalId(SpotifyId))
        {
            ids["spotify"] = SpotifyId!;
        }

        if (MusicBrainzId.HasValue && MusicBrainzId.Value != Guid.Empty)
        {
            ids["musicbrainz"] = MusicBrainzId.Value.ToString();
        }

        if (IsValidExternalId(DiscogsId))
        {
            ids["discogs"] = DiscogsId!;
        }

        if (IsValidExternalId(ItunesId))
        {
            ids["itunes"] = ItunesId!;
        }

        if (DeezerId.HasValue && DeezerId.Value > 0)
        {
            ids["deezer"] = DeezerId.Value.ToString();
        }

        if (IsValidExternalId(LastFmId))
        {
            ids["lastfm"] = LastFmId!;
        }

        if (IsValidExternalId(AmgId))
        {
            ids["amg"] = AmgId!;
        }

        if (IsValidExternalId(WikiDataId))
        {
            ids["wikidata"] = WikiDataId!;
        }

        return ids;
    }

    private static bool IsValidExternalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !InvalidSentinelValues.Contains(value);
    }

    /// <summary>
    /// Check if artist has a valid external ID for the specified source.
    /// </summary>
    public bool HasExternalIdForSource(string source)
    {
        return source.ToLowerInvariant() switch
        {
            "spotify" => IsValidExternalId(SpotifyId),
            "musicbrainz" => MusicBrainzId.HasValue && MusicBrainzId.Value != Guid.Empty,
            "discogs" => IsValidExternalId(DiscogsId),
            "itunes" => IsValidExternalId(ItunesId),
            "deezer" => DeezerId.HasValue && DeezerId.Value > 0,
            "lastfm" => IsValidExternalId(LastFmId),
            "amg" => IsValidExternalId(AmgId),
            "wikidata" => IsValidExternalId(WikiDataId),
            _ => false
        };
    }
}
