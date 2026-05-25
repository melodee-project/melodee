using FluentAssertions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Services.Scanning;
using Serilog;

namespace Melodee.Tests.Common.Services.Scanning;

public sealed class StagingAlbumRevalidationStateStoreTests : IDisposable
{
    private readonly string _stagingPath;
    private readonly StagingAlbumRevalidationStateStore _store;

    public StagingAlbumRevalidationStateStoreTests()
    {
        _stagingPath = Path.Combine(Path.GetTempPath(), $"melodee-revalidation-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stagingPath);
        _store = new StagingAlbumRevalidationStateStore(Log.Logger);
    }

    [Fact]
    public async Task OpenAsync_AfterAttemptRecorded_DefersAlbumUntilNextAttempt()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var album = CreateAlbum(_stagingPath);

        await using (var session = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None))
        {
            session.GetDecision(album, now, force: false).IsDue.Should().BeTrue();
            session.RecordAttempt(album, now, "ArtistLookupNoMatch");
            await session.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None);
        var decision = reopened.GetDecision(album, now.AddMinutes(5), force: false);

        decision.IsDue.Should().BeFalse();
        decision.AttemptCount.Should().Be(1);
        decision.NextAttemptAt.Should().Be(StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(1, now));
    }

    [Fact]
    public async Task GetDecision_WhenAlbumFingerprintChanged_ReturnsDue()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var album = CreateAlbum(_stagingPath);

        await using (var session = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None))
        {
            session.RecordAttempt(album, now, "ArtistLookupNoMatch");
            await session.SaveChangesAsync(CancellationToken.None);
        }

        album.Artist = new Artist("Corrected Artist", "CORRECTEDARTIST", "Corrected Artist");

        await using var reopened = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None);
        var decision = reopened.GetDecision(album, now.AddMinutes(5), force: false);

        decision.IsDue.Should().BeTrue();
        decision.Reason.Should().Be("AlbumChanged");
    }

    [Fact]
    public void CalculateNextAttemptAt_UsesBoundedBackoff()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

        StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(1, now).Should().Be(now.AddHours(6));
        StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(2, now).Should().Be(now.AddHours(12));
        StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(3, now).Should().Be(now.AddDays(1));
        StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(4, now).Should().Be(now.AddDays(3));
        StagingAlbumRevalidationStateStore.CalculateNextAttemptAt(99, now).Should().Be(now.AddDays(7));
    }

    [Fact]
    public async Task OpenAsync_RemovesStatesForAlbumsNoLongerInStaging()
    {
        var now = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var album = CreateAlbum(_stagingPath, "Artist - [2026] Album One");
        var remainingAlbum = CreateAlbum(_stagingPath, "Artist - [2026] Album Two");

        await using (var session = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None))
        {
            session.RecordAttempt(album, now, "ArtistLookupNoMatch");
            await session.SaveChangesAsync(CancellationToken.None);
        }

        await using (var session = await _store.OpenAsync(_stagingPath, [remainingAlbum], CancellationToken.None))
        {
            await session.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None);
        var decision = reopened.GetDecision(album, now.AddMinutes(5), force: false);

        decision.IsDue.Should().BeTrue();
        decision.Reason.Should().Be("NoState");
    }

    [Fact]
    public async Task OpenAsync_WhenStateDatabaseIsInvalid_RecreatesDatabase()
    {
        var album = CreateAlbum(_stagingPath);
        var databasePath = StagingAlbumRevalidationStateStore.GetDatabasePath(_stagingPath);
        await File.WriteAllTextAsync(databasePath, "this is not a valid decentdb file");

        await using var session = await _store.OpenAsync(_stagingPath, [album], CancellationToken.None);
        var decision = session.GetDecision(
            album,
            new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero),
            force: false);

        decision.IsDue.Should().BeTrue();
        decision.Reason.Should().Be("NoState");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stagingPath))
            {
                Directory.Delete(_stagingPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static Album CreateAlbum(string stagingPath, string directoryName = "Artist - [2026] Album")
    {
        var albumPath = Path.Combine(stagingPath, directoryName);
        return new Album
        {
            Id = Guid.NewGuid(),
            AlbumType = AlbumType.Album,
            Artist = new Artist("Artist", "ARTIST", "Artist"),
            Directory = new FileSystemDirectoryInfo
            {
                Path = albumPath,
                Name = directoryName
            },
            OriginalDirectory = new FileSystemDirectoryInfo
            {
                Path = albumPath,
                Name = directoryName
            },
            Status = AlbumStatus.Invalid,
            StatusReasons = AlbumNeedsAttentionReasons.HasInvalidArtists,
            ViaPlugins = [],
            Tags =
            [
                new MetaTag<object?> { Identifier = MetaTagIdentifier.AlbumArtist, Value = "Artist" },
                new MetaTag<object?> { Identifier = MetaTagIdentifier.Album, Value = "Album" },
                new MetaTag<object?> { Identifier = MetaTagIdentifier.RecordingYear, Value = "2026" }
            ]
        };
    }
}
