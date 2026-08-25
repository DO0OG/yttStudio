namespace YttStudio.Video.Tests;

public sealed class MpvCompatibilityTests
{
    [Theory]
    // libmpv 2.0 이상만 통과시키고 그보다 낮으면 거부한다.
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

        // 조치 가능한 실패 메시지는 발견한 값과 요구 값과 출처를 함께 알려준다.
        Assert.Contains("1.0", message, StringComparison.Ordinal);
        Assert.Contains("2.0", message, StringComparison.Ordinal);
        Assert.Contains("/opt/libmpv.so", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashMetadataCarriesVersionAndPath()
    {
        // 크래시 보고서에서 libmpv 버전을 되짚을 수 있어야 한다.
        string line = MpvCompatibility.DescribeForCrashLog(2u << 16, "/opt/libmpv.so");

        Assert.Contains("client-api=2.0", line, StringComparison.Ordinal);
        Assert.Contains("/opt/libmpv.so", line, StringComparison.Ordinal);
    }
}
