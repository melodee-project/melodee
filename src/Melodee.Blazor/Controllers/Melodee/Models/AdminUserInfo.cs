namespace Melodee.Blazor.Controllers.Melodee.Models;

/// <summary>
/// Admin user information (no sensitive data like passwords or tokens).
/// </summary>
public record AdminUserInfo(
    Guid Id,
    string Username,
    string? Email,
    bool IsAdmin,
    bool IsEnabled,
    string CreatedAt,
    string? LastLoginAt);
