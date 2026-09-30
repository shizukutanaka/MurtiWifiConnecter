using System;
using FluentAssertions;
using MWC.Core.Services;
using Xunit;

namespace MWC.Core.Tests;

public class AdapterMacResolverTests
{
    private static readonly Guid Wlan = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Match_ReturnsColonSeparatedUpperHex()
    {
        var nics = new[] { ("{11111111-2222-3333-4444-555555555555}", new byte[] { 0xaa, 0xbb, 0xcc, 0x01, 0x02, 0x03 }) };
        AdapterMacResolver.Resolve(Wlan, nics).Should().Be("AA:BB:CC:01:02:03");
    }

    [Theory]
    [InlineData("{11111111-2222-3333-4444-555555555555}")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public void Match_IsTolerantOfBracesAndCase(string nicId)
    {
        var nics = new[] { (nicId, new byte[] { 2, 0, 0, 0, 0, 1 }) };
        AdapterMacResolver.Resolve(Wlan, nics).Should().Be("02:00:00:00:00:01");
    }

    [Fact]
    public void Match_IsCaseInsensitiveForHexGuids()
    {
        var id = Guid.Parse("ABCDEF01-2222-3333-4444-555555555555");
        var nics = new[] { ("{abcdef01-2222-3333-4444-555555555555}", new byte[] { 2, 0, 0, 0, 0, 1 }) };
        AdapterMacResolver.Resolve(id, nics).Should().Be("02:00:00:00:00:01");
    }

    [Fact]
    public void NoMatchingNic_ReturnsNull()
    {
        var nics = new[] { ("{99999999-2222-3333-4444-555555555555}", new byte[] { 1, 2, 3, 4, 5, 6 }) };
        AdapterMacResolver.Resolve(Wlan, nics).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]   // 空アドレス
    [InlineData(5)]   // 6 オクテットでない
    [InlineData(8)]
    public void WrongLengthAddress_ReturnsNull(int len)
    {
        var nics = new[] { ("{11111111-2222-3333-4444-555555555555}", new byte[len]) };
        AdapterMacResolver.Resolve(Wlan, nics).Should().BeNull();
    }

    [Fact]
    public void NonGuidNicIds_AreIgnored_NotThrown()
    {
        var nics = new[] { ("eth0", new byte[] { 1, 2, 3, 4, 5, 6 }),
                           ("{11111111-2222-3333-4444-555555555555}", new byte[] { 9, 9, 9, 9, 9, 9 }) };
        AdapterMacResolver.Resolve(Wlan, nics).Should().Be("09:09:09:09:09:09");
    }
}
