using System.Text.Json;
using System.Text.Json.Serialization;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public sealed class BackupExportCommand : CommandBase<BackupExportSettings>
{
    private static readonly string[] SecretPatterns = { "secret", "token", "password" };

    public override async Task<int> ExecuteAsync(CommandContext context, BackupExportSettings settings, CancellationToken cancellationToken)
    {
        var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var settingsList = await db.Settings
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);

        var librariesList = await db.Libraries
            .OrderBy(l => l.Type)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var exportData = CreateExportData(settingsList, librariesList, settings.RedactSecrets);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(exportData, jsonOptions);

        if (settings.WriteToStdout || settings.ReturnRaw)
        {
            Console.WriteLine(json);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            var outputDir = Path.GetDirectoryName(settings.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await File.WriteAllTextAsync(settings.OutputPath, json, cancellationToken);
            AnsiConsole.MarkupLine($"[green]Export written to:[/] {settings.OutputPath}");
            AnsiConsole.MarkupLine($"[grey]Settings: {settingsList.Count}, Libraries: {librariesList.Count}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[yellow]No output specified. Use --output <path> or --stdout.[/]");
        return 1;
    }

    private static object CreateExportData(List<Setting> settings, List<Library> libraries, bool redactSecrets)
    {
        var exportedSettings = settings.Select(s => new ExportedSetting
        {
            Key = s.Key,
            Value = ShouldRedact(s.Key) && redactSecrets ? "[REDACTED]" : s.Value,
            Comment = s.Comment,
            Category = s.Category
        }).ToList();

        var exportedLibraries = libraries.Select(l => new ExportedLibrary
        {
            Name = l.Name,
            Type = l.TypeValue.ToString(),
            Path = l.Path,
            ApiKey = l.ApiKey.ToString(),
            Description = l.Description
        }).ToList();

        return new
        {
            schemaVersion = "1.0",
            exportedAt = DateTime.UtcNow.ToString("O"),
            settings = exportedSettings,
            libraries = exportedLibraries
        };
    }

    private static bool ShouldRedact(string key)
    {
        var lowerKey = key.ToLowerInvariant();
        return SecretPatterns.Any(pattern => lowerKey.Contains(pattern));
    }

    private sealed class ExportedSetting
    {
        [JsonPropertyName("key")]
        public string Key { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;

        [JsonPropertyName("comment")]
        public string? Comment { get; init; }

        [JsonPropertyName("category")]
        public int? Category { get; init; }
    }

    private sealed class ExportedLibrary
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
