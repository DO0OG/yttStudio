namespace YttStudio.Core;

/// <summary>Implements the player coordinate and YTT font-scale transforms.</summary>
public static class YttMath
{
    /// <summary>Converts a stored YTT font scale to the user-visible percentage.</summary>
    public static int ToRealFontPercent(int yttScale)
    {
        // SPEC §5.6 [UPSTREAM]: the player applies one quarter of the stored scale delta.
        // 근거: YttDocument.GetRealFontScale(), docs/YTT-VERIFICATION.md.
        return checked((int)Math.Round(
            (1 + (((yttScale / 100.0) - 1) / YttConstants.FontScaleDivisor)) * 100,
            MidpointRounding.ToEven));
    }

    /// <summary>Converts a user-visible font percentage to the stored YTT scale.</summary>
    public static int ToYttFontScale(int realPercent)
    {
        // SPEC §5.6 [UPSTREAM]: YTT has a 75% lower bound and no upper scale limit.
        // 근거: YttDocument.GetYouTubeFontScale(), docs/YTT-VERIFICATION.md.
        double realScale = Math.Max(realPercent, 75) / 100.0;
        double yttScale = Math.Max(1 + ((realScale - 1) * YttConstants.FontScaleDivisor), 0);
        return checked((int)Math.Round(yttScale * 100, MidpointRounding.ToEven));
    }

    /// <summary>Converts an integer YTT coordinate to a screen-space pixel coordinate.</summary>
    public static double ToPixelCoordinate(int yttCoordinate, double maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        // SPEC §5.2 [UPSTREAM]: the player applies coordinate * 0.96 + 2.
        // 근거: YttDocument.GetPixelCoord(), docs/YTT-VERIFICATION.md.
        return (YttConstants.CoordinateOffset + (yttCoordinate * YttConstants.CoordinateScale)) / 100 * maximum;
    }

    /// <summary>Converts a pixel coordinate to the integer YTT coordinate accepted by upload.</summary>
    public static int ToYttCoordinate(double pixelCoordinate, double maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        // SPEC §5.2 [UPSTREAM]: upload accepts integers after undoing the player's transform.
        // 근거: YttDocument.GetYouTubeCoord(), docs/YTT-VERIFICATION.md.
        double percentage = ((pixelCoordinate / maximum * 100) - YttConstants.CoordinateOffset) /
            YttConstants.CoordinateScale;
        return checked((int)Math.Round(Math.Clamp(percentage, 0, 100), MidpointRounding.ToEven));
    }
}
