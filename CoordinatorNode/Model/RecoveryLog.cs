using CoordinatorNode.DTO;

namespace CoordinatorNode.Model;

public class RecoveryLog
{
    public string TransactionId { get; set; } = "";
    public string Value { get; set; } = "";
    public CoordinatorState State { get; set; } = CoordinatorState.Idle;
}