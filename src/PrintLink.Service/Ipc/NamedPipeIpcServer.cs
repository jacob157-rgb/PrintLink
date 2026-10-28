using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrintLink.Core.Ipc;

namespace PrintLink.Service.Ipc;

public interface IIpcRequestHandler
{
    Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken);
}

public sealed class NamedPipeIpcServer : IAsyncDisposable
{
    private readonly IIpcRequestHandler _handler;
    private readonly ILogger<NamedPipeIpcServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    private readonly string _pipeName;

    public NamedPipeIpcServer(IIpcRequestHandler handler, ILogger<NamedPipeIpcServer> logger, string pipeName = IpcConstants.PipeName)
    {
        _handler = handler;
        _logger = logger;
        _pipeName = pipeName;
    }

    public void Start()
    {
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IPC Named Pipe Server listening on \\\\.\\pipe\\{PipeName}", _pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await using (pipeServer.ConfigureAwait(false))
                {
                    await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    using var reader = new StreamReader(pipeServer, leaveOpen: true);
                    using var writer = new StreamWriter(pipeServer, leaveOpen: true) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var request = JsonSerializer.Deserialize<IpcRequest>(line);
                        IpcResponse response;

                        if (request != null)
                        {
                            response = await _handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            response = IpcResponse.Error("Invalid JSON request");
                        }

                        string responseJson = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing IPC pipe request.");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_listenTask != null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _cts.Dispose();
    }
}
