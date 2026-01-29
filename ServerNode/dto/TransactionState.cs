namespace ServerNode.dto;

public enum TransactionState
{
    // Nieznana/Zaniechana 
    Unknown = 0, 
    // TAK -> czekam na decyzję
    Prepared = 1, 
    // Potwierdzony COMMIT
    Committed = 2, 
    // Potwierdzony ABORT
    Aborted = 3
}