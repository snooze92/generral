using System.Net;
using Generral.Core.Extensions;

namespace Generral.Core.Test.Extensions;

public class IpAddressExtensionsTests
{
    [TestCase("127.0.0.1", true)]
    [TestCase("::1", true)]
    [TestCase("10.1.2.3", true)]
    [TestCase("172.16.0.1", true)]
    [TestCase("172.31.255.255", true)]
    [TestCase("172.32.0.1", false)]
    [TestCase("192.168.0.5", true)]
    [TestCase("169.254.1.1", true)]
    [TestCase("::ffff:192.168.0.5", true)]
    [TestCase("fe80::1", true)]
    [TestCase("fd00::1", true)]
    [TestCase("8.8.8.8", false)]
    [TestCase("2001:4860:4860::8888", false)]
    public void IsLocalAddress(string address, bool expected)
    {
        Assert.That(IPAddress.Parse(address).IsLocalAddress(), Is.EqualTo(expected));
    }
}
