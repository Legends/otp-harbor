using System.IO.Pipes;
using System.Buffers.Binary;
using System.Text;
using TOTP.Core.Platform;

namespace TOTP.Infrastructure.Services;

public sealed class NamedPipeActivationDispatcher(string pipeName) : IActivationDispatcher
{
    private const int MaximumPayloadBytes = 32 * 1024;

    public bool TryDispatch(ApplicationActivationRequest request)
    {
        if (!request.IsSupported) return false;
        try
        {
            var payload = request.Payload is null
                ? []
                : Encoding.UTF8.GetBytes(request.Payload);
            if (payload.Length > MaximumPayloadBytes) return false;

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1000);
            Span<byte> header = stackalloc byte[6];
            header[0] = (byte)request.Version;
            header[1] = (byte)request.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(header[2..], payload.Length);
            client.Write(header);
            if (payload.Length > 0) client.Write(payload);
            client.Flush();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}

public sealed class NamedPipeActivationListener(string pipeName) : IActivationListener
{
    private const int MaximumPayloadBytes = 32 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _started;

    public void Start(Action<ApplicationActivationRequest> onActivation)
    {
        ArgumentNullException.ThrowIfNull(onActivation);
        if (_started) throw new InvalidOperationException("The activation listener can only be started once.");
        _started = true;
        _ = ListenAsync(onActivation, _lifetime.Token);
    }

    private async Task ListenAsync(
        Action<ApplicationActivationRequest> onActivation,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                var header = new byte[6];
                await server.ReadExactlyAsync(header, cancellationToken);
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
                if (payloadLength is < 0 or > MaximumPayloadBytes) continue;
                var payloadBytes = new byte[payloadLength];
                if (payloadLength > 0)
                    await server.ReadExactlyAsync(payloadBytes, cancellationToken);
                var payload = payloadLength == 0
                    ? null
                    : StrictUtf8.GetString(payloadBytes);

                var request = new ApplicationActivationRequest(
                    header[0],
                    (ApplicationActivationKind)header[1],
                    payload);
                if (request.IsSupported) onActivation(request);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or DecoderFallbackException)
            {
            }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
