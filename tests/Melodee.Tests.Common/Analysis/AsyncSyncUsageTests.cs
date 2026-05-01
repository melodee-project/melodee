using System.Text.RegularExpressions;

namespace Melodee.Tests.Common.Analysis;

public class AsyncSyncUsageTests
{
    [Fact]
    public void Source_Should_Not_Use_Blocking_TaskSync_APIs()
    {
        var repoRoot = GetRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");

        // Files to ignore (acceptable synchronous usage or generated code)
        var ignore = new[]
        {
            Path.Combine(srcDir, "Melodee.Blazor/Services/BaseUrlService.cs"),
            Path.Combine(srcDir, "Melodee.Common/Migrations"),
            Path.Combine(srcDir, "Melodee.Mql/MqlExpressionCache.cs"), // Synchronous cleanup is intentional
            Path.Combine(srcDir, "Melodee.Common/Services/Security/SecretProtector.cs"), // Singleton needs sync config at startup
        };

        var patterns = new[]
        {
            // Strong indicators of sync-blocking in async contexts
            new Regex(@"\.(Wait|WaitAll|WaitAny)\s*\(", RegexOptions.Compiled),
            new Regex(@"GetAwaiter\(\)\.GetResult\(\)", RegexOptions.Compiled)
        };

        var offenders = new List<(string file, int line, string content)>();

        foreach (var file in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (ignore.Any(i => file.StartsWith(i, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (patterns.Any(p => p.IsMatch(line)))
                {
                    offenders.Add((file, i + 1, line.Trim()));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "blocking .Result/.Wait/GetResult calls reduce throughput. Offenders: \n" +
            string.Join('\n', offenders.Select(o => $"{o.file}:{o.line}: {o.content}")));
    }

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "Melodee.sln")))
        {
            dir = Directory.GetParent(dir)!.FullName;
        }
        return dir;
    }
}
