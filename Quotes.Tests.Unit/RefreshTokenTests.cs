using QuotesApi.Models;

namespace Quotes.Tests.Unit;

// Tests for RefreshToken's three computed properties and the reuse-detection signal.
// RefreshToken uses mutable auto-properties so we can construct test instances inline.

public sealed class RefreshTokenTests
{
    // ── IsExpired ─────────────────────────────────────────────────────────

    [Fact]
    public void IsExpired_ExpiresAtIsInThePast_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(-1) };

        // Act / Assert
        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ExpiresAtIsInTheFuture_ReturnsFalse()
    {
        // Arrange
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(1) };

        // Act / Assert
        token.IsExpired.Should().BeFalse();
    }

    // ── IsRevoked ─────────────────────────────────────────────────────────

    [Fact]
    public void IsRevoked_RevokedAtHasValue_ReturnsTrue()
    {
        // Arrange — RevokedAt is set when the token is explicitly revoked or rotated
        var token = new RefreshToken { RevokedAt = DateTime.UtcNow.AddMinutes(-5) };

        // Act / Assert
        token.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public void IsRevoked_RevokedAtIsNull_ReturnsFalse()
    {
        // Arrange — a freshly issued token has no revocation date
        var token = new RefreshToken { RevokedAt = null };

        // Act / Assert
        token.IsRevoked.Should().BeFalse();
    }

    // ── IsActive ──────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_NotExpiredAndNotRevoked_ReturnsTrue()
    {
        // Arrange — normal in-flight token
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null,
        };

        // Act / Assert
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_Expired_ReturnsFalse()
    {
        // Arrange — expired but not explicitly revoked
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            RevokedAt = null,
        };

        // Act / Assert
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_Revoked_ReturnsFalse()
    {
        // Arrange — still within expiry window but manually revoked (e.g. logout)
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow,
        };

        // Act / Assert
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_BothExpiredAndRevoked_ReturnsFalse()
    {
        // Arrange — old revoked token long past its window
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-10),
            RevokedAt = DateTime.UtcNow.AddDays(-9),
        };

        // Act / Assert
        token.IsActive.Should().BeFalse();
    }

    // ── Reuse-detection signal ────────────────────────────────────────────
    //
    // AuthController refresh logic:
    //   if (stored.ReplacedByToken is not null)   // ← this token was already rotated
    //       revoke every token in stored.Family   // ← family-wide revocation
    //
    // These tests pin that the ReplacedByToken field is the flag the controller
    // reads when deciding to trigger the family revocation.

    [Fact]
    public void ReplacedByToken_WhenNull_TokenHasNotBeenRotated()
    {
        // Arrange — brand-new token fresh from login
        var token = new RefreshToken
        {
            ExpiresAt        = DateTime.UtcNow.AddDays(7),
            RevokedAt        = null,
            ReplacedByToken  = null,
        };

        // Act / Assert
        token.ReplacedByToken.Should().BeNull();
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ReplacedByToken_WhenNotNull_MeansTokenHasBeenRotated_TriggeringReuseDetection()
    {
        // Arrange — this token was used once (legitimate refresh), which set
        // ReplacedByToken and RevokedAt.  A second use of this same raw token
        // reaches the controller with ReplacedByToken != null → revoke family.
        var token = new RefreshToken
        {
            ExpiresAt        = DateTime.UtcNow.AddDays(7),
            RevokedAt        = DateTime.UtcNow.AddMinutes(-2),   // revoked by rotation
            ReplacedByToken  = "abc123hashedvalue",              // hashed successor
        };

        // Act / Assert
        token.ReplacedByToken.Should().NotBeNull(
            because: "a non-null ReplacedByToken is the signal the controller checks to revoke the whole family");
        token.IsRevoked.Should().BeTrue(
            because: "a rotated token is also revoked — it must never be accepted again");
        token.IsActive.Should().BeFalse(
            because: "a rotated token is no longer active");
    }
}
