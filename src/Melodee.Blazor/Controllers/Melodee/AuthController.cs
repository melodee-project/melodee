using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Data = Melodee.Common.Data.Models;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Authentication endpoints for login, logout, token refresh, and Google Sign-In.
/// </summary>
[ApiController]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    IUserAuthenticationService userAuthenticationService,
    IUserProfileService userProfileService,
    IConfiguration configuration,
    IBlacklistService blacklistService,
    IMelodeeConfigurationFactory configurationFactory,
    IGoogleTokenService googleTokenService,
    IRefreshTokenService refreshTokenService,
    IOptions<GoogleAuthOptions> googleAuthOptions,
    IOptions<AuthPolicyOptions> authPolicyOptions,
    IOptions<TokenOptions> tokenOptions,
    ILogger<AuthController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private readonly GoogleAuthOptions _googleAuthOptions = googleAuthOptions.Value;
    private readonly AuthPolicyOptions _authPolicyOptions = authPolicyOptions.Value;
    private readonly TokenOptions _tokenOptions = tokenOptions.Value;

    /// <summary>
    /// Authenticate a user with username/email and password.
    /// </summary>
    /// <remarks>
    /// Returns a JWT access token and optionally a refresh token for maintaining sessions.
    /// </remarks>
    [HttpPost]
    [Route("authenticate")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AuthenticateAsync([FromBody] LoginModel model, CancellationToken cancellationToken = default)
    {
        var authResult = await TryAuthenticateUserAsync(model, cancellationToken).ConfigureAwait(false);
        if (authResult.ErrorResult != null)
        {
            return authResult.ErrorResult;
        }

        return await GenerateAuthResponseAsync(authResult.User!, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Authenticate a user and issue a browser auth cookie.
    /// </summary>
    [HttpPost]
    [Route("cookie/sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CookieSignInAsync([FromBody] LoginModel model, CancellationToken cancellationToken = default)
    {
        var authResult = await TryAuthenticateUserAsync(model, cancellationToken).ConfigureAwait(false);
        if (authResult.ErrorResult != null)
        {
            return authResult.ErrorResult;
        }

        var principal = await CreateCookiePrincipalAsync(authResult.User!, cancellationToken).ConfigureAwait(false);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);

        return Ok(new { message = "Signed in" });
    }

    /// <summary>
    /// Sign out the current browser session and clear the auth cookie.
    /// </summary>
    [HttpPost]
    [Route("cookie/sign-out")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CookieSignOutAsync(CancellationToken cancellationToken = default)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Ok(new { message = "Signed out" });
    }

    /// <summary>
    /// Exchange a Google ID token for Melodee JWT and refresh tokens.
    /// </summary>
    /// <remarks>
    /// Validates the Google ID token, links or creates a user account, and returns
    /// Melodee authentication tokens. New users are created if self-registration is enabled.
    /// </remarks>
    [HttpPost]
    [Route("google")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GoogleAuthAsync([FromBody] GoogleAuthRequest request, CancellationToken cancellationToken = default)
    {
        var authResult = await TryResolveGoogleUserAsync(request, cancellationToken).ConfigureAwait(false);
        if (authResult.ErrorResult != null)
        {
            return authResult.ErrorResult;
        }

        return await GenerateAuthResponseAsync(authResult.User!, request.DeviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exchange a Google ID token for a browser auth cookie.
    /// </summary>
    [HttpPost]
    [Route("cookie/google")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GoogleCookieAuthAsync([FromBody] GoogleAuthRequest request, CancellationToken cancellationToken = default)
    {
        var authResult = await TryResolveGoogleUserAsync(request, cancellationToken).ConfigureAwait(false);
        if (authResult.ErrorResult != null)
        {
            return authResult.ErrorResult;
        }

        var principal = await CreateCookiePrincipalAsync(authResult.User!, cancellationToken).ConfigureAwait(false);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);

        return Ok(new { message = "Signed in" });
    }

    /// <summary>
    /// Refresh an access token using a refresh token.
    /// </summary>
    /// <remarks>
    /// Implements token rotation: each successful refresh invalidates the old refresh token
    /// and issues a new one. Reuse of an old token indicates a potential attack and will
    /// revoke the entire token family.
    /// </remarks>
    [HttpPost]
    [Route("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshTokenWithRotationAsync([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var clientIp = GetRequestIp(HttpContext);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            LogAuthEvent("refresh", "missing_token", clientIp);
            return Unauthorized(new ApiError(ApiError.Codes.RefreshTokenInvalid, "Refresh token is required", GetCorrelationId()));
        }

        var rotateResult = await refreshTokenService.RotateTokenAsync(
            request.RefreshToken,
            request.DeviceId,
            Request.Headers.UserAgent.ToString(),
            clientIp,
            cancellationToken).ConfigureAwait(false);

        if (!rotateResult.IsSuccess)
        {
            var errorCode = rotateResult.ErrorCode ?? ApiError.Codes.RefreshTokenInvalid;
            LogAuthEvent("refresh", errorCode, clientIp);
            return Unauthorized(new ApiError(errorCode, rotateResult.ErrorMessage ?? "Invalid refresh token", GetCorrelationId()));
        }

        // Get the user
        var userResult = await userProfileService.GetAsync(rotateResult.UserId!.Value, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            LogAuthEvent("refresh", "user_not_found", clientIp, userId: rotateResult.UserId);
            return Unauthorized(new ApiError(ApiError.Codes.RefreshTokenInvalid, "User not found", GetCorrelationId()));
        }

        var user = userResult.Data;

        if (user.IsLocked)
        {
            // Revoke all tokens for this user
            await refreshTokenService.RevokeAllUserTokensAsync(user.Id, "account_disabled", cancellationToken).ConfigureAwait(false);
            LogAuthEvent("refresh", "account_disabled", clientIp, user.UserName, user.Id);
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError(ApiError.Codes.AccountDisabled, "Account is disabled", GetCorrelationId()));
        }

        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(clientIp).ConfigureAwait(false))
        {
            await refreshTokenService.RevokeAllUserTokensAsync(user.Id, "blacklisted", cancellationToken).ConfigureAwait(false);
            LogAuthEvent("refresh", "blacklisted", clientIp, user.UserName, user.Id);
            return ApiBlacklisted();
        }

        var (accessToken, expiresAt) = GenerateJwtToken(user);
        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        LogAuthEvent("refresh", "success", clientIp, user.UserName, user.Id);
        return Ok(new AuthenticationResponse
        {
            User = user.ToUserModel(GetBaseUrl(melodeeConfig)),
            ServerVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.2.0.0",
            Token = accessToken,
            ExpiresAt = expiresAt,
            RefreshToken = rotateResult.Token,
            RefreshTokenExpiresAt = rotateResult.ExpiresAt?.ToDateTimeUtc()
        });
    }

    /// <summary>
    /// Refresh an existing JWT token (legacy endpoint, requires valid JWT).
    /// </summary>
    /// <remarks>
    /// This is the legacy refresh endpoint that requires a valid (but potentially expired) JWT.
    /// For refresh token rotation, use POST /auth/refresh-token instead.
    /// </remarks>
    [HttpPost]
    [Route("refresh")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ServiceFilter(typeof(MelodeeApiAuthFilter))]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return ApiBlacklisted();
        }

        var (token, expiresAt) = GenerateJwtToken(user);
        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            user = user.ToUserModel(GetBaseUrl(melodeeConfig)),
            serverVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.2.0.0",
            token,
            expiresAt
        });
    }

    /// <summary>
    /// Logout the current user and revoke all refresh tokens.
    /// </summary>
    /// <remarks>
    /// Revokes all active refresh tokens for the authenticated user,
    /// effectively logging them out of all devices.
    /// </remarks>
    [HttpPost]
    [Route("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ServiceFilter(typeof(MelodeeApiAuthFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        // Revoke all refresh tokens for this user
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id, "logout", cancellationToken).ConfigureAwait(false);

        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Revoke a specific refresh token.
    /// </summary>
    /// <remarks>
    /// Allows a user to revoke a specific refresh token (e.g., to log out a specific device).
    /// </remarks>
    [HttpPost]
    [Route("revoke")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ServiceFilter(typeof(MelodeeApiAuthFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeTokenAsync([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ApiValidationError("Refresh token is required");
        }

        await refreshTokenService.RevokeTokenAsync(request.RefreshToken, "user_revoked", cancellationToken).ConfigureAwait(false);

        return Ok(new { message = "Token revoked successfully" });
    }

    /// <summary>
    /// Request a password reset magic link.
    /// </summary>
    /// <remarks>
    /// Generates a token and returns it. In production, this token would be sent via email.
    /// The client can then call POST /auth/password-reset/confirm with the token and new password.
    /// </remarks>
    [HttpPost]
    [Route("password-reset/request")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestPasswordResetAsync([FromBody] PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return ApiValidationError("Email is required");
        }

        var result = await userService.GeneratePasswordResetTokenAsync(request.Email, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // Don't reveal if user exists - return success anyway for security
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        }

        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = GetBaseUrl(melodeeConfig);
        var resetUrl = $"{baseUrl}/reset-password?token={result.Data}";

        return Ok(new
        {
            message = "If an account with that email exists, a password reset link has been sent.",
            resetToken = result.Data,
            resetUrl
        });
    }

    /// <summary>
    /// Validate a password reset token without consuming it.
    /// </summary>
    [HttpGet]
    [Route("password-reset/validate/{token}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidatePasswordResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ApiValidationError("Token is required");
        }

        var result = await userService.ValidatePasswordResetTokenAsync(token, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Invalid or expired token");
        }

        return Ok(new { valid = true });
    }

    /// <summary>
    /// Reset password using a valid reset token.
    /// </summary>
    [HttpPost]
    [Route("password-reset/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPasswordResetAsync([FromBody] PasswordResetConfirmRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiValidationError("Token is required");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ApiValidationError("New password is required");
        }

        if (request.NewPassword.Length < 8)
        {
            return ApiValidationError("Password must be at least 8 characters");
        }

        var result = await userService.ResetPasswordWithTokenAsync(request.Token, request.NewPassword, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Failed to reset password");
        }

        return Ok(new { message = "Password has been reset successfully" });
    }

    /// <summary>
    /// Validates user credentials and applies policy checks before issuing auth responses.
    /// </summary>
    private async Task<(Data.User? User, IActionResult? ErrorResult)> TryAuthenticateUserAsync(LoginModel model, CancellationToken cancellationToken)
    {
        var clientIp = GetRequestIp(HttpContext);

        if (string.IsNullOrWhiteSpace(model.Email) && string.IsNullOrWhiteSpace(model.Password))
        {
            LogAuthEvent("password", "validation_error", clientIp, model.UserName ?? model.Email);
            return (null, ApiValidationError("Email or password are required"));
        }

        // Authentication is always performed via the service layer regardless of which identifier is provided.
        // Both paths perform full credential validation - this is not a security bypass.
        var hasUsername = !string.IsNullOrWhiteSpace(model.UserName);
        var authResult = hasUsername
            ? await userAuthenticationService.LoginUserByUsernameAsync(model.UserName!, model.Password, cancellationToken).ConfigureAwait(false)
            : await userAuthenticationService.LoginUserAsync(model.Email ?? string.Empty, model.Password, cancellationToken).ConfigureAwait(false);

        if (!authResult.IsSuccess || authResult.Data == null)
        {
            LogAuthEvent("password", "invalid_credentials", clientIp, model.UserName ?? model.Email);
            return (null, ApiUnauthorized("Invalid credentials"));
        }

        if (authResult.Data.IsLocked)
        {
            LogAuthEvent("password", "account_disabled", clientIp, model.UserName ?? model.Email, authResult.Data.Id);
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                new ApiError(ApiError.Codes.AccountDisabled, "Account is disabled", GetCorrelationId())));
        }

        if (await blacklistService.IsEmailBlacklistedAsync(authResult.Data.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(clientIp).ConfigureAwait(false))
        {
            LogAuthEvent("password", "blacklisted", clientIp, authResult.Data.UserName, authResult.Data.Id);
            return (null, ApiBlacklisted());
        }

        LogAuthEvent("password", "success", clientIp, authResult.Data.UserName, authResult.Data.Id);
        return (authResult.Data, null);
    }

    /// <summary>
    /// Resolves a Google-authenticated user, enforcing policy and blacklist checks.
    /// </summary>
    private async Task<(Data.User? User, IActionResult? ErrorResult)> TryResolveGoogleUserAsync(GoogleAuthRequest request, CancellationToken cancellationToken)
    {
        var clientIp = GetRequestIp(HttpContext);

        if (!_googleAuthOptions.Enabled)
        {
            LogAuthEvent("google", "not_enabled", clientIp);
            return (null, ApiBadRequest("Google authentication is not enabled"));
        }

        var validationResult = await googleTokenService.ValidateTokenAsync(request.IdToken, cancellationToken).ConfigureAwait(false);

        if (!validationResult.IsValid || validationResult.Payload == null)
        {
            var errorCode = validationResult.ErrorCode ?? ApiError.Codes.InvalidGoogleToken;
            LogAuthEvent("google", errorCode, clientIp);
            var statusCode = errorCode == ApiError.Codes.ExpiredGoogleToken
                ? StatusCodes.Status401Unauthorized
                : errorCode == ApiError.Codes.ForbiddenTenant
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status400BadRequest;

            return (null, StatusCode(statusCode,
                new ApiError(errorCode, validationResult.ErrorMessage ?? "Google token validation failed", GetCorrelationId())));
        }

        var payload = validationResult.Payload;
        var googleSubject = payload.Subject;
        var googleEmail = payload.Email;

        if (await blacklistService.IsIpBlacklistedAsync(clientIp).ConfigureAwait(false))
        {
            LogAuthEvent("google", "blacklisted", clientIp, googleEmail);
            return (null, ApiBlacklisted());
        }

        var socialLoginResult = await userService.GetUserBySocialLoginAsync("Google", googleSubject, cancellationToken).ConfigureAwait(false);

        Data.User? user = null;
        var authOutcome = "existing_link";

        if (socialLoginResult.IsSuccess && socialLoginResult.Data != null)
        {
            user = socialLoginResult.Data;

            if (user.IsLocked)
            {
                LogAuthEvent("google", "account_disabled", clientIp, googleEmail, user.Id);
                return (null, StatusCode(StatusCodes.Status403Forbidden,
                    new ApiError(ApiError.Codes.AccountDisabled, "Account is disabled", GetCorrelationId())));
            }

            if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false))
            {
                LogAuthEvent("google", "blacklisted", clientIp, user.UserName, user.Id);
                return (null, ApiBlacklisted());
            }

            await userService.UpdateSocialLoginLastLoginAsync("Google", googleSubject, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (_googleAuthOptions.AutoLinkEnabled && !string.IsNullOrEmpty(googleEmail))
            {
                var userByEmail = await userProfileService.GetByEmailAddressAsync(googleEmail, cancellationToken).ConfigureAwait(false);
                if (userByEmail.IsSuccess && userByEmail.Data != null)
                {
                    user = userByEmail.Data;
                    authOutcome = "auto_linked";

                    if (user.IsLocked)
                    {
                        LogAuthEvent("google", "account_disabled", clientIp, googleEmail, user.Id);
                        return (null, StatusCode(StatusCodes.Status403Forbidden,
                            new ApiError(ApiError.Codes.AccountDisabled, "Account is disabled", GetCorrelationId())));
                    }

                    await userService.LinkSocialLoginAsync(
                        user.Id,
                        "Google",
                        googleSubject,
                        googleEmail,
                        payload.Name,
                        payload.HostedDomain,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (user == null)
            {
                if (!_authPolicyOptions.SelfRegistrationEnabled)
                {
                    LogAuthEvent("google", "signup_disabled", clientIp, googleEmail);
                    return (null, StatusCode(StatusCodes.Status403Forbidden,
                        new ApiError(ApiError.Codes.SignupDisabled, "Self-registration is disabled", GetCorrelationId())));
                }

                if (!string.IsNullOrEmpty(googleEmail) &&
                    await blacklistService.IsEmailBlacklistedAsync(googleEmail).ConfigureAwait(false))
                {
                    LogAuthEvent("google", "blacklisted", clientIp, googleEmail);
                    return (null, ApiBlacklisted());
                }

                var createResult = await userService.CreateUserFromGoogleAsync(
                    googleSubject,
                    googleEmail ?? $"{googleSubject}@google.user",
                    payload.Name ?? googleSubject,
                    payload.HostedDomain,
                    cancellationToken).ConfigureAwait(false);

                if (!createResult.IsSuccess || createResult.Data == null)
                {
                    LogAuthEvent("google", "create_failed", clientIp, googleEmail);
                    return (null, StatusCode(StatusCodes.Status409Conflict,
                        new ApiError(ApiError.Codes.GoogleAccountNotLinked,
                            createResult.Messages?.FirstOrDefault() ?? "Failed to create account",
                            GetCorrelationId())));
                }

                user = createResult.Data;
                authOutcome = "new_user";
            }
        }

        if (user == null)
        {
            LogAuthEvent("google", "not_linked", clientIp, googleEmail);
            return (null, StatusCode(StatusCodes.Status409Conflict,
                new ApiError(ApiError.Codes.GoogleAccountNotLinked,
                    "Google account is not linked. Please log in with password and link your Google account.",
                    GetCorrelationId())));
        }

        LogAuthEvent("google", $"success_{authOutcome}", clientIp, user.UserName, user.Id);
        return (user, null);
    }

    /// <summary>
    /// Builds the cookie authentication principal for a UI session.
    /// </summary>
    private async Task<ClaimsPrincipal> CreateCookiePrincipalAsync(Data.User user, CancellationToken cancellationToken)
    {
        var melodeeConfig = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        return user.ToUserInfo().ToClaimsPrincipal(melodeeConfig, string.Empty);
    }

    private async Task<IActionResult> GenerateAuthResponseAsync(Data.User user, string? deviceId, CancellationToken cancellationToken)
    {
        var (accessToken, expiresAt) = GenerateJwtToken(user);

        // Create refresh token
        var refreshResult = await refreshTokenService.CreateTokenAsync(
            user.Id,
            deviceId,
            Request.Headers.UserAgent.ToString(),
            GetRequestIp(HttpContext),
            cancellationToken).ConfigureAwait(false);

        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new AuthenticationResponse
        {
            User = user.ToUserModel(GetBaseUrl(melodeeConfig)),
            ServerVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.2.0.0",
            Token = accessToken,
            ExpiresAt = expiresAt,
            RefreshToken = refreshResult.IsSuccess ? refreshResult.Token : null,
            RefreshTokenExpiresAt = refreshResult.IsSuccess ? refreshResult.ExpiresAt?.ToDateTimeUtc() : null
        });
    }

    private (string token, DateTime expiresAt) GenerateJwtToken(Data.User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtKey = Configuration.GetValue<string>("Jwt:Key") ?? throw new InvalidOperationException("JWT signing key is not configured");
        var issuer = Configuration.GetValue<string>("Jwt:Issuer") ?? throw new InvalidOperationException("JWT issuer is not configured");
        var audience = Configuration.GetValue<string>("Jwt:Audience") ?? throw new InvalidOperationException("JWT audience is not configured");
        var key = Encoding.UTF8.GetBytes(jwtKey);

        // Use TokenOptions for expiration if available, fall back to legacy config
        var expiresAt = _tokenOptions.AccessTokenLifetimeMinutes > 0
            ? DateTime.UtcNow.AddMinutes(_tokenOptions.AccessTokenLifetimeMinutes)
            : DateTime.UtcNow.AddHours(SafeParser.ToNumber<int>(Configuration.GetSection("MelodeeAuthSettings:TokenHours").Value));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Sid, user.ApiKey.ToString()),
                new Claim(ClaimTypes.Role, user.IsAdmin ? RoleNameRegistry.Administrator : RoleNameRegistry.User),
                new Claim(ClaimTypeRegistry.UserPreferredTheme, user.PreferredTheme ?? string.Empty)
            ]),
            Expires = expiresAt,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return (tokenHandler.WriteToken(token), expiresAt);
    }

    /// <summary>
    /// Logs an authentication event for telemetry and monitoring.
    /// No sensitive data (tokens, passwords) is logged. Identifiers are masked.
    /// </summary>
    private void LogAuthEvent(string method, string outcome, string? clientIp, string? identifier = null, int? userId = null)
    {
        // Sanitize all user-controlled input to prevent log forging and avoid logging identifiers
        logger.LogInformation(
            "Auth event: Method={Method}, Outcome={Outcome}, ClientIp={ClientIp}, UserId={UserId}",
            LogSanitizer.Sanitize(method),
            LogSanitizer.Sanitize(outcome),
            LogSanitizer.Sanitize(clientIp) ?? "unknown",
            userId);
    }
}
