using Microsoft.Extensions.Primitives;
using NATS.Net;

namespace NATS.Client.Services.Tests;

public class NatsSvcMsgExtensionsTest
{
    [Fact]
    public void IsServiceSuccess_returns_true_when_no_headers()
    {
        Msg().IsServiceSuccess().Should().BeTrue();
    }

    [Fact]
    public void IsServiceSuccess_returns_true_when_error_header_absent()
    {
        var headers = new NatsHeaders { { "X-Other", "value" } };

        Msg(headers).IsServiceSuccess().Should().BeTrue();
    }

    [Fact]
    public void IsServiceSuccess_returns_false_when_error_header_present()
    {
        var headers = new NatsHeaders
        {
            { NatsSvcConstants.ServiceErrorHeader, "boom" },
            { NatsSvcConstants.ServiceErrorCodeHeader, "500" },
        };

        Msg(headers).IsServiceSuccess().Should().BeFalse();
    }

    [Fact]
    public void IsServiceSuccess_throws_on_no_responders_by_default()
    {
        var msg = Msg(flags: NatsMsgFlags.NoResponders);

        msg.Invoking(m => m.IsServiceSuccess())
            .Should().Throw<NatsNoRespondersException>();
    }

    [Fact]
    public void IsServiceSuccess_ignores_no_responders_when_opted_out()
    {
        Msg(flags: NatsMsgFlags.NoResponders)
            .IsServiceSuccess(throwOnNoResponders: false)
            .Should().BeTrue();
    }

    [Fact]
    public void GetServiceStatus_returns_success_when_no_headers()
    {
        var status = Msg().GetServiceStatus();

        status.IsSuccess.Should().BeTrue();
        status.Code.Should().Be(0);
        status.Message.Should().BeNull();
        status.HasNoResponders.Should().BeFalse();
    }

    [Fact]
    public void GetServiceStatus_returns_code_and_message_when_present()
    {
        var headers = new NatsHeaders
        {
            { NatsSvcConstants.ServiceErrorHeader, "Division by zero" },
            { NatsSvcConstants.ServiceErrorCodeHeader, "400" },
        };

        var status = Msg(headers).GetServiceStatus();

        status.IsSuccess.Should().BeFalse();
        status.Code.Should().Be(400);
        status.Message.Should().Be("Division by zero");
        status.HasNoResponders.Should().BeFalse();
    }

    [Fact]
    public void GetServiceStatus_defaults_code_to_zero_when_code_header_missing()
    {
        var headers = new NatsHeaders { { NatsSvcConstants.ServiceErrorHeader, "no code" } };

        var status = Msg(headers).GetServiceStatus();

        status.IsSuccess.Should().BeFalse();
        status.Code.Should().Be(0);
        status.Message.Should().Be("no code");
    }

    [Fact]
    public void GetServiceStatus_defaults_code_to_zero_when_code_header_not_an_int()
    {
        var headers = new NatsHeaders
        {
            { NatsSvcConstants.ServiceErrorHeader, "bad code" },
            { NatsSvcConstants.ServiceErrorCodeHeader, "not-a-number" },
        };

        var status = Msg(headers).GetServiceStatus();

        status.IsSuccess.Should().BeFalse();
        status.Code.Should().Be(0);
        status.Message.Should().Be("bad code");
    }

    [Fact]
    public void GetServiceStatus_takes_last_value_when_header_appears_multiple_times()
    {
        var headers = new NatsHeaders
        {
            [NatsSvcConstants.ServiceErrorHeader] = new StringValues(new[] { "first", "last" }),
            [NatsSvcConstants.ServiceErrorCodeHeader] = new StringValues(new[] { "111", "222" }),
        };

        var status = Msg(headers).GetServiceStatus();

        status.Code.Should().Be(222);
        status.Message.Should().Be("last");
    }

    [Fact]
    public void GetServiceStatus_throws_on_no_responders_by_default()
    {
        var msg = Msg(flags: NatsMsgFlags.NoResponders);

        msg.Invoking(m => m.GetServiceStatus())
            .Should().Throw<NatsNoRespondersException>();
    }

    [Fact]
    public void GetServiceStatus_returns_no_responders_status_when_opted_out()
    {
        var status = Msg(flags: NatsMsgFlags.NoResponders).GetServiceStatus(throwOnNoResponders: false);

        status.IsSuccess.Should().BeFalse();
        status.HasNoResponders.Should().BeTrue();
        status.Code.Should().Be(0);
        status.Message.Should().BeNull();
    }

    [Fact]
    public void EnsureServiceSuccess_returns_message_when_no_error()
    {
        var result = Msg(data: 42).EnsureServiceSuccess();

        result.Data.Should().Be(42);
    }

    [Fact]
    public void EnsureServiceSuccess_throws_with_code_and_message()
    {
        var headers = new NatsHeaders
        {
            { NatsSvcConstants.ServiceErrorHeader, "Division by zero" },
            { NatsSvcConstants.ServiceErrorCodeHeader, "400" },
        };
        var msg = Msg(headers);

        var ex = msg.Invoking(m => m.EnsureServiceSuccess()).Should().Throw<NatsSvcEndpointException>().Which;
        ex.Code.Should().Be(400);
        ex.Message.Should().Be("Division by zero");
    }

    [Fact]
    public void EnsureServiceSuccess_throws_on_no_responders_by_default()
    {
        var msg = Msg(flags: NatsMsgFlags.NoResponders);

        msg.Invoking(m => m.EnsureServiceSuccess())
            .Should().Throw<NatsNoRespondersException>();
    }

    [Fact]
    public void EnsureServiceSuccess_ignores_no_responders_when_opted_out()
    {
        var msg = Msg(flags: NatsMsgFlags.NoResponders);

        msg.EnsureServiceSuccess(throwOnNoResponders: false).HasNoResponders.Should().BeTrue();
    }

    [Fact]
    public void EnsureServiceSuccess_prefers_no_responders_over_service_error()
    {
        var headers = new NatsHeaders { { NatsSvcConstants.ServiceErrorHeader, "boom" } };
        var msg = Msg(headers, flags: NatsMsgFlags.NoResponders);

        msg.Invoking(m => m.EnsureServiceSuccess())
            .Should().Throw<NatsNoRespondersException>();
    }

    private static NatsMsg<int> Msg(NatsHeaders? headers = null, int data = 0, NatsMsgFlags flags = NatsMsgFlags.None)
        => new("subject", replyTo: null, size: 0, headers: headers, data: data, connection: null, flags: flags);
}
