using Avalonia.Media;

namespace YttStudio.App.Tests;

public sealed class ColorPaletteTests
{
    [Fact]
    public void EverySwatchNameHasAllThreeLanguages()
    {
        // 이름이 비면 선택기에 빈 도구 설명이 붙는다. 여기서 잡는다.
        List<string> incomplete = [];
        foreach (ColorSwatch swatch in ColorPalette.Swatches)
        {
            LocalizedText? text = Localizer.Find(swatch.NameKey);
            if (text is null
                || string.IsNullOrWhiteSpace(text.Korean)
                || string.IsNullOrWhiteSpace(text.English)
                || string.IsNullOrWhiteSpace(text.Japanese))
            {
                incomplete.Add(swatch.NameKey);
            }
        }

        Assert.Empty(incomplete);
    }

    [Fact]
    public void SwatchHexIsOpaqueSoPickingAColourNeverChangesOpacity()
    {
        Assert.All(ColorPalette.Swatches, swatch => Assert.EndsWith("FF", swatch.Hex, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ColorWhite", "#FFFFFFFF")]
    [InlineData("ColorBlack", "#000000FF")]
    [InlineData("ColorRed", "#FF0000FF")]
    [InlineData("ColorCyan", "#00FFFFFF")]
    public void SwatchHexUsesRedGreenBlueAlphaOrder(string nameKey, string expected)
    {
        ColorSwatch swatch = ColorPalette.Swatches.Single(candidate => candidate.NameKey == nameKey);

        Assert.Equal(expected, swatch.Hex);
    }

    [Fact]
    public void SwatchNamesAreUnique()
    {
        Assert.Equal(
            ColorPalette.Swatches.Count,
            ColorPalette.Swatches.Select(swatch => swatch.NameKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("#FF0000FF", 255, 0, 0)]
    [InlineData("#0080C0", 0, 128, 192)]
    public void PreviewBrushKeepsTheColourAndDropsTheAlpha(string hex, byte red, byte green, byte blue)
    {
        // 미리보기 칸이 반투명하면 뒤에 깔린 패널 색이 섞여 실제 색을 잘못 읽게 된다.
        IBrush brush = MainWindowViewModel.CreateSwatchBrush(hex);

        SolidColorBrush solid = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Color.FromRgb(red, green, blue), solid.Color);
    }

    [Theory]
    [InlineData("—")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-colour")]
    public void PreviewBrushIsTransparentWhenThereIsNoSingleColour(string? hex)
    {
        Assert.Equal(Brushes.Transparent, MainWindowViewModel.CreateSwatchBrush(hex));
    }
}
