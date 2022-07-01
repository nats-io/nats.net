using System.Collections;

namespace AlterNats.Tests;

public class TransportTypeGenerator : IEnumerable<object[]>
{
    private readonly List<object[]> _data = new()
    {
        new object[] { TransportType.Tcp },
        new object[] { TransportType.WebSocket }
    };

    public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
