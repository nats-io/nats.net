// origin: https://github.com/microsoft/vs-threading/blob/9065e6e4b5593e6ed6e3ff0a9159d4e2765430d6/src/Microsoft.VisualStudio.Threading/TplExtensions.cs
// license: MIT
//
// Microsoft.VisualStudio.Threading
// Copyright (c) Microsoft Corporation
// All rights reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
//     to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.

namespace NATS.Client.Core.Internal;

internal static class AsyncEventExtensions
{
    /// <summary>
    /// Invokes asynchronous event handlers, returning a task that completes when all event handlers have been invoked.
    /// Each handler is fully executed (including continuations) before the next handler in the list is invoked.
    /// </summary>
    /// <param name="handlers">The event handlers.  May be <see langword="null" />.</param>
    /// <param name="sender">The event source.</param>
    /// <param name="args">The event argument.</param>
    /// <returns>The task that completes when all handlers have completed.</returns>
    /// <exception cref="AggregateException">Thrown if any handlers fail. It contains a collection of all failures.</exception>
    public static async Task InvokeAsync(this AsyncEventHandler? handlers, object? sender, EventArgs args)
    {
        if (handlers != null)
        {
            var individualHandlers = handlers.GetInvocationList();
            List<Exception>? exceptions = null;
            foreach (var asyncHandler in individualHandlers)
            {
                var handler = (AsyncEventHandler)asyncHandler;
                try
                {
                    await handler(sender, args).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    exceptions ??= new List<Exception>(2);
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null)
            {
                throw new AggregateException(exceptions);
            }
        }
    }

    /// <summary>
    /// Invokes asynchronous event handlers, returning a task that completes when all event handlers have been invoked.
    /// Each handler is fully executed (including continuations) before the next handler in the list is invoked.
    /// </summary>
    /// <typeparam name="TEventArgs">The type of argument passed to each handler.</typeparam>
    /// <param name="handlers">The event handlers.  May be <see langword="null" />.</param>
    /// <param name="sender">The event source.</param>
    /// <param name="args">The event argument.</param>
    /// <returns>The task that completes when all handlers have completed.  The task is faulted if any handlers throw an exception.</returns>
    /// <exception cref="AggregateException">Thrown if any handlers fail. It contains a collection of all failures.</exception>
    public static async Task InvokeAsync<TEventArgs>(this AsyncEventHandler<TEventArgs>? handlers, object? sender, TEventArgs args)
    {
        if (handlers != null)
        {
            var individualHandlers = handlers.GetInvocationList();
            List<Exception>? exceptions = null;
            foreach (var asyncHandler in individualHandlers)
            {
                var handler = (AsyncEventHandler<TEventArgs>)asyncHandler;
                try
                {
                    await handler(sender, args).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    exceptions ??= new List<Exception>(2);
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null)
            {
                throw new AggregateException(exceptions);
            }
        }
    }
}
