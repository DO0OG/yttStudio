using YttStudio.Core;

namespace YttStudio.Core.Tests;

public sealed class FormatResolverTests
{
    [Theory]
    [InlineData("inherit")]
    [InlineData("font")]
    [InlineData("size")]
    [InlineData("flags")]
    [InlineData("colors")]
    [InlineData("edge")]
    public void ResolvePreservesInheritanceAndExplicitOverrides(string scenario)
    {
        SectionFormat baseFormat = new()
        {
            Font = YtFont.Serif,
            SizePercent = 125,
            Bold = true,
            Italic = false,
            Underline = true,
            Offset = ScriptOffset.Regular,
            Foreground = new RgbaColor(10, 20, 30, 200),
            Background = new RgbaColor(40, 50, 60, 100),
            SecondaryColor = new RgbaColor(70, 80, 90, 180),
            Edge = EdgeType.Glow,
            EdgeColor = new RgbaColor(1, 2, 3, 254),
            Pack = false,
        };
        SectionOverrides overrides = new();

        switch (scenario)
        {
            case "font":
                overrides.Font = YtFont.Casual;
                break;
            case "size":
                overrides.SizePercent = 100;
                break;
            case "flags":
                overrides.Bold = false;
                overrides.Italic = true;
                overrides.Underline = false;
                overrides.Pack = true;
                break;
            case "colors":
                overrides.Foreground = RgbaColor.White;
                overrides.Background = RgbaColor.Transparent;
                overrides.SecondaryColor = RgbaColor.SecondaryDefault;
                break;
            case "edge":
                overrides.Edge = EdgeType.SoftShadow;
                overrides.EdgeColor = new RgbaColor(200, 100, 50, 254);
                overrides.Offset = ScriptOffset.Superscript;
                break;
            default:
                // "inherit" 시나리오는 override 를 두지 않아 상속 결과를 그대로 검증한다.
                break;
        }

        ResolvedFormat resolved = FormatResolver.Resolve(baseFormat, overrides);

        Assert.Equal(overrides.Font ?? baseFormat.Font, resolved.Font);
        Assert.Equal(overrides.SizePercent ?? baseFormat.SizePercent, resolved.SizePercent);
        Assert.Equal(overrides.Bold ?? baseFormat.Bold, resolved.Bold);
        Assert.Equal(overrides.Italic ?? baseFormat.Italic, resolved.Italic);
        Assert.Equal(overrides.Underline ?? baseFormat.Underline, resolved.Underline);
        Assert.Equal(overrides.Foreground ?? baseFormat.Foreground, resolved.Foreground);
        Assert.Equal(overrides.Background ?? baseFormat.Background, resolved.Background);
        Assert.Equal(overrides.Edge ?? baseFormat.Edge, resolved.Edge);
        Assert.Equal(overrides.Pack ?? baseFormat.Pack, resolved.Pack);
    }
}
