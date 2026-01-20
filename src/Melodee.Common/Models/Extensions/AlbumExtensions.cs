using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Utility;
using Image = SixLabors.ImageSharp.Image;

namespace Melodee.Common.Models.Extensions;

public static class AlbumExtensions
{
    private const int MinimumLengthForSoundtrackRecordingTitle = 2;

    //public static readonly Regex SoundtrackRecordingArtistParseRegex = new(@"(soundtrack|(\(*\s*ost\))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex SoundtrackRecordingArtistParseRegex =
        new(@"(s\s*o\s*u\s*n\s*d\s*t\s*r\s*a\s*c\s*k|(\(?\s*o\s*s\s*t\s*\)?))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string[] SoundtrackTypeAlbumGenres
        =>
        [
            "SOUNDTRACK",
            "ORIGINALSOUNDTRACK",
            "ORIGINALSOUNDTRACKRECORDING",
            "OST"
        ];

    private static string[] OriginalCastTypeAlbumGenres
        =>
        [
            "THEATRE",
            "THEATER",
            "AUDIODRAMA",
            "AUDIOPLAY",
            "AUDIOTHEATRE",
            "AUDIOTHEATER",
            "BROADWAY",
            "CASTRECORDING",
            "RADIOTHEATRE",
            "OCT",
            "ORIGINALRECORDING",
            "ORIGINALCAST",
            "ORIGINALCASTRECORDING"
        ];

    public static KeyValue ToKeyValue(this Album album)
    {
        return new KeyValue(
            album.AlbumDbId?.ToString() ?? album.MusicBrainzId?.ToString() ??
            album.AlbumTitle().ToNormalizedString() ?? album.AlbumTitle() ?? string.Empty,
            album.AlbumTitle().ToNormalizedString() ?? album.AlbumTitle());
    }

    public static long? ArtistAlbumUniqueId(this Album album)
    {
        return SafeParser.Hash(album.Artist.Name.ToNormalizedString() ?? album.Artist.Name,
            album.AlbumTitle() ?? string.Empty);
    }

    public static bool IsStudioTypeAlbum(this Album album)
    {
        return album.Directory.IsDirectoryStudioAlbums() && album.AlbumType is AlbumType.Album or AlbumType.EP;
    }

    public static bool IsSoundTrackTypeAlbum(this Album album)
    {
        var albumTitle = album.AlbumTitle() ?? string.Empty;

        return SoundtrackTypeAlbumGenres.Contains(album.Genre()?.ToNormalizedString() ?? string.Empty) ||
               (albumTitle.Length > MinimumLengthForSoundtrackRecordingTitle &&
                SoundtrackRecordingArtistParseRegex.IsMatch(albumTitle));
    }


    public static bool IsOriginalCastTypeAlbum(this Album album)
    {
        return OriginalCastTypeAlbumGenres.Contains(album.Genre()?.ToNormalizedString() ?? string.Empty) ||
               album.Artist.IsCastRecording() ||
               ArtistExtensions.CastRecordingArtistOrAlbumTitleParseRegex.IsMatch(album.AlbumTitle() ?? string.Empty);
    }

    public static bool IsVariousArtistTypeAlbum(this Album album)
    {
        var songs = (album.Songs ?? []).ToArray();
        if (songs.Length == 0)
        {
            return false;
        }

        if (IsSoundTrackTypeAlbum(album))
        {
            return true;
        }

        var genre = songs.Select(x => x.Genre().Nullify()).Distinct().ToArray();
        if (genre.Length > 0)
        {
            if (genre.Any(_ => album.Artist.IsVariousArtist() || album.Artist.IsCastRecording()))
            {
                return true;
            }
        }

        var t = album.Tags?.ToList() ?? [];
        t.AddRange(album.Songs?.Where(x => x.Tags != null).SelectMany(x => x.Tags!) ?? []);
        if (t.Any(x => x.Identifier == MetaTagIdentifier.Composer &&
                       (ArtistExtensions.VariousArtistParseRegex.IsMatch(x.Value?.ToString() ?? string.Empty) ||
                        x.Value?.ToString().ToNormalizedString() == "VARIOUS" ||
                        x.Value?.ToString().ToNormalizedString() == "VARIOUSARTISTS")))
        {
            return true;
        }

        return album.Artist.IsVariousArtist() || album.Artist.IsCastRecording();
    }

    public static bool HasSongArtists(this Album album)
    {
        var songs = (album.Songs ?? []).ToArray();
        if (songs.Length == 0)
        {
            return false;
        }

        var songArtists = songs
            .Select(x => x.SongArtist())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        return songArtists.Length > 0;
    }

    public static bool Delete(this Album album)
    {
        if (album.Directory.Exists())
        {
            album.Directory.Delete();
            return true;
        }

        return false;
    }

    public static T? MetaTagValue<T>(this Album album, MetaTagIdentifier metaTagIdentifier)
    {
        var d = default(T?);
        if (album.Tags == null || !album.Tags.Any())
        {
            return d;
        }

        var tType = typeof(T?);
        var vv = album.Tags?.FirstOrDefault(x => x.Identifier == metaTagIdentifier)?.Value;
        try
        {
            if (vv == null)
            {
                return d;
            }

            var converter = TypeDescriptor.GetConverter(typeof(T?));
            if (typeof(T?) == typeof(short?))
            {
                return SafeParser.ToNumber<T?>(vv.ToString());
            }

            if (vv is JsonElement)
            {
                vv = vv.ToString() ?? string.Empty;
            }
            // Handle string conversion specially to avoid StringConverter issues
            if (typeof(T?) == typeof(string))
            {
                return (T?)(object)(vv.ToString() ?? string.Empty);
            }

            return (T?)converter.ConvertFrom(vv);
        }
        catch (Exception e)
        {
            Trace.WriteLine(
                $"Song [{album}] MetaTagIdentifier [{metaTagIdentifier.ToString()}] Value [{vv}] to type [{tType}] [{e}]");
        }

        return d;
    }

    public static string ToMelodeeJsonName(this Album album, IMelodeeConfiguration configuration,
        bool? isForAlbumDirectory = null)
    {
        var artistAndAlbumPart = string.Empty;
        if (!(isForAlbumDirectory ?? false))
        {
            var artistNameToUse = (album.Artist.SortName ?? album.Artist.Name).Nullify() ??
                                  album.MetaTagValue<string?>(MetaTagIdentifier.AlbumArtist).Nullify() ??
                                  (album.Songs ?? [])
                                  .Select(x => x.AlbumArtist().Nullify())
                                  .FirstOrDefault(x => x != null) ??
                                  (album.Songs ?? [])
                                  .Select(x => x.SongArtist().Nullify())
                                  .FirstOrDefault(x => x != null) ??
                                  album.Directory.Name;

            var artist = album.Artist.ToAlphanumericName(false, false)
                             .ToTitleCase(false)
                             .Nullify()
                             ?.ToFileNameFriendly() ??
                         SafeParser.Hash(artistNameToUse).ToString();

            var albumTitleValue = album.AlbumTitle().Nullify();
            var albumTitle = albumTitleValue?.ToAlphanumericName(false, false)
                                 .ToTitleCase(false)
                                 .Nullify()
                                 ?.ToFileNameFriendly() ??
                             SafeParser.Hash(albumTitleValue ?? album.Directory.Name).ToString();
            artistAndAlbumPart = $"{artist}_{albumTitle}.";
        }

        if (artistAndAlbumPart.Length + Album.JsonFileName.Length >
            configuration.GetValue<int>(SettingRegistry.ProcessingMaximumAlbumDirectoryNameLength))
        {
            // When artist path is too long generate a unique hash and use that.  
            artistAndAlbumPart = SafeParser.Hash(Guid.NewGuid().ToString()).ToString();
        }

        return $"{artistAndAlbumPart}{Album.JsonFileName}";
    }

    public static string ToDirectoryName(this Album album)
    {
        var artistNameToUse = (album.Artist.SortName ?? album.Artist.Name).Nullify() ??
                              album.MetaTagValue<string?>(MetaTagIdentifier.AlbumArtist).Nullify() ??
                              (album.Songs ?? [])
                              .Select(x => x.AlbumArtist().Nullify())
                              .FirstOrDefault(x => x != null) ??
                              (album.Songs ?? [])
                              .Select(x => x.SongArtist().Nullify())
                              .FirstOrDefault(x => x != null) ??
                              album.Directory.Name;

        var artistSortName = album.Artist.SortName?.ToAlphanumericName(false, false).ToTitleCase().Nullify();
        var artist = (artistSortName ?? album.Artist.ToAlphanumericName(false, false).ToTitleCase().Nullify())
                         ?.ToFileNameFriendly() ??
                     SafeParser.Hash(artistNameToUse).ToString();

        var albumTitleValue = album.AlbumTitle().Nullify();
        var albumTitle = albumTitleValue?.ToAlphanumericName(false, false)
                             .ToTitleCase(false)
                             .Nullify()
                             ?.ToFileNameFriendly() ??
                         SafeParser.Hash(albumTitleValue ?? album.Directory.Name).ToString();
        return $"{artist} - [{album.AlbumYear()}] {albumTitle}".ToFileNameFriendly() ??
               throw new Exception($"[{album}] Unable to determine Album Directory name.");
    }

    public static AlbumArtistType ArtistType(this Album album)
    {
        var artistName = album.Artist.Name;
        if (album.Songs != null)
        {
            if (album.Songs.Any(x => string.Equals(x.SongArtist(), artistName, StringComparison.OrdinalIgnoreCase)))
            {
                return AlbumArtistType.VariousArtists;
            }

            if (album.Songs.Any(x => !string.Equals(x.SongArtist(), artistName, StringComparison.OrdinalIgnoreCase)))
            {
                return AlbumArtistType.VariousArtists;
            }
        }

        return album.ArtistType();
    }


    public static string? DiscSubtitle(this Album album, short discNumber)
    {
        return album.Songs?.Select(x => x.MetaTagValue<string?>(MetaTagIdentifier.DiscSetSubtitle))
            .FirstOrDefault(x => x.Nullify() == null);
    }

    public static string? ArtistSort(this Album album)
    {
        return album.MetaTagValue<string?>(MetaTagIdentifier.SortAlbumArtist);
    }

    /// <summary>
    ///     Return the value set for the Album Tag.
    /// </summary>
    public static string? AlbumTitle(this Album album)
    {
        return album.MetaTagValue<string?>(MetaTagIdentifier.Album);
    }

    /// <summary>
    ///     Return the value set for the Album date.
    /// </summary>
    public static int? AlbumYear(this Album album)
    {
        return (album.MetaTagValue<int?>(MetaTagIdentifier.RecordingYear) ??
                album.MetaTagValue<int?>(MetaTagIdentifier.RecordingDateOrYear) ??
                album.MetaTagValue<int?>(MetaTagIdentifier.OrigAlbumYear)) ??
               SafeParser.ToDateTime(album.MetaTagValue<string?>(MetaTagIdentifier.AlbumDate))?.Year;
    }

    /// <summary>
    ///     Return the value set for the OrigAlbumYear ?? RecordingYear ?? RecordingDateOrYear
    /// </summary>
    public static int? OriginalAlbumYear(this Album album)
    {
        return album.MetaTagValue<int?>(MetaTagIdentifier.OrigAlbumYear) ??
               album.MetaTagValue<int?>(MetaTagIdentifier.RecordingYear) ??
               album.MetaTagValue<int?>(MetaTagIdentifier.RecordingDateOrYear);
    }

    public static bool HasValidAlbumYear(this Album album, Dictionary<string, object?> configuration)
    {
        var albumYear = album.AlbumYear() ?? 0;
        var minimumAlbumYear = configuration.ContainsKey(SettingRegistry.ValidationMinimumAlbumYear)
            ? SafeParser.ToNumber<int>(configuration[SettingRegistry.ValidationMinimumAlbumYear])
            : SettingDefaults.ValidationMinimumAlbumYear;
        var maximumAlbumYear = configuration.ContainsKey(SettingRegistry.ValidationMaximumAlbumYear)
            ? SafeParser.ToNumber<int>(configuration[SettingRegistry.ValidationMaximumAlbumYear])
            : DateTime.Now.Year + 1;

        return albumYear > DateTime.MinValue.Year && albumYear < DateTime.MaxValue.Year &&
               albumYear >= minimumAlbumYear &&
               albumYear <= maximumAlbumYear;
    }

    public static long TotalBytes(this Album album)
    {
        return album.Songs?.Sum(x => x.File.Size) ?? 0;
    }

    public static short SongTotalValue(this Album album)
    {
        var songTotalFromAlbum = album.MetaTagValue<short?>(MetaTagIdentifier.SongTotal);
        if (songTotalFromAlbum is > 0)
        {
            return songTotalFromAlbum.Value;
        }

        var songTotalFromSongNumberTotal = album.MetaTagValue<string>(MetaTagIdentifier.SongNumberTotal);
        if (songTotalFromSongNumberTotal != null)
        {
            var total = songTotalFromSongNumberTotal.Split('/');
            if (total.Length > 1)
            {
                return SafeParser.ToNumber<short?>(total[1]) ?? 0;
            }

            return SafeParser.ToNumber<short?>(songTotalFromSongNumberTotal) ?? 0;
        }

        var songTotalFromSongs = album.Songs?.FirstOrDefault(x => x.SongTotalNumber() > 0);
        if (songTotalFromSongs != null)
        {
            return songTotalFromSongs.SongTotalNumber();
        }

        return SafeParser.ToNumber<short>(album.Songs?.Count() ?? 0);
    }

    public static string? Genre(this Album album)
    {
        return album.MetaTagValue<string?>(MetaTagIdentifier.Genre);
    }

    public static DirectoryInfo ToOriginalDirectoryInfo(this Album album)
    {
        return new DirectoryInfo(album.OriginalDirectory.Path);
    }

    public static IEnumerable<FileInfo> FileInfosForExtension(this Album album, string extension)
    {
        var dirInfo = album.ToOriginalDirectoryInfo();
        if (!dirInfo.Exists)
        {
            return [];
        }

        return dirInfo.GetFiles($"*.{extension}", SearchOption.AllDirectories);
    }

    public static DirectoryInfo[] ImageDirectories(this Album album)
    {
        var result = new List<DirectoryInfo>();
        if (album.Directory.Exists())
        {
            foreach (var directory in Directory.GetDirectories(album.Directory.FullName(), "*.*",
                         SearchOption.TopDirectoryOnly))
            {
                if (ImageHelper.ImageFilesInDirectory(directory, SearchOption.TopDirectoryOnly).Length > 0)
                {
                    result.Add(new DirectoryInfo(directory));
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    ///     Is the given File related to this Album. There can be multiple albums in a directory and images for each.
    /// </summary>
    public static bool IsFileForAlbum(this Album album, IAlbumNamesInDirectoryPlugin albumNamesInDirectoryPlugin,
        FileSystemInfo fileSystemInfo)
    {
        if (!fileSystemInfo.Exists)
        {
            return false;
        }

        // If there is only a single Album in the directory with the image, its logical that image is for that Album.
        var albumNamesInDirectory = albumNamesInDirectoryPlugin.AlbumNamesInDirectory(album.Directory);
        if (albumNamesInDirectory.Data.Length == 1)
        {
            return true;
        }

        if (album.Files.Any())
        {
            var fileSystemInfoJustName = Path.GetFileNameWithoutExtension(fileSystemInfo.FullName).ToAlphanumericName();
            if (album.Files.Select(albumFile => albumFile.FileNameNoExtension().ToAlphanumericName())
                .Any(albumFileNameJustName => albumFileNameJustName.DoStringsMatch(fileSystemInfoJustName)))
            {
                return true;
            }
        }

        var normalizedName = fileSystemInfo.Name.ToAlphanumericName();
        if (string.IsNullOrEmpty(normalizedName))
        {
            return false;
        }

        try
        {
            var normalizedTitleName = album.AlbumTitle()?.ToAlphanumericName() ?? string.Empty;

            if (normalizedName.Contains(normalizedTitleName))
            {
                return true;
            }

            if (album.Songs != null)
            {
                if (album.Songs.Select(song => song.Title()?.ToAlphanumericName() ?? string.Empty)
                    .Any(normalizedSongName => normalizedName.Contains(normalizedSongName)))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Trace.WriteLine(
                $"Error trying to determine if file [{fileSystemInfo.FullName}] is for Album [{album}] Ex [{e.Message}]",
                "Error");
        }

        var fi = new FileInfo(fileSystemInfo.FullName);
        return ImageHelper.IsAlbumImage(fi) || ImageHelper.IsAlbumSecondaryImage(fi);
    }

    /// <summary>
    ///     Return the path to the Albums directory, this does not include the Artists and Library path.
    /// </summary>
    public static string AlbumDirectoryName(this Album album, Dictionary<string, object?> configuration)
    {
        var albumTitleValue = album.AlbumTitle();
        if (string.IsNullOrWhiteSpace(albumTitleValue))
        {
            throw new Exception("Unable to determine Album title from Album.");
        }

        var albumTitle = albumTitleValue;
        var albumPathTitle = albumTitle.ToAlphanumericName(false, false).ToDirectoryNameFriendly()?.ToTitleCase(false);
        if (albumPathTitle.Nullify() == null)
        {
            albumPathTitle = album.Directory.Name.Split(' ').Last().Nullify();
            Trace.WriteLine($"Using directory name [{albumPathTitle} as Album Path Title for album [{album}]");
        }

        if (string.IsNullOrEmpty(albumPathTitle))
        {
            albumPathTitle = SafeParser.Hash(albumTitleValue).ToString();
        }

        var maxFnLength =
            SafeParser.ToNumber<int>(configuration[SettingRegistry.ProcessingMaximumAlbumDirectoryNameLength]) - 7;
        if (albumPathTitle.Length > maxFnLength)
        {
            albumPathTitle = albumPathTitle.Substring(0, maxFnLength);
        }

        var minimumAlbumYear = configuration.ContainsKey(SettingRegistry.ValidationMinimumAlbumYear)
            ? SafeParser.ToNumber<int>(configuration[SettingRegistry.ValidationMinimumAlbumYear])
            : SettingDefaults.ValidationMinimumAlbumYear;
        var maximumValidAlbumYear = configuration.ContainsKey(SettingRegistry.ValidationMaximumAlbumYear)
            ? SafeParser.ToNumber<int>(configuration[SettingRegistry.ValidationMaximumAlbumYear])
            : DateTime.Now.Year + 1;
        var albumYear = album.AlbumYear();

        // If album year is invalid, use a default value instead of throwing an exception
        if (albumYear.HasValue && (albumYear < minimumAlbumYear || albumYear > maximumValidAlbumYear))
        {
            // Use a default year for invalid years instead of throwing
            albumYear = minimumAlbumYear; // Use minimum valid year as fallback
        }

        return $"[{albumYear}] {albumPathTitle}/";
    }

    // public static string ArtistDirectoryName(this Album album, Dictionary<string, object?> configuration)
    // {
    //     var artistNameToUse = album.ArtistSort() ?? album.Artist;
    //     if (string.IsNullOrWhiteSpace(artistNameToUse))
    //     {
    //         throw new Exception("Neither Artist or ArtistSort tag is set on Album.");
    //     }
    //
    //     var artistDirectory = artistNameToUse.ToAlphanumericName(false, false).ToDirectoryNameFriendly()?.ToTitleCase(false);
    //     if (string.IsNullOrEmpty(artistDirectory))
    //     {
    //         throw new Exception($"Unable to determine artist directory for Album ArtistNameToUse [{artistNameToUse}].");
    //     }
    //
    //     var afUpper = artistDirectory.ToUpper();
    //     var fnSubPart1 = afUpper.ToUpper().ToCharArray().Take(1).First();
    //     if (!char.IsLetterOrDigit(fnSubPart1))
    //     {
    //         fnSubPart1 = '#';
    //     }
    //     else if (char.IsNumber(fnSubPart1))
    //     {
    //         fnSubPart1 = '0';
    //     }
    //
    //     var fnSubPart2 = afUpper.Length > 2 ? afUpper.Substring(0, 2) : afUpper;
    //     if (fnSubPart2.EndsWith(" "))
    //     {
    //         var pos = 1;
    //         while (fnSubPart2.EndsWith(" "))
    //         {
    //             pos++;
    //             fnSubPart2 = fnSubPart2.Substring(0, 1) + afUpper.Substring(pos, 1);
    //         }
    //     }
    //
    //     var fnSubPart = Path.Combine(fnSubPart1.ToString(), fnSubPart2);
    //     var fnIdPart = $" [{album.ArtistUniqueId()}]";
    //     var maxFnLength = SafeParser.ToNumber<int>(configuration[SettingRegistry.ProcessingMaximumArtistDirectoryNameLength]) - (fnSubPart.Length + fnIdPart.Length) - 2;
    //     if (artistDirectory.Length > maxFnLength)
    //     {
    //         artistDirectory = artistDirectory.Substring(0, maxFnLength);
    //     }
    //
    //     return Path.Combine(fnSubPart, $"{artistDirectory}{fnIdPart}");
    // }

    public static decimal TotalDurationInMinutes(this Album album)
    {
        var albumDuration = album.TotalDuration();
        return albumDuration > 0 ? new TimeInfo(SafeParser.ToNumber<decimal>(albumDuration)).TotalMinutes : 0;
    }

    /// <summary>
    ///     Total duration of album in Milliseconds.
    /// </summary>
    public static double TotalDuration(this Album album)
    {
        return album.Songs?.Sum(x => x.Duration()) ?? 0;
    }

    public static string Duration(this Album album)
    {
        var songTotalDuration = album.Songs?.Sum(x => x.Duration()) ?? 0;
        return songTotalDuration > 0
            ? new TimeInfo(SafeParser.ToNumber<decimal>(songTotalDuration)).ToFullFormattedString()
            : "--:--";
    }

    /// <summary>
    ///     Return a FileInfo for the first Cover image for the album.
    /// </summary>
    /// <returns>Null if not found otherwise a FileInfo with full path to the cover image.</returns>
    public static FileInfo? CoverImage(this Album album)
    {
        if (!album.Images?.Any() ?? false)
        {
            return null;
        }

        var coverImage = album.Images?.OrderBy(x => x.SortOrder)
            .FirstOrDefault(x => x.PictureIdentifier == PictureIdentifier.Front);
        if (coverImage?.FileInfo != null)
        {
            var result = new FileInfo(coverImage.FileInfo.FullName(album.Directory));
            return result.Exists ? result : null;
        }

        return null;
    }

    public static async Task<string?> CoverImageBase64Async(this Album album,
        CancellationToken cancellationToken = default)
    {
        var cover = album.CoverImage();
        if (cover != null)
        {
            var imageBytes = await File.ReadAllBytesAsync(cover.FullName, cancellationToken);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
        }

        return null;
    }

    public static async Task<byte[]?> CoverImageBytesAsync(this Album album,
        CancellationToken cancellationToken = default)
    {
        var coverImage = album.CoverImage();
        if (coverImage != null)
        {
            return await File.ReadAllBytesAsync(coverImage.FullName, cancellationToken);
        }

        return null;
    }

    public static AlbumQuery ToAlbumQuery(this Album album)
    {
        return new AlbumQuery
        {
            Artist = album.Artist.Name,
            Name = album.AlbumTitle() ?? string.Empty,
            MusicBrainzId = album.MusicBrainzId.ToString(),
            Year = album.AlbumYear() ?? 0
        };
    }

    public static bool RenumberImages(this Album album, CancellationToken cancellationToken = default)
    {
        if (album.Images?.Count() < 1)
        {
            return false;
        }

        var renumberedImages = new List<ImageInfo>();
        foreach (var ii in album.Images!.OrderBy(x => x.SortOrder).Select((x, i) => (x, i)).ToArray())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var newFilename =
                $"{ImageInfo.ImageFilePrefix}{(ii.i + 1).ToStringPadLeft(MelodeeConfiguration.ImageNameNumberPadding)}-{ii.x.PictureIdentifier.ToString()}.jpg";
            if (ii.x.FileInfo?.Exists(album.Directory) ?? false)
            {
                File.Move(ii.x.FileInfo.FullName(album.Directory),
                    Path.Combine(album.Directory.FullName(), newFilename), true);
                renumberedImages.Add(ii.x with
                {
                    FileInfo = new FileSystemFileInfo
                    {
                        Name = newFilename,
                        Size = ii.x.FileInfo?.Size ?? 0,
                        OriginalName = ii.x.FileInfo?.OriginalName
                    }
                });
            }
        }

        album.Images = renumberedImages.ToArray();
        return true;
    }

    public static async Task<IEnumerable<ImageInfo>> FindImages(this Album album,
        IAlbumNamesInDirectoryPlugin albumNamesInDirectoryPlugin, int duplicateThreshold, ImageConvertor imageConvertor,
        IImageValidator imageValidator, bool doDeleteInvalid, CancellationToken cancellationToken = default)
    {
        var imageInfos = new List<ImageInfo>();
        var imageFiles = ImageHelper.ImageFilesInDirectory(album.Directory.Path, SearchOption.TopDirectoryOnly)
            .ToList();
        // If there are directories in the album directory that contains images include the images in that; we don't want to do AllDirectories as
        // there might be nested albums each with their own album image directories.
        foreach (var dir in album.ImageDirectories())
        {
            imageFiles.AddRange(ImageHelper.ImageFilesInDirectory(dir.FullName, SearchOption.TopDirectoryOnly));
        }

        var index = 1;
        foreach (var imageFile in imageFiles.Order())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var fileInfo = new FileInfo(imageFile);

            if (album.IsFileForAlbum(albumNamesInDirectoryPlugin, fileInfo))
            {
                var validationResult = await imageValidator.ValidateImage(fileInfo,
                    ImageHelper.IsAlbumImage(fileInfo) ? PictureIdentifier.Front : PictureIdentifier.SecondaryFront,
                    cancellationToken).ConfigureAwait(false);
                if (!validationResult.Data.IsValid)
                {
                    // Try converting (resizing and padding if needed) image and then revalidate
                    await imageConvertor.ProcessFileAsync(fileInfo.Directory!.ToDirectorySystemInfo(),
                        fileInfo.ToFileSystemInfo(), cancellationToken).ConfigureAwait(false);
                    validationResult = await imageValidator.ValidateImage(fileInfo,
                        ImageHelper.IsAlbumImage(fileInfo) ? PictureIdentifier.Front : PictureIdentifier.SecondaryFront,
                        cancellationToken).ConfigureAwait(false);
                    if (!validationResult.Data.IsValid)
                    {
                        if (doDeleteInvalid)
                        {
                            fileInfo.Delete();
                            Trace.WriteLine(
                                $"Album find images is deleting invalid image [{imageFile}] ValidationResult [{validationResult.Data}");
                        }
                        else
                        {
                            Trace.WriteLine(
                                $"Album find images is skipping invalid image [{imageFile}] ValidationResult [{validationResult.Data}");
                        }

                        continue;
                    }
                }

                var pictureIdentifier = PictureIdentifier.Front;
                if (ImageHelper.IsAlbumSecondaryImage(fileInfo))
                {
                    pictureIdentifier = PictureIdentifier.SecondaryFront;
                }

                var imageInfo = await Image.LoadAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
                var fileInfoFileSystemInfo = fileInfo.ToFileSystemInfo();

                var crc32 = Crc32.Calculate(fileInfo);
                if (imageInfos.Any(x => x.IsCrcHashMatch(crc32)))
                {
                    Trace.WriteLine(
                        $"Album find images is skipping duplicate image [{imageFile}] with crc32 [{crc32}]");
                    continue;
                }

                var fileFileSystemDirectoryInfo = fileInfo.Directory!.ToDirectorySystemInfo();
                var isSameDirectory = string.Equals(fileFileSystemDirectoryInfo.Path, album.Directory.Path, StringComparison.OrdinalIgnoreCase);
                imageInfos.Add(new ImageInfo
                {
                    CrcHash = crc32,
                    FileInfo = new FileSystemFileInfo
                    {
                        Name = fileInfo.Name,
                        Size = fileInfoFileSystemInfo.Size,
                        OriginalName = fileInfo.Name
                    },
                    DirectoryInfo = isSameDirectory ? null : fileFileSystemDirectoryInfo,
                    OriginalFilename = fileInfo.Name,
                    PictureIdentifier = pictureIdentifier,
                    Width = imageInfo.Width,
                    Height = imageInfo.Height,
                    SortOrder = index + (int)pictureIdentifier * 1000 + imageInfo.Width + imageInfo.Height
                });
                index++;
            }
        }

        // If there are images, but not a primary image, then take the first secondary and make it the primary
        if (imageInfos.Count != 0 && imageInfos.All(x => x.PictureIdentifier != PictureIdentifier.Front))
        {
            var firstSecondaryImage = imageInfos
                .OrderBy(x => x.SortOrder)
                .First(x => x.PictureIdentifier == PictureIdentifier.SecondaryFront);
            imageInfos.Remove(firstSecondaryImage);
            var newImageInfos = new List<ImageInfo>
            {
                firstSecondaryImage with
                {
                    PictureIdentifier = PictureIdentifier.Front
                }
            };
            newImageInfos.AddRange(imageInfos);
            imageInfos = newImageInfos;
        }

        // Compare images to attempt to find duplicate images ignoring resolution
        if (imageInfos.Count > 1)
        {
            var newImageInfos = new List<ImageInfo>();
            var imageInfosByCrc = imageInfos.ToDictionary(x => x.CrcHash);
            var seenImageBytes = new List<byte[]>();
            foreach (var imageInfo in imageInfos.OrderBy(x => x.SortOrder))
            {
                var imageFullname = imageInfo.FileInfo?.FullName(imageInfo.DirectoryInfo ?? album.Directory);
                if (imageInfosByCrc.TryGetValue(imageInfo.CrcHash, out var existingImageInfo))
                {
                    if (imageInfo != existingImageInfo && existingImageInfo.Width == imageInfo.Width &&
                        existingImageInfo.Height == imageInfo.Height)
                    {
                        Trace.WriteLine(
                            $"Album find images is skipping duplicate image [{imageInfo.FileInfo?.Name}] with crc32 [{imageInfo.CrcHash}]");
                        if (imageFullname != null)
                        {
                            File.Delete(imageFullname);
                        }

                        continue;
                    }
                }

                if (imageFullname != null)
                {
                    var imageBytes = await File.ReadAllBytesAsync(imageFullname, cancellationToken).ConfigureAwait(false);
                    if (seenImageBytes.Any(x => ImageHasher.Similarity(x, imageBytes) > duplicateThreshold))
                    {
                        Trace.WriteLine(
                            $"Album find images is skipping duplicate image [{imageInfo.FileInfo?.Name}] with crc32 [{imageInfo.CrcHash}]");
                        File.Delete(imageFullname);
                        continue;
                    }

                    seenImageBytes.Add(imageBytes);
                }

                newImageInfos.Add(imageInfo);
            }

            imageInfos = newImageInfos;
        }

        return imageInfos;
    }


    /// <summary>
    ///     Look in the album directory and see if there are any artist images. Most of the time an artist image is one up from
    ///     an album directory in the 'artist' directory.
    /// </summary>
    public static async Task<IEnumerable<ImageInfo>> FindArtistImages(this Album album,
        ImageConvertor imageConvertor,
        IImageValidator imageValidator,
        bool doDeleteOriginal,
        bool doDeleteInvalid,
        CancellationToken cancellationToken = default)
    {
        var imageInfos = new List<ImageInfo>();
        var imageFiles = ImageHelper.ImageFilesInDirectory(album.Directory.Path, SearchOption.TopDirectoryOnly)
            .ToList();
        // If there are directories in the album directory that contains images include the images in that; we don't want to do AllDirectories as
        // there might be nested albums each with their own artist image directories.
        foreach (var dir in album.ImageDirectories())
        {
            imageFiles.AddRange(ImageHelper.ImageFilesInDirectory(dir.FullName, SearchOption.TopDirectoryOnly));
        }

        // Sometimes the album is in a directory with the parent holding an image artist that is not a discography directory 
        var parents = album.Directory.GetParents().ToArray();
        var lookAtParentDirectoriesCount = parents.Length < 2 ? parents.Length : 2;
        for (var i = 0; i < lookAtParentDirectoriesCount; i++)
        {
            imageFiles.AddRange(
                ImageHelper.ImageFilesInDirectory(parents[i].FullName(), SearchOption.TopDirectoryOnly));
        }

        var index = 1;
        foreach (var imageFile in imageFiles.Order())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var fileInfo = new FileInfo(imageFile);
            if (ImageHelper.IsArtistImage(fileInfo) || ImageHelper.IsArtistSecondaryImage(fileInfo))
            {
                // Move the image if not in the album directory
                if (fileInfo.DirectoryName != album.Directory.FullName())
                {
                    var imageFileName = album.Directory.GetNextFileNameForType(Data.Models.Artist.ImageType).Item1;
                    File.Copy(fileInfo.FullName, imageFileName, true);
                    if (doDeleteOriginal)
                    {
                        File.Delete(fileInfo.FullName);
                    }

                    fileInfo = new FileInfo(imageFileName);
                }

                if (!(await imageValidator.ValidateImage(fileInfo,
                        ImageHelper.IsArtistImage(fileInfo)
                            ? PictureIdentifier.Artist
                            : PictureIdentifier.ArtistSecondary, cancellationToken).ConfigureAwait(false)).Data.IsValid)
                {
                    // Try converting (resizing and padding if needed) image and then revalidate
                    await imageConvertor
                        .ProcessFileAsync(fileInfo.ToDirectorySystemInfo(), fileInfo.ToFileSystemInfo(),
                            cancellationToken).ConfigureAwait(false);
                    var validationResult = await imageValidator.ValidateImage(fileInfo,
                        ImageHelper.IsArtistImage(fileInfo)
                            ? PictureIdentifier.Artist
                            : PictureIdentifier.ArtistSecondary, cancellationToken).ConfigureAwait(false);
                    if (!validationResult.IsSuccess)
                    {
                        if (doDeleteInvalid)
                        {
                            fileInfo.Delete();
                            Trace.WriteLine(
                                $"Album find artist images is deleting invalid image [{imageFile}] ValidationResult [{validationResult.Data}");
                        }
                        else
                        {
                            Trace.WriteLine(
                                $"Album find images is skipping invalid image [{imageFile}] ValidationResult [{validationResult.Data}");
                        }
                    }
                }

                var pictureIdentifier = PictureIdentifier.Band;
                if (ImageHelper.IsArtistSecondaryImage(fileInfo))
                {
                    pictureIdentifier = PictureIdentifier.BandSecondary;
                }

                var imageInfo = await Image.LoadAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
                var fileInfoFileSystemInfo = fileInfo.ToFileSystemInfo();

                var crc32 = Crc32.Calculate(fileInfo);
                if (imageInfos.Any(x => x.IsCrcHashMatch(crc32)))
                {
                    Trace.WriteLine(
                        $"Album find artist images is skipping duplicate image [{imageFile}] with crc32 [{crc32}]");
                    continue;
                }

                imageInfos.Add(new ImageInfo
                {
                    CrcHash = Crc32.Calculate(fileInfo),
                    FileInfo = new FileSystemFileInfo
                    {
                        Name = fileInfo.Name,
                        Size = fileInfoFileSystemInfo.Size,
                        OriginalName = fileInfo.Name
                    },
                    OriginalFilename = fileInfo.Name,
                    PictureIdentifier = pictureIdentifier,
                    Width = imageInfo.Width,
                    Height = imageInfo.Height,
                    SortOrder = index
                });
                index++;
            }
        }

        return imageInfos;
    }
}
