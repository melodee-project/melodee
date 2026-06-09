using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;

[Index(nameof(NameNormalized))]
[Index(nameof(ArtistId), nameof(NameNormalized), IsUnique = true)]
public sealed class ArtistAlias
{
    [Key] public int Id { get; set; }

    public int ArtistId { get; set; }

    public Artist Artist { get; set; } = null!;

    [Required][MaxLength(2000)] public required string NameNormalized { get; set; }
}
