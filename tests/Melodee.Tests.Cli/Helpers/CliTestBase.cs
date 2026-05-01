using Microsoft.Extensions.Configuration;

namespace Melodee.Tests.Cli.Helpers;

/// <summary>
/// Base class for CLI command tests with simple setup
/// </summary>
public abstract class CliTestBase : IDisposable
{
    protected IConfigurationRoot Configuration { get; }

    protected CliTestBase()
    {
        Configuration = CreateTestConfiguration();
    }

    private static IConfigurationRoot CreateTestConfiguration()
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            {"ConnectionStrings:DefaultConnection", "DataSource=:memory:"},
            {"Serilog:MinimumLevel", "Information"}
        });
        return configurationBuilder.Build();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
