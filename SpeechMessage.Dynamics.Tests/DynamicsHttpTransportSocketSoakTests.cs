using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 以作業系統真實 loopback TCP socket 驗證 Dynamics HTTP transport 的連線上限與排空行為。
/// 測試伺服器刻意在每個回應後關閉連線，製造高頻 socket churn；即使如此，同時連線數仍不得超過
/// SocketsHttpHandler.MaxConnectionsPerServer，transport Dispose 後也不得留下 active socket 或背景 accept 工作。
/// </summary>
[Collection(Phase4ResourceSoakCollection.Name)]
public sealed class DynamicsHttpTransportSocketSoakTests
{
    /// <summary>
    /// 以 256 個要求與 32 路呼叫端併發壓測 4 條實際 TCP 連線上限，並在完成後等待伺服器端連線計數歸零。
    /// 這項測試不使用固定 sleep 判斷完成，而是等待由 socket 擁有者維護的明確 idle 條件。
    /// </summary>
    [Fact]
    public async Task Real_tcp_socket_churn_stays_bounded_and_drains_after_transport_disposal()
    {
        await using var server = new ClosingLoopbackHttpServer();
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 4,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(1),
            PooledConnectionLifetime = TimeSpan.FromSeconds(5)
        };
        var transport = new DynamicsHttpTransport(
            handler,
            NullLogger<DynamicsHttpTransport>.Instance,
            disposeHandler: true);

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, 256),
                new ParallelOptions { MaxDegreeOfParallelism = 32 },
                async (_, cancellationToken) =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, server.Endpoint)
                    {
                        Headers = { ConnectionClose = true }
                    };
                    using var response = await transport.SendAsync(request, cancellationToken);
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    (await response.Content.ReadAsStringAsync(cancellationToken)).Should().Be("{}");
                }).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await transport.DisposeAsync();
        }

        await server.WaitForIdleAsync(TimeSpan.FromSeconds(5));
        server.AcceptedConnections.Should().Be(256);
        server.MaximumActiveConnections.Should().BeLessThanOrEqualTo(4,
            "SocketsHttpHandler 必須以 MaxConnectionsPerServer 對真實 TCP 連線施加回壓");
        server.ActiveConnections.Should().Be(0,
            "所有 client、NetworkStream 與伺服器連線工作都必須在 transport 排空後結束");
    }

    /// <summary>
    /// 最小化的 loopback HTTP/1.1 伺服器；每個連線只處理一個要求並回傳 Connection: close。
    /// 此型別擁有 TcpListener、accept loop、每條連線工作與租用緩衝區，DisposeAsync 會依序停止 accept、
    /// await 所有已接受工作並釋放 CancellationTokenSource，避免測試本身製造 socket 或 Task 洩漏。
    /// </summary>
    private sealed class ClosingLoopbackHttpServer : IAsyncDisposable
    {
        private static readonly byte[] ResponseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{}");

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopCts = new();
        private readonly ConcurrentBag<Task> _connectionTasks = new();
        private readonly Task _acceptTask;
        private int _acceptedConnections;
        private int _activeConnections;
        private int _maximumActiveConnections;

        public ClosingLoopbackHttpServer()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Endpoint = new Uri($"http://127.0.0.1:{endpoint.Port}/WhoAmI");
            _acceptTask = AcceptLoopAsync(_stopCts.Token);
        }

        public Uri Endpoint { get; }
        public int AcceptedConnections => Volatile.Read(ref _acceptedConnections);
        public int ActiveConnections => Volatile.Read(ref _activeConnections);
        public int MaximumActiveConnections => Volatile.Read(ref _maximumActiveConnections);

        public async Task WaitForIdleAsync(TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (ActiveConnections != 0 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            ActiveConnections.Should().Be(0, "連線必須在宣告的有界等待時間內排空");
        }

        public async ValueTask DisposeAsync()
        {
            _stopCts.Cancel();
            _listener.Stop();
            await _acceptTask.ConfigureAwait(false);
            await Task.WhenAll(_connectionTasks.ToArray()).ConfigureAwait(false);
            _stopCts.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _acceptedConnections);
                    _connectionTasks.Add(HandleConnectionAsync(client, cancellationToken));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // DisposeAsync 擁有的正常停止路徑。
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                // Windows 在 listener.Stop 後可能以 SocketException 結束 pending accept；仍屬正常停止路徑。
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                // TcpListener 已由擁有者停止，不再接受新連線。
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeConnections);
            ObserveMaximum(active);
            var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
            try
            {
                using (client)
                await using (var stream = client.GetStream())
                {
                    await ReadHeadersAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(ResponseBytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Array.Clear(buffer, 0, buffer.Length);
                ArrayPool<byte>.Shared.Return(buffer);
                Interlocked.Decrement(ref _activeConnections);
            }
        }

        private static async Task ReadHeadersAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, buffer.Length - totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("Client closed before sending complete HTTP headers.");
                }

                totalRead += read;
                if (ContainsHeaderTerminator(buffer.AsSpan(0, totalRead)))
                {
                    return;
                }
            }

            throw new InvalidDataException("Loopback request headers exceeded the 8 KiB test limit.");
        }

        private static bool ContainsHeaderTerminator(ReadOnlySpan<byte> value)
        {
            for (var index = 3; index < value.Length; index++)
            {
                if (value[index - 3] == (byte)'\r' &&
                    value[index - 2] == (byte)'\n' &&
                    value[index - 1] == (byte)'\r' &&
                    value[index] == (byte)'\n')
                {
                    return true;
                }
            }

            return false;
        }

        private void ObserveMaximum(int active)
        {
            var observed = Volatile.Read(ref _maximumActiveConnections);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maximumActiveConnections, active, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }
}
