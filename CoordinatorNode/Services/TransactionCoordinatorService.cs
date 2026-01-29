using CoordinatorNode.DTO;
using CoordinatorNode.Hubs;
using CoordinatorNode.Model;
using Microsoft.AspNetCore.SignalR;

namespace CoordinatorNode.Services;

public class TransactionCoordinatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<CoordinatorHub, ITransactionClient> _hubContext;
    
    // Lista węzłów (w prawdziwym projekcie pobierana z appsettings.json)
    private readonly string[] _nodes;

    private readonly int _transactionTimeoutMs = 7000; 

    // --- SYMULACJA ZAPIS STANU  ---
    private RecoveryLog _stableStorage = new();

    // --- SYMULACJA AWARII ---
    private string _currentErrorState = "None";

    private bool _isCrashed = false;

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

        // Powiadamiamy UI 
        await NotifyUI("COORDINATOR", "STATUS_CHANGE", errorType);
        
        if (errorType == "None")
        {
            await RecoveryProcess();
        }
    }

    // --- GŁÓWNA LOGIKA TRANSAKCJI ---
    public async Task<bool> PerformTwoPhaseCommitAsync(string value)
    {

        if (_isCrashed) return false;

        var transactionId = Guid.NewGuid().ToString();
        SaveState(transactionId, value, CoordinatorState.WaitingForVotes);

        var client = _httpClientFactory.CreateClient();
        await NotifyUI("COORDINATOR", "START", $"ID: {transactionId}. Prepare...");

        // --- FAZA 1: GŁOSOWANIE ---
        
        bool voteResult = await GatherVotesAsync(client, transactionId, value);

        if (_isCrashed) return false; // Symulacja: padł w trakcie podejmowania decyzji

        // --- FAZA 2: DECYZJA I ROZGŁASZANIE ---

        if (voteResult)
        {
            // COMMIT
            SaveState(transactionId, value, CoordinatorState.DecidedCommit);
            await NotifyUI("COORDINATOR", "DECISION", "Decyzja: COMMIT");
            
            if (CheckCrashPoint("CrashBeforeCommitSend")) return true; 

            await SendGlobalDecisionAsync(client, transactionId, value, commit: true);
            await NotifyUI("COORDINATOR", "SUCCESS", "Transakcja zakończona (Committed).");
            
            SaveState("", "", CoordinatorState.Idle);
            return true;
        }
        else
        {
            // Decyzja: ABORT
            SaveState(transactionId, value, CoordinatorState.DecidedAbort);
            await NotifyUI("COORDINATOR", "DECISION", "Decyzja: ABORT (Timeout lub Veto)");
            
            await SendGlobalDecisionAsync(client, transactionId, value, commit: false);
            await NotifyUI("COORDINATOR", "ROLLBACK_END", "Transakcja anulowana.");
            
            SaveState("", "", CoordinatorState.Idle);
            return false;
        }
    }

    // --- MECHANIZMY POMOCNICZE 2PC ---

    private async Task<bool> GatherVotesAsync(HttpClient client, string txId, string value)
    {
        var request = new TransactionRequest(txId, value);
        
        var voteTasks = _nodes.Select(async node =>
        {
            try
            {
                await NotifyUI("COORDINATOR", "PREPARE_SEND", $"Do: {node}");
                var response = await client.PostAsJsonAsync($"{node}/prepare", request);
                
                if (response.IsSuccessStatusCode)
                {
                    await NotifyUI(node, "VOTE_COMMIT", "Głos na TAK");
                    return true;
                }
                else
                {
                    await NotifyUI(node, "VOTE_ABORT", "Głos na NIE");
                    return false;
                }
            }
            catch
            {
                await NotifyUI("COORDINATOR", "ERROR", $"Brak kontaktu z {node}");
                return false; 
            }
        }).ToList();

        try
        {
            var allVotesTask = Task.WhenAll(voteTasks);
            var timeoutTask = Task.Delay(_transactionTimeoutMs);

            var completedTask = await Task.WhenAny(allVotesTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                await NotifyUI("COORDINATOR", "TIMEOUT", "Przekroczono czas na głosy! Abort.");
                return false; 
            }

            // Sprawdzamy wyniki
            var results = await allVotesTask;
            return results.All(vote => vote); 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error gathering votes: {ex.Message}");
            return false;
        }
    }

    private async Task SendGlobalDecisionAsync(HttpClient client, string txId, string value, bool commit)
    {
        var request = new TransactionRequest(txId, value);
        var endpoint = commit ? "commit" : "abort";

        var tasks = _nodes.Select(node => 
            client.PostAsJsonAsync($"{node}/{endpoint}", request)
        );

        await Task.WhenAll(tasks);
    }

    // --- REKONSTRUKCJA (Recovery Protocol) ---

    private async Task RecoveryProcess()
    {
        var log = _stableStorage;
        var client = _httpClientFactory.CreateClient();

        Console.WriteLine($"[RECOVERY] Wznawianie. Stan z dysku: {log.State}, ID: {log.TransactionId}");

        switch (log.State)
        {
            case CoordinatorState.Idle:
                await NotifyUI("COORDINATOR", "INFO", "System wstał. Czysty stan.");
                break;

            case CoordinatorState.WaitingForVotes:
               
                await NotifyUI("COORDINATOR", "RECOVERY", "Wznawiam głosowanie (VR)...");
                await SendGlobalDecisionAsync(client, log.TransactionId, log.Value, commit: false);
                await NotifyUI("COORDINATOR", "RECOVERY_ACTION", "Wysłano globalny ABORT dla przerwanej transakcji.");
                _stableStorage.State = CoordinatorState.Idle;
                break;

            case CoordinatorState.DecidedCommit:
                await NotifyUI("COORDINATOR", "RECOVERY", "Dosyłam decyzję COMMIT...");
                await SendGlobalDecisionAsync(client, log.TransactionId, log.Value, commit: true);
                _stableStorage.State = CoordinatorState.Idle;
                break;

            case CoordinatorState.DecidedAbort:
                await NotifyUI("COORDINATOR", "RECOVERY", "Dosyłam decyzję ABORT...");
                await SendGlobalDecisionAsync(client, log.TransactionId, log.Value, commit: false);
                _stableStorage.State = CoordinatorState.Idle;
                break;
        }
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
    private void SaveState(string txId, string value, CoordinatorState state)
    {
        _stableStorage = new RecoveryLog { TransactionId = txId, Value = value, State = state };
        Console.WriteLine($"[DISK WRITE] State: {state}, ID: {txId}");
    }

    private bool CheckCrashPoint(string trigger)
    {
        if (_currentErrorState == trigger)
        {
            _currentErrorState = "Crash"; 
            NotifyUI("COORDINATOR", "STATUS_CHANGE", "Crash").Wait();
            return true;
        }
        return false;
    }

    private async Task NotifyUI(string source, string status, string message)
    {
        await _hubContext.Clients.All.ReceiveUpdate(source, status, message);
    }
}