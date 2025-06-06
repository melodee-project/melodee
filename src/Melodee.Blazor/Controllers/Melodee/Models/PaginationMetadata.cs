namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PaginationMetadata(int TotalCount, short PageSize, int CurrentPage, int TotalPages)
{
    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;
}
