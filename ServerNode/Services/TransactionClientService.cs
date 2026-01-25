namespace ServerNode.Services;

using Microsoft.AspNetCore.SignalR.Client;

public class TransactionClientService : IAsyncDisposable
{
    private readonly ILogger<TransactionClientService> _logger;
    private readonly string _nodeId;
    private readonly HubConnection _hubConnection;

    // --- STAN WĘZŁA ---
    private string _storedValue = "null";
    private string _currentErrorState = "None"; // None, Timeout, Crash, DbError
    private bool _isFailed => _currentErrorState == "Crash"; 

    public TransactionClientService(IConfiguration config, ILogger<TransactionClientService> logger)
    {
        _logger = logger;
        _nodeId = config["NodeId"] ?? "UNKNOWN_CLIENT";
        var hubUrl = config["CoordinatorUrl"] ?? "http://localhost:5000/hubs/transaction";

        // Konfiguracja połączenia z Hubem Koordynatora
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();
    }

    // Metoda startująca połączenie 
    public async Task StartAsync()
    {
        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Połączono z Koordynatorem jako {NodeId}", _nodeId);
            await BroadcastStatus("STATUS_CHANGE", _currentErrorState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się połączyć z Hubem Koordynatora");
        }
    }

    // --- LOGIKA 2PC ---

    public async Task<bool> PrepareAsync(string value)
    {
        _logger.LogInformation("Otrzymano PREPARE dla wartości: {Value}", value);

        // Symulacja awarii
        if (_currentErrorState == "Crash") return false;
        if (_currentErrorState == "Timeout") await Task.Delay(5000); // Symulacja zwisu

        return true;
    }

    public async Task<bool> CommitAsync(string value)
    {
        _logger.LogInformation("Otrzymano COMMIT dla wartości: {Value}", value);

        if (_currentErrorState == "Crash" || _currentErrorState == "DbError") return false;
        
        // Zapis wartości "trwały"
        _storedValue = value;
        return true;
    }

    public void Abort()
    {
        _logger.LogInformation("Otrzymano ABORT. Wycofywanie zmian.");
        // Tu zwolnienie blokad
    }

    // --- STEROWANIE BŁĘDAMI ---

    public async Task SetErrorStateAsync(string errorType)
    {
        _currentErrorState = errorType;
        _logger.LogWarning("Zmiana stanu awarii na: {ErrorType}", errorType);
        
        await BroadcastStatus("STATUS_CHANGE", errorType);
    }

    public string GetStatus() => $"State: {_currentErrorState}, Value: {_storedValue}";

    private async Task BroadcastStatus(string status, string message)
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, status, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}