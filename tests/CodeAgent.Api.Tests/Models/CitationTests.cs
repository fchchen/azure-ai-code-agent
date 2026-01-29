using FluentAssertions;
using CodeAgent.Api.Models;

namespace CodeAgent.Api.Tests.Models;

public class CitationTests
{
    [Fact]
    public void FormatReference_SingleLine_ReturnsCorrectFormat()
    {
        // Arrange
        var citation = new Citation
        {
            FilePath = "src/Services/UserService.cs",
            StartLine = 42,
            EndLine = 42
        };

        // Act
        var result = citation.FormatReference();

        // Assert
        result.Should().Be("src/Services/UserService.cs:42");
    }

    [Fact]
    public void FormatReference_MultipleLines_ReturnsRangeFormat()
    {
        // Arrange
        var citation = new Citation
        {
            FilePath = "src/Services/UserService.cs",
            StartLine = 10,
            EndLine = 25
        };

        // Act
        var result = citation.FormatReference();

        // Assert
        result.Should().Be("src/Services/UserService.cs:10-25");
    }
}
