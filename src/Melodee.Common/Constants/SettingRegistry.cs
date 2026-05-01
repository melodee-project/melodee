namespace Melodee.Common.Constants;

public static class SettingRegistry
{
    public const string ArtistBiographyPlaceHolderText = "artist.biographyPlaceHolderText";
    public const string ConversionBitrate = "conversion.bitrate";
    public const string ConversionEnabled = "conversion.enabled";
    public const string ConversionSamplingRate = "conversion.samplingRate";
    public const string ConversionVbrLevel = "conversion.vbrLevel";
    public const string DefaultsBatchSize = "defaults.batchSize";
    public const string DefaultsPageSize = "defaults.pagesize";
    public const string DefaultCacheDurationInMinutes = "defaults.cacheDurationInMinutes";
    public const string DefaultsDashboardLatestPageSize = "defaults.dashboard.latestPageSize";
    public const string EncryptionPrivateKey = "encryption.privateKey";
    public const string FilteringLessThanDuration = "filtering.lessThanDuration";
    public const string FilteringLessThanSongCount = "filtering.lessThanSongCount";
    public const string FormattingDateTimeDisplayActivityFormat = "formatting.dateTimeDisplayActivityFormat";
    public const string FormattingDateTimeDisplayFormatShort = "formatting.dateTimeDisplayFormatShort";
    public const string ImagingDoLoadEmbeddedImages = "imaging.doLoadEmbeddedImages";
    public const string ImagingMaximumNumberOfAlbumImages = "imaging.maximumNumberOfAlbumImages";
    public const string ImagingMaximumNumberOfArtistImages = "imaging.maximumNumberOfArtistImages";
    public const string ImagingMinimumImageSize = "imaging.minimumImageSize";
    public const string ImagingSmallSize = "imaging.smallSize";
    public const string ImagingMediumSize = "imaging.mediumSize";
    public const string ImagingLargeSize = "imaging.largeSize";
    public const string ImagingThumbnailSize = "imaging.thumbnailSize";
    public const string ImagingDuplicateThreshold = "imaging.duplicateThreshold";
    public const string JobsArtistHousekeepingCronExpression = "jobs.artistHousekeeping.cronExpression";
    public const string JobsArtistSearchEngineHousekeepingCronExpression = "jobs.artistSearchEngineHousekeeping.cronExpression";
    public const string JobsChartUpdateCronExpression = "jobs.chartUpdate.cronExpression";
    public const string JobsLibraryProcessCronExpression = "jobs.libraryProcess.cronExpression";
    public const string JobsLibraryInsertCronExpression = "jobs.libraryInsert.cronExpression";
    public const string JobsNowPlayingCleanupCronExpression = "jobs.nowPlayingCleanup.cronExpression";
    public const string JobsStagingAutoMoveCronExpression = "jobs.stagingAutoMove.cronExpression";
    public const string JobsStagingAlbumRevalidationCronExpression = "jobs.stagingAlbumRevalidation.cronExpression";
    public const string JobsMusicBrainzUpdateDatabaseCronExpression = "jobs.musicbrainzUpdateDatabase.cronExpression";
    public const string LyricFilesEnabled = "lyrics.filesEnabled";
    public const string MagicDoRemoveFeaturingArtistFromSongArtist = "magic.doRemoveFeaturingArtistFromSongArtist";
    public const string MagicDoRemoveFeaturingArtistFromSongTitle = "magic.doRemoveFeaturingArtistFromSongTitle";
    public const string MagicDoRemoveUnwantedTextFromAlbumTitle = "magic.doRemoveUnwantedTextFromAlbumTitle";
    public const string MagicDoRemoveUnwantedTextFromSongTitles = "magic.doRemoveUnwantedTextFromSongTitles";
    public const string MagicDoRenumberSongs = "magic.doRenumberSongs";
    public const string MagicDoReplaceSongsArtistSeparators = "magic.doReplaceSongsArtistSeparators";
    public const string MagicDoSetYearToCurrentIfInvalid = "magic.doSetYearToCurrentIfInvalid";
    public const string MagicEnabled = "magic.enabled";
    public const string OpenSubsonicIndexesArtistLimit = "openSubsonicServer.openSubsonicServer.index.artistLimit";
    public const string OpenSubsonicServerLicenseEmail = "openSubsonicServer.openSubsonicServerLicenseEmail";
    public const string OpenSubsonicServerSupportedVersion = "openSubsonicServer.openSubsonic.serverSupportedVersion";
    public const string OpenSubsonicServerType = "openSubsonicServer.openSubsonicServer.type";
    public const string PartyModeEnabled = "partyMode.enabled";

