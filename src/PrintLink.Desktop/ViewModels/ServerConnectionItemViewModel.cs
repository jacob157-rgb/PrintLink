namespace PrintLink.Desktop.ViewModels;

public sealed class ServerConnectionItemViewModel : ViewModelBase
{
    private string _status = "Untested";
    private long _latencyMs;

    public string Host { get; }
    public int Port { get; }
    public string? Token { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public long LatencyMs
    {
        get => _latencyMs;
        set => SetProperty(ref _latencyMs, value);
    }

    public string Endpoint => $"{Host}:{Port}";

    public ServerConnectionItemViewModel(string host, int port, string? token)
    {
        Host = host;
        Port = port;
        Token = token;
    }
}
