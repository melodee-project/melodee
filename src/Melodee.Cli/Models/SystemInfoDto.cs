namespace Melodee.Cli.Models;

/// <summary>
/// System information from the Melodee server.
/// Matches the response from GET /api/v1/system/info
/// </summary>
public record SystemInfoDto(
    string Name,
    string Description,
    int MajorVersion,
    int MinorVersion,
    int PatchVersion)
{
    public string Version => $"{MajorVersion}.{MinorVersion}.{PatchVersion}";
}
