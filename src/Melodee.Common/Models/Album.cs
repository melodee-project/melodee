using System.Diagnostics;
using System.Text.Json.Serialization;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Utility;
using NodaTime;

namespace Melodee.Common.Models;

/// <summary>
///     This is a representation of an Album (a published collection of Songs) including all known MetaData.
/// </summary>
public sealed record Album
{
    public const string JsonFileName = "melodee.json";

    /// <summary>
    ///     Artist for the Album
    /// </summary>
    public Artist Artist { get; set; } = Artist.NewArtistFromName(string.Empty);

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    public Guid Id { get; set; } = Guid.NewGuid();

    public int? AlbumDbId { get; set; }

    public Guid? MusicBrainzId { get; set; }

    public string? ItunesId { get; set; }

    public string? AmgId { get; set; }

    public string? DiscogsId { get; set; }

    public string? WikiDataId { get; set; }

    public string? LastFmId { get; set; }

    public string? SpotifyId { get; set; }

    public DateTimeOffset? Modified { get; set; }

    /// <summary>
    ///     What plugins were utilized in discovering this Album.
    /// </summary>
    public required IEnumerable<string> ViaPlugins { get; set; }

    /// <summary>
    ///     This is the directory where the Album was created, it will not be the "Staging" or "Library" directory where the
    ///     Album is moved to once processed.
    /// </summary>
    public required FileSystemDirectoryInfo OriginalDirectory { get; init; }

    /// <summary>
    ///     This is the directory that is holding the melodee.json file. At creation this is likely equal to Original but when
    ///     moved into another library directory this gets updated and Original does not.
    /// </summary>
    public required FileSystemDirectoryInfo Directory { get; set; }

    /// <summary>
    ///     The full path to the melodee.json file.
    /// </summary>
    public string? MelodeeDataFileName { get; set; }

    public IEnumerable<ImageInfo>? Images { get; set; }

    public IEnumerable<MetaTag<object?>>? Tags { get; set; }

    public KeyValue? SearchEngineResultKeyValue { get; init; }

    public IEnumerable<Song>? Songs { get; set; }

    public IEnumerable<ValidationResultMessage> ValidationMessages { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AlbumStatus Status { get; set; } = AlbumStatus.New;

    [JsonIgnore] public bool IsValid => Status == AlbumStatus.Ok;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AlbumType AlbumType { get; set; } = AlbumType.NotSet;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AlbumNeedsAttentionReasons StatusReasons { get; set; } = AlbumNeedsAttentionReasons.NotSet;

    public IEnumerable<AlbumFile> Files { get; set; } = [];

    public int SortOrder { get; set; }

    public string SortOrderValue
    {
        get
        {
            if (SortOrder > 0)
            {
                return SortOrder.ToString();
            }

            return SafeParser.Hash(Directory.Name).ToString();
        }
    }

    public Duration Duration => Duration.FromMilliseconds(this.TotalDuration());

    public string DisplaySummary => $"{this.SongTotalValue().ToStringPadLeft(3)} : {this.AlbumTitle()}";


    public Album MergeSongs(IEnumerable<Song> pluginResultData)
    {
        var songs = new List<Song>(Songs ?? []);
        foreach (var song in pluginResultData)
        {
            if (!songs.Contains(song))
            {
                songs.Add(song);
            }
        }

        var albumTags = Tags?.ToList();
        var songTotalTag = albumTags?.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.SongTotal);
        if (songTotalTag != null)
        {
            albumTags!.Remove(songTotalTag);
            albumTags.Add(new MetaTag<object?>
            {
                Identifier = MetaTagIdentifier.SongTotal,
                Value = songs.Count,
                SortOrder = songTotalTag.SortOrder,
                StyleClass = songTotalTag.StyleClass
            });
        }

        return this with { Songs = songs.ToArray(), Tags = albumTags!.ToArray() };
    }

