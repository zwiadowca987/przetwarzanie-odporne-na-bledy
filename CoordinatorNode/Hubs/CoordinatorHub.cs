using Microsoft.AspNetCore.SignalR;

namespace CoordinatorNode.Hubs;

public interface ITransactionClient
{
    Task ReceiveStateUpdate(string nodeId, string status, string details);
    Task ReceiveUpdate(string source, string status, string message);
}

public class CoordinatorHub : Hub<ITransactionClient>
{
    public async Task SubscribeToTransaction(string transactionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, transactionId);
    }
    public async Task BroadcastNodeStatus(string nodeId, string status, string message)
    {
        // Kiedy Klient wywoła tę metodę, Koordynator przekaże ją do UI
        await Clients.All.ReceiveUpdate(nodeId, status, message);
    }
}