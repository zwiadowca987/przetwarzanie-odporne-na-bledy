using CoordinatorNode.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CoordinatorNode.Services;

public class TransactionCoordinatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<CoordinatorHub, ITransactionClient> _hubContext;
    
    // Lista węzłów (w prawdziwym projekcie pobierana z appsettings.json)
    private readonly string[] _nodes;

    // Przechowujemy aktualny stan (np. "None", "Error1", "Crash")
    private string _currentErrorState = "None";

    public TransactionCoordinatorService(
        IHttpClientFactory httpClientFactory,
        IHubContext<CoordinatorHub, ITransactionClient> hubContext,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
        
        _nodes = configuration.GetSection("NodeUrls").Get<string[]>() ?? [];
    }

    public async Task SetErrorStateAsync(string errorType)
    {
        _currentErrorState = errorType;
        
        Console.WriteLine($"[SIMULATION] Zmiana stanu Koordynatora na: {errorType}");

        // Powiadamiamy UI, że stan się zmienił. 
        await NotifyUI("COORDINATOR", "STATUS_CHANGE", errorType);
    }

    public async Task<bool> PerformTwoPhaseCommitAsync(string value)
    {
        var client = _httpClientFactory.CreateClient();
        await NotifyUI("COORDINATOR", "START", $"Rozpoczynam transakcję dla wartości: {value}");

        // --- FAZA 1: PREPARE (Głosowanie) ---
        foreach (var node in _nodes)
        {
            try
            {
                await NotifyUI("COORDINATOR", "PREPARE_SEND", $"Wysyłam PREPARE do {node}");

                var response = await client.PostAsJsonAsync($"{node}/prepare", value);

                if (!response.IsSuccessStatusCode)
                {
                    await NotifyUI(node, "VOTE_ABORT", "Węzeł odrzucił transakcję.");
                    await RollbackAsync(client);
                    return false; // Przerwij transakcję
                }

                await NotifyUI(node, "VOTE_COMMIT", "Węzeł gotowy.");
            }
            catch (Exception ex)
            {
                await NotifyUI("COORDINATOR", "ERROR", $"Błąd połączenia z {node}: {ex.Message}");
                await RollbackAsync(client);
                return false;
            }
        }

        // --- FAZA 2: COMMIT (Zatwierdzenie) ---
        await NotifyUI("COORDINATOR", "DECISION", "Wszyscy gotowi. COMMIT GLOBALNY.");

        foreach (var node in _nodes)
        {
            // Fire and forget commit
            _ = client.PostAsJsonAsync($"{node}/commit", value);
        }

        await NotifyUI("COORDINATOR", "SUCCESS", "Transakcja zakończona sukcesem.");
        return true;
    }

    private async Task RollbackAsync(HttpClient client)
    {
        await NotifyUI("COORDINATOR", "ROLLBACK_START", "Wycofywanie transakcji (ABORT)...");
        foreach (var node in _nodes)
        {
            _ = client.PostAsync($"{node}/abort", null);
        }

        await NotifyUI("COORDINATOR", "ROLLBACK_END", "Transakcja anulowana.");
    }

    private async Task NotifyUI(string source, string status, string message)
    {
        await _hubContext.Clients.All.ReceiveUpdate(source, status, message);
    }
}