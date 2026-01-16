using System.IO;
using FluentAssertions;
using Melodee.Common.Utility;

namespace Melodee.Tests.Common.Utility;

public class LibraryPathValidationTests
{
    [Fact]
    public void PathsOverlap_CaseInsensitiveComparison_DetectsOverlap()
    {
        var result = LibraryPathValidation.PathsOverlap(
            "/Music/Inbound",
            "/music/inbound/subfolder",
            StringComparison.OrdinalIgnoreCase);

        result.Should().BeTrue();
    }

    [Fact]
    public void PathsOverlap_CaseSensitiveComparison_DoesNotMatchDifferentCase()
    {
        var result = LibraryPathValidation.PathsOverlap(
            "/Music/Inbound",
            "/music/inbound/subfolder",
            StringComparison.Ordinal);

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsTraversal_DetectsDotSegments()
    {
        LibraryPathValidation.ContainsTraversal("/data/../music").Should().BeTrue();
        LibraryPathValidation.ContainsTraversal("/data/./music").Should().BeTrue();
    }

    [Fact]
    public void IsPathLengthRecommended_ReturnsFalseForLongPath()
    {
        var longPath = $"{Path.DirectorySeparatorChar}{new string('a', LibraryPathValidation.RecommendedMaxPathLength + 10)}";

        LibraryPathValidation.IsPathLengthRecommended(longPath).Should().BeFalse();
    }
}
