using System.Text.Json.Serialization;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using MelodeeSong = Melodee.Common.Models.Song;

namespace Melodee.Common.Plugins.MetaData.Directory.Blackbeard;

/// <summary>
///     Processes Blackbeard provenance files and builds album metadata from the normalized release manifest.
/// </summary>
public sealed class Blackbeard(
    ISerializer serializer,
    IAlbumValidator albumValidator,
    IMelodeeConfiguration configuration)
    : AlbumMetaDataBase(configuration), IDirectoryPlugin
{
    public const string HandlesFileName = ".blackbeard.provenance.json";
    public const int SupportedSchemaVersion = 1;

    public override string Id => "8397443D-B13E-477E-B7C3-82F3459DB878";

    public override string DisplayName => nameof(Blackbeard);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 1;

    public async Task<OperationResult<int>> ProcessDirectoryAsync(FileSystemDirectoryInfo fileSystemDirectoryInfo,
        CancellationToken cancellationToken = default)
    {
        StopProcessing = false;

        var provenanceFiles = ProvenanceFiles(fileSystemDirectoryInfo);
        if (provenanceFiles.Length == 0)
        {
            return new OperationResult<int>("Skipping Blackbeard provenance. No provenance files found.")
            {
                Type = OperationResponseType.NotFound,
                Data = -1
            };
        }

        var messages = new List<string>();
        var processedFiles = 0;
        foreach (var provenanceFile in provenanceFiles)
        {
            using (Operation.At(LogEventLevel.Debug)
                       .Time("[{Plugin}] Processing [{FileName}]", DisplayName, provenanceFile.Name))
            {
                try
                {
                    var album = await AlbumForProvenanceFileAsync(
                            provenanceFile,
                            fileSystemDirectoryInfo,
                            cancellationToken,
                            messages)
                        .ConfigureAwait(false);
                    if (album is null)
                    {
                        continue;
                    }

                    var stagingAlbumDataName = Path.Combine(
                        fileSystemDirectoryInfo.FullName(),
                        album.ToMelodeeJsonName(MelodeeConfiguration));
                    if (File.Exists(stagingAlbumDataName))
                    {
                        var existingAlbum = await Album
                            .DeserializeAndInitializeAlbumAsync(serializer, stagingAlbumDataName, cancellationToken)
                            .ConfigureAwait(false);
                        if (existingAlbum is not null)
                        {
                            album = album.Merge(existingAlbum);
                        }
                    }

                    var validationResult = albumValidator.ValidateAlbum(album);
                    album.ValidationMessages = validationResult.Data.Messages ?? [];
                    album.Status = validationResult.Data.AlbumStatus;
                    album.StatusReasons = validationResult.Data.AlbumStatusReasons;

                    await File.WriteAllTextAsync(
                            stagingAlbumDataName,
                            serializer.Serialize(album),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (SafeParser.ToBoolean(Configuration[SettingRegistry.ProcessingDoDeleteOriginal]))
                    {
                        provenanceFile.Delete();
                        Log.Information("[{Plugin}] Deleted Blackbeard provenance file [{FileName}]",
                            DisplayName,
                            provenanceFile.Name);
                    }

                    Log.Debug(
                        "[{Plugin}] created [{StagingAlbumDataName}] Status [{Status}] validation reason [{ValidationReason}]",
                        DisplayName,
                        album.ToMelodeeJsonName(MelodeeConfiguration),
                        album.Status.ToString(),
                        album.StatusReasons.ToString());

                    processedFiles++;
                }
                catch (Exception e)
                {
                    Log.Error(e, "[{Plugin}] processing provenance file [{FileName}]", DisplayName, provenanceFile.Name);
                    StopProcessing = true;
                    return new OperationResult<int>
                    {
                        Type = OperationResponseType.Error,
                        Errors = [e],
                        Data = processedFiles
                    };
                }
            }
        }

        return new OperationResult<int>(messages)
        {
            Data = processedFiles
        };
    }

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        return fileSystemInfo.Name.DoStringsMatch(HandlesFileName);
    }

    public async Task<Album?> AlbumForProvenanceFileAsync(
        FileInfo provenanceFile,
        FileSystemDirectoryInfo directoryInfo,
        CancellationToken cancellationToken = default,
        ICollection<string>? messages = null)
    {
        var document = serializer.Deserialize<BlackbeardProvenanceDocument>(
            await File.ReadAllBytesAsync(provenanceFile.FullName, cancellationToken).ConfigureAwait(false));
        if (document is null)
        {
            Log.Warning("[{Plugin}] unable to deserialize Blackbeard provenance [{FileName}]",
                DisplayName,
                provenanceFile.Name);
            return null;
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            var message =
                $"[{DisplayName}] unsupported Blackbeard schema version [{document.SchemaVersion}] in [{provenanceFile.Name}]. Supported version is [{SupportedSchemaVersion}].";
            Log.Warning("[{Plugin}] unsupported Blackbeard schema version [{SchemaVersion}] in [{FileName}]. Supported version is [{SupportedSchemaVersion}]",
                DisplayName,
                document.SchemaVersion,
                provenanceFile.Name,
                SupportedSchemaVersion);
            messages?.Add(message);
            return null;
        }

        var songs = SongsForDocument(document, directoryInfo).ToArray();
        if (songs.Length == 0)
        {
            Log.Warning("[{Plugin}] no matching song files found for provenance [{FileName}]",
                DisplayName,
                provenanceFile.Name);
            return null;
        }

        var albumArtist = document.Release?.AlbumArtist.Nullify() ??
                          songs.FirstOrDefault(x => x.AlbumArtist().Nullify() is not null)?.AlbumArtist() ??
                          songs.FirstOrDefault(x => x.SongArtist().Nullify() is not null)?.SongArtist();
        var albumTitle = document.Release?.AlbumTitle.Nullify() ??
                         songs.FirstOrDefault(x => x.AlbumTitle().Nullify() is not null)?.AlbumTitle();
        var releaseYear = document.Release?.ReleaseYear ??
                          songs.FirstOrDefault(x => x.AlbumYear() is not null)?.AlbumYear();
        var tracks = document.Tracks ?? [];
        var songTotal = tracks.Length == 0 ? songs.Length : tracks.Max(x => x.TrackTotal ?? x.TrackNumber) ?? songs.Length;
        var discTotal = tracks.Length == 0 ? 1 : tracks.Max(x => x.DiscTotal ?? x.DiscNumber) ?? 1;

        var albumTags = AlbumTags(albumTitle, albumArtist, releaseYear, songTotal, discTotal, songs);
        var dirInfo = new DirectoryInfo(directoryInfo.FullName());
        var parentDirectory = dirInfo.Parent?.ToDirectorySystemInfo();

        return new Album
        {
            AlbumType = albumTitle.TryToDetectAlbumType(),
            Artist = Artist.NewArtistFromName(albumArtist ?? throw new Exception("Invalid Blackbeard album artist.")),
            Directory = directoryInfo,
            Files =
            [
                new AlbumFile
                {
                    AlbumFileType = AlbumFileType.MetaData,
                    ProcessedByPlugin = DisplayName,
                    FileSystemFileInfo = provenanceFile.ToFileSystemInfo()
                }
            ],
            OriginalDirectory = new FileSystemDirectoryInfo
            {
                ParentId = parentDirectory?.UniqueId ?? 0,
                Path = directoryInfo.Path,
                Name = directoryInfo.Name,
                TotalItemsFound = songs.Length,
                MusicFilesFound = songs.Length,
                MusicMetaDataFilesFound = 1
            },
            Images = [],
            Tags = albumTags,
            Songs = songs.OrderBy(x => x.SortOrder).ToArray(),
            ViaPlugins = [DisplayName]
        };
    }

    private static FileInfo[] ProvenanceFiles(FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.FullName());
        return !dirInfo.Exists
            ? []
            : dirInfo.GetFiles(HandlesFileName, SearchOption.TopDirectoryOnly).OrderBy(x => x.Name).ToArray();
    }

    private static IEnumerable<MelodeeSong> SongsForDocument(
        BlackbeardProvenanceDocument document,
        FileSystemDirectoryInfo directoryInfo)
    {
        foreach (var track in document.Tracks ?? [])
        {
            if (track.FinalValidation?.Passed == false)
            {
                continue;
            }

            var trackFile = FileForTrack(directoryInfo, track);
            if (trackFile is null)
            {
                continue;
            }

            var tagsAfter = track.Normalization?.TagsAfter;
            var title = tagsAfter?.Title.Nullify() ?? track.Title.Nullify();
            var artist = tagsAfter?.Artist.Nullify() ?? track.Artist.Nullify();
            var albumArtist = tagsAfter?.AlbumArtist.Nullify() ?? track.AlbumArtist.Nullify();
            var albumTitle = tagsAfter?.AlbumTitle.Nullify() ?? track.AlbumTitle.Nullify();
            var releaseYear = tagsAfter?.ReleaseYear ?? track.ReleaseYear;
            var trackNumber = tagsAfter?.TrackNumber ?? track.TrackNumber;
            var trackTotal = tagsAfter?.TrackTotal ?? track.TrackTotal;
            var discNumber = tagsAfter?.DiscNumber ?? track.DiscNumber ?? 1;
            var discTotal = tagsAfter?.DiscTotal ?? track.DiscTotal ?? 1;

            yield return new MelodeeSong
            {
                CrcHash = Crc32.Calculate(trackFile),
                File = trackFile.ToFileSystemInfo(),
                Tags = SongTags(
                    title,
                    artist,
                    albumArtist,
                    albumTitle,
                    releaseYear,
                    trackNumber,
                    trackTotal,
                    discNumber,
                    discTotal,
                    tagsAfter?.Genres),
                MediaAudios = MediaAudios(tagsAfter),
                SortOrder = (trackNumber ?? 0) + (discNumber * MediaEditService.SortOrderMediaMultiplier) -
                            MediaEditService.SortOrderMediaMultiplier
            };
        }
    }

    private static FileInfo? FileForTrack(FileSystemDirectoryInfo directoryInfo, BlackbeardTrack track)
    {
        foreach (var relativePath in new[] { track.Output?.Path, track.StagedTrackPath })
        {
            var fileInfo = FileUnderDirectory(directoryInfo, relativePath);
            if (fileInfo?.Exists == true)
            {
                return fileInfo;
            }
        }

        return null;
    }

    private static FileInfo? FileUnderDirectory(FileSystemDirectoryInfo directoryInfo, string? relativePath)
    {
        var path = relativePath.Nullify();
        if (path is null || Path.IsPathFullyQualified(path))
        {
            return null;
        }

        var root = Path.GetFullPath(directoryInfo.FullName());
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        var candidate = Path.GetFullPath(Path.Combine(root, path));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FileInfo(candidate);
    }

    private static MetaTag<object?>[] AlbumTags(
        string? albumTitle,
        string? albumArtist,
        int? releaseYear,
        int songTotal,
        int discTotal,
        MelodeeSong[] songs)
    {
        var tags = new List<MetaTag<object?>>();
        AddTag(tags, MetaTagIdentifier.Album, albumTitle, 1);
        AddTag(tags, MetaTagIdentifier.AlbumArtist, albumArtist, 2);
        AddTag(tags, MetaTagIdentifier.DiscTotal, discTotal, 4);

        var sortOrder = 5;
        foreach (var genre in songs.SelectMany(x => x.Tags ?? [])
                     .Where(x => x.Identifier == MetaTagIdentifier.Genre)
                     .Select(x => x.Value?.ToString())
                     .Where(x => x.Nullify() is not null)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddTag(tags, MetaTagIdentifier.Genre, genre, sortOrder++);
        }

        AddTag(tags, MetaTagIdentifier.RecordingYear, releaseYear, 100);
        AddTag(tags, MetaTagIdentifier.SongTotal, songTotal, 101);

        return tags.ToArray();
    }

    private static MetaTag<object?>[] SongTags(
        string? title,
        string? artist,
        string? albumArtist,
        string? albumTitle,
        int? releaseYear,
        int? trackNumber,
        int? trackTotal,
        int? discNumber,
        int? discTotal,
        string[]? genres)
    {
        var tags = new List<MetaTag<object?>>();
        AddTag(tags, MetaTagIdentifier.Title, title, 1);
        AddTag(tags, MetaTagIdentifier.Artist, artist, 2);
        AddTag(tags, MetaTagIdentifier.AlbumArtist, albumArtist, 3);
        AddTag(tags, MetaTagIdentifier.Album, albumTitle, 4);
        AddTag(tags, MetaTagIdentifier.DiscTotal, discTotal, 5);
        AddTag(tags, MetaTagIdentifier.DiscNumber, discNumber, 6);
        AddTag(tags, MetaTagIdentifier.RecordingYear, releaseYear, 100);
        AddTag(tags, MetaTagIdentifier.TrackNumber, trackNumber, 101);
        AddTag(tags, MetaTagIdentifier.SongTotal, trackTotal, 102);

        var sortOrder = 200;
        foreach (var genre in genres ?? [])
        {
            AddTag(tags, MetaTagIdentifier.Genre, genre, sortOrder++);
        }

        return tags.ToArray();
    }

    private static MediaAudio<object?>[] MediaAudios(BlackbeardTrackTags? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var mediaAudios = new List<MediaAudio<object?>>();
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.DurationMs, tags.DurationMs, 1);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.BitRate, tags.Bitrate, 2);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.SampleRate, tags.SampleRate, 3);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.Channels, tags.Channels, 4);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.BitDepth, tags.BitDepth, 5);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.CodecLongName, tags.Codec, 6);
        AddMediaAudio(mediaAudios, MediaAudioIdentifier.FormatName, tags.Container, 7);
        return mediaAudios.ToArray();
    }

    private static void AddTag(List<MetaTag<object?>> tags, MetaTagIdentifier identifier, object? value, int sortOrder)
    {
        if (value is string s && s.Nullify() is null)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        tags.Add(new MetaTag<object?>
        {
            Identifier = identifier,
            Value = value,
            SortOrder = sortOrder
        });
    }

    private static void AddMediaAudio(
        List<MediaAudio<object?>> mediaAudios,
        MediaAudioIdentifier identifier,
        object? value,
        int sortOrder)
    {
        if (value is string s && s.Nullify() is null)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        mediaAudios.Add(new MediaAudio<object?>
        {
            Identifier = identifier,
            Value = value,
            SortOrder = sortOrder
        });
    }
}

