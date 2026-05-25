namespace QuotesApi.Services;

public interface IExternalQuoteService
{
    Task<string> GetRandomQuoteAsync(CancellationToken ct = default);
}

public class ExternalQuoteService : IExternalQuoteService
{
    private readonly HttpClient _http;
    private readonly ILogger<ExternalQuoteService> _logger;

    public ExternalQuoteService(IHttpClientFactory factory, ILogger<ExternalQuoteService> logger)
    {
        _http   = factory.CreateClient("external-quotes");
        _logger = logger;
    }

    public async Task<string> GetRandomQuoteAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Calling external quotes API");
        var response = await _http.GetAsync("/api/random", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
