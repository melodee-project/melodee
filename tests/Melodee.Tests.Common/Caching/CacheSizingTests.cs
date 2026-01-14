using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Serilog;

namespace Melodee.Tests.Common.Caching;

public class CacheSizingTests
{
    [Fact]
    public void GetObjectSizeInBytes_ReturnsZeroForNull()
    {
        var manager = CreateTestCacheManager();
        var size = manager.GetType()
            .InvokeMember("GetObjectSizeInBytes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.InvokeMethod,
                null, manager, new object?[] { null });

        Assert.Equal(0L, size);
    }

    [Fact]
    public void GetObjectSizeInBytes_ReturnsCorrectSizeForString()
    {
        var manager = CreateTestCacheManager();
        var method = manager.GetType()
            .GetMethod("GetObjectSizeInBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = method?.Invoke(manager, new object[] { "hello" });

        Assert.Equal(5L, result);
    }

    [Fact]
    public void GetObjectSizeInBytes_ReturnsCorrectSizeForPrimitives()
    {
        var manager = CreateTestCacheManager();
        var method = manager.GetType()
            .GetMethod("GetObjectSizeInBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.Equal(4L, method?.Invoke(manager, new object[] { 42 }));
        Assert.Equal(8L, method?.Invoke(manager, new object[] { 42L }));
        Assert.Equal(8L, method?.Invoke(manager, new object[] { 42.0 }));
        Assert.Equal(1L, method?.Invoke(manager, new object[] { true }));
    }

    [Fact]
    public void GetObjectSizeInBytes_ReturnsEstimatedSizeForCollections()
    {
        var manager = CreateTestCacheManager();
        var method = manager.GetType()
            .GetMethod("GetObjectSizeInBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var list = new List<string> { "a", "b", "c" };
        var result = method?.Invoke(manager, new object[] { list });

        Assert.Equal(96L, result);
    }

    private static MemoryCacheManager CreateTestCacheManager()
    {
        var serilogLogger = new LoggerConfiguration().CreateLogger();
        var serilogLoggerForSerializer = new LoggerConfiguration().CreateLogger();
        return new MemoryCacheManager(
            serilogLogger,
            TimeSpan.FromMinutes(5),
            new Serializer(serilogLoggerForSerializer));
    }
}
