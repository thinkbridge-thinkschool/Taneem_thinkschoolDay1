namespace QuotesApi.Models;

public class RefreshToken
{
    public int       Id               { get; set; }
    public string    Token            { get; set; } = string.Empty; // hashed
    public string    Family           { get; set; } = string.Empty; // tracks token chain
    public int       UserId           { get; set; }
    public DateTime  ExpiresAt        { get; set; }
    public DateTime? RevokedAt        { get; set; }
    public string?   ReplacedByToken  { get; set; } // hashed replacement

    public bool IsExpired  => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked  => RevokedAt is not null;
    public bool IsActive   => !IsExpired && !IsRevoked;
}