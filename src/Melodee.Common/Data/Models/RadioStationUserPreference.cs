using System.ComponentModel.DataAnnotations;
using NodaTime;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Per-user preferences for radio stations (favorites, hidden, sort order)
/// </summary>
[Serializable]
public class RadioStationUserPreference
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int RadioStationId { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsHidden { get; set; }

    public int SortOrder { get; set; } = 1000;

    [Required]
    public required Instant CreatedAt { get; set; }

    public Instant? UpdatedAt { get; set; }

    // Navigation properties
    public User? User { get; set; }
    public RadioStation? RadioStation { get; set; }
}
