namespace ServerNode.dto;

public record VoteResponse(string NodeId, string TransactionId, bool VoteCommit, string Reason);