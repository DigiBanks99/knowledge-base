using System.Net;

using KnowledgeBase.Agent.Research.Fetch;

namespace KnowledgeBase.Agent.Tests.Research.Fetch;

public sealed class AddressClassifierTests
{
    [Test]
    [Arguments("93.184.215.14")]
    [Arguments("1.1.1.1")]
    [Arguments("172.15.255.255")]
    [Arguments("172.32.0.1")]
    [Arguments("100.63.255.255")]
    [Arguments("2606:4700:4700::1111")]
    [Arguments("::ffff:93.184.215.14")]
    [Arguments("64:ff9b::5db8:d70e")]
    [Arguments("2002:5db8:d70e::1")]
    public void AnAddressIsOnTheInternet(string address)
    {
        bool isPublic = AddressClassifier.IsPublic(IPAddress.Parse(address));

        ItShouldBeAllowed();

        void ItShouldBeAllowed() => isPublic.ShouldBeTrue();
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("127.255.255.254")]
    [Arguments("0.0.0.0")]
    [Arguments("10.0.0.5")]
    [Arguments("172.16.0.1")]
    [Arguments("172.31.255.255")]
    [Arguments("192.168.1.10")]
    [Arguments("169.254.169.254")]
    [Arguments("169.254.0.1")]
    [Arguments("100.64.0.1")]
    [Arguments("100.100.100.200")]
    [Arguments("192.0.0.170")]
    [Arguments("192.0.2.1")]
    [Arguments("198.18.0.1")]
    [Arguments("224.0.0.1")]
    [Arguments("255.255.255.255")]
    [Arguments("::1")]
    [Arguments("::")]
    [Arguments("fe80::1")]
    [Arguments("fc00::1")]
    [Arguments("fd00:ec2::254")]
    [Arguments("fec0::1")]
    [Arguments("ff02::1")]
    [Arguments("2001:db8::1")]
    [Arguments("::ffff:127.0.0.1")]
    [Arguments("::ffff:169.254.169.254")]
    [Arguments("64:ff9b::a00:1")]
    [Arguments("2002:a00:1::1")]
    [Arguments("::127.0.0.1")]
    public void AnAddressIsLocalPrivateOrSpecial(string address)
    {
        bool isPublic = AddressClassifier.IsPublic(IPAddress.Parse(address));

        ItShouldBeRefused();

        void ItShouldBeRefused() => isPublic.ShouldBeFalse();
    }
}
