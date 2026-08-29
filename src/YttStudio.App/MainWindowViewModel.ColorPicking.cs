using Avalonia.Media;
using YttStudio.Core;

namespace YttStudio.App;

/// <summary>색 선택기 한 칸에 필요한 것을 모두 담는다.</summary>
public sealed record ColorSwatchOption(string Name, string Hex, IBrush Brush);

/// <summary>색을 직접 적지 않고 고를 수 있게 한다.</summary>
/// <remarks>
/// hex 입력은 그대로 둔다. 고르는 쪽은 자주 쓰는 색을 빠르게 넣기 위한 것이고,
/// 팔레트에 없는 색은 여전히 입력란에 직접 적는다.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    /// <summary>선택기에 놓을 색을 현재 언어의 이름과 함께 가져온다.</summary>
    public IReadOnlyList<ColorSwatchOption> ColorSwatches =>
        [.. ColorPalette.Swatches.Select(swatch =>
            new ColorSwatchOption(Loc[swatch.NameKey], swatch.Hex, swatch.Brush))];

    /// <summary>전경색 칸에 고른 색을 넣는다.</summary>
    public DelegateCommand<string> PickForegroundColorCommand { get; private set; } = null!;

    /// <summary>배경색 칸에 고른 색을 넣는다.</summary>
    public DelegateCommand<string> PickBackgroundColorCommand { get; private set; } = null!;

    /// <summary>테두리색 칸에 고른 색을 넣는다.</summary>
    public DelegateCommand<string> PickEdgeColorCommand { get; private set; } = null!;

    /// <summary>현재 전경색을 미리 보여 줄 붓을 가져온다.</summary>
    public IBrush ForegroundSwatchBrush => CreateSwatchBrush(ForegroundHex);

    /// <summary>현재 배경색을 미리 보여 줄 붓을 가져온다.</summary>
    public IBrush BackgroundSwatchBrush => CreateSwatchBrush(BackgroundHex);

    /// <summary>현재 테두리색을 미리 보여 줄 붓을 가져온다.</summary>
    public IBrush EdgeSwatchBrush => CreateSwatchBrush(EdgeColorHex);

    /// <summary>hex 문자열을 미리보기 붓으로 바꾼다.</summary>
    /// <remarks>
    /// 여러 자막을 골라 색이 서로 다르면 hex 자리에 "—" 가 온다. 그때는 보여 줄 색이
    /// 없으므로 투명하게 둔다. 알파는 일부러 버린다. 미리보기 칸이 반투명하면 뒤에
    /// 깔린 패널 색이 섞여 실제 색을 잘못 읽게 된다.
    /// </remarks>
    internal static IBrush CreateSwatchBrush(string? hex)
        => TryParseColor(hex, out RgbaColor color)
            ? new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue))
            : Brushes.Transparent;

    private void CreateColorPickingCommands()
    {
        PickForegroundColorCommand = new DelegateCommand<string>(hex => ForegroundHex = hex ?? string.Empty);
        PickBackgroundColorCommand = new DelegateCommand<string>(hex => BackgroundHex = hex ?? string.Empty);
        PickEdgeColorCommand = new DelegateCommand<string>(hex => EdgeColorHex = hex ?? string.Empty);
    }

    private void NotifyColorSwatchesChanged()
    {
        OnPropertyChanged(nameof(ForegroundSwatchBrush));
        OnPropertyChanged(nameof(BackgroundSwatchBrush));
        OnPropertyChanged(nameof(EdgeSwatchBrush));
    }
}
