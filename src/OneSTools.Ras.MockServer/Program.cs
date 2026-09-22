using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OneSTools.Ras.MockServer.Handlers;

namespace OneSTools.Ras.MockServer;

/// <summary>
/// Mock-RAS: эмулятор агента администрирования 1С по протоколу SWP.
/// Реализует Negotiate → Connect → ConnectAck handshake и отвечает на KEEP_ALIVE.
/// Используется для локального тестирования клиента без реального сервера 1С.
/// </summary>
public static class Program
{
    private const int DefaultPort = 1545;

    public static async Task Main(string[] args)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        await RunAsync(args, shutdown.Token);
    }

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(options => options.SingleLine = true)
            .SetMinimumLevel(LogLevel.Information));

        ILogger logger = loggerFactory.CreateLogger("MockRas");

        int port = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : DefaultPort;
        int maxAdminVersion = args.Length > 1 && int.TryParse(args[1], out int parsedMax) ? parsedMax : 14;

        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        logger.LogInformation("Mock-RAS запущен на 0.0.0.0:{Port} (ожидается magic {Magic}), {Service} версии 3.0..{Max}.0", port, 475223888, ClientSession.AdminServiceName, maxAdminVersion);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                logger.LogInformation("Новое подключение: {Endpoint}", client.Client.RemoteEndPoint);
                _ = HandleClientAsync(client, logger, maxAdminVersion, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
            logger.LogInformation("Mock-RAS остановлен");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, ILogger logger, int maxAdminVersion, CancellationToken cancellationToken)
    {
        try
        {
            await new ClientSession(client, logger, maxAdminVersion).RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
        }
    }
}