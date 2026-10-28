using System.Text.Json;
using PrintLink.Network.Discovery;

namespace PrintLink.Tests.Network;

public class LanDiscoveryTests
{
    [Fact]
    public void LanBeaconPayload_SerializesAndDeserializes_Faithfully()
    {
        // Arrange
        var original = new LanBeaconPayload(
            Protocol: LanBeaconBroadcaster.ProtocolId,
            MachineName: "TEST-PC-SERVER",
            Port: 8270,
            OperatingMode: "Server",
            PrinterCount: 3,
            Version: "1.0.0",
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<LanBeaconPayload>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Protocol, deserialized.Protocol);
        Assert.Equal(original.MachineName, deserialized.MachineName);
        Assert.Equal(original.Port, deserialized.Port);
        Assert.Equal(original.OperatingMode, deserialized.OperatingMode);
        Assert.Equal(original.PrinterCount, deserialized.PrinterCount);
        Assert.Equal(original.Version, deserialized.Version);
    }

    [Fact]
    public void DiscoveredServer_DisplayProperties_FormatCorrectly()
    {
        var server = new DiscoveredServer(
            HostAddress: "192.168.1.100",
            Port: 8270,
            ServerName: "FINANCE-SERVER",
            OperatingMode: "Server",
            SharedPrinterCount: 4,
            Version: "1.0.0",
            LatencyMs: 12);

        Assert.Equal("192.168.1.100:8270", server.DisplayEndpoint);
        Assert.Equal("FINANCE-SERVER (192.168.1.100)", server.DisplayTitle);
        Assert.Equal(12, server.LatencyMs);
    }
}
