namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PartyQueueItem(
    Guid? ApiKey,
    Guid SongApiKey,
    string EnqueuedAt,
    int SortOrder,
    string? Source,
    string? Note);
