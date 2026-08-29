using Avalonia.Media;

namespace YttStudio.App;

/// <summary>색 선택기에 한 칸으로 놓이는 미리 정해진 색이다.</summary>
/// <param name="NameKey">현지화 표에서 이름을 찾을 키다.</param>
public sealed record ColorSwatch(string NameKey, byte Red, byte Green, byte Blue)
{
    /// <summary>
    /// 입력란에 넣을 <c>#RRGGBBAA</c> 문자열을 가져온다.
    /// </summary>
    /// <remarks>
    /// 알파는 항상 불투명이다. 불투명도는 옆의 별도 입력이 담당하므로 색만 골랐는데
    /// 투명도까지 바뀌면 안 된다.
    /// </remarks>
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}FF";

    /// <summary>선택기 칸을 칠할 붓을 가져온다.</summary>
    public IBrush Brush => new SolidColorBrush(Color.FromRgb(Red, Green, Blue));
}

/// <summary>자막 색으로 자주 쓰는 색을 모아 둔다.</summary>
/// <remarks>
/// 유튜브 자막 설정이 제공하는 여덟 색과 같다. 어떤 영상 위에 올려도 읽히는 것이
/// 확인된 조합이라 임의로 늘리지 않는다. 여기 없는 색은 입력란에 직접 적는다.
/// </remarks>
public static class ColorPalette
{
    /// <summary>선택기에 놓을 색을 순서대로 가져온다.</summary>
    public static IReadOnlyList<ColorSwatch> Swatches { get; } =
    [
        new("ColorWhite", 0xFF, 0xFF, 0xFF),
        new("ColorBlack", 0x00, 0x00, 0x00),
        new("ColorRed", 0xFF, 0x00, 0x00),
        new("ColorGreen", 0x00, 0xFF, 0x00),
        new("ColorBlue", 0x00, 0x00, 0xFF),
        new("ColorYellow", 0xFF, 0xFF, 0x00),
        new("ColorMagenta", 0xFF, 0x00, 0xFF),
        new("ColorCyan", 0x00, 0xFF, 0xFF),
    ];
}