    public override string ToString()
    {
        var statusSummary = "OK";
        if (Status != AlbumStatus.Ok)
        {
            statusSummary = $"{StatusReasons.ToString()}";
        }

        return
            $"\u251c {statusSummary} \u2524 AlbumDbId [{AlbumDbId}] MusicBrainzId [{MusicBrainzId}] Status [{Status}] SongCount [{Songs?.Count() ?? 0}] ImageCount [{Images?.Count() ?? 0}] Directory [{Directory}]";
    }

    public IEnumerable<MetaTag<object?>> ModifiedTags()
    {
        var modifiedAlbumTags = Tags?.Where(x => x.WasModified) ?? [];
        var modifiedSongTags = Songs?.SelectMany(x => x.Tags?.Where(y => y.WasModified) ?? []).ToArray() ?? [];
        return modifiedAlbumTags.Concat(modifiedSongTags);
    }

    public Album Merge(Album otherAlbum)
    {
        var files = new List<AlbumFile>(Files);
        var images = new List<ImageInfo>(Images ?? []);
        var messages = new List<ValidationResultMessage>(ValidationMessages);
        var tags = new List<MetaTag<object?>>(Tags ?? []);
        var songs = new List<Song>(Songs ?? []);
        var viaPlugins = new List<string>(ViaPlugins);

        if (otherAlbum.Images != null)
        {
            foreach (var image in otherAlbum.Images)
            {
                if (!images.Any(x => x.IsCrcHashMatch(image.CrcHash)))
                {
                    images.Add(image);
                }
            }
        }

        if (otherAlbum.Tags != null)
        {
            foreach (var tag in otherAlbum.Tags)
            {
                if (tags.FirstOrDefault(x => x.Identifier == tag.Identifier)?.Value?.ToString() !=
                    tag.Value?.ToString())
                {
                    tags.Add(tag);
                }
            }
        }

        if (otherAlbum.Songs != null)
        {
            foreach (var song in otherAlbum.Songs)
            {
                if (Songs != null && !Songs.Select(x => x.SongUniqueId()).Contains(song.SongUniqueId()))
                {
                    songs.Add(song);
                }
            }
        }

        if (otherAlbum.ViaPlugins.Any())
        {
            foreach (var plugin in otherAlbum.ViaPlugins)
            {
                if (!viaPlugins.Contains(plugin))
                {
                    viaPlugins.Add(plugin);
                }
            }
        }

        if (otherAlbum.Files.Any())
        {
            foreach (var file in otherAlbum.Files)
            {
                if (!files.Contains(file))
                {
                    files.Add(file);
                }
            }
        }

        messages.AddRange(otherAlbum.ValidationMessages);

        var isAlbumOk = Status == AlbumStatus.Ok && otherAlbum.Status == AlbumStatus.Ok;
        var isAlbumInvalid = Status == AlbumStatus.Invalid || otherAlbum.Status == AlbumStatus.Invalid;

        var isAlbumTypeAlbum = AlbumType == AlbumType.Album || otherAlbum.AlbumType == AlbumType.Album;
        var isAlbumTypeEP = (!isAlbumTypeAlbum && AlbumType == AlbumType.EP) || otherAlbum.AlbumType == AlbumType.EP;

        return new Album
        {
            AlbumType = isAlbumTypeAlbum && !isAlbumTypeEP ? AlbumType.Album :
                isAlbumTypeEP ? AlbumType.EP : AlbumType.NotSet,
            Artist = Artist.Merge(otherAlbum.Artist),
            Directory = Directory,
            Files = files.ToArray(),
            Images = images.ToArray(),
            ValidationMessages = messages.Distinct().ToArray(),
            MelodeeDataFileName = MelodeeDataFileName,
            OriginalDirectory = OriginalDirectory,
            Songs = songs.ToArray(),
            SortOrder = SortOrder,
            Status = isAlbumOk ? AlbumStatus.Ok : isAlbumInvalid ? AlbumStatus.Invalid : AlbumStatus.New,
            Tags = tags,
            ViaPlugins = viaPlugins
        };
    }

