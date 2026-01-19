namespace Melodee.Cli.Models;

/// <summary>
/// Search results from POST /api/v1/search
/// Simplified DTO - actual structure will be dynamic JSON
/// </summary>
public record SearchResultsDto(
    object Data,
    object Meta);
