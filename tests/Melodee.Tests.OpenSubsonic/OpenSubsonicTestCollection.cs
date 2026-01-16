namespace Melodee.Tests.OpenSubsonic;

/// <summary>
/// Collection definition for OpenSubsonic tests.
/// Tests in the same collection run sequentially to avoid WebApplicationFactory and DbContext conflicts.
/// </summary>
[CollectionDefinition(Name)]
public class OpenSubsonicTestCollection : ICollectionFixture<object>
{
    public const string Name = "OpenSubsonic";
}
