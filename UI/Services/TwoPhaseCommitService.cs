public class TwoPhaseCommitService
{
    private readonly HttpClient _http;
    private readonly List<string> _servers = new()
    {
        "http://localhost:5001",
        "http://localhost:5002",
        "http://localhost:5003"
    };

    public TwoPhaseCommitService(HttpClient http) => _http = http;

    public async Task StartTransaction(string value)
    {
        var votes = new List<string>();
        foreach (var s in _servers)
        {
            var res = await _http.PostAsJsonAsync($"{s}/api/TwoPc/prepare", value);
            votes.Add(await res.Content.ReadAsStringAsync());
        }

        if (votes.All(v => v == "YES"))
            foreach (var s in _servers)
                await _http.PostAsync($"{s}/api/TwoPc/commit", null);
        else
            foreach (var s in _servers)
                await _http.PostAsync($"{s}/api/TwoPc/abort", null);
    }

    public async Task<List<ServerState>> GetServersState()
    {
        var list = new List<ServerState>();
        foreach (var s in _servers)
        {
            try
            {
                var res = await _http.GetAsync($"{s}/api/TwoPc/state");
                list.Add(new ServerState { Url = s, State = await res.Content.ReadAsStringAsync() });
            }
            catch
            {
                list.Add(new ServerState { Url = s, State = "Offline" });
            }
        }
        return list;
    }
}
