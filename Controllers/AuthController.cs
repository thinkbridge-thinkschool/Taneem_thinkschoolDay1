using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        bool passwordValid;
        using (var span = ServiceExtensions.ActivitySource.StartActivity("bcrypt.verify"))
        {
            span?.SetTag("user.id", user?.Id);
            passwordValid = user is not null &&
                            BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        }

        if (!passwordValid || user is null)
            return Unauthorized();

        var accessToken  = MintAccessToken(user);
        var (rawRefresh, storedRefresh) = CreateRefreshToken(user.Id);

        _db.RefreshTokens.Add(storedRefresh);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            access_token  = accessToken,
            refresh_token = rawRefresh,
            expires_in    = (int)TimeSpan.FromMinutes(
                double.Parse(_config["Jwt:ExpiresInMinutes"]!)).TotalSeconds
        });
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var hashed = HashToken(request.RefreshToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == hashed);

        if (existing is null)
            return Unauthorized();

        // ── Reuse detection ───────────────────────────────────────────────
        // Token was already replaced — someone is reusing a rotated token.
        // Revoke the entire family and force re-auth.
        if (existing.ReplacedByToken is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} family {Family}. " +
                "Revoking entire chain.", existing.UserId, existing.Family);

            var family = await _db.RefreshTokens
                .Where(t => t.Family == existing.Family)
                .ToListAsync();

            foreach (var t in family)
                t.RevokedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Unauthorized(new { message = "Token reuse detected. Please log in again." });
        }

        if (!existing.IsActive)
            return Unauthorized();

        var user = await _db.Users.FindAsync(existing.UserId);
        if (user is null)
            return Unauthorized();

        // ── Rotate ───────────────────────────────────────────────────────
        var (newRawRefresh, newStoredRefresh) = CreateRefreshToken(user.Id, existing.Family);

        // Mark old token as revoked and point to replacement
        existing.RevokedAt       = DateTime.UtcNow;
        existing.ReplacedByToken = newStoredRefresh.Token;

        _db.RefreshTokens.Add(newStoredRefresh);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            access_token  = MintAccessToken(user),
            refresh_token = newRawRefresh,
            expires_in    = (int)TimeSpan.FromMinutes(
                double.Parse(_config["Jwt:ExpiresInMinutes"]!)).TotalSeconds
        });
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        var hashed   = HashToken(request.RefreshToken);
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == hashed);

        if (existing is not null && existing.IsActive)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private string MintAccessToken(User user)
    {
        var key          = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);
        var expiresInMin = double.Parse(_config["Jwt:ExpiresInMinutes"]!);
        var handler      = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email,          user.Email),
                new Claim("role",                    user.Role),
                new Claim("jti",                     Guid.NewGuid().ToString())
            }),
            Expires            = DateTime.UtcNow.AddMinutes(expiresInMin),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature),
            Issuer = "self"
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static (string Raw, RefreshToken Stored) CreateRefreshToken(
        int userId, string? family = null)
    {
        var raw    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hashed = HashToken(raw);
        var fam    = family ?? Guid.NewGuid().ToString();  // new family on login

        return (raw, new RefreshToken
        {
            Token     = hashed,
            Family    = fam,
            UserId    = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Request records ───────────────────────────────────────────────────

    public sealed class LoginRequest
    {
        public string Email    { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}