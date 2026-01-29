using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SampleApi.Data;
using SampleApi.Models;

namespace SampleApi.Services;

/// <summary>
/// Service for authentication and authorization
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task<bool> ValidateTokenAsync(string token);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly Dictionary<string, (int UserId, DateTime Expiry)> _tokens = new();

    public AuthService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Authenticate a user and return a token
    /// </summary>
    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null)
        {
            return new AuthResult { Success = false, Error = "Invalid username or password" };
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            return new AuthResult { Success = false, Error = "Invalid username or password" };
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate token
        var token = GenerateToken();
        _tokens[token] = (user.Id, DateTime.UtcNow.AddHours(24));

        return new AuthResult
        {
            Success = true,
            Token = token,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role
            }
        };
    }

    /// <summary>
    /// Validate if a token is still valid
    /// </summary>
    public Task<bool> ValidateTokenAsync(string token)
    {
        if (!_tokens.TryGetValue(token, out var tokenInfo))
        {
            return Task.FromResult(false);
        }

        if (DateTime.UtcNow > tokenInfo.Expiry)
        {
            _tokens.Remove(token);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Hash a password using SHA256
    /// </summary>
    public string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Verify a password against its hash
    /// </summary>
    public bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }

    private string GenerateToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

public class AuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public UserDto? User { get; set; }
    public string? Error { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
