using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;
using SubtitleRenderOptions = YttStudio.Render.RenderOptions;

namespace YttStudio.App;

/// <summary>선택한 큐의 서식 · 위치 · 색상 바인딩 속성을 담당한다.</summary>
public sealed partial class MainWindowViewModel
{

    public bool MoveEffectEnabled
    {
        get => HasSelectedEffect<MoveEffect>();
        set => SetSelectedEffect(CueEffectKind.Move, value);
    }

    public bool FadeEffectEnabled
    {
        get => HasSelectedEffect<FadeEffect>();
        set => SetSelectedEffect(CueEffectKind.Fade, value);
    }

    public bool ShakeEffectEnabled
    {
        get => HasSelectedEffect<ShakeEffect>();
        set => SetSelectedEffect(CueEffectKind.Shake, value);
    }

    public bool ChromaEffectEnabled
    {
        get => HasSelectedEffect<ChromaEffect>();
        set => SetSelectedEffect(CueEffectKind.Chroma, value);
    }

    public bool AnimateEffectEnabled
    {
        get => HasSelectedEffect<AnimateEffect>();
        set => SetSelectedEffect(CueEffectKind.Animate, value);
    }

    public string SelectedText
    {
        get
        {
            string[] texts = selectedCueIds
                .Select(id => project?.Cues[id]?.Sections.FirstOrDefault()?.Text)
                .Where(text => text is not null)
                .Select(text => text!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return texts.Length == 1 ? texts[0] : texts.Length == 0 ? string.Empty : "—";
        }
        set
        {
            if (isInlineEditing || editor is null || selectedCueIds.Count == 0 || value == "—")
            {
                return;
            }

            editor.BeginTransaction("텍스트 변경");
            foreach (Guid id in selectedCueIds)
            {
                if (project?.Cues[id] is Cue cue && cue.Sections.Count > 0 && cue.Sections[0].Text != value)
                {
                    editor.SetText(id, 0, value ?? string.Empty);
                }
            }

            editor.EndTransaction();
            AfterMutation(refreshRows: true);
        }
    }

    public double SelectedPositionX
    {
        get => SelectedCue?.PositionX ?? 50;
        set => ApplyPosition(value, null);
    }

    public double SelectedPositionY
    {
        get => SelectedCue?.PositionY ?? 90;
        set => ApplyPosition(null, value);
    }

    public string SelectedPositionXText
    {
        get => GetCommonCueValue(cue => cue.PositionX)?.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                ApplyPosition(parsed, null);
            }
        }
    }

    public string SelectedPositionYText
    {
        get => GetCommonCueValue(cue => cue.PositionY)?.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                ApplyPosition(null, parsed);
            }
        }
    }

    public AnchorPoint SelectedAnchor
    {
        get => SelectedCue?.Anchor ?? AnchorPoint.BottomCenter;
        set => ApplyAnchor(value);
    }

    public Justification SelectedJustification
    {
        get => SelectedCue?.Justify ?? Justification.Center;
        set
        {
            if (editor is not null && selectedCueIds.Count > 0)
            {
                editor.SetJustification(selectedCueIds, value);
                AfterMutation();
            }
        }
    }

    public string SelectedAnchorDisplay
        => GetCommonCueValue(cue => cue.Anchor)?.ToString() ?? "—";

    public string SelectedJustificationDisplay
        => GetCommonCueValue(cue => cue.Justify)?.ToString() ?? "—";

    public YtFont? SelectedFont
    {
        get => GetCommonFormat(format => format.Font);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Font = value.Value });
            }
        }
    }

    public int SelectedSizePercent
    {
        get => SelectedFormat?.SizePercent ?? 100;
        set => ApplyFormat(new SectionFormatPatch { SizePercent = Math.Max(75, value) });
    }

    public double SelectedSizePercentValue
    {
        get
        {
            int? value = GetCommonFormat(format => format.SizePercent);
            return value ?? 100;
        }
        set
        {
            ApplyFormat(new SectionFormatPatch { SizePercent = (int)Math.Round(value) });
        }
    }

    public bool? SelectedBold
    {
        get => GetCommonFormat(format => format.Bold);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Bold = value.Value });
            }
        }
    }

    public bool? SelectedItalic
    {
        get => GetCommonFormat(format => format.Italic);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Italic = value.Value });
            }
        }
    }

    public bool? SelectedUnderline
    {
        get => GetCommonFormat(format => format.Underline);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Underline = value.Value });
            }
        }
    }

    public ScriptOffset? SelectedScriptOffset
    {
        get => GetCommonFormat(format => format.Offset);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Offset = value.Value });
            }
        }
    }

    public bool? SelectedPack
    {
        get => GetCommonFormat(format => format.Pack);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Pack = value.Value });
            }
        }
    }

    public EdgeType? SelectedEdge
    {
        get => GetCommonFormat(format => format.Edge);
        set
        {
            if (value.HasValue)
            {
                ApplyFormat(new SectionFormatPatch { Edge = value.Value });
            }
        }
    }

    public string ForegroundHex
    {
        get => GetCommonFormat(format => format.Foreground) is RgbaColor color ? ToHex(color) : "—";
        set
        {
            if (TryParseColor(value, out RgbaColor color))
            {
                ApplyFormat(new SectionFormatPatch { Foreground = color });
            }
        }
    }

    public string BackgroundHex
    {
        get => GetCommonFormat(format => format.Background) is RgbaColor color ? ToHex(color) : "—";
        set
        {
            if (TryParseColor(value, out RgbaColor color))
            {
                ApplyFormat(new SectionFormatPatch { Background = color });
            }
        }
    }

    public string EdgeColorHex
    {
        get => GetCommonFormat(format => format.EdgeColor) is RgbaColor color ? ToHex(color) : "—";
        set
        {
            if (TryParseColor(value, out RgbaColor color))
            {
                ApplyFormat(new SectionFormatPatch { EdgeColor = color });
            }
        }
    }

    public double? ForegroundOpacity
    {
        get => GetCommonFormat(format => (double)format.Foreground.Alpha);
        set => ApplyColorOpacity(value, format => format.Foreground, (color, alpha) =>
            new RgbaColor(color.Red, color.Green, color.Blue, alpha),
            color => new SectionFormatPatch { Foreground = color });
    }

    public double? BackgroundOpacity
    {
        get => GetCommonFormat(format => (double)format.Background.Alpha);
        set => ApplyColorOpacity(value, format => format.Background, (color, alpha) =>
            new RgbaColor(color.Red, color.Green, color.Blue, alpha),
            color => new SectionFormatPatch { Background = color });
    }

    public double? EdgeOpacity
    {
        get => GetCommonFormat(format => (double)format.EdgeColor.Alpha);
        set => ApplyColorOpacity(value, format => format.EdgeColor, (color, alpha) =>
            new RgbaColor(color.Red, color.Green, color.Blue, alpha),
            color => new SectionFormatPatch { EdgeColor = color });
    }

    public TextDirection? SelectedDirection
    {
        get => GetCommonCueValue(cue => cue.Direction);
        set
        {
            if (editor is not null && selectedCueIds.Count > 0 && value.HasValue)
            {
                editor.SetDirection(selectedCueIds, value.Value);
                AfterMutation();
            }
        }
    }

    public string SelectionSummary
        => selectedCueIds.Count switch
        {
            0 => "선택 없음",
            1 => "자막 1개 선택",
            _ when HasMixedSelection => $"자막 {selectedCueIds.Count}개 선택 · 혼합 값은 — 로 표시 · 변경은 전체 적용",
            _ => $"자막 {selectedCueIds.Count}개 선택 · 변경은 전체 적용",
        };

    public bool HasMixedSelection
        => selectedCueIds.Count > 1 &&
            (HasDifferentCueValues(cue => cue.PositionX) ||
             HasDifferentCueValues(cue => cue.PositionY) ||
             HasDifferentCueValues(cue => cue.Anchor) ||
             HasDifferentCueValues(cue => cue.Justify) ||
             HasDifferentCueValues(cue => cue.Direction) ||
             HasDifferentFormatValues());

    private bool HasSelectedEffect<TEffect>() where TEffect : CueEffect
        => SelectedCue?.Effects.OfType<TEffect>().Any() == true;

    private void SetSelectedEffect(CueEffectKind kind, bool enabled)
    {
        if (editor is null || selectedCueIds.Count == 0)
        {
            return;
        }
        bool current = kind switch
        {
            CueEffectKind.Move => MoveEffectEnabled,
            CueEffectKind.Fade => FadeEffectEnabled,
            CueEffectKind.Shake => ShakeEffectEnabled,
            CueEffectKind.Chroma => ChromaEffectEnabled,
            CueEffectKind.Animate => AnimateEffectEnabled,
            _ => false,
        };
        if (current == enabled)
        {
            return;
        }
        editor.SetEffectEnabled(selectedCueIds, kind, enabled);
        AfterMutation();
    }

    private T? GetCommonFormat<T>(Func<ResolvedFormat, T> selector) where T : struct
    {
        T[] values = SelectedFormats.Select(selector).ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        T first = values[0];
        return values.All(value => EqualityComparer<T>.Default.Equals(value, first)) ? first : null;
    }

    private T? GetCommonCueValue<T>(Func<Cue, T> selector) where T : struct
    {
        T[] values = selectedCueIds
            .Select(id => project?.Cues[id])
            .Where(cue => cue is not null)
            .Select(cue => selector(cue!))
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        T first = values[0];
        return values.All(value => EqualityComparer<T>.Default.Equals(value, first)) ? first : null;
    }

    private bool HasDifferentCueValues<T>(Func<Cue, T> selector) where T : struct
    {
        T[] values = selectedCueIds
            .Select(id => project?.Cues[id])
            .Where(cue => cue is not null)
            .Select(cue => selector(cue!))
            .Distinct()
            .Take(2)
            .ToArray();
        return values.Length > 1;
    }

    private bool HasDifferentFormatValues()
    {
        ResolvedFormat? first = SelectedFormats.FirstOrDefault();
        return first is not null && SelectedFormats.Skip(1).Any(format => format != first);
    }

    private Guid? GetCommonCueStyleId()
    {
        if (selectedCueIds.Count == 0)
        {
            return null;
        }

        Guid? first = null;
        foreach (Guid id in selectedCueIds)
        {
            if (project?.Cues[id] is not Cue cue)
            {
                continue;
            }

            Guid value = cue.StyleId ?? Guid.Empty;
            if (first is null)
            {
                first = value;
            }
            else if (first.Value != value)
            {
                return null;
            }
        }

        return first;
    }

    private void ApplyColorOpacity(
        double? value,
        Func<ResolvedFormat, RgbaColor> selector,
        Func<RgbaColor, byte, RgbaColor> colorFactory,
        Func<RgbaColor, SectionFormatPatch> patchFactory)
    {
        if (!value.HasValue || SelectedFormat is not ResolvedFormat format)
        {
            return;
        }

        byte alpha = (byte)Math.Clamp(Math.Round(value.Value), 0, YttConstants.MaximumOpacity);
        ApplyFormat(patchFactory(colorFactory(selector(format), alpha)));
    }

    private static string ToHex(RgbaColor color)
        => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}{color.Alpha:X2}";

    private static bool TryParseColor(string? value, out RgbaColor color)
    {
        string text = value?.Trim().TrimStart('#') ?? string.Empty;
        if ((text.Length == 6 || text.Length == 8) && uint.TryParse(text,
            System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
        {
            if (text.Length == 6)
            {
                color = new RgbaColor((byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed,
                    YttConstants.MaximumOpacity);
            }
            else
            {
                color = new RgbaColor((byte)(parsed >> 24), (byte)(parsed >> 16), (byte)(parsed >> 8),
                    (byte)Math.Min(parsed & 0xff, YttConstants.MaximumOpacity));
            }

            return true;
        }

        color = default;
        return false;
    }
}
