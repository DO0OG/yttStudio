namespace YttStudio.Video.Tests;

public sealed class MpvCompatibilityTests
{
    [Theory]
    // SPEC §18: the gate accepts libmpv 2.0 and newer and rejects anything older.
    [InlineData(2u << 16, true)]
    [InlineData((2u << 16) | 5u, true)]
    [InlineData(3u << 16, true)]
    [InlineData(1u << 16, false)]
    [InlineData((1u << 16) | 109u, false)]
    public void IsSupportedGatesOnMajorVersion(uint version, bool expected)
    {
        Assert.Equal(expected, MpvCompatibility.IsSupported(version));
    }

    [Fact]
    public void FormatSplitsMajorAndMinor()
    {
        Assert.Equal("2.1", MpvCompatibility.Format((2u << 16) | 1u));
    }

    [Fact]
    public void UnsupportedMessageNamesBothVersionsAndPath()
    {
        string message = MpvCompatibility.DescribeUnsupported(1u << 16, "/opt/libmpv.so");

        // An actionable failure says what was found, what is required, and where it came from.
        Assert.Contains("1.0", message, StringComparison.Ordinal);
        Assert.Contains("2.0", message, StringComparison.Ordinal);
        Assert.Contains("/opt/libmpv.so", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashMetadataCarriesVersionAndPath()
    {
        // SPEC §18 requires the libmpv version to be recoverable from a crash report.
        string line = MpvCompatibility.DescribeForCrashLog(2u << 16, "/opt/libmpv.so");

        Assert.Contains("client-api=2.0", line, StringComparison.Ordinal);
        Assert.Contains("/opt/libmpv.so", line, StringComparison.Ordinal);
    }
}
