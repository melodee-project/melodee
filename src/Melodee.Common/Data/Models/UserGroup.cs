using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Represents a user group for organizing users and managing library access permissions.
/// </summary>
[Serializable]
public class UserGroup : DataModelBase
{
    public const string CacheRegion = "urn:region:usergroup";

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    [Required]
    public required string Name { get; set; }

    /// <summary>
    /// Collection of users who are members of this group.
    /// </summary>
    public ICollection<UserGroupMember> Members { get; set; } = new List<UserGroupMember>();

    /// <summary>
    /// Collection of library access controls that reference this group.
    /// </summary>
    public ICollection<LibraryAccessControl> LibraryAccessControls { get; set; } = new List<LibraryAccessControl>();

    public override string ToString()
    {
        return $"UserGroup [{Id}] Name [{Name}]";
    }
}
