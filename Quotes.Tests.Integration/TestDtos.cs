using System.Text.Json.Serialization;

namespace Quotes.Tests.Integration;

// Local test DTOs — mirror the API response shapes without importing production types.
// Using PropertyNameCaseInsensitive = true in JsonOpts handles camelCase → PascalCase mapping.
// snake_case fields (access_token etc.) need explicit [JsonPropertyName].

public record QuoteResponse(int Id, string Author, string Text);

public record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")]   int    ExpiresIn);

public record CollectionItemResponse(int QuoteId, DateTimeOffset AddedAt);

public record CollectionResponse(
    int                        Id,
    string                     Name,
    int                        OwnerId,
    List<CollectionItemResponse> Items);