    // Jukebox settings
    public const string JukeboxEnabled = "jukebox.enabled";
    public const string JukeboxBackendType = "jukebox.backendType";

    // MPV Backend settings
    public const string MpvPath = "mpv.path";
    public const string MpvAudioDevice = "mpv.audioDevice";
    public const string MpvExtraArgs = "mpv.extraArgs";
    public const string MpvSocketPath = "mpv.socketPath";
    public const string MpvInitialVolume = "mpv.initialVolume";
    public const string MpvEnableDebugOutput = "mpv.enableDebugOutput";

    // MPD Backend settings
    public const string MpdInstanceName = "mpd.instanceName";
    public const string MpdHost = "mpd.host";
    public const string MpdPort = "mpd.port";
    public const string MpdPassword = "mpd.password";
    public const string MpdTimeoutMs = "mpd.timeoutMs";
    public const string MpdInitialVolume = "mpd.initialVolume";
    public const string MpdEnableDebugOutput = "mpd.enableDebugOutput";

    public const string PlaylistDynamicPlaylistsDisabled = "playlist.dynamicPlaylist.disabled";
    public const string PlaylistMaximumAllowedPageSize = "playlist.maximumAllowedPageSize";
    public const string PluginEnabledCueSheet = "plugin.cueSheet.enabled";
    public const string PluginEnabledM3u = "plugin.m3u.enabled";
    public const string PluginEnabledNfo = "plugin.nfo.enabled";
    public const string PluginEnabledSimpleFileVerification = "plugin.simpleFileVerification.enabled";
    public const string ProcessingAlbumTitleRemovals = "processing.albumTitleRemovals";

    // Podcast settings
    public const string PodcastEnabled = "podcast.enabled";
    public const string PodcastHttpAllowHttp = "podcast.http.allowHttp";
    public const string PodcastHttpTimeoutSeconds = "podcast.http.timeoutSeconds";
    public const string PodcastHttpMaxRedirects = "podcast.http.maxRedirects";
    public const string PodcastHttpMaxFeedBytes = "podcast.http.maxFeedBytes";
    public const string PodcastRefreshMaxItemsPerChannel = "podcast.refresh.maxItemsPerChannel";
    public const string PodcastDownloadMaxConcurrentGlobal = "podcast.download.maxConcurrent.global";
    public const string PodcastDownloadMaxConcurrentPerUser = "podcast.download.maxConcurrent.perUser";
    public const string PodcastDownloadMaxEnclosureBytes = "podcast.download.maxEnclosureBytes";
    public const string PodcastQuotaMaxBytesPerUser = "podcast.quota.maxBytesPerUser";
    public const string PodcastRetentionDownloadedEpisodesInDays = "podcast.retention.downloadedEpisodesInDays";
    public const string PodcastRetentionKeepLastNEpisodes = "podcast.retention.keepLastNEpisodes";
    public const string PodcastRetentionKeepUnplayedOnly = "podcast.retention.keepUnplayedOnly";
    public const string PodcastRecoveryStuckDownloadThresholdMinutes = "podcast.recovery.stuckDownloadThresholdMinutes";
    public const string PodcastRecoveryOrphanedUsageThresholdHours = "podcast.recovery.orphanedUsageThresholdHours";
    public const string JobsPodcastRefreshCronExpression = "jobs.podcastRefresh.cronExpression";
    public const string JobsPodcastDownloadCronExpression = "jobs.podcastDownload.cronExpression";
    public const string JobsPodcastCleanupCronExpression = "jobs.podcastCleanup.cronExpression";
    public const string JobsPodcastRecoveryCronExpression = "jobs.podcastRecovery.cronExpression";