    public void SetTagValue(MetaTagIdentifier identifier, object? value, bool? doSetSongValue = true)
    {
        var tags = (Tags ?? []).ToList();
        var existingTag = tags.FirstOrDefault(x => x.Identifier == identifier);
        if (existingTag != null)
        {
            tags.Remove(existingTag);
        }

        if (value != null)
        {
            var ov = existingTag?.OriginalValue ?? existingTag?.Value;
            tags.Add(new MetaTag<object?>
            {
                Identifier = identifier,
                OriginalValue = ov == value ? null : ov,
                SortOrder = existingTag?.SortOrder ?? 0,
                StyleClass = existingTag?.StyleClass ?? StyleClass.Normal,
                Value = value
            });
        }

        Tags = tags.ToArray();
        if (doSetSongValue ?? true)
        {
            foreach (var song in Songs ?? [])
            {
                SetSongTagValue(song.Id, identifier, value);
            }
        }
    }

    public void RemoveSongTagValue(Guid songId, MetaTagIdentifier identifier)
    {
        SetSongTagValue(songId, identifier, null);
    }

    public void SetSongTagValue(Guid songId, MetaTagIdentifier identifier, object? value)
    {
        var songs = (Songs ?? []).ToList();
        var song = songs.FirstOrDefault(x => x.Id == songId);
        if (song != null)
        {
            var tags = (song.Tags ?? []).ToList();
            var existingTag = tags.FirstOrDefault(x => x.Identifier == identifier);
            if (existingTag != null)
            {
                tags.Remove(existingTag);
            }

            if (value != null)
            {
                tags.Add(new MetaTag<object?>
                {
                    Identifier = identifier,
                    OriginalValue = existingTag?.OriginalValue != value ? existingTag?.OriginalValue : null,
                    SortOrder = existingTag?.SortOrder ?? 0,
                    StyleClass = existingTag?.StyleClass ?? StyleClass.Normal,
                    Value = value
                });
            }

            songs.Remove(song);
            songs.Add(song with { Tags = tags.ToArray() });
            Songs = songs.ToArray();
        }
    }

    public static async Task<Album?> DeserializeAndInitializeAlbumAsync(ISerializer serializer,
        string fullPathToMelodeeDataFile, CancellationToken cancellationToken = default)
    {
        var result =
            serializer.Deserialize<Album>(await File.ReadAllBytesAsync(fullPathToMelodeeDataFile, cancellationToken)
                .ConfigureAwait(false));
        if (result == null)
        {
            return null;
        }

        result.MelodeeDataFileName = fullPathToMelodeeDataFile;

        // See if Artist melodee.json exists in one folder up, if present use it for the albums artist overwriting any artist set on the albums melodee.json file
        var fi = new FileInfo(fullPathToMelodeeDataFile);
        var artistMelodeeJsonFile = fi.Directory?.Parent?.FindFirstFileInParentDirectories(Artist.JsonFileName);
        try
        {
            if (artistMelodeeJsonFile != null && File.Exists(artistMelodeeJsonFile?.FullName))
            {
                var artist = serializer.Deserialize<Artist>(await File
                    .ReadAllBytesAsync(artistMelodeeJsonFile.FullName, cancellationToken).ConfigureAwait(false));
                result.Artist = artist ?? result.Artist;
                if (artist != null)
                {
                    Trace.WriteLine(
                        $"Using artist {Artist.JsonFileName} file [{artistMelodeeJsonFile}] for album [{result.AlbumTitle()}] Artist [{artist.Name}]");
                }
            }
        }
        catch (Exception e)
        {
            Trace.WriteLine(
                $"Unable to read artist melodee.json file [{artistMelodeeJsonFile}] Exception [{e.Message}]");
        }


        return result;
    }
}
