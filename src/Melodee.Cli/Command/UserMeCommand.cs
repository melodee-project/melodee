using System.Text.Json;
using Melodee.Cli.Client;
using Melodee.Cli.CommandSettings;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
/// Get information about the current authenticated user.
/// Works in both local and remote mode.
/// </summary>
public class UserMeCommand : CommandBase<UserMeSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, UserMeSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateMelodeeClient(settings);
            var userInfo = await client.GetCurrentUserAsync(cancellationToken);

            if (settings.Json)
            {
                var json = JsonSerializer.Serialize(userInfo, new JsonSerializerOptions { WriteIndented = false });
                Console.WriteLine(json);
            }
            else
            {
                var json = JsonSerializer.Serialize(userInfo, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }

            return 0;
        }
        catch (MelodeeRemoteException ex)
        {
            Console.Error.WriteLine($"ERROR ({ex.HttpStatusCode ?? 0} {ex.Message})");
            return ex.GetExitCode();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 15; // Unexpected error
        }
    }
}
