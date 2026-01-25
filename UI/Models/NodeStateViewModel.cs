namespace UI.Models;

public class NodeStateViewModel
{
    public string Name { get; set; } = "Nieznany";
    public string Type { get; set; } = "Client"; // "Coordinator" lub "Client"
    public string Url { get; set; } = "";
    
    // Stany maszyny
    public bool IsActive { get; set; } = true;
    public string CurrentErrorState { get; set; } = "Brak błędów"; 
    public string TransactionState { get; set; } = "Idle";
    public string LastValue { get; set; } = "-";
}