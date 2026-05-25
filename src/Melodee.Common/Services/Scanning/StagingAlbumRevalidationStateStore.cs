using System.Security.Cryptography;
using System.Text;
using DecentDB.EntityFrameworkCore;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services.Scanning;

public sealed class StagingAlbumRevalidationStateStore(ILogger logger) : IStagingAlbumRevalidationStateStore
{
    public const string DatabaseFileName = ".melodee-revalidation.ddb";

    public async Task<IStagingAlbumRevalidationStateSession> OpenAsync(
        string stagingPath,
        IReadOnlyCollection<Album> currentAlbums,
        CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(stagingPath);

        var databasePath = GetDatabasePath(stagingPath);
        try
        {
            return await OpenSessionAsync(databasePath, currentAlbums, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(
                ex,
                "[{Name}] Unable to open staging revalidation state database [{DatabasePath}]. Recreating it.",
                nameof(StagingAlbumRevalidationStateStore),
                databasePath);

            try
            {
                await RecreateDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);
                return await OpenSessionAsync(databasePath, currentAlbums, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception retryEx) when (retryEx is not OperationCanceledException)
            {
                logger.Warning(
                    retryEx,
                    "[{Name}] Unable to recreate staging revalidation state database [{DatabasePath}]. Continuing without persistent revalidation backoff.",
                    nameof(StagingAlbumRevalidationStateStore),
                    databasePath);
                return new PassthroughStagingAlbumRevalidationStateSession();
            }
        }
    }

    public static string GetDatabasePath(string stagingPath)
    {
        return Path.Combine(stagingPath, DatabaseFileName);
    }

    public static DateTimeOffset CalculateNextAttemptAt(int attemptCount, DateTimeOffset now)
    {
        var delay = attemptCount switch
        {
            <= 1 => TimeSpan.FromHours(6),
            2 => TimeSpan.FromHours(12),
            3 => TimeSpan.FromDays(1),
            4 => TimeSpan.FromDays(3),
            _ => TimeSpan.FromDays(7)
        };

        return now.ToUniversalTime().Add(delay);
    }

    public static string CreateAlbumKey(Album album)
    {
        var identity = $"{album.Id:N}|{Normalize(album.Directory.FullName())}";
        return Hash(identity);
    }

    public static string CreateFingerprint(Album album)
    {
        var artist = album.Artist.NameNormalized.Nullify() ??
                     album.Artist.Name.Nullify() ??
                     string.Empty;
        var title = album.AlbumTitle().ToNormalizedString() ??
                    album.AlbumTitle() ??
                    string.Empty;
        var year = album.AlbumYear()?.ToString() ?? string.Empty;
        var identity = string.Join(
            '|',
            Normalize(artist),
            Normalize(title),
            year,
            album.Artist.AmgId ?? string.Empty,
            album.Artist.ArtistDbId?.ToString() ?? string.Empty,
            album.Artist.DiscogsId ?? string.Empty,
            album.Artist.ItunesId ?? string.Empty,
            album.Artist.LastFmId ?? string.Empty,
            album.Artist.MusicBrainzId?.ToString() ?? string.Empty,
            album.Artist.SearchEngineResultUniqueId?.ToString() ?? string.Empty,
            album.Artist.SpotifyId ?? string.Empty,
            album.Artist.WikiDataId ?? string.Empty,
            album.StatusReasons.ToString());

        return Hash(identity);
    }

    private async Task<StagingAlbumRevalidationStateSession> OpenSessionAsync(
        string databasePath,
        IReadOnlyCollection<Album> currentAlbums,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<StagingAlbumRevalidationStateDbContext>()
            .UseDecentDB($"Data Source={databasePath}")
            .Options;

        var dbContext = new StagingAlbumRevalidationStateDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            var states = await dbContext.AlbumRevalidationStates
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var currentKeys = currentAlbums
                .Select(CreateAlbumKey)
                .ToHashSet(StringComparer.Ordinal);

            var staleStates = states
                .Where(x => !currentKeys.Contains(x.AlbumKey))
                .ToArray();

            if (staleStates.Length > 0)
            {
                dbContext.AlbumRevalidationStates.RemoveRange(staleStates);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.Debug(
                    "[{Name}] Removed [{Count}] stale staging revalidation state rows from [{DatabasePath}]",
                    nameof(StagingAlbumRevalidationStateStore),
                    staleStates.Length,
                    databasePath);
            }

            return new StagingAlbumRevalidationStateSession(
                dbContext,
                states.Except(staleStates).ToDictionary(x => x.AlbumKey, StringComparer.Ordinal));
        }
        catch
        {
            await dbContext.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task RecreateDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        DeleteDatabaseFiles(databasePath);

        var options = new DbContextOptionsBuilder<StagingAlbumRevalidationStateDbContext>()
            .UseDecentDB($"Data Source={databasePath}")
            .Options;

        await using var dbContext = new StagingAlbumRevalidationStateDbContext(options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in DatabasePaths(databasePath))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> DatabasePaths(string databasePath)
    {
        yield return databasePath;
        yield return $"{databasePath}-wal";
        yield return $"{databasePath}-shm";
        yield return $"{databasePath}.wal";
        yield return $"{databasePath}.shm";
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed class StagingAlbumRevalidationStateSession(
        StagingAlbumRevalidationStateDbContext dbContext,
        Dictionary<string, StagingAlbumRevalidationState> states) : IStagingAlbumRevalidationStateSession
    {
        public StagingAlbumRevalidationDecision GetDecision(Album album, DateTimeOffset now, bool force)
        {
            if (force)
            {
                return new StagingAlbumRevalidationDecision(true, Reason: "Force");
            }

            var albumKey = CreateAlbumKey(album);
            if (!states.TryGetValue(albumKey, out var state))
            {
                return new StagingAlbumRevalidationDecision(true, Reason: "NoState");
            }

            var fingerprint = CreateFingerprint(album);
            if (!string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new StagingAlbumRevalidationDecision(true, Reason: "AlbumChanged");
            }

            var nextAttemptAt = state.NextAttemptAt?.ToUniversalTime();
            if (nextAttemptAt is null || nextAttemptAt <= now.ToUniversalTime())
            {
                return new StagingAlbumRevalidationDecision(
                    true,
                    state.AttemptCount,
                    nextAttemptAt,
                    "Due");
            }

            return new StagingAlbumRevalidationDecision(
                false,
                state.AttemptCount,
                nextAttemptAt,
                "Backoff");
        }

        public void RecordAttempt(Album album, DateTimeOffset now, string outcome)
        {
            var albumKey = CreateAlbumKey(album);
            var fingerprint = CreateFingerprint(album);
            var nowUtc = now.ToUniversalTime();

            if (!states.TryGetValue(albumKey, out var state))
            {
                state = new StagingAlbumRevalidationState
                {
                    AlbumKey = albumKey,
                    Fingerprint = fingerprint
                };
                states[albumKey] = state;
                dbContext.AlbumRevalidationStates.Add(state);
            }
            else if (!string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                state.Fingerprint = fingerprint;
                state.AttemptCount = 0;
            }

            state.AttemptCount++;
            state.AlbumDirectory = album.Directory.FullName();
            state.LastAttemptedAt = nowUtc;
            state.NextAttemptAt = CalculateNextAttemptAt(state.AttemptCount, nowUtc);
            state.LastOutcome = outcome;
            state.UpdatedAt = nowUtc;
        }

        public void RecordSuccess(Album album)
        {
            var albumKey = CreateAlbumKey(album);
            if (!states.Remove(albumKey, out var state))
            {
                return;
            }

            dbContext.AlbumRevalidationStates.Remove(state);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return dbContext.SaveChangesAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await dbContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class PassthroughStagingAlbumRevalidationStateSession : IStagingAlbumRevalidationStateSession
    {
        public StagingAlbumRevalidationDecision GetDecision(Album album, DateTimeOffset now, bool force)
        {
            return new StagingAlbumRevalidationDecision(true, Reason: "Passthrough");
        }

        public void RecordAttempt(Album album, DateTimeOffset now, string outcome)
        {
        }

        public void RecordSuccess(Album album)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
