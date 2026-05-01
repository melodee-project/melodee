namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PlaylistImportResult(
    Guid PlaylistApiKey,
    int TotalEntries,
    int MatchedCount,
    int MissingCount,
    string[] MissingReferences);
