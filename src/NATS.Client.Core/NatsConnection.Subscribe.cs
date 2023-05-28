// using Microsoft.Extensions.Logging;
//
// namespace NATS.Client.Core;
//
// public partial class NatsConnection : INatsCommand
// {
//     public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default)
//     {
//         return SubscribeRequestAsync(key.Key, requestHandler, cancellationToken);
//     }
//
//     public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddRequestHandlerAsync(key, requestHandler, cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, requestHandler, cancellationToken, static (self, key, requestHandler, token) =>
//             {
//                 return self._subscriptionManager.AddRequestHandlerAsync(key, requestHandler, token);
//             });
//         }
//     }
//
//     public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default)
//     {
//         return SubscribeRequestAsync(key.Key, requestHandler, cancellationToken);
//     }
//
//     public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddRequestHandlerAsync(key, requestHandler, cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, requestHandler, cancellationToken, static (self, key, requestHandler, token) =>
//             {
//                 return self._subscriptionManager.AddRequestHandlerAsync(key, requestHandler, token);
//             });
//         }
//     }
//
//     // public ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler, CancellationToken cancellationToken = default)
//     // {
//     //     return SubscribeAsync<byte[]>(key, _ => handler(), cancellationToken);
//     // }
//     //
//     // public ValueTask<IDisposable> SubscribeAsync(string key, Action handler, CancellationToken cancellationToken = default)
//     // {
//     //     return SubscribeAsync<byte[]>(key, _ => handler(), cancellationToken);
//     // }
//     //
//     // public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler, CancellationToken cancellationToken = default)
//     // {
//     //     return SubscribeAsync(key.Key, handler, cancellationToken);
//     // }
//     //
//     // public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
//     // {
//     //     return SubscribeAsync(key.Key, asyncHandler, cancellationToken);
//     // }
//     //
//     // public ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
//     // {
//     //     if (ConnectionState == NatsConnectionState.Open)
//     //     {
//     //         return _subscriptionManager.AddAsync<T>(
//     //             key,
//     //             null,
//     //             new Func<T, Task>(async x =>
//     //             {
//     //                 try
//     //                 {
//     //                     await asyncHandler(x).ConfigureAwait(false);
//     //                 }
//     //                 catch (Exception ex)
//     //                 {
//     //                     _logger.LogError(ex, "Error occured during subscribe message.");
//     //                 }
//     //             }),
//     //             cancellationToken);
//     //     }
//     //     else
//     //     {
//     //         return WithConnectAsync(key, asyncHandler, cancellationToken, static (self, key, asyncHandler, token) =>
//     //         {
//     //             return self._subscriptionManager.AddAsync<T>(
//     //                 key,
//     //                 null,
//     //                 new Func<T, Task>(async x =>
//     //                 {
//     //                     try
//     //                     {
//     //                         await asyncHandler(x).ConfigureAwait(false);
//     //                     }
//     //                     catch (Exception ex)
//     //                     {
//     //                         self._logger.LogError(ex, "Error occured during subscribe message.");
//     //                     }
//     //                 }),
//     //                 token);
//     //         });
//     //     }
//     // }
//     public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddAsync<T>(key.Key, queueGroup, handler, cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, queueGroup, handler, cancellationToken, static (self, key, queueGroup, handler, token) =>
//             {
//                 return self._subscriptionManager.AddAsync<T>(key.Key, queueGroup, handler, token);
//             });
//         }
//     }
//
//     public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddAsync<T>(key, new NatsKey(queueGroup, true), handler, cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, queueGroup, handler, cancellationToken, static (self, key, queueGroup, handler, token) =>
//             {
//                 return self._subscriptionManager.AddAsync<T>(key, new NatsKey(queueGroup, true), handler, token);
//             });
//         }
//     }
//
//     public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddAsync<T>(
//                 key.Key,
//                 queueGroup,
//                 new Func<T, Task>(async x =>
//                 {
//                     try
//                     {
//                         await asyncHandler(x).ConfigureAwait(false);
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(ex, "Error occured during subscribe message.");
//                     }
//                 }),
//                 cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, queueGroup, asyncHandler, cancellationToken, static (self, key, queueGroup, asyncHandler, token) =>
//             {
//                 return self._subscriptionManager.AddAsync<T>(
//                     key.Key,
//                     queueGroup,
//                     new Func<T, Task>(async x =>
//                     {
//                         try
//                         {
//                             await asyncHandler(x).ConfigureAwait(false);
//                         }
//                         catch (Exception ex)
//                         {
//                             self._logger.LogError(ex, "Error occured during subscribe message.");
//                         }
//                     }),
//                     token);
//             });
//         }
//     }
//
//     public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler, CancellationToken cancellationToken = default)
//     {
//         if (ConnectionState == NatsConnectionState.Open)
//         {
//             return _subscriptionManager.AddAsync<T>(
//                 key,
//                 new NatsKey(queueGroup, true),
//                 new Func<T, Task>(async x =>
//                 {
//                     try
//                     {
//                         await asyncHandler(x).ConfigureAwait(false);
//                     }
//                     catch (Exception ex)
//                     {
//                         _logger.LogError(ex, "Error occured during subscribe message.");
//                     }
//                 }),
//                 cancellationToken);
//         }
//         else
//         {
//             return WithConnectAsync(key, queueGroup, asyncHandler, cancellationToken, static (self, key, queueGroup, asyncHandler, token) =>
//             {
//                 return self._subscriptionManager.AddAsync<T>(
//                     key,
//                     new NatsKey(queueGroup, true),
//                     new Func<T, Task>(async x =>
//                     {
//                         try
//                         {
//                             await asyncHandler(x).ConfigureAwait(false);
//                         }
//                         catch (Exception ex)
//                         {
//                             self._logger.LogError(ex, "Error occured during subscribe message.");
//                         }
//                     }),
//                     token);
//             });
//         }
//     }
// }
