namespace PrintLink.Core.Enums;

/// <summary>
/// Operational mode configured for the PrintLink instance.
/// </summary>
public enum OperatingMode
{
    /// <summary>
    /// Shares selected local Windows printers with authorized clients.
    /// </summary>
    Server = 1,

    /// <summary>
    /// Connects to remote PrintLink servers and uses authorized shared printers.
    /// </summary>
    Client = 2,

    /// <summary>
    /// Shares local printers while simultaneously accessing printers from other servers.
    /// Enforces loop detection to prevent re-sharing remote shares.
    /// </summary>
    Combined = 3
}
