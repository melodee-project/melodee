using System.Data;
using System.Linq.Expressions;
using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.Models.Collection;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Service for user profile management operations.
/// </summary>
public sealed class UserProfileService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    PlaylistService playlistService,
    PodcastService podcastService,
    IBus bus,
    IPasswordHashService passwordHashService,
    IOpenSubsonicSecretProtector openSubsonicSecretProtector)
: ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly ILogger _logger = logger;
    private readonly IMelodeeConfigurationFactory _configurationFactory = configurationFactory;
    private readonly LibraryService _libraryService = libraryService;
    private readonly ArtistService _artistService = artistService;
    private readonly AlbumService _albumService = albumService;
    private readonly SongService _songService = songService;
    private readonly PlaylistService _playlistService = playlistService;
    private readonly PodcastService _podcastService = podcastService;
    private readonly IBus _bus = bus;
    private readonly IPasswordHashService _passwordHashService = passwordHashService;
    private readonly IOpenSubsonicSecretProtector _openSubsonicSecretProtector = openSubsonicSecretProtector;

    public IDbContextFactory<MelodeeDbContext> GetContextFactory() => ContextFactory;

    public async Task<UserArtist?> UserArtistAsync(int userId, Guid artistApiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserArtists
            .AsNoTracking()
            .Include(ua => ua.Artist)
            .Where(ua => ua.UserId == userId && ua.Artist.ApiKey == artistApiKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MelodeeModels.PagedResult<UserDataInfo>> ListAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Build the base query with performance optimizations
        var baseQuery = scopedContext.Users
            .AsNoTracking();

        // Apply filters using EF Core instead of raw SQL
        var filteredQuery = ApplyFilters(baseQuery, pagedRequest);

        // Get count efficiently
        var userCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        UserDataInfo[] users = [];
        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            // Apply ordering, skip, and take with projection to UserDataInfo
            var orderedQuery = ApplyOrdering(filteredQuery, pagedRequest);

            users = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .Select(u => new UserDataInfo(
                    u.Id,
                    u.ApiKey,
                    u.IsLocked,
                    u.UserName,
                    u.Email,
                    u.IsAdmin,
                    u.LastActivityAt,
                    u.CreatedAt,
                    u.Tags,
                    u.LastUpdatedAt,
                    u.LastLoginAt))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return new MelodeeModels.PagedResult<UserDataInfo>
        {
            TotalCount = userCount,
            TotalPages = pagedRequest.TotalPages(userCount),
            Data = users
        };
    }

    private static IQueryable<User> ApplyFilters(IQueryable<User> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        // If there's only one filter, apply it directly
        if (pagedRequest.FilterBy.Length == 1)
        {
            var filter = pagedRequest.FilterBy[0];
            var filterValue = filter.Value.ToString().ToNormalizedString() ?? string.Empty;

            return filter.PropertyName.ToLowerInvariant() switch
            {
                "username" or "usernamenormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(u => u.UserNameNormalized.Contains(filterValue)),
                    FilterOperator.Equals => query.Where(u => u.UserNameNormalized == filterValue),
                    FilterOperator.StartsWith => query.Where(u => u.UserNameNormalized.StartsWith(filterValue)),
                    _ => query
                },
                "email" or "emailnormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(u => u.EmailNormalized.Contains(filterValue)),
                    FilterOperator.Equals => query.Where(u => u.EmailNormalized == filterValue),
                    FilterOperator.StartsWith => query.Where(u => u.EmailNormalized.StartsWith(filterValue)),
                    _ => query
                },
                "islocked" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        query.Where(u => u.IsLocked == boolValue),
                    _ => query
                },
                "isadmin" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        query.Where(u => u.IsAdmin == boolValue),
                    _ => query
                },
                _ => query
            };
        }

        // For multiple filters, combine them with OR logic
        var filterPredicates = new List<Expression<Func<User, bool>>>();

        foreach (var filter in pagedRequest.FilterBy)
        {
            var filterValue = filter.Value.ToString().ToNormalizedString() ?? string.Empty;

            var predicate = filter.PropertyName.ToLowerInvariant() switch
            {
                "username" or "usernamenormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => (Expression<Func<User, bool>>)(u => u.UserNameNormalized.Contains(filterValue)),
                    FilterOperator.Equals => (Expression<Func<User, bool>>)(u => u.UserNameNormalized == filterValue),
                    FilterOperator.StartsWith => (Expression<Func<User, bool>>)(u => u.UserNameNormalized.StartsWith(filterValue)),
                    _ => null
                },
                "email" or "emailnormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => (Expression<Func<User, bool>>)(u => u.EmailNormalized.Contains(filterValue)),
                    FilterOperator.Equals => (Expression<Func<User, bool>>)(u => u.EmailNormalized == filterValue),
                    FilterOperator.StartsWith => (Expression<Func<User, bool>>)(u => u.EmailNormalized.StartsWith(filterValue)),
                    _ => null
                },
                "islocked" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        (Expression<Func<User, bool>>)(u => u.IsLocked == boolValue),
                    _ => null
                },
                "isadmin" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        (Expression<Func<User, bool>>)(u => u.IsAdmin == boolValue),
                    _ => null
                },
                _ => null
            };

            if (predicate != null)
            {
                filterPredicates.Add(predicate);
            }
        }

        // If we have predicates, combine them with OR logic
        if (filterPredicates.Count > 0)
        {
            var combinedPredicate = filterPredicates.Aggregate((prev, next) =>
            {
                var parameter = Expression.Parameter(typeof(User), "u");
                var left = Expression.Invoke(prev, parameter);
                var right = Expression.Invoke(next, parameter);
                var or = Expression.OrElse(left, right);
                return Expression.Lambda<Func<User, bool>>(or, parameter);
            });

            query = query.Where(combinedPredicate);
        }

        return query;
    }

    private static IQueryable<User> ApplyOrdering(IQueryable<User> query, MelodeeModels.PagedRequest pagedRequest)
    {
        // Use the existing OrderByValue method from PagedRequest
        var orderByClause = pagedRequest.OrderByValue("UserName", MelodeeModels.PagedRequest.OrderAscDirection);

        // Parse the order by clause to determine field and direction
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "username" or "usernamenormalized" => isDescending ? query.OrderByDescending(u => u.UserNameNormalized) : query.OrderBy(u => u.UserNameNormalized),
            "email" or "emailnormalized" => isDescending ? query.OrderByDescending(u => u.EmailNormalized) : query.OrderBy(u => u.EmailNormalized),
            "createdat" => isDescending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
            "lastupdatedat" => isDescending ? query.OrderByDescending(u => u.LastUpdatedAt) : query.OrderBy(u => u.LastUpdatedAt),
            "lastactivityat" => isDescending ? query.OrderByDescending(u => u.LastActivityAt) : query.OrderBy(u => u.LastActivityAt),
            "lastloginat" => isDescending ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
            "isadmin" => isDescending ? query.OrderByDescending(u => u.IsAdmin) : query.OrderBy(u => u.IsAdmin),
            "islocked" => isDescending ? query.OrderByDescending(u => u.IsLocked) : query.OrderBy(u => u.IsLocked),
            _ => query.OrderBy(u => u.UserName)
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] userIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userIds, nameof(userIds));

        bool result;

        foreach (var userId in userIds)
        {
            var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
            if (user.Data == null || !user.IsSuccess)
            {
                return new MelodeeModels.OperationResult<bool>
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }
        }

        var userImageLibrary = await _libraryService.GetUserImagesLibraryAsync(cancellationToken).ConfigureAwait(false);

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var userId in userIds)
            {
                var user = await scopedContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken).ConfigureAwait(false);
                if (user != null)
                {
                    var userAvatarFullname = user.ToAvatarFileName(userImageLibrary.Data.Path);
                    if (File.Exists(userAvatarFullname))
                    {
                        File.Delete(userAvatarFullname);
                    }

                    scopedContext.Users.Remove(user);
                }
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result = true;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByEmailAddressAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(emailAddress, nameof(emailAddress));

        var emailAddressNormalized = emailAddress.ToNormalizedString() ?? emailAddress;
        var id = await CacheManager.GetAsync(
            CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(emailAddressNormalized), async () =>
            {
                using (Operation.At(LogEventLevel.Debug).Time("[{ServiceName}] GetByEmailAddressAsync [{EmailAddress}]",
                           nameof(UserProfileService), emailAddress))
                {
                    await using (var scopedContext =
                                 await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return await scopedContext.Users
                            .Where(u => u.EmailNormalized == emailAddressNormalized)
                            .Select(u => (int?)u.Id)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        return id == null
            ? new MelodeeModels.OperationResult<User?>("User not found")
            {
                Type = MelodeeModels.OperationResponseType.NotFound,
                Data = null
            }
            : await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(username, nameof(username));
        var usernameNormalized = username.ToNormalizedString() ?? username;
        var id = await CacheManager.GetAsync(CacheKeyDetailByUsernameTemplate.FormatSmart(usernameNormalized),
            async () =>
            {
                using (Operation.At(LogEventLevel.Debug).Time("[{ServiceName}] GetByUsernameAsync [{Username}]",
                           nameof(UserProfileService), username))
                {
                    await using (var scopedContext =
                                 await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return await scopedContext.Users
                            .Where(u => u.UserNameNormalized == usernameNormalized)
                            .Select(u => (int?)u.Id)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        return id == null
            ? new MelodeeModels.OperationResult<User?>("User not found")
            {
                Data = null
            }
            : await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsUserAdminAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        return user.Data?.IsAdmin ?? false;
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByApiKeyAsync(Guid apiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext.Users
                    .Where(u => u.ApiKey == apiKey)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        return id == null
            ? new MelodeeModels.OperationResult<User?>
            {
                Data = null
            }
            : await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            using (Operation.At(LogEventLevel.Debug).Time("[{ServiceName}] GetAsync [{id}]", nameof(UserProfileService), id))
            {
                await using (var scopedContext =
                             await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var user = await scopedContext
                        .Users
                        .Include(x => x.Pins)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                        .ConfigureAwait(false);

                    if (user?.Pins.Count > 0)
                    {
                        foreach (var pin in user.Pins)
                        {
                            switch (pin.PinTypeValue)
                            {
                                case UserPinType.Artist:
                                    var artistResult = await _artistService.GetAsync(pin.PinId, cancellationToken)
                                        .ConfigureAwait(false);
                                    if (artistResult is { IsSuccess: true, Data: not null })
                                    {
                                        pin.Icon = "artist";
                                        pin.ImageUrl = $"/images/{artistResult.Data.ToApiKey()}{ImageSize.Thumbnail}";
                                        pin.LinkUrl = $"/data/artist/ {artistResult.Data.ApiKey}";
                                        pin.Text = artistResult.Data.Name;
                                    }

                                    break;
                                case UserPinType.Album:
                                    var albumResult = await _albumService.GetAsync(pin.PinId, cancellationToken)
                                        .ConfigureAwait(false);
                                    if (albumResult is { IsSuccess: true, Data: not null })
                                    {
                                        pin.Icon = "album";
                                        pin.ImageUrl = $"/images/{albumResult.Data.ToApiKey()}/{ImageSize.Thumbnail}";
                                        pin.LinkUrl = $"/data/album/ {albumResult.Data.ApiKey}";
                                        pin.Text = albumResult.Data.Name;
                                    }

                                    break;
                                case UserPinType.Song:
                                    var songResult = await _songService.GetAsync(pin.PinId, cancellationToken)
                                        .ConfigureAwait(false);
                                    if (songResult is { IsSuccess: true, Data: not null })
                                    {
                                        pin.Icon = "music_note";
                                        pin.ImageUrl = $"/images/{songResult.Data.ToApiKey()}/{ImageSize.Thumbnail}";
                                        pin.LinkUrl = $"/data/album/ {songResult.Data.Album.ApiKey}";
                                        pin.Text = songResult.Data.Title;
                                    }

                                    break;
                                case UserPinType.Playlist:
                                    var playlistResult = await _playlistService.GetAsync(pin.PinId, cancellationToken)
                                        .ConfigureAwait(false);
                                    if (playlistResult is { IsSuccess: true, Data: not null })
                                    {
                                        pin.Icon = "playlist_play";
                                        pin.ImageUrl = $"/images/{playlistResult.Data.ToApiKey()}/{ImageSize.Thumbnail}";
                                        pin.LinkUrl = $"/data/playlist/ {playlistResult.Data.ApiKey}";
                                        pin.Text = playlistResult.Data.Name;
                                    }

                                    break;
                                case UserPinType.PodcastChannel:
                                    var podcastResult = await _podcastService.GetChannelAsync(pin.PinId, pin.UserId, cancellationToken)
                                        .ConfigureAwait(false);
                                    if (podcastResult is { IsSuccess: true, Data: not null })
                                    {
                                        pin.Icon = "podcasts";
                                        pin.ImageUrl = podcastResult.Data.ImageUrl ?? string.Empty;
                                        pin.LinkUrl = $"/data/podcasts/{podcastResult.Data.ApiKey}";
                                        pin.Text = podcastResult.Data.Title;
                                    }

                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }

                    return user;
                }
            }
        }, cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<User?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveProfileImageAsync(int userId, byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(imageBytes, nameof(imageBytes));
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var userResult = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>(["Unknown user id"])
            {
                Data = false
            };
        }

        var user = userResult.Data!;
        var userImageLibrary = await _libraryService.GetUserImagesLibraryAsync(cancellationToken).ConfigureAwait(false);
        var userAvatarFullname = user.ToAvatarFileName(userImageLibrary.Data.Path);
        if (File.Exists(userAvatarFullname))
        {
            File.Delete(userAvatarFullname);
        }

        imageBytes = await ImageConvertor.ConvertToGifFormat(imageBytes, cancellationToken).ConfigureAwait(false);

        await File.WriteAllBytesAsync(userAvatarFullname, imageBytes, cancellationToken).ConfigureAwait(false);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<MelodeeModels.OperationResult<User?>> RegisterAsync(string username,
        string emailAddress,
        string plainTextPassword,
        string? registerPrivateCode,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(emailAddress, nameof(emailAddress));
        Guard.Against.NullOrWhiteSpace(plainTextPassword, nameof(plainTextPassword));

        // Ensure no user exists with given email address
        var dbUserByEmailAddress = await GetByEmailAddressAsync(emailAddress, cancellationToken).ConfigureAwait(false);
        if (dbUserByEmailAddress.IsSuccess)
        {
            return new MelodeeModels.OperationResult<User?>(["User exists with Email address."])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        // Ensure no user exists with given username
        var dbUserByUserName = await GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (dbUserByUserName.IsSuccess)
        {
            return new MelodeeModels.OperationResult<User?>(["User exists with Username."])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken);

            var configuredRegisterPrivateCode = configuration.GetValue<string>(SettingRegistry.RegisterPrivateCode);
            if (configuredRegisterPrivateCode != null && registerPrivateCode != configuredRegisterPrivateCode)
            {
                return new MelodeeModels.OperationResult<User?>("Invalid access code.")
                {
                    Data = null,
                    Type = MelodeeModels.OperationResponseType.Unauthorized
                };
            }

            var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
            var emailNormalized = emailAddress.ToNormalizedString() ?? emailAddress.ToUpperInvariant();
            var newUser = new User
            {
                UserName = username,
                UserNameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant(),
                Email = emailAddress,
                EmailNormalized = emailNormalized,
                PublicKey = usersPublicKey,
                PasswordEncrypted =
                    EncryptionHelper.Encrypt(configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                        plainTextPassword, usersPublicKey),
                PasswordHash = _passwordHashService.Hash(plainTextPassword),
                PasswordHashAlgorithm = "bcrypt",
                OpenSubsonicSecretProtected = _openSubsonicSecretProtector.Protect(UserAuthenticationService.GenerateOpenSubsonicSecretStatic()),
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            scopedContext.Users.Add(newUser);
            if (await scopedContext
                    .SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false) < 1)
            {
                return new MelodeeModels.OperationResult<User?>
                {
                    Data = null,
                    Type = MelodeeModels.OperationResponseType.Error
                };
            }

            // See if user is first user to register, is so then set to administrator
            var dbUserCount = await scopedContext
                .Users
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
            if (dbUserCount == 1)
            {
                await scopedContext
                    .Users
                    .Where(x => x.Email == emailAddress)
                    .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsAdmin, true), cancellationToken)
                    .ConfigureAwait(false);
            }

            ClearCache(newUser.EmailNormalized, newUser.ApiKey, newUser.Id, newUser.UserNameNormalized);

            await new UserAuthenticationService(
                _logger,
                _passwordHashService,
                _openSubsonicSecretProtector,
                _bus,
                this,
                _configurationFactory).LoginUserAsync(emailAddress, plainTextPassword, cancellationToken).ConfigureAwait(false);

            return await GetByEmailAddressAsync(emailAddress, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(User currentUser, User detailToUpdate,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, detailToUpdate.Id, nameof(detailToUpdate));

        bool result;
        var validationResult = ValidateModel(detailToUpdate);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        // Ensure no user exists with given email address
        var dbUserByEmailAddress =
            await GetByEmailAddressAsync(currentUser.Email, cancellationToken).ConfigureAwait(false);
        if (dbUserByEmailAddress.IsSuccess && dbUserByEmailAddress.Data!.Id != detailToUpdate.Id)
        {
            return new MelodeeModels.OperationResult<bool>(["User exists with Email address."])
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        // Ensure no user exists with given username
        var dbUserByUserName = await GetByUsernameAsync(currentUser.UserName, cancellationToken).ConfigureAwait(false);
        if (dbUserByUserName.IsSuccess && dbUserByUserName.Data!.Id != detailToUpdate.Id)
        {
            return new MelodeeModels.OperationResult<bool>(["User exists with Username."])
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Load the detail by DetailToUpdate.Id
            var dbDetail = await scopedContext
                .Users
                .FirstOrDefaultAsync(x => x.Id == detailToUpdate.Id, cancellationToken)
                .ConfigureAwait(false);

            if (dbDetail == null)
            {
                return new MelodeeModels.OperationResult<bool>
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }

            // Update values and save to db
            dbDetail.Description = detailToUpdate.Description;
            dbDetail.Email = detailToUpdate.Email;
            dbDetail.EmailNormalized =
                detailToUpdate.Email.ToNormalizedString() ?? detailToUpdate.Email.ToUpperInvariant();
            dbDetail.HasCommentRole = detailToUpdate.HasCommentRole;
            dbDetail.HasCoverArtRole = detailToUpdate.HasCoverArtRole;
            dbDetail.HasDownloadRole = detailToUpdate.HasDownloadRole;
            dbDetail.HasJukeboxRole = detailToUpdate.HasJukeboxRole;
            dbDetail.HasPlaylistRole = detailToUpdate.HasPlaylistRole;
            dbDetail.HasPodcastRole = detailToUpdate.HasPodcastRole;
            dbDetail.HasSettingsRole = detailToUpdate.HasSettingsRole;
            dbDetail.HasShareRole = detailToUpdate.HasShareRole;
            dbDetail.HasStreamRole = detailToUpdate.HasStreamRole;
            dbDetail.HasUploadRole = detailToUpdate.HasUploadRole;
            dbDetail.IsAdmin = detailToUpdate.IsAdmin;
            dbDetail.IsEditor = detailToUpdate.IsEditor;
            dbDetail.IsLocked = detailToUpdate.IsLocked;
            dbDetail.IsScrobblingEnabled = detailToUpdate.IsScrobblingEnabled;
            // Take whatever is newer
            dbDetail.LastActivityAt = dbDetail.LastActivityAt > detailToUpdate.LastActivityAt
                ? dbDetail.LastActivityAt
                : detailToUpdate.LastActivityAt;
            // Take whatever is newer
            dbDetail.LastLoginAt = dbDetail.LastLoginAt > detailToUpdate.LastLoginAt
                ? dbDetail.LastLoginAt
                : detailToUpdate.LastLoginAt;
            dbDetail.Notes = detailToUpdate.Notes;
            dbDetail.PreferredLanguage = detailToUpdate.PreferredLanguage;
            dbDetail.PreferredTheme = detailToUpdate.PreferredTheme;
            dbDetail.SortOrder = detailToUpdate.SortOrder;
            dbDetail.Tags = detailToUpdate.Tags;
            dbDetail.TimeZoneId = string.IsNullOrWhiteSpace(detailToUpdate.TimeZoneId)
                ? "UTC"
                : detailToUpdate.TimeZoneId.Trim();
            dbDetail.UserName = detailToUpdate.UserName;
            dbDetail.UserNameNormalized = detailToUpdate.UserName.ToUpperInvariant();

            dbDetail.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

            if (result)
            {
                ClearCache(dbDetail.EmailNormalized, dbDetail.ApiKey, dbDetail.Id, dbDetail.UserNameNormalized);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    private void ClearCache(User user)
    {
        ClearCache(user.Email, user.ApiKey, user.Id, user.UserName);
    }

    private void ClearCache(string? emailAddress, Guid? apiKey, int? id, string? username)
    {
        if (emailAddress != null)
        {
            CacheManager.Remove(
                CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(emailAddress.ToNormalizedString() ?? emailAddress));
        }

        if (apiKey != null)
        {
            CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey));
        }

        if (id != null)
        {
            CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(id));
        }

        if (username != null)
        {
            CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(username));
        }
    }
}
