using CoordinatorNode.DTO;
using CoordinatorNode.Hubs;
using CoordinatorNode.Model;
using Microsoft.AspNetCore.SignalR;

namespace CoordinatorNode.Services;

public class TransactionCoordinatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<CoordinatorHub, ITransactionClient> _hubContext;
    private readonly string[] _nodes;
    private readonly int _transactionTimeoutMs = 7000; 

    private RecoveryLog _stableStorage = new();
    private string _currentErrorState = "None";
    private bool _isCrashed => _currentErrorState == "Crash";

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
        Console.WriteLine($"[SIMULATION] Stan Koordynatora: {errorType}");
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
        
        var voteResult = await GatherVotesAsync(client, transactionId, value);

        if (_isCrashed) return false; // Symulacja: padł w trakcie podejmowania decyzji

        // --- FAZA 2: DECYZJA I ROZGŁASZANIE ---

        if (voteResult)
        {
            SaveState(transactionId, value, CoordinatorState.DecidedCommit);
            await NotifyUI("COORDINATOR", "DECISION", "Decyzja: COMMIT");
            
            if (CheckCrashPoint("CrashBeforeCommitSend")) return true; 

            await SendGlobalDecisionAsync(client, transactionId, value, commit: true);
            
            if (_isCrashed) return true;

            await NotifyUI("COORDINATOR", "SUCCESS", "Transakcja zakończona.");
            SaveState("", "", CoordinatorState.Idle);
            return true;
        }
        else
        {
            SaveState(transactionId, value, CoordinatorState.DecidedAbort);
            await NotifyUI("COORDINATOR", "DECISION", "Decyzja: ABORT");
            
            await SendGlobalDecisionAsync(client, transactionId, value, commit: false);
            
            if (_isCrashed) return false;

            await NotifyUI("COORDINATOR", "ROLLBACK_END", "Transakcja anulowana.");
            SaveState("", "", CoordinatorState.Idle);
            return false;
        }
    }

    // --- MECHANIZMY POMOCNICZE 2PC ---

    private async Task<bool> GatherVotesAsync(HttpClient client, string txId, string value)
    {
        var request = new TransactionRequest(txId, value);
        var responses = new List<Task<bool>>();
        int count = 0;

        foreach (var node in _nodes)
        {
            if (_currentErrorState == "PartialRequest" && count >= _nodes.Length / 2)
            {
                await TriggerCrash("Symulacja Fail1: Padł w trakcie PREPARE");
                return false; 
            }

            responses.Add(SendPrepareSingle(client, node, request));
            count++;
        }

        try
        {
            var allVotesTask = Task.WhenAll(responses);
            var timeoutTask = Task.Delay(_transactionTimeoutMs);

            if (await Task.WhenAny(allVotesTask, timeoutTask) == timeoutTask)
            {
                await NotifyUI("COORDINATOR", "TIMEOUT", "Timeout głosowania!");
                return false;
            }

            var results = await allVotesTask;
            return results.Length == _nodes.Length && results.All(v => v);
        }
        catch { return false; }
    }

    private async Task<bool> SendPrepareSingle(HttpClient client, string node, TransactionRequest request)
    {
        try {
            await NotifyUI("COORDINATOR", "PREPARE_SEND", $"Do: {node}");
            var response = await client.PostAsJsonAsync($"{node}/prepare", request);
            if (response.IsSuccessStatusCode) {
                await NotifyUI(node, "VOTE_COMMIT", "OK");
                return true;
            }
            await NotifyUI(node, "VOTE_ABORT", "NO");
            return false;
        } catch { return false; }
    }

    // --- FAZA 2 z Symulacją Awarii ---
    private async Task SendGlobalDecisionAsync(HttpClient client, string txId, string value, bool commit)
    {
        var request = new TransactionRequest(txId, value);
        var endpoint = commit ? "commit" : "abort";
        int count = 0;

        foreach (var node in _nodes)
        {
            if (_currentErrorState == "CommitHalf" && count >= _nodes.Length / 2)
            {
                await TriggerCrash($"Symulacja Fail3: Padł w trakcie {endpoint.ToUpper()}");
                return; 
            }

            _ = client.PostAsJsonAsync($"{node}/{endpoint}", request);
            count++;
        }
    }

    // --- REKONSTRUKCJA (Recovery) ---
    private async Task RecoveryProcess()
    {
        var log = _stableStorage;
        var client = _httpClientFactory.CreateClient();
        Console.WriteLine($"[RECOVERY] ID: {log.TransactionId}, Stan: {log.State}");

        _currentErrorState = "None"; 

        switch (log.State)
        {
            case CoordinatorState.Idle:
                await NotifyUI("COORDINATOR", "INFO", "System wstał. Czysty stan.");
                break;
            case CoordinatorState.WaitingForVotes:
                await NotifyUI("COORDINATOR", "RECOVERY", "Przerwano w trakcie głosowania. ABORT.");
                await SendGlobalDecisionSafe(client, log.TransactionId, log.Value, false);
                _stableStorage.State = CoordinatorState.Idle;
                break;
            case CoordinatorState.DecidedCommit:
                await NotifyUI("COORDINATOR", "RECOVERY", "Dokańczam COMMIT...");
                await SendGlobalDecisionSafe(client, log.TransactionId, log.Value, true);
                _stableStorage.State = CoordinatorState.Idle;
                break;
            case CoordinatorState.DecidedAbort:
                await NotifyUI("COORDINATOR", "RECOVERY", "Dokańczam ABORT...");
                await SendGlobalDecisionSafe(client, log.TransactionId, log.Value, false);
                _stableStorage.State = CoordinatorState.Idle;
                break;
        }
    }

    private async Task SendGlobalDecisionSafe(HttpClient client, string txId, string value, bool commit)
    {
        var request = new TransactionRequest(txId, value);
        var endpoint = commit ? "commit" : "abort";
        var tasks = _nodes.Select(node => client.PostAsJsonAsync($"{node}/{endpoint}", request));
        await Task.WhenAll(tasks);
    }

    // --- UTILS ---
    private async Task TriggerCrash(string reason)
    {
        _currentErrorState = "Crash";
        await NotifyUI("COORDINATOR", "STATUS_CHANGE", "Crash");
        Console.WriteLine($"[CRASH] {reason}");
    }

    private bool CheckCrashPoint(string trigger)
    {
        if (_currentErrorState == trigger)
        {
            _ = TriggerCrash($"Symulacja {trigger}");
            return true;
        }
        return false;
    }

    private void SaveState(string txId, string value, CoordinatorState state)
    {
        _stableStorage = new RecoveryLog { TransactionId = txId, Value = value, State = state };
    }

    private async Task NotifyUI(string source, string status, string message)
    {
        await _hubContext.Clients.All.ReceiveUpdate(source, status, message);
    }
}