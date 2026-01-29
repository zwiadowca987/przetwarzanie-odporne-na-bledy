namespace CoordinatorNode.DTO;

public enum CoordinatorState
{
    Idle = 0,
    WaitingForVotes = 1, // Wysłano VR (Prepare), czekamy
    DecidedCommit = 2,   // Podjęto decyzję Commit
    DecidedAbort = 3     // Podjęto decyzję Abort
}