using System.Net;
using Warden.Agent.Transport;
using Xunit;

namespace Warden.Agent.Tests;

public sealed class BindGuardTests
{
    private static readonly IPEndPoint Expected = new(IPAddress.Parse("100.64.1.2"), 8420);

    [Fact]
    public void PassesWhenSingleAddressMatchesExactly()
    {
        BindGuard.AssertSingleExpectedAddress(["http://100.64.1.2:8420"], Expected);
    }

    [Fact]
    public void ThrowsWhenMoreThanOneAddress()
    {
        var ex = Assert.Throws<BindAssertionException>(() =>
            BindGuard.AssertSingleExpectedAddress(
                ["http://100.64.1.2:8420", "http://0.0.0.0:8420"], Expected));

        Assert.Contains("achou 2", ex.Message);
    }

    [Fact]
    public void ThrowsWhenNoAddress()
    {
        Assert.Throws<BindAssertionException>(() =>
            BindGuard.AssertSingleExpectedAddress([], Expected));
    }

    [Fact]
    public void ThrowsWhenAddressIsWildcard()
    {
        Assert.Throws<BindAssertionException>(() =>
            BindGuard.AssertSingleExpectedAddress(["http://0.0.0.0:8420"], Expected));
    }

    [Fact]
    public void ThrowsWhenPortDiffers()
    {
        Assert.Throws<BindAssertionException>(() =>
            BindGuard.AssertSingleExpectedAddress(["http://100.64.1.2:9999"], Expected));
    }
}
