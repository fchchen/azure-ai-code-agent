using Microsoft.AspNetCore.Mvc;
using SampleApi.Services;

namespace SampleApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Login with username and password
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(result);
    }

    /// <summary>
    /// Validate an authentication token
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<bool>> ValidateToken([FromHeader(Name = "Authorization")] string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new { error = "Token is required" });
        }

        // Remove "Bearer " prefix if present
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..];
        }

        var isValid = await _authService.ValidateTokenAsync(token);
        return Ok(new { valid = isValid });
    }
}
