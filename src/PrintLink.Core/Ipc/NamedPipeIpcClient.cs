using System.IO.Pipes;
using System.Text.Json;

namespace PrintLink.Core.Ipc;

public sealed class NamedPipeIpcClient
{
    private readonly string _pipeName;

    public NamedPipeIpcClient(string pipeName = IpcConstants.PipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<IpcResponse> SendRequestAsync(IpcRequest request, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using (pipeClient.ConfigureAwait(false))
        {
            await pipeClient.ConnectAsync(timeoutMs, cancellationToken).ConfigureAwait(false);

            using var reader = new StreamReader(pipeClient, leaveOpen: true);
            using var writer = new StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true };

            string requestJson = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);

            string? responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return IpcResponse.Error("Empty response from background service.");
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
            return response ?? IpcResponse.Error("Malformed response from background service.");
        }
    }
}
