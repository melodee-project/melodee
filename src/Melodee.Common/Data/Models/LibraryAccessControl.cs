using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Data.Validators;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Join table representing which user groups have access to which libraries.
/// If a library has no LibraryAccessControl records, it is accessible to all authenticated users.
/// If a library has one or more LibraryAccessControl records, only users in those groups can access it.
/// </summary>
[Serializable]
[Index(nameof(LibraryId), nameof(UserGroupId), IsUnique = true)]
public class LibraryAccessControl : DataModelBase
{
    [RequiredGreaterThanZero]
    public required int LibraryId { get; set; }

    [ForeignKey(nameof(LibraryId))]
    public Library? Library { get; set; }

    [RequiredGreaterThanZero]
    public required int UserGroupId { get; set; }

    [ForeignKey(nameof(UserGroupId))]
    public UserGroup? UserGroup { get; set; }

    public override string ToString()
    {
        return $"LibraryAccessControl LibraryId [{LibraryId}] UserGroupId [{UserGroupId}]";
    }
}
