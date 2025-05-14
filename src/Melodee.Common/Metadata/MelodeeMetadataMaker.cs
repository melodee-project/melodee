using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.SpecialArtists;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Serilog;
using SixLabors.ImageSharp;
using ImageInfo = Melodee.Common.Models.ImageInfo;

namespace Melodee.Common.Metadata;

public class MelodeeMetadataMaker(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    ISerializer serializer,
    ArtistSearchEngineService artistSearchEngineService,
    AlbumImageSearchEngineService albumImageSearchEngineService,
    IHttpClientFactory httpClientFactory,
    MediaEditService mediaEditService)
{
    /// <summary>
    ///     For a given directory generate a Melodee Metadata file (melodee.json). Does not modify files in place.
    /// </summary>
    public async Task<OperationResult<Album?>> MakeMetadataFileAsync(string directory,
        bool doCreateOnlyIfMissing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(directory, nameof(directory));

        var directoryInfo = directory.ToFileSystemDirectoryInfo();
        if (!directoryInfo.Exists())
        {
            return new OperationResult<Album?>($"Directory does not exist [{directory}] does not exist")
            {
                Data = null
            };
        }

        var isFound = directoryInfo.MelodeeJsonFiles(false).Any();
        if (isFound && doCreateOnlyIfMissing)
        {
            return new OperationResult<Album?>($"Directory already contains melodee metadata file [{directory}]")
            {
                Data = null
            };
        }

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        await artistSearchEngineService.InitializeAsync(configuration, cancellationToken).ConfigureAwait(false);
        await mediaEditService.InitializeAsync(configuration, cancellationToken).ConfigureAwait(false);

        var albumValidator = new AlbumValidator(configuration);
        var imageValidator = new ImageValidator(configuration);
        var imageConvertor = new ImageConvertor(configuration);
        var songPlugin = new AtlMetaTag(new MetaTagsProcessor(configuration, serializer), imageConvertor,
            imageValidator, configuration);
        var mp3Files = new Mp3Files([songPlugin], albumValidator, serializer, logger, configuration);

        var processResult =
            await mp3Files.ProcessDirectoryAsync(directoryInfo, cancellationToken).ConfigureAwait(false);
        if (!processResult.IsSuccess)
        {
            return new OperationResult<Album?>($"Could not generate metadata album from directory [{directory}]")
            {
                Data = null
            };
        }

        var albumFilename = Path.Combine(directoryInfo.FullName(), Album.JsonFileName);
        var album = await Album.DeserializeAndInitializeAlbumAsync(serializer, albumFilename, cancellationToken)
            .ConfigureAwait(false);
        if (album == null)
        {
            return new OperationResult<Album?>($"Could not load metadata album from [{albumFilename}]")
            {
                Data = null
            };
        }


        var albumImages = new List<ImageInfo>();
        var duplicateThreshold = configuration.GetValue<int?>(SettingRegistry.ImagingDuplicateThreshold) ??
                                 MelodeeConfiguration.DefaultImagingDuplicateThreshold;
        var foundAlbumImages =
            (await album.FindImages(songPlugin, duplicateThreshold, imageConvertor, imageValidator,
                    configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal), cancellationToken)
                .ConfigureAwait(false)).ToArray();
        if (foundAlbumImages.Length != 0)
        {
            foreach (var foundAlbumImage in foundAlbumImages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!albumImages.Any(x => x.IsCrcHashMatch(foundAlbumImage.CrcHash)))
                {
                    albumImages.Add(foundAlbumImage);
                }
            }
        }

        album.Images = albumImages.ToArray();

        album.Artist = new Artist(album.Artist.Name, album.Artist.NameNormalized, album.Artist.SortName, []);
        if (album.IsSoundTrackTypeAlbum() && album.Songs != null)
        {
            // If the album has different artists and is soundtrack then ensure artist is set to special VariousArtists
            var songsGroupedByArtist = album.Songs.GroupBy(x => x.AlbumArtist()).ToArray();
            if (songsGroupedByArtist.Length > 1)
            {
                album.Artist = new VariousArtist();
                foreach (var song in album.Songs)
                {
                    album.SetSongTagValue(song.Id, MetaTagIdentifier.AlbumArtist, album.Artist.Name);
                }
            }
        }
        else if (album.IsOriginalCastTypeAlbum() && album.Songs != null)
        {
            // If the album has different artists and is Original Cast type then ensure artist is set to special Theater
            // NOTE: Remember Original Cast Type albums with a single composer/artist is attributed to that composer/artist (e.g. Stephen Schwartz - Wicked)
            var songsGroupedByArtist = album.Songs.GroupBy(x => x.AlbumArtist()).ToArray();
            if (songsGroupedByArtist.Length > 1)
            {
                album.Artist = new Theater();
                foreach (var song in album.Songs)
                {
                    album.SetSongTagValue(song.Id, MetaTagIdentifier.AlbumArtist, album.Artist.Name);
                }
            }
        }

        album.Directory = directoryInfo;

        // See if artist can be found using ArtistSearchEngine to populate metadata, set UniqueId and MusicBrainzId

        var searchRequest = album.Artist.ToArtistQuery([
            new KeyValue((album.AlbumYear() ?? 0).ToString(),
                album.AlbumTitle().ToNormalizedString() ?? album.AlbumTitle())
        ]);
        var artistSearchResult = await artistSearchEngineService.DoSearchAsync(searchRequest,
                1,
                cancellationToken)
            .ConfigureAwait(false);
        if (artistSearchResult.IsSuccess)
        {
            var artistFromSearch = artistSearchResult.Data.OrderByDescending(x => x.Rank).FirstOrDefault();
            if (artistFromSearch != null)
            {
                album.Artist = album.Artist with
                {
                    AmgId = album.Artist.AmgId ?? artistFromSearch.AmgId,
                    ArtistDbId = album.Artist.ArtistDbId ?? artistFromSearch.Id,
                    DiscogsId = album.Artist.DiscogsId ?? artistFromSearch.DiscogsId,
                    ItunesId = album.Artist.ItunesId ?? artistFromSearch.ItunesId,
                    LastFmId = album.Artist.LastFmId ?? artistFromSearch.LastFmId,
                    MusicBrainzId = album.Artist.MusicBrainzId ?? artistFromSearch.MusicBrainzId,
                    Name = album.Artist.Name.Nullify() ?? artistFromSearch.Name,
                    NameNormalized = album.Artist.NameNormalized.Nullify() ??
                                     artistFromSearch.Name.ToNormalizedString() ?? artistFromSearch.Name,
                    OriginalName = artistFromSearch.Name != album.Artist.Name ? album.Artist.Name : null,
                    SearchEngineResultUniqueId = album.Artist.SearchEngineResultUniqueId is null or < 1
                        ? artistFromSearch.UniqueId
                        : album.Artist.SearchEngineResultUniqueId,
                    SortName = album.Artist.SortName ?? artistFromSearch.SortName,
                    SpotifyId = album.Artist.SpotifyId ?? artistFromSearch.SpotifyId,
                    WikiDataId = album.Artist.WikiDataId ?? artistFromSearch.WikiDataId
                };

                if (artistFromSearch.Releases?.FirstOrDefault() != null)
                {
                    var searchResultRelease = artistFromSearch.Releases.FirstOrDefault(x =>
                        x.Year == album.AlbumYear() && x.NameNormalized == album.AlbumTitle().ToNormalizedString());
                    if (searchResultRelease != null)
                    {
                        album.AlbumDbId = album.AlbumDbId ?? searchResultRelease.Id;
                        album.AlbumType = album.AlbumType == AlbumType.NotSet
                            ? searchResultRelease.AlbumType
                            : album.AlbumType;

                        // Artist result should override any in place for Album as its more specific and likely more accurate
                        album.MusicBrainzId = searchResultRelease.MusicBrainzId;
                        album.SpotifyId = searchResultRelease.SpotifyId;

                        if (!album.HasValidAlbumYear(configuration.Configuration))
                        {
                            album.SetTagValue(MetaTagIdentifier.RecordingYear, searchResultRelease.Year.ToString());
                        }
                    }
                }

                album.Status = AlbumStatus.Ok;

                logger.Debug(
                    "[{Name}] Using artist from search engine query [{SearchRequest}] result [{ArtistFromSearch}]",
                    nameof(MelodeeMetadataMaker),
                    searchRequest,
                    artistFromSearch);
            }
            else
            {
                logger.Warning("[{Name}] No result from search engine for artist [{searchRequest}]",
                    nameof(MelodeeMetadataMaker),
                    searchRequest);
            }
        }

        // If album has no images then see if ImageSearchEngine can find any
        if (album.Images?.Count() == 0)
        {
            var albumImageSearchRequest = album.ToAlbumQuery();
            var albumImageSearchResult = await albumImageSearchEngineService.DoSearchAsync(albumImageSearchRequest,
                    1,
                    cancellationToken)
                .ConfigureAwait(false);
            if (albumImageSearchResult.IsSuccess)
            {
                var imageSearchResult = albumImageSearchResult.Data.OrderByDescending(x => x.Rank).FirstOrDefault();
                if (imageSearchResult != null)
                {
                    album.AmgId ??= imageSearchResult.AmgId;
                    album.DiscogsId ??= imageSearchResult.DiscogsId;
                    album.ItunesId ??= imageSearchResult.ItunesId;
                    album.LastFmId ??= imageSearchResult.LastFmId;
                    album.SpotifyId ??= imageSearchResult.SpotifyId;
                    album.WikiDataId ??= imageSearchResult.WikiDataId;

                    album.Artist.AmgId ??= imageSearchResult.ArtistAmgId;
                    album.Artist.DiscogsId ??= imageSearchResult.ArtistDiscogsId;
                    album.Artist.ItunesId ??= imageSearchResult.ArtistItunesId;
                    album.Artist.LastFmId ??= imageSearchResult.ArtistLastFmId;
                    album.Artist.SpotifyId ??= imageSearchResult.ArtistSpotifyId;
                    album.Artist.WikiDataId ??= imageSearchResult.ArtistWikiDataId;

                    if (!album.HasValidAlbumYear(configuration.Configuration) && imageSearchResult.ReleaseDate != null)
                    {
                        album.SetTagValue(MetaTagIdentifier.RecordingYear, imageSearchResult.ReleaseDate.ToString());
                    }

                    var albumImageFromSearchFileName = Path.Combine(directoryInfo.FullName(),
                        directoryInfo.GetNextFileNameForType(Data.Models.Album.FrontImageType).Item1);

                    var httpClient = httpClientFactory.CreateClient();
                    if (await httpClient.DownloadFileAsync(
                            imageSearchResult.MediaUrl,
                            albumImageFromSearchFileName,
                            async (_, newFileInfo, _) =>
                                (await imageValidator.ValidateImage(newFileInfo, PictureIdentifier.Front,
                                    cancellationToken)).Data.IsValid,
                            cancellationToken).ConfigureAwait(false))
                    {
                        var newImageInfo = new FileInfo(albumImageFromSearchFileName);
                        var imageInfo = await Image.IdentifyAsync(albumImageFromSearchFileName, cancellationToken)
                            .ConfigureAwait(false);
                        album.Images = new List<ImageInfo>
                        {
                            new()
                            {
                                FileInfo = newImageInfo.ToFileSystemInfo(),
                                PictureIdentifier = PictureIdentifier.Front,
                                CrcHash = Crc32.Calculate(newImageInfo),
                                Width = imageInfo.Width,
                                Height = imageInfo.Height,
                                SortOrder = 1,
                                WasEmbeddedInSong = false
                            }
                        };
                        Log.Debug("[{Name}] Downloaded album image [{MediaUrl}]", nameof(MelodeeMetadataMaker),
                            imageSearchResult.MediaUrl);
                    }
                }
                else
                {
                    Log.Warning("[{Name}] No result from album search engine for album [{albumImageSearchRequest}]",
                        nameof(MelodeeMetadataMaker), albumImageSearchRequest);
                }
            }
        }

        var validationResult = albumValidator.ValidateAlbum(album);
        album.ValidationMessages = validationResult.Data.Messages ?? [];
        album.Status = validationResult.Data.AlbumStatus;
        album.StatusReasons = validationResult.Data.AlbumStatusReasons;

        var serialized = serializer.Serialize(album);
        var jsonName = album.ToMelodeeJsonName(configuration, true);
        if (jsonName.Nullify() != null)
        {
            await File.WriteAllTextAsync(Path.Combine(directoryInfo.FullName(), jsonName), serialized,
                cancellationToken).ConfigureAwait(false);
            if (configuration.GetValue<bool>(SettingRegistry.MagicEnabled) && !album.IsValid)
            {
                var magicResult = await mediaEditService.DoMagic(album, cancellationToken).ConfigureAwait(false);
                if (magicResult.Data.AlbumStatus != album.Status)
                {
                    album = serializer.Deserialize<Album>(Path.Combine(directoryInfo.FullName(), jsonName));
                }
            }
        }

        if (album?.IsValid ?? false)
        {
            logger.Information("[{Name}] \ud83d\udc4d created valid melodee metadata album file [{Filename}]",
                nameof(MelodeeMetadataMaker),
                Path.Combine(directoryInfo.FullName(), jsonName));
        }

        return new OperationResult<Album?>
        {
            Data = album
        };
    }
}
