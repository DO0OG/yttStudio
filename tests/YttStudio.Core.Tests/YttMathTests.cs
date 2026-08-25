using YttStudio.Core;

namespace YttStudio.Core.Tests;

public sealed class YttMathTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(200)]
    [InlineData(333)]
    public void FontScaleRoundTripIsExact(int realPercent)
    {
        int yttScale = YttMath.ToYttFontScale(realPercent);
        Assert.Equal(realPercent, YttMath.ToRealFontPercent(yttScale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void CoordinateRoundTripStaysWithinOnePixel(int yttCoordinate)
    {
        double pixel = YttMath.ToPixelCoordinate(yttCoordinate, YttConstants.ReferenceWidth);
        int roundTrippedCoordinate = YttMath.ToYttCoordinate(pixel, YttConstants.ReferenceWidth);
        double roundTrippedPixel = YttMath.ToPixelCoordinate(roundTrippedCoordinate, YttConstants.ReferenceWidth);

        Assert.InRange(Math.Abs(pixel - roundTrippedPixel), 0, 1);
    }
}
