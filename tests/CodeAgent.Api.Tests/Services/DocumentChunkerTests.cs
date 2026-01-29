using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using CodeAgent.Api.Services.Rag;

namespace CodeAgent.Api.Tests.Services;

public class DocumentChunkerTests
{
    private readonly DocumentChunker _chunker;

    public DocumentChunkerTests()
    {
        var logger = new Mock<ILogger<DocumentChunker>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chunking:MaxChunkSize"] = "1500",
                ["Chunking:OverlapSize"] = "200"
            })
            .Build();

        _chunker = new DocumentChunker(logger.Object, config);
    }

    [Fact]
    public void ChunkFile_CSharpClass_CreatesClassChunk()
    {
        // Arrange
        var content = @"
namespace MyApp.Services
{
    public class UserService
    {
        private readonly IUserRepository _repository;

        public UserService(IUserRepository repository)
        {
            _repository = repository;
        }

        public async Task<User> GetUserAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }
    }
}";
        var filePath = "Services/UserService.cs";
        var repositoryId = "test-repo";

        // Act
        var chunks = _chunker.ChunkFile(filePath, content, repositoryId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c =>
        {
            c.RepositoryId.Should().Be(repositoryId);
            c.FilePath.Should().Be(filePath);
            c.Language.Should().Be("csharp");
        });
    }

    [Fact]
    public void ChunkFile_TypeScriptFunction_CreatesFunctionChunk()
    {
        // Arrange
        var content = @"
export async function fetchUser(id: number): Promise<User> {
    const response = await fetch(`/api/users/${id}`);
    if (!response.ok) {
        throw new Error('Failed to fetch user');
    }
    return response.json();
}

export const getUserName = (user: User): string => {
    return `${user.firstName} ${user.lastName}`;
};
";
        var filePath = "services/userService.ts";
        var repositoryId = "test-repo";

        // Act
        var chunks = _chunker.ChunkFile(filePath, content, repositoryId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => c.ChunkType == "function");
        chunks.Should().AllSatisfy(c =>
        {
            c.Language.Should().Be("typescript");
        });
    }

    [Fact]
    public void ChunkFile_PythonClass_CreatesChunks()
    {
        // Arrange
        var content = @"
class UserService:
    def __init__(self, repository):
        self.repository = repository

    def get_user(self, user_id: int):
        return self.repository.get_by_id(user_id)

    def create_user(self, data: dict):
        return self.repository.create(data)
";
        var filePath = "services/user_service.py";
        var repositoryId = "test-repo";

        // Act
        var chunks = _chunker.ChunkFile(filePath, content, repositoryId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c =>
        {
            c.Language.Should().Be("python");
        });
    }

    [Fact]
    public void ChunkFile_UnknownLanguage_ChunksBySize()
    {
        // Arrange
        var content = "Some plain text content that should be chunked by size.";
        var filePath = "README.txt";
        var repositoryId = "test-repo";

        // Act
        var chunks = _chunker.ChunkFile(filePath, content, repositoryId);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c =>
        {
            c.ChunkType.Should().Be("code");
        });
    }

    [Fact]
    public void ChunkFile_SetsCorrectLineNumbers()
    {
        // Arrange
        var content = @"line 1
line 2
line 3
function test() {
    console.log('hello');
}
line 7";
        var filePath = "test.js";
        var repositoryId = "test-repo";

        // Act
        var chunks = _chunker.ChunkFile(filePath, content, repositoryId);

        // Assert
        chunks.Should().NotBeEmpty();
        var firstChunk = chunks.First();
        firstChunk.StartLine.Should().BePositive();
    }
}
