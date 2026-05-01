using Microsoft.Extensions.Configuration;

namespace Melodee.Tests.Cli.Helpers;

/// <summary>
/// Simplified base class for CLI tests that will actually compile
/// </summary>
public abstract class SimpleTestBase : IDisposable
{
    protected IConfigurationRoot Configuration { get; }

    protected SimpleTestBase()
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
