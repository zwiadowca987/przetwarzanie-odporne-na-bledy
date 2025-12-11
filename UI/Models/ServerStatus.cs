namespace UI.Models;

public class ServerStatus
{
    public bool IsOnline { get; set; }
    public bool PrepareOk { get; set; }
    public bool CommitOk { get; set; }
    public string LastValue { get; set; } = "";
    public string Error { get; set; } = "";
}
