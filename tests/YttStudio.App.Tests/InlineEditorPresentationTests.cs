using Avalonia;
using Avalonia.Media;
using YttStudio.App;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Render;

namespace YttStudio.App.Tests;

public sealed class InlineEditorPresentationTests
{
    [Theory]
    [InlineData(Justification.Left, TextAlignment.Left)]
    [InlineData(Justification.Center, TextAlignment.Center)]
    [InlineData(Justification.Right, TextAlignment.Right)]
    public void MapsJustificationAndResolvedStyle(
        Justification justification,
        TextAlignment expectedAlignment)
    {
        ResolvedFormat resolved = new(
            YtFont.Default,
            100,
            false,
            false,
            false,
            ScriptOffset.Regular,
            new RgbaColor(12, 34, 56, YttConstants.MaximumOpacity),
            RgbaColor.Transparent,
            RgbaColor.SecondaryDefault,
            EdgeType.None,
            RgbaColor.EdgeDefault,
            false);
        using BundledFontResolver fonts = new();
        FontResolution resolution = fonts.Resolve(YtFont.Default);

        InlineEditorStyle result = InlineEditorPresentationMapper.Map(
            resolved, resolution, justification);

        Assert.Equal(resolution.ActualFamilyName, result.FontFamilyName);
        Assert.Equal(YttConstants.DefaultFontSizePixels, result.ReferenceFontSize);
        Assert.Equal(expectedAlignment, result.TextAlignment);
        Assert.Equal((byte)12, result.ForegroundColor.R);
        Assert.Equal((byte)34, result.ForegroundColor.G);
        Assert.Equal((byte)56, result.ForegroundColor.B);
        Assert.Equal((byte)255, result.ForegroundColor.A);
    }

    [Fact]
    public void MapsIntermediateYttAlphaWithRendererRounding()
    {
        ResolvedFormat resolved = new(
            YtFont.Default,
            100,
            false,
            false,
            false,
            ScriptOffset.Regular,
            new RgbaColor(12, 34, 56, 127),
            RgbaColor.Transparent,
            RgbaColor.SecondaryDefault,
            EdgeType.None,
            RgbaColor.EdgeDefault,
            false);
        using BundledFontResolver fonts = new();
        InlineEditorStyle result = InlineEditorPresentationMapper.Map(
            resolved, fonts.Resolve(YtFont.Default), Justification.Center);

        Assert.Equal((byte)128, result.ForegroundColor.A);
    }

    [Fact]
    public void ScalesReferenceBoundsAndUsesMinimumReadableFontSize()
    {
        ResolvedFormat resolved = new(
            YtFont.Default,
            100,
            false,
            false,
            false,
            ScriptOffset.Regular,
            RgbaColor.White,
            RgbaColor.Transparent,
            RgbaColor.SecondaryDefault,
            EdgeType.None,
            RgbaColor.EdgeDefault,
            false);
        using BundledFontResolver fonts = new();
        InlineEditorStyle style = InlineEditorPresentationMapper.Map(
            resolved, fonts.Resolve(YtFont.Default), Justification.Center);

        InlineEditorPresentation scaled = InlineEditorPresentationMapper.Scale(
            style,
            new CanvasRect(320, 180, 640, 360),
            new Rect(10, 20, 640, 360));
        Assert.Equal(new Rect(170, 110, 320, 180), scaled.Bounds);
        Assert.Equal(16, scaled.FontSize);
        Assert.Equal(4, scaled.Padding.Left);
        Assert.Equal(2.4, scaled.Padding.Top);

        InlineEditorPresentation tiny = InlineEditorPresentationMapper.Scale(
            style,
            new CanvasRect(0, 0, 1280, 720),
            new Rect(0, 0, 64, 36));
        Assert.Equal(InlineEditorPresentationMapper.MinimumReadableFontSize, tiny.FontSize);
    }

    [Fact]
    public void ScaleUsesOffsetSubtitleSpaceForBoundsAndReferenceScale()
    {
        InlineEditorStyle style = new(
            "Test",
            32,
            Colors.White,
            TextAlignment.Center,
            new Thickness(8, 4.8));
        Rect subtitleSpace = new(100, 50, 1000, 800);
        Rect content = PreviewCanvasGeometry.GetContentRect(new Size(1600, 900), subtitleSpace);

        InlineEditorPresentation result = InlineEditorPresentationMapper.Scale(
            style,
            new CanvasRect(250, 150, 400, 300),
            content,
            subtitleSpace);

        Assert.Equal(406.25, result.Bounds.Left, 10);
        Assert.Equal(112.5, result.Bounds.Top, 10);
        Assert.Equal(450, result.Bounds.Width, 10);
        Assert.Equal(337.5, result.Bounds.Height, 10);
        Assert.Equal(36, result.FontSize, 10);
        Assert.Equal(9, result.Padding.Left, 10);
        Assert.Equal(5.4, result.Padding.Top, 10);
    }
}
