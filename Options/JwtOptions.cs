namespace QuotesApi.Options;

public sealed class JwtOptions
{
    public string   Key              { get; init; } = string.Empty;
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
