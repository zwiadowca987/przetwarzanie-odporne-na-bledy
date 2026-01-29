using ServerNode.dto;

namespace ServerNode.Services;
using System.Net.Http.Json;

using Microsoft.AspNetCore.SignalR.Client;

public class TransactionClientService : IAsyncDisposable
{
    private readonly ILogger<TransactionClientService> _logger;
    private readonly string _nodeId;
    private readonly string _myUrl;
    
    private readonly HubConnection _hubConnection;
    private readonly HttpClient _peerClient;
    private readonly string[] _peerUrls;
     
    // --- STAN WĘZŁA ---
    private Dictionary<string, string> _storedValues = new();
    private string _lastValueStored = "null";

    // --- PAMIĘĆ TRANSAKCJI ---
    private string _preparedTransactionId = "null";
    private string _preparedValue = "null";
    private TransactionState _transactionState = TransactionState.Unknown;

    // --- AWARIE ---
    private string _currentErrorState = "None"; // Typy: None, Fail, CrashBeforeVote, CrashAfterVote
    //private bool _isCrashed => _currentErrorState == "Crash" || _currentErrorState == "CrashBeforeVote" || _currentErrorState == "CrashAfterVote";
    public TransactionClientService(IConfiguration config, ILogger<TransactionClientService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _nodeId = config["NodeId"] ?? "UNKNOWN";
        _myUrl = config["ASPNETCORE_URLS"]?.Split(';').FirstOrDefault() ?? "http://localhost:5000"; 
        var hubUrl = config["CoordinatorUrl"] ?? "http://localhost:5000/hubs/transaction";
        
        _peerUrls = config.GetSection("ClusterPeers").Get<string[]>() ?? [];
        _peerClient = httpClientFactory.CreateClient();
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
            _logger.LogInformation("[CONNECT] Połączono z Koordynatorem jako {NodeId}", _nodeId);
            await BroadcastStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXCEPTION] Nie udało się połączyć z Hubem Koordynatora");
        }
    }

    // --- LOGIKA 2PC ---

    public async Task<bool> PrepareAsync(string transactionId, string value)
    {
        _logger.LogInformation("[PREPARE] ID: {TransactionId}, Val: {Value}. Stan awarii: {CurrentErrorState}", transactionId, value, _currentErrorState);
        _transactionState = TransactionState.Unknown;
        // Symulacja awarii
        if (_currentErrorState == "Fail")
        {
            return false;
        }

        // Załamanie PRZED podjęciem decyzji
        if (_currentErrorState == "CrashBeforeVote")
        {
            _logger.LogWarning("[SIMULATION] Awaria przed głosowaniem!");
            await BroadcastStatus();
            await Task.Delay(10000); // Udajemy, że nie odpowiadamy
            return false;
        }

        // Głosowanie
        _preparedTransactionId = transactionId;
        _preparedValue = value;
        _transactionState = TransactionState.Prepared;
        await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState.ToString()); 

        return true;
    }

    public async Task<bool> CommitAsync(string transactionId, string value)
    {
        _logger.LogInformation("[COMMIT] ID: {TransactionId}.", transactionId);
        
        if (_currentErrorState == "CrashAfterVote")
        {
            _logger.LogWarning("[SIMULATION] Awaria po głosowaniu (ale przed Commit)!");

            await Task.Delay(3000);
            await RecoverAsync();
            await BroadcastStatus();

            return true; 
        }
        
        if (_currentErrorState != "None") 
        {
            return false; 
        }

        if (_transactionState == TransactionState.Prepared && _preparedTransactionId == transactionId)
        {
            _storedValues[transactionId] = value;
            _lastValueStored = value;
            _transactionState = TransactionState.Committed;
            _logger.LogInformation("[COMMIT] Zatwierdzono lokalnie.");
            await BroadcastStatus();
            return true;
        }
        if (_transactionState == TransactionState.Committed && _storedValues.ContainsKey(transactionId))
            return true; 

        _logger.LogWarning("[COMMIT] Otrzymano COMMIT, ale stan lokalny to {State}", _transactionState.ToString());
        return false;
        
    }

    public async Task AbortAsync()
    {
        _logger.LogInformation("[ABORT] Czyszczenie stanu prepare.");
        _transactionState = TransactionState.Aborted;
        await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState.ToString());
        _preparedValue = "null";
    }

    // --- STEROWANIE BŁĘDAMI ---

    public async Task SetErrorStateAsync(string errorType)
    {
        _currentErrorState = errorType;
        
        _logger.LogWarning("[FAULT] Zmiana stanu awarii na: {ErrorType}.", errorType);
        await BroadcastStatus();
    }

    // --- PROTOKÓŁ REKONSTRUKCJI ---

    public async Task RecoverAsync()
    {
        _logger.LogInformation("[RECOVERY] Rozpoczynam procedurę naprawczą...");
        
        // 1. Jeśli padliśmy, będąc w stanie PREPARED, nie wiemy jaka była decyzja Koordynatora.
        // Musimy zapytać kolegów.
        if (_transactionState == TransactionState.Prepared)
        {
            _logger.LogWarning("[RECOVERY] Jestem w stanie PREPARED ({Id}). Pytam innych węzłów...", _preparedTransactionId);
            await AskPeersForDecision(_preparedTransactionId);
        }
        else
        {
            _logger.LogInformation("[RECOVERY] Stan czysty (Idle/Committed/Aborted). Nic do zrobienia.");
        }

        // Przywracamy sprawność
        _currentErrorState = "None";
        await BroadcastStatus();
    }

    private async Task AskPeersForDecision(string transactionId)
    {
        foreach (var peer in _peerUrls)
        {
            // Nie pytaj samego siebie
            if (peer.Contains(_myUrl) || string.IsNullOrEmpty(peer)) continue; 

            try
            {
                var response = await _peerClient.GetFromJsonAsync<PeerStatusResponse>($"{peer}/ask-status/{transactionId}");
                
                if (response == null) continue;

                _logger.LogInformation("[PEER-CHECK] Węzeł {Peer} odpowiedział: {State}", peer, response.State);

                // 1. Jeśli Q otrzymał COMMIT -> P może zatwierdzić
                if (response.State == TransactionState.Committed)
                {
                    _logger.LogInformation("[DECISION] Kolega ma COMMIT. Zatwierdzam lokalnie.");
                    _storedValues[transactionId] = response.Value ?? _preparedValue;
                    _lastValueStored = response.Value ?? _preparedValue;
                    _transactionState = TransactionState.Committed;
                    await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState.ToString());
                    return; 
                }

                // 2. Jeśli Q otrzymał ABORT -> P może zaniechać
                if (response.State == TransactionState.Aborted)
                {
                    _logger.LogInformation("[DECISION] Kolega ma ABORT. Anuluję lokalnie.");
                    _transactionState = TransactionState.Aborted;
                    await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState.ToString());
                    return; 
                }

                // 3. Jeśli Q jest Unknown (nie słyszał o transakcji) -> P powinien zaniechać
                if (response.State == TransactionState.Unknown)
                {
                    _logger.LogInformation("[DECISION] Kolega nie zna transakcji. Zakładam, że Koordynator padł przy VR. Anuluję.");
                    _transactionState = TransactionState.Aborted;
                    await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState,ToString());
                }

                // 4. Jeśli Q jest Prepared (też czeka) -> Szukamy dalej...
            }
            catch 
            {
                _logger.LogWarning("[PEER-CHECK] Nie udało się połączyć z {Peer}", peer);
            }
        }

        // Jeśli przeszliśmy pętlę i wszyscy są PREPARED lub nie odpowiadają:
        _logger.LogError("[BLOCKED] Wszyscy dostępni koledzy też czekają (PREPARED). Czekam na Koordynatora.");
    }
    
    // --- ENDPOINT DLA INNYCH (Responder) ---
    public PeerStatusResponse GetLocalStatus(string transactionId)
    {
        // Jeśli pytają o transakcję, której nie znamy -> Unknown
        if (_preparedTransactionId != transactionId ||  !_storedValues.ContainsKey(transactionId))
            return new PeerStatusResponse(_nodeId, TransactionState.Unknown, null);

        // Jeśli mamy zapisaną wartość, odsyłamy ją 
        var val = _transactionState == TransactionState.Committed ? _storedValues[transactionId] : null;

        return new PeerStatusResponse(_nodeId, _transactionState, val);
    }
    
    public string GetStatus() => $"State: {_currentErrorState}, Value: {_lastValueStored}";

    private async Task BroadcastStatus(string status, string message)
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, status, message);
        }
    }

    public async Task BroadcastStatus()
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            var statusMsg = _currentErrorState; 
            await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "STATUS_CHANGE", statusMsg);
            await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "VALUE_UPDATE", _lastValueStored);
            await _hubConnection.InvokeAsync("BroadcastNodeStatus", _nodeId, "TRANSACTION_STATUS", _transactionState.ToString());
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}