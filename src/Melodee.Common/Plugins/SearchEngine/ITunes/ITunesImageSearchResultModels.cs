namespace Melodee.Common.Plugins.SearchEngine.ITunes;

public record ITunesSearchResult
{
    public int? ResultCount { get; init; }
    public List<Result>? Results { get; init; }
}

public record Result
{
    public long? AmgArtistId { get; init; }

    public long? ArtistId { get; init; }

    public string? ArtistLinkUrl { get; init; }

    public string? ArtistName { get; init; }

    public string? ArtistType { get; init; }

    public string? ArtistViewUrl { get; init; }

    public string? ArtworkUrl100 { get; init; }

    public string? ArtworkUrl60 { get; init; }

    public string? CollectionCensoredName { get; init; }

    public string? CollectionExplicitness { get; init; }

    public long? CollectionId { get; init; }

    public string? CollectionName { get; init; }

    public float? CollectionPrice { get; init; }

    public string? CollectionType { get; init; }

    public string? CollectionViewUrl { get; init; }

    public string? Copyright { get; init; }

    public string? Country { get; init; }

    public string? Currency { get; init; }

    public string? PrimaryGenreName { get; init; }

    public long? PrimaryGenreId { get; init; }

    public DateTime? ReleaseDate { get; init; }

    public int? TrackCount { get; init; }

    public string? WrapperType { get; init; }
}
