using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using YttStudio.Core;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Validation;
using YttStudio.Render;
using YttStudio.Video;

namespace YttStudio.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IFileDialogService dialogs;
    private readonly SubtitleFileService fileService = new();
    private readonly SkiaSubtitleRenderer renderer;
    private readonly HashSet<Guid> selectedCueIds = [];
    private SubtitleProject? project;
    private DocumentEditor? editor;
    private MpvVideoSource? videoSource;
    private Bitmap? videoFrameImage;
    private Bitmap? subtitleImage;
    private string? sourcePath;
    private string status = "자막 또는 영상을 열어 주세요.";
    private string videoStatus = "libmpv 탐색 중";
    private double maximumMilliseconds = 1;
    private double positionMilliseconds;
    private double playbackSpeed = 1;
    private bool useCheckerboard;
    private bool videoLoaded;
    private bool updatingFromVideo;
    private int frameUpdatePending;
    private Guid? lastSelectedCueId;
    private CueRowViewModel? selectedCueRow;
    private Guid? selectedStyleId;
    private string selectedStyleName = string.Empty;
    private bool isInlineEditing;
    private ValidationIssue? selectedValidationIssue;
    private string inlineText = string.Empty;
    private double inlineEditorLeft;
    private double inlineEditorTop;
    private double inlineEditorWidth = 180;
    private bool disposed;

    public MainWindowViewModel(IFileDialogService dialogs)
    {
        this.dialogs = dialogs;
        renderer = new SkiaSubtitleRenderer(new BundledFontResolver(
            message => Serilog.Log.Information("{FontResolution}", message)));
        OpenSubtitleCommand = new AsyncCommand(OpenSubtitleAsync);
        OpenVideoCommand = new AsyncCommand(OpenVideoAsync, () => videoSource is not null);
        SaveCommand = new AsyncCommand(SaveAsync, () => project is not null);
        PlayPauseCommand = new DelegateCommand(TogglePlayback, () => videoLoaded);
        StepBackCommand = new DelegateCommand(() => StepFrame(-1), () => videoLoaded);
        StepForwardCommand = new DelegateCommand(() => StepFrame(1), () => videoLoaded);
        UndoCommand = new DelegateCommand(Undo, () => editor?.CanUndo == true);
        RedoCommand = new DelegateCommand(Redo, () => editor?.CanRedo == true);
        AddCueCommand = new DelegateCommand(AddCue, () => project is not null);
        DeleteCueCommand = new DelegateCommand(DeleteSelectedCues, () => selectedCueIds.Count > 0);
        DuplicateCueCommand = new DelegateCommand(DuplicateSelectedCues, () => selectedCueIds.Count > 0);
        AddStyleCommand = new DelegateCommand(AddStyle, () => editor is not null);
        DeleteStyleCommand = new AsyncCommand(DeleteSelectedStyleAsync,
            () => selectedStyleId is Guid id && id != Guid.Empty);
        RenameStyleCommand = new DelegateCommand(RenameSelectedStyle,
            () => editor is not null && selectedStyleId is Guid id && id != Guid.Empty);
        SaveCueAsStyleCommand = new DelegateCommand(SaveSelectedCueAsStyle,
            () => editor is not null && selectedStyleId is Guid id && id != Guid.Empty && SelectedFormat is not null &&
                selectedCueIds.Count > 0);
        ApplySelectedStyleCommand = new DelegateCommand(ApplySelectedStyle,
            () => editor is not null && selectedStyleId is Guid id && selectedCueIds.Count > 0);
        AlignLeftCommand = new DelegateCommand(() => AlignSelected("L"), () => selectedCueIds.Count > 1);
        AlignCenterCommand = new DelegateCommand(() => AlignSelected("C"), () => selectedCueIds.Count > 1);
        AlignRightCommand = new DelegateCommand(() => AlignSelected("R"), () => selectedCueIds.Count > 1);
        AlignTopCommand = new DelegateCommand(() => AlignSelected("T"), () => selectedCueIds.Count > 1);
        AlignMiddleCommand = new DelegateCommand(() => AlignSelected("M"), () => selectedCueIds.Count > 1);
        AlignBottomCommand = new DelegateCommand(() => AlignSelected("B"), () => selectedCueIds.Count > 1);
        DistributeHorizontalCommand = new DelegateCommand(() => DistributeSelected(horizontal: true),
            () => selectedCueIds.Count > 2);
        DistributeVerticalCommand = new DelegateCommand(() => DistributeSelected(horizontal: false),
            () => selectedCueIds.Count > 2);
        BringToFrontCommand = new DelegateCommand(() => MoveSelectionToZOrder(front: true),
            () => selectedCueIds.Count > 0 && project is not null);
        SendToBackCommand = new DelegateCommand(() => MoveSelectionToZOrder(front: false),
            () => selectedCueIds.Count > 0 && project is not null);
        CommitInlineEditCommand = new DelegateCommand(CommitInlineEdit, () => IsInlineEditing);
        ValidateCommand = new DelegateCommand(RunValidation, () => project is not null);
        ApplyValidationFixCommand = new DelegateCommand(ApplySelectedValidationFix);
        GoToValidationIssueCommand = new DelegateCommand(GoToSelectedValidationIssue);
        InitializeVideoSource();
        RenderFallbackFrame();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand OpenSubtitleCommand { get; }
    public AsyncCommand OpenVideoCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public DelegateCommand PlayPauseCommand { get; }
    public DelegateCommand StepBackCommand { get; }
    public DelegateCommand StepForwardCommand { get; }
    public DelegateCommand UndoCommand { get; }
    public DelegateCommand RedoCommand { get; }
    public DelegateCommand AddCueCommand { get; }
    public DelegateCommand DeleteCueCommand { get; }
    public DelegateCommand DuplicateCueCommand { get; }
    public DelegateCommand AddStyleCommand { get; }
    public AsyncCommand DeleteStyleCommand { get; }
    public DelegateCommand RenameStyleCommand { get; }
    public DelegateCommand SaveCueAsStyleCommand { get; }
    public DelegateCommand ApplySelectedStyleCommand { get; }
    public DelegateCommand AlignLeftCommand { get; }
    public DelegateCommand AlignCenterCommand { get; }
    public DelegateCommand AlignRightCommand { get; }
    public DelegateCommand AlignTopCommand { get; }
    public DelegateCommand AlignMiddleCommand { get; }
    public DelegateCommand AlignBottomCommand { get; }
    public DelegateCommand DistributeHorizontalCommand { get; }
    public DelegateCommand DistributeVerticalCommand { get; }
    public DelegateCommand BringToFrontCommand { get; }
    public DelegateCommand SendToBackCommand { get; }
    public DelegateCommand CommitInlineEditCommand { get; }
    public DelegateCommand ValidateCommand { get; }
    public DelegateCommand ApplyValidationFixCommand { get; }
    public DelegateCommand GoToValidationIssueCommand { get; }
    public ObservableCollection<CueRowViewModel> CueRows { get; } = [];
    public ObservableCollection<StyleOption> Styles { get; } = [];
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = [];
    public IReadOnlyList<CanvasCueItem> CanvasItems { get; private set; } = [];
    public IReadOnlyCollection<Guid> SelectedCueIds => selectedCueIds;
    public Array AnchorOptions { get; } = Enum.GetValues<AnchorPoint>();
    public Array JustificationOptions { get; } = Enum.GetValues<Justification>();
    public Array DirectionOptions { get; } = Enum.GetValues<TextDirection>();
    public Array ScriptOffsetOptions { get; } = Enum.GetValues<ScriptOffset>();
    public Array FontOptions { get; } = Enum.GetValues<YtFont>();
    public Array EdgeOptions { get; } = Enum.GetValues<EdgeType>();
    public double[] SpeedOptions { get; } = [0.5, 1.0, 1.5, 2.0];
    public bool HasProject => project is not null;
    public bool HasVideo => videoLoaded;
    public bool IsPlaying => videoSource?.IsPlaying == true;
    public string PlayPauseLabel => IsPlaying ? "일시정지" : "재생";

    public ValidationIssue? SelectedValidationIssue
    {
        get => selectedValidationIssue;
        set => SetField(ref selectedValidationIssue, value);
    }

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

    public Bitmap? VideoFrameImage
    {
        get => videoFrameImage;
        private set => SetImage(ref videoFrameImage, value);
    }

    public Bitmap? SubtitleImage
    {
        get => subtitleImage;
        private set => SetImage(ref subtitleImage, value);
    }

    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public string VideoStatus
    {
        get => videoStatus;
        private set => SetField(ref videoStatus, value);
    }

    public double MaximumMilliseconds
    {
        get => maximumMilliseconds;
        private set => SetField(ref maximumMilliseconds, value);
    }

    public double PositionMilliseconds
    {
        get => positionMilliseconds;
        set
        {
            double clamped = Math.Clamp(value, 0, MaximumMilliseconds);
            if (!SetField(ref positionMilliseconds, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(PositionDisplay));
            RenderSubtitlePreview();
            if (!updatingFromVideo && videoLoaded && videoSource is not null)
            {
                _ = SeekAsync(clamped, exact: false);
            }
        }
    }

    public string PositionDisplay => TimeSpan.FromMilliseconds(PositionMilliseconds).ToString(@"mm\:ss\.fff");

    public double PlaybackSpeed
    {
        get => playbackSpeed;
        set
        {
            if (SetField(ref playbackSpeed, value) && videoLoaded)
            {
                videoSource?.SetSpeed(value);
            }
        }
    }

    public bool UseCheckerboard
    {
        get => useCheckerboard;
        set
        {
            if (SetField(ref useCheckerboard, value) && !videoLoaded)
            {
                RenderFallbackFrame();
            }
        }
    }

    public CueRowViewModel? SelectedCueRow
    {
        get => selectedCueRow;
        set
        {
            if (SetField(ref selectedCueRow, value) && value is not null)
            {
                SelectCue(value.Id, toggle: false);
            }
        }
    }

    public Guid? SelectedStyleId
    {
        get => selectedStyleId;
        set
        {
            Guid selected = value ?? Guid.Empty;
            if (!SetField(ref selectedStyleId, selected))
            {
                return;
            }

            selectedStyleName = Styles.FirstOrDefault(style => style.Id == selected)?.Name ?? string.Empty;
            OnPropertyChanged(nameof(SelectedStyleOption));
            OnPropertyChanged(nameof(SelectedStyleName));
            NotifyCommandStates();
        }
    }

    public StyleOption? SelectedStyleOption
    {
        get => Styles.FirstOrDefault(style => style.Id == (selectedStyleId ?? Guid.Empty));
        set => SelectedStyleId = value?.Id ?? Guid.Empty;
    }

    /// <summary>Style assigned to every selected cue, or null when the selection is mixed.</summary>
    public StyleOption? SelectedCueStyleOption
    {
        get
        {
            Guid? styleId = GetCommonCueStyleId();
            return styleId.HasValue
                ? Styles.FirstOrDefault(style => style.Id == styleId.Value)
                : null;
        }
        set
        {
            if (value is null || editor is null || selectedCueIds.Count == 0)
            {
                return;
            }

            editor.ApplyStyle(selectedCueIds, value.Id == Guid.Empty ? null : value.Id);
            AfterMutation(refreshRows: true);
        }
    }

    public string SelectedStyleName
    {
        get => selectedStyleName;
        set => SetField(ref selectedStyleName, value ?? string.Empty);
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
            if (editor is null || selectedCueIds.Count == 0 || value == "—")
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

    public bool IsInlineEditing
    {
        get => isInlineEditing;
        private set => SetField(ref isInlineEditing, value);
    }

    public string InlineText
    {
        get => inlineText;
        set => SetField(ref inlineText, value);
    }

    public double InlineEditorLeft
    {
        get => inlineEditorLeft;
        private set => SetField(ref inlineEditorLeft, value);
    }

    public double InlineEditorTop
    {
        get => inlineEditorTop;
        private set => SetField(ref inlineEditorTop, value);
    }

    public double InlineEditorWidth
    {
        get => inlineEditorWidth;
        private set => SetField(ref inlineEditorWidth, value);
    }

    private Cue? SelectedCue
        => project is not null && lastSelectedCueId is Guid id ? project.Cues[id] : null;

    private ResolvedFormat? SelectedFormat
    {
        get
        {
            Cue? cue = SelectedCue;
            Section? section = cue?.Sections.FirstOrDefault();
            return cue is null || section is null || project is null
                ? null
                : FormatResolver.Resolve(project.GetStyle(section.StyleIdOverride ?? cue.StyleId).BaseFormat,
                    section.Overrides);
        }
    }

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

    private void RunValidation()
    {
        ValidationIssues.Clear();
        if (project is null)
        {
            return;
        }

        Dictionary<Guid, ValidationMetrics> metrics = [];
        foreach (Cue cue in project.Cues)
        {
            CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == cue.Id);
            bool outside = item is not null && (item.Bounds.Left < YttConstants.ReferenceWidth * 0.05 ||
                item.Bounds.Top < YttConstants.ReferenceHeight * 0.05 ||
                item.Bounds.Right > YttConstants.ReferenceWidth * 0.95 ||
                item.Bounds.Bottom > YttConstants.ReferenceHeight * 0.95);
            metrics[cue.Id] = new ValidationMetrics
            {
                MobileEffectRisk = cue.Effects.Count >= 3,
                IsOutsideSafeArea = outside,
                BoxWidth = item?.Bounds.Width,
                SubtitleSpaceWidth = YttConstants.ReferenceWidth,
            };
        }

        byte[]? exportedXml = null;
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"YttStudio-{Guid.NewGuid():N}.ytt");
        try
        {
            fileService.Export(project, temporaryPath);
            exportedXml = File.ReadAllBytes(temporaryPath);
        }
        catch (Exception exception)
        {
            Status = $"크기 근사 계산 실패: {exception.Message}";
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        ValidationContext context = new(project)
        {
            VideoDuration = project.Video?.Duration,
            ExportedXmlBytes = exportedXml,
            CueMetrics = metrics,
        };
        foreach (ValidationIssue issue in new DocumentValidator().Validate(project, context))
        {
            ValidationIssues.Add(issue);
        }
        Status = $"검증 {ValidationIssues.Count}건 · 크기는 실제 JSON3와 다른 근사치이며 업로드 후 확인 필요";
    }

    private void ApplySelectedValidationFix()
    {
        if (editor is null || SelectedValidationIssue is not ValidationIssue issue ||
            !new DocumentValidator().ApplyAutoFix(editor, issue))
        {
            return;
        }
        AfterMutation();
        RunValidation();
    }

    private void GoToSelectedValidationIssue()
    {
        if (SelectedValidationIssue?.CueId is not Guid cueId || project?.Cues[cueId] is null)
        {
            return;
        }
        SelectCue(cueId, toggle: false);
        Cue cue = project.Cues[cueId]!;
        PositionMilliseconds = Math.Clamp(cue.Start.TotalMilliseconds + 1, 0, MaximumMilliseconds);
    }

    private IEnumerable<ResolvedFormat> SelectedFormats
    {
        get
        {
            if (project is null)
            {
                yield break;
            }

            foreach (Guid id in selectedCueIds)
            {
                if (project.Cues[id] is not Cue cue)
                {
                    continue;
                }

                foreach (Section section in cue.Sections)
                {
                    yield return FormatResolver.Resolve(
                        project.GetStyle(section.StyleIdOverride ?? cue.StyleId).BaseFormat,
                        section.Overrides);
                }
            }
        }
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

    public void SelectCue(Guid cueId, bool toggle)
    {
        if (project?.Cues[cueId] is null)
        {
            return;
        }

        if (!toggle)
        {
            selectedCueIds.Clear();
            selectedCueIds.Add(cueId);
        }
        else if (!selectedCueIds.Remove(cueId))
        {
            selectedCueIds.Add(cueId);
        }

        lastSelectedCueId = selectedCueIds.Contains(cueId) ? cueId : selectedCueIds.LastOrDefault();
        if (selectedCueIds.Count == 0)
        {
            lastSelectedCueId = null;
        }
        selectedCueRow = lastSelectedCueId is Guid selected
            ? CueRows.FirstOrDefault(row => row.Id == selected)
            : null;
        OnPropertyChanged(nameof(SelectedCueRow));
        RefreshCanvasSelection();
        NotifySelectionProperties();
    }

    public void SelectInRectangle(CanvasRect rectangle)
    {
        selectedCueIds.Clear();
        lastSelectedCueId = null;
        foreach (CanvasCueItem item in CanvasItems.Where(item => Intersects(item.Bounds, rectangle)))
        {
            selectedCueIds.Add(item.Id);
            lastSelectedCueId = item.Id;
        }

        RefreshCanvasSelection();
        NotifySelectionProperties();
    }

    public CanvasMovePreview PreviewCanvasMove(double deltaX, double deltaY, bool altPressed)
    {
        CanvasCueItem? primary = CanvasItems.FirstOrDefault(item => item.Id == lastSelectedCueId);
        if (primary is null)
        {
            return new CanvasMovePreview(deltaX, deltaY, []);
        }

        List<SnapGuide> guides = [];
        foreach (CanvasCueItem item in CanvasItems.Where(item => !selectedCueIds.Contains(item.Id)))
        {
            guides.Add(new SnapGuide(true, item.Anchor.X, "다른 자막 앵커"));
            guides.Add(new SnapGuide(false, item.Anchor.Y, "다른 자막 앵커"));
            guides.Add(new SnapGuide(true, item.Bounds.Left, "다른 자막 경계"));
            guides.Add(new SnapGuide(true, item.Bounds.Right, "다른 자막 경계"));
            guides.Add(new SnapGuide(false, item.Bounds.Top, "다른 자막 경계"));
            guides.Add(new SnapGuide(false, item.Bounds.Bottom, "다른 자막 경계"));
        }

        CanvasPoint requested = new(primary.Anchor.X + deltaX, primary.Anchor.Y + deltaY);
        SnapResult snapped = CanvasGeometry.Snap(requested, YttConstants.ReferenceWidth,
            YttConstants.ReferenceHeight, altPressed, guides);
        return new CanvasMovePreview(
            snapped.Point.X - primary.Anchor.X,
            snapped.Point.Y - primary.Anchor.Y,
            snapped.Guides);
    }

    public void CommitCanvasMove(double deltaX, double deltaY, bool altPressed)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        CanvasMovePreview preview = PreviewCanvasMove(deltaX, deltaY, altPressed);
        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (Guid id in selectedCueIds)
        {
            Cue cue = project.Cues[id]!;
            CanvasPoint current = CanvasGeometry.ToCanvasPoint(cue.PositionX, cue.PositionY,
                YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);
            positions[id] = CanvasGeometry.ToYttPoint(current.X + preview.DeltaX, current.Y + preview.DeltaY,
                YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);
        }

        editor.MoveCues(positions);
        AfterMutation();
    }

    public void ChangeAnchor(Guid cueId, AnchorPoint anchor)
    {
        if (editor is null)
        {
            return;
        }

        CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == cueId);
        if (item is null)
        {
            return;
        }

        CanvasPoint ytt = CanvasGeometry.PreserveBoxForAnchor(item.Bounds, anchor,
            YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);
        editor.SetAnchor(cueId, anchor, ytt.X, ytt.Y);
        AfterMutation();
    }

    public void ApplySelectedAnchor(AnchorPoint anchor) => ApplyAnchor(anchor);

    public void ApplySelectedJustification(Justification justification)
        => SelectedJustification = justification;

    public void BeginInlineEdit(Guid cueId, double left, double top, double width)
    {
        SelectCue(cueId, toggle: false);
        InlineText = SelectedText;
        InlineEditorLeft = left;
        InlineEditorTop = top;
        InlineEditorWidth = Math.Max(140, width);
        IsInlineEditing = true;
        CommitInlineEditCommand.NotifyCanExecuteChanged();
    }

    public void AlignSelected(char command)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.BeginTransaction("화면 기준 정렬");
        foreach (Guid id in selectedCueIds)
        {
            Cue cue = project.Cues[id]!;
            switch (command)
            {
                case 'H':
                    editor.MoveCue(id, 50, cue.PositionY);
                    break;
                case 'V':
                    editor.MoveCue(id, cue.PositionX, 50);
                    break;
                case 'C':
                    editor.SetAnchor(id, AnchorPoint.MiddleCenter, 50, 50);
                    break;
                case 'B':
                    editor.SetAnchor(id, AnchorPoint.BottomCenter, 50, 90);
                    break;
                default:
                    // Only the four shortcuts in SPEC §9.4 (3) reach this method.
                    break;
            }
        }

        editor.EndTransaction();
        AfterMutation();
    }

    public void AlignSelected(string command)
    {
        if (command is not ("L" or "C" or "R" or "T" or "M" or "B"))
        {
            return;
        }

        CanvasCueItem[] items = CanvasItems.Where(item => selectedCueIds.Contains(item.Id)).ToArray();
        CanvasCueItem? reference = items.FirstOrDefault(item => item.Id == lastSelectedCueId);
        if (editor is null || items.Length < 2 || reference is null)
        {
            return;
        }

        bool horizontal = command is "L" or "C" or "R";
        double target = command switch
        {
            "L" => reference.Bounds.Left,
            "C" => reference.Bounds.Left + (reference.Bounds.Width / 2),
            "R" => reference.Bounds.Right,
            "T" => reference.Bounds.Top,
            "M" => reference.Bounds.Top + (reference.Bounds.Height / 2),
            _ => reference.Bounds.Bottom,
        };

        ApplyMeasuredMove(items, item =>
        {
            double current = horizontal
                ? command switch
                {
                    "L" => item.Bounds.Left,
                    "C" => item.Bounds.Left + (item.Bounds.Width / 2),
                    _ => item.Bounds.Right,
                }
                : command switch
                {
                    "T" => item.Bounds.Top,
                    "M" => item.Bounds.Top + (item.Bounds.Height / 2),
                    _ => item.Bounds.Bottom,
                };
            return horizontal ? new CanvasPoint(target - current, 0) : new CanvasPoint(0, target - current);
        });
    }

    public void DistributeSelected(bool horizontal)
    {
        CanvasCueItem[] items = CanvasItems.Where(item => selectedCueIds.Contains(item.Id))
            .OrderBy(item => horizontal ? item.Bounds.Left : item.Bounds.Top)
            .ToArray();
        if (editor is null || items.Length < 3)
        {
            return;
        }

        double first = horizontal
            ? items[0].Bounds.Left + (items[0].Bounds.Width / 2)
            : items[0].Bounds.Top + (items[0].Bounds.Height / 2);
        double last = horizontal
            ? items[^1].Bounds.Left + (items[^1].Bounds.Width / 2)
            : items[^1].Bounds.Top + (items[^1].Bounds.Height / 2);
        double step = (last - first) / (items.Length - 1);
        ApplyMeasuredMove(items, item =>
        {
            int index = Array.IndexOf(items, item);
            double current = horizontal
                ? item.Bounds.Left + (item.Bounds.Width / 2)
                : item.Bounds.Top + (item.Bounds.Height / 2);
            double target = first + (step * index);
            return horizontal ? new CanvasPoint(target - current, 0) : new CanvasPoint(0, target - current);
        });
    }

    private void MoveSelectionToZOrder(bool front)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        int boundary = front
            ? project.Cues.Max(cue => cue.ZOrder)
            : project.Cues.Min(cue => cue.ZOrder);
        editor.SetZOrder(selectedCueIds, front ? boundary + 1 : boundary - 1);
        AfterMutation();
    }

    private void ApplyMeasuredMove(IEnumerable<CanvasCueItem> items, Func<CanvasCueItem, CanvasPoint> deltaFactory)
    {
        if (editor is null)
        {
            return;
        }

        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (CanvasCueItem item in items)
        {
            CanvasPoint delta = deltaFactory(item);
            positions[item.Id] = CanvasGeometry.ToYttPoint(item.Anchor.X + delta.X, item.Anchor.Y + delta.Y,
                YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);
        }

        if (positions.Count > 0)
        {
            editor.MoveCues(positions);
            AfterMutation();
        }
    }

    public Task SeekExactAsync(double milliseconds) => SeekAsync(milliseconds, exact: true);

    public Cue? GetCue(Guid id) => project?.Cues[id];

    public void UpdateCueText(Guid id, string text)
    {
        Cue? cue = project?.Cues[id];
        if (editor is null || cue is null || cue.Sections.Count == 0)
        {
            return;
        }

        editor.SetText(id, 0, text);
        AfterMutation(refreshRows: false);
    }

    public void UpdateCueTiming(Guid id, double startMilliseconds, double endMilliseconds, int track)
    {
        if (editor is null)
        {
            return;
        }

        editor.SetTiming(id, TimeSpan.FromMilliseconds(Math.Max(0, startMilliseconds)),
            TimeSpan.FromMilliseconds(Math.Max(startMilliseconds + 1, endMilliseconds)), track);
        AfterMutation(refreshRows: false);
    }

    public void NudgeSelected(double deltaX, double deltaY)
    {
        if (editor is not null && selectedCueIds.Count > 0)
        {
            editor.Nudge(selectedCueIds, deltaX, deltaY);
            AfterMutation();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (videoSource is not null)
        {
            videoSource.FrameReady -= OnVideoFrameReady;
            videoSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        VideoFrameImage?.Dispose();
        SubtitleImage?.Dispose();
        renderer.Dispose();
    }

    private void InitializeVideoSource()
    {
        if (MpvVideoSource.TryCreate(out MpvVideoSource? source, out string diagnostic))
        {
            MpvVideoSource loadedSource = source!;
            videoSource = loadedSource;
            loadedSource.FrameReady += OnVideoFrameReady;
            VideoStatus = $"libmpv {loadedSource.LibraryVersion} · SW 콜백 렌더링";
            Serilog.Log.Information("libmpv initialized: {Version}; {Path}", loadedSource.LibraryVersion,
                loadedSource.LibraryPath);
        }
        else
        {
            VideoStatus = "libmpv 없음 · 배경 모드";
            Serilog.Log.Warning("libmpv unavailable: {Diagnostic}", diagnostic);
        }

        OpenVideoCommand.NotifyCanExecuteChanged();
    }

    private async Task OpenSubtitleAsync()
    {
        string? path = await dialogs.OpenSubtitleAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            ImportResult result = fileService.Import(path);
            project = result.Project;
            editor = new DocumentEditor(project);
            sourcePath = path;
            UpdateMaximum();
            PositionMilliseconds = Math.Min(
                project.Cues.Select(cue => cue.Start.TotalMilliseconds).DefaultIfEmpty(0).Min() + 1,
                MaximumMilliseconds);
            Status = result.Warnings.Count == 0
                ? $"{Path.GetFileName(path)} — 큐 {project.Cues.Count}개"
                : $"{Path.GetFileName(path)} — {string.Join(" · ", result.Warnings.Select(warning => warning.Message))}";
            selectedCueIds.Clear();
            lastSelectedCueId = null;
            RefreshRowsAndStyles();
            AfterMutation(refreshRows: false);
        }
        catch (Exception exception)
        {
            Status = $"열기 실패: {exception.Message}";
        }
    }

    private async Task OpenVideoAsync()
    {
        if (videoSource is null)
        {
            return;
        }

        string? path = await dialogs.OpenVideoAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            Status = "영상 메타데이터 읽는 중…";
            await videoSource.LoadAsync(path, CancellationToken.None);
            videoLoaded = true;
            UpdateMaximum();
            VideoStatus = $"{Path.GetFileName(path)} · {videoSource.Info.Width}×{videoSource.Info.Height} · " +
                $"{videoSource.Info.NominalFps:0.###} fps (표시용)";
            Status = "영상 로드 완료";
            NotifyVideoState();
        }
        catch (Exception exception)
        {
            videoLoaded = false;
            Status = $"영상 열기 실패: {exception.Message}";
            RenderFallbackFrame();
            NotifyVideoState();
        }
    }

    private async Task SaveAsync()
    {
        if (project is null)
        {
            return;
        }

        string suggestedName = Path.GetFileNameWithoutExtension(sourcePath ?? "subtitles") + ".ytt";
        string? path = await dialogs.SaveYttAsync(suggestedName);
        if (path is null)
        {
            return;
        }

        try
        {
            fileService.Export(project, path);
            Status = $"저장 완료: {path}";
        }
        catch (Exception exception)
        {
            Status = $"저장 실패: {exception.Message}";
        }
    }

    private void TogglePlayback()
    {
        if (videoSource is null || !videoLoaded)
        {
            return;
        }

        if (videoSource.IsPlaying)
        {
            videoSource.Pause();
        }
        else
        {
            videoSource.Play();
        }

        NotifyVideoState();
    }

    private void StepFrame(int delta)
    {
        videoSource?.StepFrame(delta);
        NotifyVideoState();
    }

    private void Undo()
    {
        editor?.Undo();
        AfterMutation(refreshRows: true);
    }

    private void Redo()
    {
        editor?.Redo();
        AfterMutation(refreshRows: true);
    }

    private void AddCue()
    {
        if (editor is null)
        {
            project = new SubtitleProject();
            editor = new DocumentEditor(project);
        }

        Cue cue = editor.AddCue(TimeSpan.FromMilliseconds(PositionMilliseconds),
            TimeSpan.FromMilliseconds(PositionMilliseconds + 2000), "새 자막");
        RefreshRowsAndStyles();
        SelectCue(cue.Id, toggle: false);
        AfterMutation(refreshRows: false);
    }

    private void DeleteSelectedCues()
    {
        editor?.RemoveCues(selectedCueIds.ToArray());
        selectedCueIds.Clear();
        lastSelectedCueId = null;
        AfterMutation(refreshRows: true);
    }

    private void DuplicateSelectedCues()
    {
        if (editor is null)
        {
            return;
        }

        IReadOnlyList<Cue> copies = editor.DuplicateCues(selectedCueIds);
        selectedCueIds.Clear();
        foreach (Cue cue in copies)
        {
            selectedCueIds.Add(cue.Id);
            lastSelectedCueId = cue.Id;
        }

        AfterMutation(refreshRows: true);
    }

    private void AddStyle()
    {
        StylePreset? style = editor?.AddStyle($"스타일 {Styles.Count}");
        RefreshRowsAndStyles();
        if (style is not null)
        {
            SelectedStyleId = style.Id;
        }
    }

    private void RenameSelectedStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        editor.RenameStyle(id, selectedStyleName);
        RefreshRowsAndStyles();
        AfterMutation(refreshRows: false);
    }

    private void SaveSelectedCueAsStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty ||
            SelectedFormat is not ResolvedFormat format || SelectedCue is not Cue cue)
        {
            return;
        }

        editor.UpdateStyle(id, new SectionFormatPatch
        {
            Font = format.Font,
            SizePercent = format.SizePercent,
            Bold = format.Bold,
            Italic = format.Italic,
            Underline = format.Underline,
            Offset = format.Offset,
            Foreground = format.Foreground,
            Background = format.Background,
            SecondaryColor = format.SecondaryColor,
            Edge = format.Edge,
            EdgeColor = format.EdgeColor,
            Pack = format.Pack,
        }, cue.Anchor, cue.Justify);
        RefreshRowsAndStyles();
        AfterMutation();
    }

    private void ApplySelectedStyle()
    {
        if (editor is null || selectedStyleId is not Guid id || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.ApplyStyle(selectedCueIds, id == Guid.Empty ? null : id);
        AfterMutation(refreshRows: true);
    }

    private void CommitInlineEdit()
    {
        if (IsInlineEditing)
        {
            SelectedText = InlineText;
            IsInlineEditing = false;
            CommitInlineEditCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DeleteSelectedStyleAsync()
    {
        if (editor is null || selectedStyleId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        bool confirmed = await dialogs.ConfirmAsync("스타일 삭제",
            "참조 중인 자막은 현재 해석된 값을 override로 굳혀 외형을 유지합니다. 삭제할까요?");
        if (!confirmed)
        {
            return;
        }

        editor.DeleteStyle(id);
        SelectedStyleId = Guid.Empty;
        RefreshRowsAndStyles();
        AfterMutation(refreshRows: false);
    }

    private void ApplyPosition(double? x, double? y)
    {
        if (editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        Dictionary<Guid, CanvasPoint> positions = selectedCueIds.ToDictionary(
            id => id,
            id => new CanvasPoint(x ?? project.Cues[id]!.PositionX, y ?? project.Cues[id]!.PositionY));
        editor.MoveCues(positions);
        AfterMutation();
    }

    private void ApplyAnchor(AnchorPoint anchor)
    {
        if (editor is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.BeginTransaction("앵커 변경");
        foreach (Guid id in selectedCueIds)
        {
            CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null)
            {
                continue;
            }

            CanvasPoint ytt = CanvasGeometry.PreserveBoxForAnchor(item.Bounds, anchor,
                YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);
            editor.SetAnchor(id, anchor, ytt.X, ytt.Y);
        }

        editor.EndTransaction();
        AfterMutation();
    }

    private void ApplyFormat(SectionFormatPatch patch)
    {
        if (editor is null || selectedCueIds.Count == 0)
        {
            return;
        }

        editor.ApplyFormat(selectedCueIds, patch);
        AfterMutation();
    }

    private async Task SeekAsync(double milliseconds, bool exact)
    {
        try
        {
            if (videoSource is not null)
            {
                await videoSource.SeekAsync(TimeSpan.FromMilliseconds(milliseconds), exact);
            }
        }
        catch (Exception exception)
        {
            Status = $"시크 실패: {exception.Message}";
        }
    }

    private void OnVideoFrameReady()
    {
        if (Interlocked.Exchange(ref frameUpdatePending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(BlitLatestFrame, DispatcherPriority.Render);
    }

    private unsafe void BlitLatestFrame()
    {
        Interlocked.Exchange(ref frameUpdatePending, 0);
        if (disposed || videoSource is null || !videoSource.TryLockLatestFrame(out VideoFrameLock frame))
        {
            return;
        }

        using (frame)
        {
            WriteableBitmap bitmap;
            if (VideoFrameImage is not WriteableBitmap existing || existing.PixelSize.Width != frame.Width ||
                existing.PixelSize.Height != frame.Height)
            {
                bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                VideoFrameImage = bitmap;
            }
            else
            {
                bitmap = existing;
            }

            using ILockedFramebuffer destination = bitmap.Lock();
            fixed (byte* source = frame.Pixels)
            {
                for (int row = 0; row < frame.Height; row++)
                {
                    Buffer.MemoryCopy(source + (row * frame.Stride),
                        (byte*)destination.Address + (row * destination.RowBytes),
                        destination.RowBytes,
                        Math.Min(frame.Width * 4, destination.RowBytes));
                }
            }
        }

        OnPropertyChanged(nameof(VideoFrameImage));
        updatingFromVideo = true;
        PositionMilliseconds = videoSource.Position.TotalMilliseconds;
        updatingFromVideo = false;
        NotifyVideoState();
    }

    private void RenderFallbackFrame()
    {
        if (videoLoaded || disposed)
        {
            return;
        }

        using SKBitmap bitmap = new(new SKImageInfo(YttConstants.ReferenceWidth, YttConstants.ReferenceHeight,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        if (UseCheckerboard)
        {
            DrawCheckerboard(canvas, bitmap.Width, bitmap.Height);
        }
        else
        {
            canvas.Clear(new SKColor(32, 32, 32));
        }

        VideoFrameImage = EncodeBitmap(bitmap);
    }

    private void RenderSubtitlePreview()
    {
        if (project is null || disposed)
        {
            SubtitleImage = null;
            CanvasItems = [];
            OnPropertyChanged(nameof(CanvasItems));
            return;
        }

        using SKBitmap bitmap = new(new SKImageInfo(YttConstants.ReferenceWidth, YttConstants.ReferenceHeight,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);
        TimeSpan time = TimeSpan.FromMilliseconds(PositionMilliseconds);
        double framesPerSecond = project.Video?.NominalFps is > 0 ? project.Video.NominalFps : 30;
        long frameIndex = checked((long)Math.Floor(time.TotalSeconds * framesPerSecond));
        PlayerViewport viewport = PlayerViewport.VideoFrame(bitmap.Width, bitmap.Height);
        renderer.Render(canvas, viewport, project, time, new RenderOptions { FrameIndex = frameIndex });
        SubtitleImage = EncodeBitmap(bitmap);
        CanvasItems = renderer.Measure(viewport, project, time)
            .Select(hit => new CanvasCueItem(
                hit.Cue.Id,
                new CanvasRect(hit.Bounds.Left, hit.Bounds.Top, hit.Bounds.Width, hit.Bounds.Height),
                new CanvasPoint(hit.AnchorScreenPoint.X, hit.AnchorScreenPoint.Y),
                hit.Cue.Anchor,
                selectedCueIds.Contains(hit.Cue.Id)))
            .ToArray();
        OnPropertyChanged(nameof(CanvasItems));
    }

    private static Bitmap EncodeBitmap(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        return new Bitmap(stream);
    }

    private void AfterMutation(bool refreshRows = false)
    {
        if (refreshRows)
        {
            RefreshRowsAndStyles();
        }

        UpdateMaximum();
        RenderSubtitlePreview();
        NotifySelectionProperties();
        NotifyCommandStates();
    }

    private void RefreshRowsAndStyles()
    {
        CueRows.Clear();
        Styles.Clear();
        Styles.Add(new StyleOption(Guid.Empty, project?.Styles.Default.Name ?? "Default"));
        if (project is null)
        {
            selectedStyleId = Guid.Empty;
            selectedStyleName = string.Empty;
            return;
        }

        int number = 1;
        foreach (Cue cue in project.Cues.OrderBy(cue => cue.Start))
        {
            CueRows.Add(new CueRowViewModel(this, cue.Id, number++));
        }

        foreach (StylePreset style in project.Styles.Where(style => style.Id != Guid.Empty).OrderBy(style => style.Name))
        {
            Styles.Add(new StyleOption(style.Id, style.Name));
        }

        if (selectedStyleId is not Guid id || !Styles.Any(style => style.Id == id))
        {
            selectedStyleId = Guid.Empty;
        }

        selectedStyleName = Styles.FirstOrDefault(style => style.Id == selectedStyleId)?.Name ?? string.Empty;

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(SelectedStyleId));
        OnPropertyChanged(nameof(SelectedStyleOption));
        OnPropertyChanged(nameof(SelectedStyleName));
    }

    private void RefreshCanvasSelection()
    {
        CanvasItems = CanvasItems.Select(item => item with { Selected = selectedCueIds.Contains(item.Id) }).ToArray();
        OnPropertyChanged(nameof(CanvasItems));
    }

    private void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasMixedSelection));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectedPositionX));
        OnPropertyChanged(nameof(SelectedPositionY));
        OnPropertyChanged(nameof(SelectedPositionXText));
        OnPropertyChanged(nameof(SelectedPositionYText));
        OnPropertyChanged(nameof(SelectedAnchor));
        OnPropertyChanged(nameof(SelectedAnchorDisplay));
        OnPropertyChanged(nameof(SelectedJustification));
        OnPropertyChanged(nameof(SelectedJustificationDisplay));
        OnPropertyChanged(nameof(SelectedDirection));
        OnPropertyChanged(nameof(SelectedCueStyleOption));
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(SelectedSizePercent));
        OnPropertyChanged(nameof(SelectedSizePercentValue));
        OnPropertyChanged(nameof(SelectedBold));
        OnPropertyChanged(nameof(SelectedItalic));
        OnPropertyChanged(nameof(SelectedUnderline));
        OnPropertyChanged(nameof(SelectedScriptOffset));
        OnPropertyChanged(nameof(SelectedPack));
        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(ForegroundHex));
        OnPropertyChanged(nameof(ForegroundOpacity));
        OnPropertyChanged(nameof(BackgroundHex));
        OnPropertyChanged(nameof(BackgroundOpacity));
        OnPropertyChanged(nameof(EdgeColorHex));
        OnPropertyChanged(nameof(EdgeOpacity));
        OnPropertyChanged(nameof(MoveEffectEnabled));
        OnPropertyChanged(nameof(FadeEffectEnabled));
        OnPropertyChanged(nameof(ShakeEffectEnabled));
        OnPropertyChanged(nameof(ChromaEffectEnabled));
        OnPropertyChanged(nameof(AnimateEffectEnabled));

        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        PlayPauseCommand.NotifyCanExecuteChanged();
        StepBackCommand.NotifyCanExecuteChanged();
        StepForwardCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        AddCueCommand.NotifyCanExecuteChanged();
        DeleteCueCommand.NotifyCanExecuteChanged();
        DuplicateCueCommand.NotifyCanExecuteChanged();
        AddStyleCommand.NotifyCanExecuteChanged();
        DeleteStyleCommand.NotifyCanExecuteChanged();
        RenameStyleCommand.NotifyCanExecuteChanged();
        SaveCueAsStyleCommand.NotifyCanExecuteChanged();
        ApplySelectedStyleCommand.NotifyCanExecuteChanged();
        AlignLeftCommand.NotifyCanExecuteChanged();
        AlignCenterCommand.NotifyCanExecuteChanged();
        AlignRightCommand.NotifyCanExecuteChanged();
        AlignTopCommand.NotifyCanExecuteChanged();
        AlignMiddleCommand.NotifyCanExecuteChanged();
        AlignBottomCommand.NotifyCanExecuteChanged();
        DistributeHorizontalCommand.NotifyCanExecuteChanged();
        DistributeVerticalCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
    }

    private void NotifyVideoState()
    {
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
        NotifyCommandStates();
    }

    private void UpdateMaximum()
    {
        double cueMaximum = project?.Cues.Select(cue => cue.End.TotalMilliseconds).DefaultIfEmpty(1).Max() ?? 1;
        double videoMaximum = videoLoaded ? videoSource?.Info.Duration.TotalMilliseconds ?? 1 : 1;
        MaximumMilliseconds = Math.Max(1, Math.Max(cueMaximum, videoMaximum));
    }

    private static bool Intersects(CanvasRect first, CanvasRect second)
        => first.Left <= second.Right && first.Right >= second.Left &&
            first.Top <= second.Bottom && first.Bottom >= second.Top;

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

    private static void DrawCheckerboard(SKCanvas canvas, int width, int height)
    {
        const int cellSize = 32;
        using SKPaint light = new() { Color = new SKColor(64, 64, 64) };
        using SKPaint dark = new() { Color = new SKColor(40, 40, 40) };
        for (int y = 0; y < height; y += cellSize)
        {
            for (int x = 0; x < width; x += cellSize)
            {
                canvas.DrawRect(x, y, cellSize, cellSize,
                    ((x / cellSize) + (y / cellSize)) % 2 == 0 ? light : dark);
            }
        }
    }

    private void SetImage(ref Bitmap? field, Bitmap? value, [CallerMemberName] string? propertyName = null)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        Bitmap? previous = field;
        field = value;
        OnPropertyChanged(propertyName);
        previous?.Dispose();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
