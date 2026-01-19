using System.Text.Json;
using Melodee.Cli.Client;
using Melodee.Cli.CommandSettings;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
/// Search for artists, albums, songs, and playlists.
/// Works in both local and remote mode.
/// </summary>
public class SearchCommand : CommandBase<SearchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateMelodeeClient(settings);
            var request = new Models.SearchRequestDto(
                settings.Query,
                null, // Type - search all types
                (short)settings.Limit
            );

            var searchResults = await client.SearchAsync(request, cancellationToken);

            if (settings.Json)
            {
                var json = JsonSerializer.Serialize(searchResults, new JsonSerializerOptions { WriteIndented = false });
                Console.WriteLine(json);
            }
            else
            {
                var json = JsonSerializer.Serialize(searchResults, new JsonSerializerOptions { WriteIndented = true });
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
