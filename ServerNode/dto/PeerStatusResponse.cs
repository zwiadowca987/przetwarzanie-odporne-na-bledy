namespace ServerNode.dto;

public record PeerStatusResponse(string NodeId, TransactionState State, string? Value);