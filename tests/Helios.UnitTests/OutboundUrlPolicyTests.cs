using System.Net;
using Helios.Infrastructure.Webhooks;

namespace Helios.UnitTests;

public class OutboundUrlPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // cloud metadata
    [InlineData("100.64.0.1")]        // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]   // IPv4-mapped private
    [InlineData("::")]
    public void Internal_addresses_are_disallowed(string address)
    {
        Assert.True(OutboundUrlPolicy.IsDisallowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("2001:4860:4860::8888")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.False(OutboundUrlPolicy.IsDisallowed(IPAddress.Parse(address)));
    }

    [Fact]
    public void Https_public_hosts_register_and_the_development_switch_relaxes_only_what_it_says()
    {
        var strict = new OutboundUrlPolicy(allowPrivateNetworks: false);
        var development = new OutboundUrlPolicy(allowPrivateNetworks: true);

        Assert.Null(strict.ValidateForRegistration("https://hooks.example.com/helios"));
        Assert.NotNull(strict.ValidateForRegistration("http://hooks.example.com/helios"));
        Assert.Null(development.ValidateForRegistration("http://localhost:5000/hook"));

        // Credentials in a URL are refused even in development.
        Assert.NotNull(development.ValidateForRegistration("https://user:pw@hooks.example.com/"));
    }
}
