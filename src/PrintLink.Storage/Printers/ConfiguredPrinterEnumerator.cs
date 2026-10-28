using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Storage.Repositories;

namespace PrintLink.Storage.Printers;

/// <summary>
/// Decorator around IPrinterEnumerator that merges printer sharing configurations
/// (e.g. Printer:Shared:{name} and Printer:ShareName:{name}) stored persistently in SQLite.
/// </summary>
public sealed class ConfiguredPrinterEnumerator : IPrinterEnumerator
{
    private readonly IPrinterEnumerator _inner;
    private readonly IConfigRepository _configRepo;

    public ConfiguredPrinterEnumerator(IPrinterEnumerator inner, IConfigRepository configRepo)
    {
        _inner = inner;
        _configRepo = configRepo;
    }

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        var rawPrinters = _inner.GetPrinters();
        Dictionary<string, string> configs;

        try
        {
            configs = _configRepo.GetValuesByPrefix("Printer:");
        }
        catch
        {
            configs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new List<PrinterInfo>(rawPrinters.Count);
        foreach (var p in rawPrinters)
        {
            bool isShared = p.IsShared;
            string? shareName = p.ShareName;

            if (configs.TryGetValue($"Printer:Shared:{p.Name}", out var storedShared))
            {
                if (string.Equals(storedShared, "true", StringComparison.OrdinalIgnoreCase))
                {
                    isShared = true;
                }
                else if (string.Equals(storedShared, "false", StringComparison.OrdinalIgnoreCase))
                {
                    isShared = false;
                }
            }

            if (configs.TryGetValue($"Printer:ShareName:{p.Name}", out var storedShareName) && !string.IsNullOrWhiteSpace(storedShareName))
            {
                shareName = storedShareName;
            }
            else if (isShared && string.IsNullOrWhiteSpace(shareName))
            {
                shareName = p.Name.Replace(" ", "_");
            }

            result.Add(p with
            {
                IsShared = isShared,
                ShareName = shareName
            });
        }

        return result;
    }

    public PrinterInfo? GetPrinter(string name)
    {
        return GetPrinters().FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.ShareName, name, StringComparison.OrdinalIgnoreCase));
    }
}
