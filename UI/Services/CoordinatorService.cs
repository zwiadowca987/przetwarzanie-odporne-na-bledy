namespace UI.Services;

using Microsoft.Extensions.Logging;

public class CoordinatorService
{
    private readonly HttpClient _http;
    private readonly ILogger<CoordinatorService> _logger;

    public CoordinatorService(HttpClient http, ILogger<CoordinatorService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> SendUpdate(string value)
    {
        var url = "http://localhost:5000/update";
        _logger.LogInformation("TRANSACTION START: {Value}", value);

        try
        {
            var response = await _http.PostAsJsonAsync(url, new { value });
            if (!response.IsSuccessStatusCode) return "ERROR";
            var result = await response.Content.ReadFromJsonAsync<TransactionResultResponse>();
            return result?.Status ?? "UNKNOWN";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd połączenia z koordynatorem");
            return "CONNECTION_ERROR";
        }
    }

    public async Task SetCoordinatorErrorState(string errorType)
    {
        await SendFaultRequest("http://localhost:5000", errorType);
    }

    public async Task SetClientErrorState(string clientUrl, string errorType)
    {
        await SendFaultRequest(clientUrl, errorType);
    }

    private async Task SendFaultRequest(string baseUrl, string errorType)
    {
        // Mapowanie nazw z UI na endpointy API
        string endpoint = errorType switch
        {
            "None" => "/restore",
            "Fail" => "/fail/Fail",
            "CrashBeforeVote" => "/fail/CrashBeforeVote", 
            "CrashAfterVote" => "/fail/CrashAfterVote",
            "Timeout" => "/fail/Timeout",
            "Crash" => "/fail/Crash",
            "CrashBeforeCommitSend" => "/fail/CrashBeforeCommitSend",
            "PartialRequest" => "/fail/PartialRequest",
            "CommitHalf" => "/fail/CommitHalf",
            _ => "/restore"
        };

        var url = $"{baseUrl}{endpoint}";
        _logger.LogInformation("Wstrzykiwanie awarii: {Url}", url);

        try
        {
            await _http.PostAsync(url, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wstrzykiwania błędu do {Url}", url);
        }
    }

    private record TransactionResultResponse(string Status);

}