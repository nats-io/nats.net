// using System.Net;
// using System.Net.Sockets;
// using System.Net.WebSockets;
// using System.Text;
// using Microsoft.AspNetCore;
// using Microsoft.AspNetCore.Builder;
// using Microsoft.AspNetCore.Hosting;
// using Microsoft.AspNetCore.Http;
// using Microsoft.Extensions.Logging;
//
// namespace NATS.Client.TestUtilities;
//
// public class WebSocketMockServer : IAsyncDisposable
// {
//     private readonly string _natsServerUrl;
//     private readonly Action<string> _logger;
//     private readonly CancellationTokenSource _cts;
//     private readonly Task _wsServerTask;
//
//     public WebSocketMockServer(
//         string natsServerUrl,
//         Func<HttpContext, bool> connectHandler,
//         Action<string> logger,
//         CancellationToken cancellationToken = default)
//     {
//         _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
//         cancellationToken = _cts.Token;
//         _natsServerUrl = natsServerUrl;
//         _logger = logger;
//         WebSocketPort = 5004;
//
//         _wsServerTask = RunWsServer(connectHandler, cancellationToken);
//     }
//
//     public int WebSocketPort { get; }
//
//     public string WebSocketUrl => $"ws://127.0.0.1:{WebSocketPort}";
//
//     public async ValueTask DisposeAsync()
//     {
//         _cts.Cancel();
//
//         try
//         {
//             await _wsServerTask.WaitAsync(TimeSpan.FromSeconds(10));
//         }
//         catch (TimeoutException)
//         {
//         }
//         catch (OperationCanceledException)
//         {
//         }
//         catch (SocketException)
//         {
//         }
//         catch (IOException)
//         {
//         }
//     }
//
//     private Task RunWsServer(Func<HttpContext, bool> connectHandler, CancellationToken ct)
//     {
//         var wsServerTask = WebHost.CreateDefaultBuilder()
//             .SuppressStatusMessages(true)
//             .ConfigureLogging(logging => logging.ClearProviders())
//             .ConfigureKestrel(options => options.ListenLocalhost(WebSocketPort)) // unfortunately need to hard-code WebSocket port because ListenLocalhost() doesn't support picking a dynamic port
//             .Configure(app => app.UseWebSockets().Run(async context =>
//             {
//                 _logger($"[WS] Received WebSocketRequest with authorization header: {context.Request.Headers.Authorization}");
//
//                 if (!connectHandler(context))
//                     return;
//
//                 if (context.WebSockets.IsWebSocketRequest)
//                 {
//                     using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
//                     await HandleRequestResponse(webSocket, ct);
//                 }
//             }))
//             .Build().RunAsync(ct);
//
//         return wsServerTask;
//     }
//
//     private async Task HandleRequestResponse(WebSocket webSocket, CancellationToken ct)
//     {
//         var wsRequestBuffer = new byte[1024 * 4];
//         using TcpClient tcpClient = new();
//         var endpoint = IPEndPoint.Parse(_natsServerUrl);
//         await tcpClient.ConnectAsync(endpoint);
//         await using var stream = tcpClient.GetStream();
//
//         // send responses received from NATS mock server back to WebSocket client
//         var respondBackTask = Task.Run(async () =>
//         {
//             try
//             {
//                 var tcpResponseBuffer = new byte[1024 * 4];
//
//                 while (!ct.IsCancellationRequested)
//                 {
//                     var received = await stream.ReadAsync(tcpResponseBuffer, ct);
//
//                     var message = Encoding.UTF8.GetString(tcpResponseBuffer, 0, received);
//
//                     await webSocket.SendAsync(
//                         new ArraySegment<byte>(tcpResponseBuffer, 0, received),
//                         WebSocketMessageType.Binary,
//                         true,
//                         ct);
//                 }
//             }
//             catch (Exception e)
//             {
//                 // if our TCP connection with the NATS mock server breaks then close the WebSocket too.
//                 _logger($"[WS] Exception in response task: {e.Message}");
//                 webSocket.Abort();
//             }
//         });
//
//         // forward received message via TCP to NATS mock server
//         var receiveResult = await webSocket.ReceiveAsync(
//             new ArraySegment<byte>(wsRequestBuffer), ct);
//
//         while (!receiveResult.CloseStatus.HasValue)
//         {
//             await stream.WriteAsync(wsRequestBuffer, 0, receiveResult.Count, ct);
//
//             receiveResult = await webSocket.ReceiveAsync(
//                 new ArraySegment<byte>(wsRequestBuffer), ct);
//         }
//
//         await webSocket.CloseAsync(
//             receiveResult.CloseStatus.Value,
//             receiveResult.CloseStatusDescription,
//             CancellationToken.None);
//     }
// }
