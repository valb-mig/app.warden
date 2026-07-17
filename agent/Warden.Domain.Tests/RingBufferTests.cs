using Xunit;

namespace Warden.Domain.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void TailReturnsAllWhenNExceedsSize()
    {
        var buffer = new RingBuffer(maxLen: 10);
        buffer.Append("a");
        buffer.Append("b");

        Assert.Equal(["a", "b"], buffer.Tail(5));
    }

    [Fact]
    public void TailReturnsLastN()
    {
        var buffer = new RingBuffer(maxLen: 10);
        foreach (var line in new[] { "a", "b", "c", "d" }) buffer.Append(line);

        Assert.Equal(["c", "d"], buffer.Tail(2));
    }

    [Fact]
    public void MaxLenEvictsOldest()
    {
        var buffer = new RingBuffer(maxLen: 2);
        buffer.Append("a");
        buffer.Append("b");
        buffer.Append("c");

        Assert.Equal(["b", "c"], buffer.Tail(10));
    }
}
