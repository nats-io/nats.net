namespace NATS.Client.Core.Tests;

public class NatsConnectionPoolTest
{
    [Fact]
    public async Task ConnectionsShouldBeNonDisposable()
    {
        // Arrange
        NatsConnectionPool pool = new(1);

        // Act
        await using (var con = pool.GetConnection())
        {
        }

        // Assert
        var con2 = (NatsConnection)pool.GetConnection();
        con2.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectionsShouldBeDisposedWhenPoolIsDisposed()
    {
        // Arrange
        NatsConnectionPool pool = new(1);

        var con = (NatsConnection)pool.GetConnection();

        // Act
        await pool.DisposeAsync();

        // Assert
        con.IsDisposed.Should().BeTrue();
    }
}
