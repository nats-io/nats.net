# Queue Groups

When subscribers register themselves to receive messages from a publisher,
the 1:N fan-out pattern of messaging ensures that any message sent by a publisher,
reaches all subscribers that have registered. NATS provides an additional feature
named "queue", which allows subscribers to register themselves as part of a queue.
Subscribers that are part of a queue, form the "queue group".

Code below demonstrates multiple subscriptions on the same queue group,
receiving messages randomly distributed among them. This example also shows
how queue groups can be used to load balance responders:

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Core/QueuePage.cs#queue)]

Output should look similar to this:

```text
[1] Received request: 0
Reply: 'Answer is: 0'
[2] Received request: 1
Reply: 'Answer is: 2'
[2] Received request: 2
Reply: 'Answer is: 4'
[2] Received request: 3
Reply: 'Answer is: 6'
[2] Received request: 4
Reply: 'Answer is: 8'
[0] Received request: 5
[0] Received request: 7
Reply: 'Answer is: 14'
[1] Received request: 8
Reply: 'Answer is: 16'
[1] Received request: 9
Reply: 'Answer is: 18'
Stopping...
[2] Done
[0] Done
[1] Done
All done
```

In the example above, three subscribers are created, each of which is part of the same queue group.
The publisher sends 10 messages, which are randomly distributed among the subscribers.
Each subscriber processes the message and sends a response back to the publisher.

The queue group feature is useful when you want to distribute messages among a group of subscribers
in a load-balanced manner.
Combined with other messaging patterns, such as scatter-gather,
queue groups can be used to create highly scalable and fault-tolerant systems.
