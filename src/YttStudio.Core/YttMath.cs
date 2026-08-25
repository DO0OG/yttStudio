namespace YttStudio.Core;

/// <summary>플레이어 좌표 변환과 YTT 폰트 배율 변환을 구현한다.</summary>
public static class YttMath
{
    /// <summary>저장된 YTT 폰트 배율을 사용자에게 보이는 백분율로 변환한다.</summary>
    public static int ToRealFontPercent(int yttScale)
    {
        // [UPSTREAM] 플레이어는 저장된 배율 차이의 4분의 1만 적용한다.
        // 근거: YttDocument.GetRealFontScale(), docs/YTT-VERIFICATION.md
        return checked((int)Math.Round(
            (1 + (((yttScale / 100.0) - 1) / YttConstants.FontScaleDivisor)) * 100,
            MidpointRounding.ToEven));
    }

    /// <summary>사용자에게 보이는 폰트 백분율을 저장용 YTT 배율로 변환한다.</summary>
    public static int ToYttFontScale(int realPercent)
    {
        // [UPSTREAM] YTT 는 하한이 75% 이고 상한은 없다.
        // 근거: YttDocument.GetYouTubeFontScale(), docs/YTT-VERIFICATION.md
        double realScale = Math.Max(realPercent, 75) / 100.0;
        double yttScale = Math.Max(1 + ((realScale - 1) * YttConstants.FontScaleDivisor), 0);
        return checked((int)Math.Round(yttScale * 100, MidpointRounding.ToEven));
    }

    /// <summary>정수 YTT 좌표를 화면 공간 픽셀 좌표로 변환한다.</summary>
    public static double ToPixelCoordinate(int yttCoordinate, double maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        // [UPSTREAM] 플레이어는 좌표에 0.96 을 곱하고 2 를 더한다.
        // 근거: YttDocument.GetPixelCoord(), docs/YTT-VERIFICATION.md
        return (YttConstants.CoordinateOffset + (yttCoordinate * YttConstants.CoordinateScale)) / 100 * maximum;
    }

    /// <summary>픽셀 좌표를 업로드가 허용하는 정수 YTT 좌표로 변환한다.</summary>
    public static int ToYttCoordinate(double pixelCoordinate, double maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        // [UPSTREAM] 업로드는 플레이어 변환을 되돌린 정수값만 받는다.
        // 근거: YttDocument.GetYouTubeCoord(), docs/YTT-VERIFICATION.md
        double percentage = ((pixelCoordinate / maximum * 100) - YttConstants.CoordinateOffset) /
            YttConstants.CoordinateScale;
        return checked((int)Math.Round(Math.Clamp(percentage, 0, 100), MidpointRounding.ToEven));
    }
}
