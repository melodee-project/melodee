using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Manage user equalizer presets for audio customization.
/// </summary>
/// <remarks>
/// Equalizer presets allow users to save custom audio frequency adjustments.
/// Each preset contains frequency bands with gain values that can be applied during playback.
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/equalizer/presets")]
public class EqualizerPresetsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    EqualizerPresetService equalizerPresetService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// List all equalizer presets for the current user.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="limit">Number of results per page (max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of equalizer presets.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(EqualizerPresetsPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPresetsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await equalizerPresetService.ListAsync(
            user.Id,
            new PagedRequest { Page = validatedPage, PageSize = validatedLimit },
            cancellationToken).ConfigureAwait(false);

        var presets = result.Data.Select(p => new EqualizerPreset(
            p.ApiKey,
            p.Name,
            EqualizerPresetService.ParseBands(p.BandsJson).Select(b => new EqualizerBand(b.Frequency, b.Gain)).ToArray(),
            p.IsDefault)).ToArray();

        return Ok(new
        {
            presets,
            meta = new PaginationMetadata(
                result.TotalCount,
                validatedLimit,
                validatedPage,
                result.TotalPages)
        });
    }

    /// <summary>
    /// Create or update an equalizer preset. If a preset with the same name exists, it will be updated.
    /// </summary>
    /// <param name="request">The preset configuration including name, bands, and default status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated preset.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(EqualizerPreset), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertPresetAsync([FromBody] CreateEqualizerPresetRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiValidationError("name is required");
        }

        if (request.Bands == null || request.Bands.Length == 0)
        {
            return ApiValidationError("bands is required and must not be empty");
        }

        foreach (var band in request.Bands)
        {
            if (band.Frequency <= 0)
            {
                return ApiValidationError("Each band frequency must be > 0");
            }
        }

        var bands = request.Bands.Select(b => new EqualizerPresetService.EqualizerBandDto(b.Frequency, b.Gain)).ToArray();

        var result = await equalizerPresetService.UpsertAsync(
            user.Id,
            request.Name,
            bands,
            request.IsDefault,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return ApiValidationError(result.Messages?.FirstOrDefault() ?? "Failed to save preset");
        }

        var parsedBands = EqualizerPresetService.ParseBands(result.Data.BandsJson)
            .Select(b => new EqualizerBand(b.Frequency, b.Gain)).ToArray();

        return Ok(new EqualizerPreset(
            result.Data.ApiKey,
            result.Data.Name,
            parsedBands,
            result.Data.IsDefault));
    }

    /// <summary>
    /// Delete an equalizer preset by ID.
    /// </summary>
    /// <param name="id">The unique identifier (API key) of the preset to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success status.</returns>
    [HttpDelete]
    [Route("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePresetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var result = await equalizerPresetService.DeleteAsync(user.Id, id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            if (result.Type == OperationResponseType.NotFound)
            {
                return ApiNotFound("Preset");
            }
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Failed to delete preset");
        }

        return Ok();
    }
}
