namespace Melodee.Cli.Models;

/// <summary>
/// Admin user listing response from GET /api/v1/admin/users
/// </summary>
public record AdminUserDto(
    Guid Id,
    string Username,
    string? Email,
    bool IsAdmin,
    bool IsEnabled,
    string CreatedAt,
    string? LastLoginAt);
