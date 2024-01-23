namespace NATS.Client.Core;

/// <summary>
/// An asynchronous event handler.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="args">Event arguments.</param>
/// <returns>A value task whose completion signals handling is finished.</returns>
public delegate ValueTask AsyncEventHandler(object? sender, EventArgs args);

/// <summary>
/// An asynchronous event handler.
/// </summary>
/// <typeparam name="TEventArgs">The type of event arguments.</typeparam>
/// <param name="sender">The sender of the event.</param>
/// <param name="args">Event arguments.</param>
/// <returns>A value task whose completion signals handling is finished.</returns>
public delegate ValueTask AsyncEventHandler<in TEventArgs>(object? sender, TEventArgs args);
