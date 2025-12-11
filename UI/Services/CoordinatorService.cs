using System.Net.Http.Json;

namespace UI.Services;

public class CoordinatorService
{
    private readonly HttpClient _http;

    public CoordinatorService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> SendUpdate(string value)
    {
        var response = await _http.PostAsJsonAsync("http://localhost:5000/update", new { value });

        if (!response.IsSuccessStatusCode)
            return "ERROR";

        return await response.Content.ReadAsStringAsync();
    }
}
