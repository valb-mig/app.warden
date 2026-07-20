using Warden.Domain.Events;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_bus.py`.</summary>
public sealed class EventBusTests
{
    [Fact]
    public void PublishCallsAllSubscribers()
    {
        var received = new List<Event>();
        var bus = new EventBus();
        bus.Subscribe(received.Add);
        bus.Subscribe(e => received.Add(e));

        var @event = new Event("demo", EventType.Started);
        bus.Publish(@event);

        Assert.Equal([@event, @event], received);
    }
}
