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
    _logger.LogInformation("Wysyłanie POST do {Url} z danymi: {Value}", url, value);

    try
    {
        var response = await _http.PostAsJsonAsync(url, new { value });
        
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Serwer zwrócił błąd {StatusCode}: {Content}", response.StatusCode, content);
            
            
            return string.IsNullOrEmpty(content) ? "ERROR" : content.Trim('"'); 
            
        }

        return content.Trim('"');
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Krytyczny błąd połączenia z {Url}", url);
        throw; 
    }
}
}
