using System.Net;

namespace SignalRadar.Worker.Runtime;

internal static class WorkerHttp
{
    public static SocketsHttpHandler CreateHandler(int maxConnectionsPerServer)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = maxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };
    }
}