    public const string ProcessingArtistNameReplacements = "processing.artistNameReplacements";
    public const string ProcessingConvertedExtension = "processing.convertedExtension";
    public const string ProcessingDoContinueOnDirectoryProcessingErrors = "processing.doContinueOnDirectoryProcessingErrors";
    public const string ProcessingDoDeleteComments = "processing.doDeleteComments";
    public const string ProcessingDoDeleteOriginal = "processing.doDeleteOriginal";
    public const string ProcessingDontDeleteExistingMelodeeDataFiles = "processing.dontDeleteExisitingMelodeeDataFiles";
    public const string ProcessingFileExtensionsToDelete = "processing.fileExtensionsToDelete";
    public const string ProcessingDoOverrideExistingMelodeeDataFiles = "processing.doOverrideExistingMelodeeDataFiles";
    public const string ProcessingDoUseCurrentYearAsDefaultOrigAlbumYearValue = "processing.doUseCurrentYearAsDefaultOrigAlbumYearValue";
    public const string ProcessingDuplicateAlbumPrefix = "processing.duplicateAlbumPrefix";
    public const string ProcessingIgnoredArticles = "processing.ignoredArticles";
    public const string ProcessingIgnoredPerformers = "processing.ignoredPerformers";
    public const string ProcessingIgnoredProduction = "processing.ignoredProduction";
    public const string ProcessingIgnoredPublishers = "processing.ignoredPublishers";
    public const string ProcessingMaximumAlbumDirectoryNameLength = "processing.maximumAlbumDirectoryNameLength";
    public const string ProcessingMaximumArtistDirectoryNameLength = "processing.maximumArtistDirectoryNameLength";
    public const string ProcessingMaximumProcessingCount = "processing.maximumProcessingCount";
    public const string ProcessingProcessedExtension = "processing.processedExtension";
    public const string ProcessingSongTitleRemovals = "processing.songTitleRemovals";
    public const string RegisterPrivateCode = "register.privateCode";
    public const string RegisterDisabled = "register.disabled";
    public const string ScriptingEnabled = "scripting.enabled";
    public const string ScriptingPostDiscoveryScript = "scripting.postDiscoveryScript";
    public const string ScriptingPreDiscoveryScript = "scripting.preDiscoveryScript";
    public const string ScrobblingEnabled = "scrobbling.enabled";
    public const string ScrobblingLastFmApiKey = "scrobbling.lastFm.apiKey";
    public const string ScrobblingLastFmSharedSecret = "scrobbling.lastFm.sharedSecret";
    public const string ScrobblingLastFmEnabled = "scrobbling.lastFm.Enabled";
    public const string SearchEngineDefaultPageSize = "searchEngine.defaultPageSize";
    public const string SearchEngineMaximumAllowedPageSize = "searchEngine.maximumAllowedPageSize";
    public const string SearchEngineBraveEnabled = "searchEngine.brave.enabled";
    public const string SearchEngineBraveApiKey = "searchEngine.brave.apiKey";
    public const string SearchEngineBraveBaseUrl = "searchEngine.brave.baseUrl";
    public const string SearchEngineBraveImageSearchPath = "searchEngine.brave.imageSearchPath";
    public const string SearchEngineDeezerEnabled = "searchEngine.deezer.enabled";
    public const string SearchEngineITunesEnabled = "searchEngine.itunes.enabled";
    public const string SearchEngineLastFmEnabled = "searchEngine.lastFm.Enabled";
    public const string SearchEngineMusicBrainzEnabled = "searchEngine.musicbrainz.enabled";
    public const string SearchEngineMusicBrainzImportMaximumToProcess = "searchEngine.musicbrainz.importMaximumToProcess";
    public const string SearchEngineMusicBrainzImportBatchSize = "searchEngine.musicbrainz.importBatchSize";
    public const string SearchEngineMusicBrainzStoragePath = "searchEngine.musicbrainz.storagePath";
    public const string SearchEngineMusicBrainzImportLastImportTimestamp = "searchEngine.musicbrainz.importLastImportTimestamp";
    public const string SearchEngineArtistSearchDatabaseRefreshInDays = "searchEngine.artistSearchDatabaseRefreshInDays";
    public const string SearchEngineSpotifyEnabled = "searchEngine.spotify.enabled";
    public const string SearchEngineSpotifyApiKey = "searchEngine.spotify.apiKey";
    public const string SearchEngineSpotifyClientSecret = "searchEngine.spotify.sharedSecret";
    public const string SearchEngineSpotifyAccessToken = "searchEngine.spotify.accessToken";
    public const string SearchEngineDiscogsEnabled = "searchEngine.discogs.enabled";
    public const string SearchEngineDiscogsUserToken = "searchEngine.discogs.userToken";
    public const string SearchEngineWikiDataEnabled = "searchEngine.wikidata.enabled";
    public const string SearchEngineMetalApiEnabled = "searchEngine.metalApi.enabled";
    public const string SearchEngineUserAgent = "searchEngine.userAgent";
    public const string SearchResultsDefaultPageSize = "searchResults.defaultPageSize";
    public const string SecurityBlacklistedEmails = "security.blacklistedEmails";
    public const string SecurityBlacklistedIPs = "security.blacklistedIPs";
    public const string SystemBaseUrl = "system.baseUrl";
    public const string SystemSiteName = "system.siteName";
    public const string SystemIsDownloadingEnabled = "system.isDownloadingEnabled";
    public const string SystemMaxUploadSize = "system.maxUploadSize";
    public const string SystemOnboardingCompletedAt = "system.onboardingCompletedAt";
    // Streaming settings
    public const string StreamingUseBufferedResponses = "streaming.useBufferedResponses"; // bool: fallback to buffered responses
    public const string StreamingMaxConcurrentStreamsGlobal = "streaming.maxConcurrentStreams.global"; // int: 0 or less = unlimited
    public const string StreamingMaxConcurrentStreamsPerUser = "streaming.maxConcurrentStreams.perUser"; // int: 0 or less = unlimited
    public const string TranscodingCommandAac = "transcoding.command.aac";
    public const string TranscodingCommandMp3 = "transcoding.command.mp3";
    public const string TranscodingCommandOpus = "transcoding.command.opus";
    public const string TranscodingDefault = "transcoding.default";

