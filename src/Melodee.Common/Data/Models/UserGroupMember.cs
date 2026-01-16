using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Data.Validators;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Join table representing membership of a user in a user group.
/// </summary>
[Serializable]
[Index(nameof(UserId), nameof(UserGroupId), IsUnique = true)]
public class UserGroupMember : DataModelBase
{
    [RequiredGreaterThanZero]
    public required int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [RequiredGreaterThanZero]
    public required int UserGroupId { get; set; }

    [ForeignKey(nameof(UserGroupId))]
    public UserGroup? UserGroup { get; set; }

    public override string ToString()
    {
        return $"UserGroupMember UserId [{UserId}] UserGroupId [{UserGroupId}]";
    }
}
