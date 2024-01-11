// origin: https://github.com/microsoft/vs-threading/blob/9065e6e4b5593e6ed6e3ff0a9159d4e2765430d6/src/Microsoft.VisualStudio.Threading/AsyncEventHandler.cs
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

namespace NATS.Client.Core;

/// <summary>
/// An asynchronous event handler.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="args">Event arguments.</param>
/// <returns>A task whose completion signals handling is finished.</returns>
public delegate Task AsyncEventHandler(object? sender, EventArgs args);

/// <summary>
/// An asynchronous event handler.
/// </summary>
/// <typeparam name="TEventArgs">The type of event arguments.</typeparam>
/// <param name="sender">The sender of the event.</param>
/// <param name="args">Event arguments.</param>
/// <returns>A task whose completion signals handling is finished.</returns>
public delegate Task AsyncEventHandler<in TEventArgs>(object? sender, TEventArgs args);
