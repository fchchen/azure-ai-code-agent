using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using CodeAgent.Api.Services.Grounding;

namespace CodeAgent.Api.Tests.Services;

public class CitationServiceTests
{
    private readonly CitationService _citationService;

    public CitationServiceTests()
    {
        var logger = new Mock<ILogger<CitationService>>();
        _citationService = new CitationService(logger.Object);
    }

    [Fact]
    public void ExtractCitations_FromToolResults_ExtractsCitations()
    {
        // Arrange
        var toolResults = new List<string>
        {
            @"Found 2 relevant code sections:

--- [src/Services/UserService.cs:10-25] (method: GetUserAsync) [Score: 0.95] ---
```csharp
public async Task<User> GetUserAsync(int id)
{
    return await _repository.GetByIdAsync(id);
}
```

--- [src/Models/User.cs:5-15] (class: User) [Score: 0.85] ---
```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```
"
        };

        // Act
        var citations = _citationService.ExtractCitationsFromToolResults(toolResults);

        // Assert
        citations.Should().HaveCount(2);
        citations[0].FilePath.Should().Be("src/Services/UserService.cs");
        citations[0].StartLine.Should().Be(10);
        citations[0].EndLine.Should().Be(25);
        citations[0].RelevanceScore.Should().Be(0.95);
    }

    [Fact]
    public void ExtractCitations_FromContent_ParsesFileLineReferences()
    {
        // Arrange
        var content = "The authentication logic is in [src/Auth/AuthService.cs:42] and uses JWT tokens configured in [src/Config/JwtConfig.cs:10-15].";
        var toolResults = new List<string>();

        // Act
        var result = _citationService.ExtractCitations(content, toolResults);

        // Assert
        result.Citations.Should().HaveCount(2);
        result.Citations.Should().Contain(c => c.FilePath == "src/Auth/AuthService.cs" && c.StartLine == 42);
        result.Citations.Should().Contain(c => c.FilePath == "src/Config/JwtConfig.cs" && c.StartLine == 10);
    }

    [Fact]
    public void AddCitationMarkers_ReplacesCitationReferences()
    {
        // Arrange
        var content = "See [src/file.cs:10] for details.";
        var citations = new List<CodeAgent.Api.Models.Citation>
        {
            new()
            {
                FilePath = "src/file.cs",
                StartLine = 10,
                EndLine = 10
            }
        };

        // Act
        var result = _citationService.AddCitationMarkers(content, citations);

        // Assert
        result.Should().Contain("[1]");
        result.Should().NotContain("[src/file.cs:10]");
    }

    [Fact]
    public void ExtractCitations_DeduplicatesCitations()
    {
        // Arrange
        var content = "Found in [src/file.cs:10] and also [src/file.cs:10].";
        var toolResults = new List<string>();

        // Act
        var result = _citationService.ExtractCitations(content, toolResults);

        // Assert
        result.Citations.Should().HaveCount(1);
    }

    [Fact]
    public void ExtractCitations_SortsByRelevanceScore()
    {
        // Arrange
        var toolResults = new List<string>
        {
            @"--- [file1.cs:1-5] [Score: 0.50] ---
```csharp
code1
```

--- [file2.cs:1-5] [Score: 0.90] ---
```csharp
code2
```

--- [file3.cs:1-5] [Score: 0.70] ---
```csharp
code3
```
"
        };

        // Act
        var citations = _citationService.ExtractCitationsFromToolResults(toolResults);

        // Assert
        citations[0].RelevanceScore.Should().Be(0.90);
        citations[1].RelevanceScore.Should().Be(0.70);
        citations[2].RelevanceScore.Should().Be(0.50);
    }
}