public sealed record BlackbeardProvenanceDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("blackbeard_version")]
    public string? BlackbeardVersion { get; init; }

    [JsonPropertyName("release")]
    public BlackbeardRelease? Release { get; init; }

    [JsonPropertyName("tracks")]
    public BlackbeardTrack[]? Tracks { get; init; }
}

public sealed record BlackbeardRelease
{
    [JsonPropertyName("album_artist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("album_title")]
    public string? AlbumTitle { get; init; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; init; }
}

public sealed record BlackbeardTrack
{
    [JsonPropertyName("staged_track_path")]
    public string? StagedTrackPath { get; init; }

    [JsonPropertyName("track_number")]
    public int? TrackNumber { get; init; }

    [JsonPropertyName("track_total")]
    public int? TrackTotal { get; init; }

    [JsonPropertyName("disc_number")]
    public int? DiscNumber { get; init; }

    [JsonPropertyName("disc_total")]
    public int? DiscTotal { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("album_artist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("album_title")]
    public string? AlbumTitle { get; init; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; init; }

    [JsonPropertyName("output")]
    public BlackbeardTrackOutput? Output { get; init; }

    [JsonPropertyName("normalization")]
    public BlackbeardTrackNormalization? Normalization { get; init; }

    [JsonPropertyName("final_validation")]
    public BlackbeardFinalValidation? FinalValidation { get; init; }
}

public sealed record BlackbeardTrackOutput
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed record BlackbeardTrackNormalization
{
    [JsonPropertyName("tags_after")]
    public BlackbeardTrackTags? TagsAfter { get; init; }
}

public sealed record BlackbeardTrackTags
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("album_artist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("album_title")]
    public string? AlbumTitle { get; init; }

    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; init; }

    [JsonPropertyName("track_number")]
    public int? TrackNumber { get; init; }

    [JsonPropertyName("track_total")]
    public int? TrackTotal { get; init; }

    [JsonPropertyName("disc_number")]
    public int? DiscNumber { get; init; }

    [JsonPropertyName("disc_total")]
    public int? DiscTotal { get; init; }

    [JsonPropertyName("genres")]
    public string[]? Genres { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("container")]
    public string? Container { get; init; }

    [JsonPropertyName("bitrate")]
    public int? Bitrate { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("channels")]
    public int? Channels { get; init; }

    [JsonPropertyName("duration_ms")]
    public double? DurationMs { get; init; }

    [JsonPropertyName("bit_depth")]
    public int? BitDepth { get; init; }
}

public sealed record BlackbeardFinalValidation
{
    [JsonPropertyName("passed")]
    public bool? Passed { get; init; }
}
