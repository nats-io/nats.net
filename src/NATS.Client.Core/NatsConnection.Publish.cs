// using System.Buffers;
// using NATS.Client.Core.Commands;
//
// namespace NATS.Client.Core;
//
// public partial class NatsConnection : INatsCommand
// {
//     // New API doesn't have batching concept
//
//     // public ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values, CancellationToken cancellationToken = default)
//     // {
//     //     if (ConnectionState == NatsConnectionState.Open)
//     //     {
//     //         var command = AsyncPublishBatchCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), values, Options.Serializer);
//     //         if (TryEnqueueCommand(command))
//     //         {
//     //             return command.AsValueTask();
//     //         }
//     //         else
//     //         {
//     //             return EnqueueAndAwaitCommandAsync(command);
//     //         }
//     //     }
//     //     else
//     //     {
//     //         return WithConnectAsync(values, cancellationToken, static (self, v, token) =>
//     //         {
//     //             var command = AsyncPublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(token), v, self.Options.Serializer);
//     //             return self.EnqueueAndAwaitCommandAsync(command);
//     //         });
//     //     }
//     // }
//     //
//     // public ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values, CancellationToken cancellationToken = default)
//     // {
//     //     if (ConnectionState == NatsConnectionState.Open)
//     //     {
//     //         var command = AsyncPublishBatchCommand<T>.Create(_pool, GetCommandTimer(cancellationToken), values, Options.Serializer);
//     //         if (TryEnqueueCommand(command))
//     //         {
//     //             return command.AsValueTask();
//     //         }
//     //         else
//     //         {
//     //             return EnqueueAndAwaitCommandAsync(command);
//     //         }
//     //     }
//     //     else
//     //     {
//     //         return WithConnectAsync(values, cancellationToken, static (self, values, token) =>
//     //         {
//     //             var command = AsyncPublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(token), values, self.Options.Serializer);
//     //             return self.EnqueueAndAwaitCommandAsync(command);
//     //         });
//     //     }
//     // }
//     //
//     // public void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values)
//     // {
//     //     if (ConnectionState == NatsConnectionState.Open)
//     //     {
//     //         var command = PublishBatchCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), values, Options.Serializer);
//     //         EnqueueCommandSync(command);
//     //     }
//     //     else
//     //     {
//     //         WithConnect(values, static (self, v) =>
//     //         {
//     //             var command = PublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), v, self.Options.Serializer);
//     //             self.EnqueueCommandSync(command);
//     //         });
//     //     }
//     // }
//     //
//     // public void PostPublishBatch<T>(IEnumerable<(string, T?)> values)
//     // {
//     //     if (ConnectionState == NatsConnectionState.Open)
//     //     {
//     //         var command = PublishBatchCommand<T>.Create(_pool, GetCommandTimer(CancellationToken.None), values, Options.Serializer);
//     //         EnqueueCommandSync(command);
//     //     }
//     //     else
//     //     {
//     //         WithConnect(values, static (self, v) =>
//     //         {
//     //             var command = PublishBatchCommand<T>.Create(self._pool, self.GetCommandTimer(CancellationToken.None), v, self.Options.Serializer);
//     //             self.EnqueueCommandSync(command);
//     //         });
//     //     }
//     // }
//     //
// }
