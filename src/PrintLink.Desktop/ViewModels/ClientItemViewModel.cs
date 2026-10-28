using PrintLink.Storage.Repositories;

namespace PrintLink.Desktop.ViewModels;

public sealed class ClientItemViewModel : ViewModelBase
{
    public string ClientId { get; }
    public string FriendlyName { get; }
    public bool IsRevoked { get; private set; }
    public string CreatedAtText { get; }
    public string LastSeenText { get; }

    public string StatusText => IsRevoked ? "Revoked" : "Authorized";

    public ClientItemViewModel(AuthorizedClientRecord record)
    {
        ClientId = record.ClientId;
        FriendlyName = record.FriendlyName;
        IsRevoked = record.IsRevoked;
        CreatedAtText = record.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        LastSeenText = record.LastSeenAt.HasValue
            ? record.LastSeenAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Never";
    }

    public void MarkRevoked()
    {
        IsRevoked = true;
        OnPropertyChanged(nameof(IsRevoked));
        OnPropertyChanged(nameof(StatusText));
    }
}
