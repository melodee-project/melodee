using FluentAssertions;
using Melodee.Common.Constants;

namespace Melodee.Tests.Common.Pagination;

public class PaginationCapTests
{
    [Fact]
    public void ApiDefaults_HasCorrectMaxPageSize()
    {
        ApiDefaults.MaxPageSize.Should().Be(200);
    }

    [Fact]
    public void ApiDefaults_HasCorrectDefaultPageSize()
    {
        ApiDefaults.DefaultPageSize.Should().Be(50);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void PageSize_ValidValues_AreWithinBounds(int pageSize)
    {
        pageSize.Should().BeGreaterThanOrEqualTo(1);
        pageSize.Should().BeLessThanOrEqualTo(ApiDefaults.MaxPageSize);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(500)]
    [InlineData(1000)]
    public void PageSize_ValuesAboveMax_ExceedLimit(int pageSize)
    {
        pageSize.Should().BeGreaterThan(ApiDefaults.MaxPageSize);
    }

    [Fact]
    public void ControllerBase_TryValidatePaging_ClampsToMaxPageSize()
    {
        var requestedPageSize = 500;

        var normalizedPageSize = (short)Math.Clamp(requestedPageSize, 1, ApiDefaults.MaxPageSize);

        normalizedPageSize.Should().Be(ApiDefaults.MaxPageSize);
    }

    [Fact]
    public void ControllerBase_TryValidatePaging_MinimumIsOne()
    {
        var requestedPageSize = 0;

        var normalizedPageSize = (short)Math.Clamp(requestedPageSize, 1, ApiDefaults.MaxPageSize);

        normalizedPageSize.Should().Be(1);
    }
}