    // User Device Profile settings
    public const string UserDeviceProfileEnabled = "userDeviceProfile.enabled";

    public const string UserInterfaceToastAutoCloseTime = "userinterface.toastAutoCloseTime";
    public const string ValidationMaximumAlbumYear = "validation.maximumAlbumYear";
    public const string ValidationMaximumSongNumber = "validation.maximumSongNumber";
    public const string ValidationMinimumAlbumYear = "validation.minimumAlbumYear";
    public const string ValidationMinimumSongCount = "validation.minimumSongCount";
    public const string ValidationMinimumAlbumDuration = "validation.minimumAlbumDuration";

    // Email settings
    public const string EmailEnabled = "email.enabled";
    public const string EmailFromName = "email.fromName";
    public const string EmailFromEmail = "email.fromEmail";
    public const string EmailSmtpHost = "email.smtpHost";
    public const string EmailSmtpPort = "email.smtpPort";
    public const string EmailSmtpUsername = "email.smtpUsername";
    public const string EmailSmtpPassword = "email.smtpPassword";
    public const string EmailSmtpUseSsl = "email.smtpUseSsl";
    public const string EmailSmtpUseStartTls = "email.smtpUseStartTls";
    public const string EmailResetPasswordSubject = "email.resetPassword.subject";
    public const string EmailResetPasswordTextBodyTemplate = "email.resetPassword.textBodyTemplate";
    public const string EmailResetPasswordHtmlBodyTemplate = "email.resetPassword.htmlBodyTemplate";

    // Security settings
    public const string SecuritySecretKey = "security.secretKey";
    public const string SecurityPasswordResetTokenExpiryMinutes = "security.passwordResetTokenExpiryMinutes";

    // Jellyfin API settings
    public const string JellyfinEnabled = "jellyfin.enabled";
    public const string JellyfinRoutePrefix = "jellyfin.routePrefix";
    public const string JellyfinTokenExpiresAfterHours = "jellyfin.token.expiresAfterHours";
    public const string JellyfinTokenMaxActivePerUser = "jellyfin.token.maxActivePerUser";
    public const string JellyfinTokenAllowLegacyHeaders = "jellyfin.token.allowLegacyHeaders";
    public const string JellyfinTokenPepper = "jellyfin.token.pepper";
    public const string JellyfinRateLimitApiRequestsPerPeriod = "jellyfin.rateLimit.apiRequestsPerPeriod";
    public const string JellyfinRateLimitApiPeriodSeconds = "jellyfin.rateLimit.apiPeriodSeconds";
    public const string JellyfinRateLimitStreamConcurrentPerUser = "jellyfin.rateLimit.streamConcurrentPerUser";

    // Theme settings
    public const string ThemeLibraryPath = "theme.libraryPath";
    public const string SystemDefaultTheme = "system.defaultTheme";
    public const string ThemeMaxUploadSizeMb = "theme.maxUploadSizeMb";
    public const string ThemeEnforceContrastValidation = "theme.enforceContrastValidation";
}
