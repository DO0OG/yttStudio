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

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string PlayIcon = "▶";
    private const string PauseIcon = "⏸";
    // 편집 가이드용 여백이다. 유튜브를 측정해 얻은 값이 아니다. 실측에서 확인된 것은
    // 자막 컨테이너가 플레이어 영역과 같다는 사실뿐이고 안전 여백 자체는 재본 적이 없다.
    // 측정한 자막 창의 하단 2% 를 안전선으로 쓰면 최대 위치의 큐 바닥이 그 선에 정확히
    // 걸려 W103 의 세로 판정이 영영 발동하지 않는다. 경고로 쓸 수 있는 여백을 남긴다.
    private const double EditorSafeAreaInsetPercent = 5.0;
    private static readonly SKSize ReferencePlayerSize = new(
        YttConstants.ReferenceWidth, YttConstants.ReferenceHeight);

    private readonly IFileDialogService dialogs;
    private readonly PreferencesStore preferencesStore;
    private readonly AppPreferences preferences;
    private readonly SubtitleFileService fileService = new();
    private readonly SkiaSubtitleRenderer renderer;
    private readonly HashSet<Guid> selectedCueIds = [];
    private SubtitleProject? project;
    private DocumentEditor? editor;
    private MpvVideoSource? videoSource;
    private Bitmap? videoFrameImage;
    private Bitmap? subtitleImage;
    private string? sourcePath;
    private string? projectPath;
    private bool unsavedChanges;
    private AutosaveService? autosave;
    private string searchPattern = string.Empty;
    private string replacementText = string.Empty;
    private bool useRegex;
    private bool matchCase;
    private double shiftMilliseconds;
    private double snapThreshold = YttConstants.DefaultSnapThresholdPixels;
    private bool showSafeArea;
    private bool showAnchors;
    private string status = "자막 또는 영상을 열어 주세요.";
    private string videoStatus = "libmpv 탐색 중";
    private double maximumMilliseconds = 1;
    private double positionMilliseconds;
    private double playbackSpeed = 1;
    private double volume = 100;
    private bool isMuted;
    private bool useCheckerboard;
    private bool videoLoaded;
    private bool updatingFromVideo;
    private int frameUpdatePending;
    // 타임라인을 끄는 동안 탐색이 나가는 최소 간격이다. 초당 열몇 번이면 눈으로는
    // 이어져 보이고, 그 위로 올리면 디코딩과 화면 전송만 포화된다.
    private const int MinimumScrubIntervalMilliseconds = 70;
    private int playbackScaleDivisor = 1;
    private bool seekInFlight;
    private double? pendingSeekMilliseconds;
    private bool pendingSeekExact;
    private long lastSeekDispatchedAt;
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
    private double inlineEditorHeight = InlineEditorPlacement.DefaultHeight;
    private FontFamily inlineEditorFontFamily = new("Roboto");
    private double inlineEditorFontSize = YttConstants.DefaultFontSizePixels;
    private IBrush inlineEditorForeground = Brushes.White;
    private TextAlignment inlineEditorTextAlignment = TextAlignment.Center;
    private Thickness inlineEditorPadding;
    private Guid? inlineEditCueId;
    private int inlineEditSectionIndex;
    private string inlineEditOriginalText = string.Empty;
    private Guid? pendingInlineEditCueId;
    private bool inlineEditOriginalUnsavedChanges;
    private bool inlineEditIncludesNewCue;
    private InlineEditorStyle? inlineEditorStyle;
    private CanvasRect? inlineEditReferenceBounds;
    private Rect inlineEditorContentBounds;
    private Rect inlineEditorViewport;
    private bool inlineEditorUsesReferencePlacement;
    private readonly Dictionary<(Guid CueId, int SectionIndex), int> canvasResizeBaselines = [];
    private readonly Dictionary<Guid, CanvasResizeGeometry> canvasResizeGeometry = [];
    private bool canvasResizeActive;
    private bool canvasResizeChanged;
    private bool canvasResizeOriginalUnsavedChanges;
    private bool disposed;
    private bool validationHasRun;
    private bool karaokeTimelineTransaction;
    private AppThemeMode themeMode;
    private PreviewViewportMode previewViewportMode = PreviewViewportMode.VideoFrame;
    private PlayerViewport previewViewport = PlayerViewport.VideoFrame(ReferencePlayerSize);
    private SKSize fullscreenPlayerSize = ReferencePlayerSize;
    private string mpvPath = string.Empty;
    private string? loadedVideoPath;
    private SettingsWindow? settingsWindow;
    private readonly MpvAutoInstaller? mpvAutoInstaller =
        MpvAutoInstaller.IsWindowsInstallationSupported ? new MpvAutoInstaller() : null;

    public MainWindowViewModel(IFileDialogService dialogs)
        : this(dialogs, null)
    {
    }

    public MainWindowViewModel(IFileDialogService dialogs, PreferencesStore? preferencesStore)
    {
        this.dialogs = dialogs;
        this.preferencesStore = preferencesStore ?? new PreferencesStore();
        preferences = this.preferencesStore.Load();
        preferences.AutosaveIntervalSeconds = NormalizeAutosaveIntervalSeconds(
            preferences.AutosaveIntervalSeconds);
        themeMode = preferences.Theme;
        previewViewportMode = AppPreferences.NormalizePreviewViewportMode(
            preferences.PreviewViewportMode);
        preferences.PreviewViewportMode = previewViewportMode;
        snapThreshold = Math.Clamp(preferences.SnapThreshold, 0, 64);
        volume = double.IsFinite(preferences.Volume) ? Math.Clamp(preferences.Volume, 0, 100) : 100;
        isMuted = preferences.IsMuted;
        playbackScaleDivisor = Math.Clamp(preferences.PlaybackScaleDivisor, 1, 8);
        mpvPath = NormalizeMpvPath(preferences.MpvPath);
        if (!string.IsNullOrWhiteSpace(mpvPath))
        {
            Environment.SetEnvironmentVariable("YTTSTUDIO_MPV_PATH", mpvPath);
        }
        Loc.Language = preferences.Language;
        // 모든 표시 문자열은 로컬라이저를 거친다. 그래야 언어를
        // 전환하면 화면을 다시 만들지 않고도 전체 바인딩을 다시 읽는다.
        Loc.LanguageChanged += OnLanguageChanged;
        renderer = new SkiaSubtitleRenderer(new BundledFontResolver(
            message => Serilog.Log.Information("{FontResolution}", message)));
        previewViewport = CreatePlayerViewport(ReferencePlayerSize);
        RestartAutosave(preferences.AutosaveEnabled, preferences.AutosaveIntervalSeconds);
        InitializeProjectAndPlaybackCommands();
        InitializeEditorCommands();
        InitializeValidationCommands();
        InitializeVideoSource();
        RenderFallbackFrame();
    }

    private void InitializeProjectAndPlaybackCommands()
    {
        ExitCommand = new DelegateCommand(RequestShutdown);
        AboutCommand = new AsyncCommand(ShowAboutAsync);
        OpenProjectCommand = new AsyncCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncCommand(SaveProjectAsync, () => project is not null);
        ReplaceAllCommand = new DelegateCommand(
            ReplaceAll,
            () => editor is not null && !string.IsNullOrEmpty(searchPattern));
        ShiftSelectedCommand = new DelegateCommand(
            () => ShiftTimes(selectedOnly: true),
            () => editor is not null && selectedCueIds.Count > 0);
        ShiftAllCommand = new DelegateCommand(
            () => ShiftTimes(selectedOnly: false),
            () => editor is not null);
        OpenSubtitleCommand = new AsyncCommand(OpenSubtitleAsync);
        OpenVideoCommand = new AsyncCommand(OpenVideoAsync, () => videoSource is not null);
        SaveCommand = new AsyncCommand(SaveAsync, () => project is not null);
        PlayPauseCommand = new DelegateCommand(TogglePlayback, () => videoLoaded);
        StepBackCommand = new DelegateCommand(() => StepFrame(-1), () => videoLoaded);
        StepForwardCommand = new DelegateCommand(() => StepFrame(1), () => videoLoaded);
        SelectVideoFrameViewportCommand = new DelegateCommand(
            () => SelectedViewportMode = PreviewViewportMode.VideoFrame);
        SelectYouTubeDefaultViewportCommand = new DelegateCommand(
            () => SelectedViewportMode = PreviewViewportMode.YouTubeDefault);
        SelectYouTubeTheaterViewportCommand = new DelegateCommand(
            () => SelectedViewportMode = PreviewViewportMode.YouTubeTheater);
        SelectYouTubeFullscreenViewportCommand = new DelegateCommand(
            () => SelectedViewportMode = PreviewViewportMode.YouTubeFullscreen);
    }

    private void InitializeEditorCommands()
    {
        UndoCommand = new DelegateCommand(Undo, () => !isInlineEditing && editor?.CanUndo == true);
        RedoCommand = new DelegateCommand(Redo, () => !isInlineEditing && editor?.CanRedo == true);
        AddCueCommand = new DelegateCommand(AddCue, () => !isInlineEditing && project is not null);
        DeleteCueCommand = new DelegateCommand(DeleteSelectedCues,
            () => !isInlineEditing && selectedCueIds.Count > 0);
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
    }

    private void InitializeValidationCommands()
    {
        ValidateCommand = new DelegateCommand(RunValidation, () => project is not null);
        ApplyValidationFixCommand = new DelegateCommand(ApplySelectedValidationFix);
        GoToValidationIssueCommand = new DelegateCommand(GoToSelectedValidationIssue);
        AutoSplitKaraokeCommand = new DelegateCommand(
            AutoSplitSelectedKaraokeCue,
            () => HasKaraokeCue);
        SelectMpvPathCommand = new AsyncCommand(SelectMpvPathAsync);
        ApplyMpvPathCommand = new AsyncCommand(ApplyMpvPathAsync);
        OpenMpvInstallationGuideCommand = new DelegateCommand(OpenMpvInstallationGuide);
        OpenSettingsCommand = new AsyncCommand(OpenSettingsAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 모든 뷰 요소가 <c>{Binding Loc[Key]}</c> 로 바인딩하는 문자열 테이블을 가져온다.
    /// 한국어와 영어와 일본어를 제공한다.
    /// </summary>
    public Localizer Loc { get; } = new();

    /// <summary>검색과 치환에 쓰는 패턴을 가져온다.</summary>
    public string SearchPattern
    {
        get => searchPattern;
        set
        {
            if (searchPattern == value)
            {
                return;
            }

            searchPattern = value;
            OnPropertyChanged();
            ReplaceAllCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary><see cref="ReplaceAllCommand"/> 가 적용할 치환 텍스트를 가져온다.</summary>
    public string ReplacementText
    {
        get => replacementText;
        set
        {
            if (replacementText == value)
            {
                return;
            }

            replacementText = value;
            OnPropertyChanged();
        }
    }

    /// <summary><see cref="SearchPattern"/> 을 정규식으로 다루는지 가져온다.</summary>
    public bool UseRegex
    {
        get => useRegex;
        set
        {
            if (useRegex == value)
            {
                return;
            }

            useRegex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>검색이 대소문자를 구분하는지 가져온다.</summary>
    public bool MatchCase
    {
        get => matchCase;
        set
        {
            if (matchCase == value)
            {
                return;
            }

            matchCase = value;
            OnPropertyChanged();
        }
    }

    /// <summary>일괄 시간 이동량을 밀리초로 가져온다. 음수는 큐를 앞으로 당긴다.</summary>
    public double ShiftMilliseconds
    {
        get => shiftMilliseconds;
        set
        {
            if (Math.Abs(shiftMilliseconds - value) < double.Epsilon)
            {
                return;
            }

            shiftMilliseconds = value;
            OnPropertyChanged();
        }
    }

    /// <summary>선택 가능한 루비 역할을 가져온다.</summary>
    public IReadOnlyList<RubyRole> RubyRoles { get; } =
        [RubyRole.None, RubyRole.Base, RubyRole.Above, RubyRole.Below];

    /// <summary>선택한 큐 첫 섹션의 루비 역할을 가져오거나 설정한다.</summary>
    public RubyRole? SelectedRubyRole
    {
        get => FirstSelectedSection()?.Ruby;
        set
        {
            if (editor is null || value is null)
            {
                return;
            }

            Cue? cue = SingleSelectedCue();
            if (cue is null)
            {
                return;
            }

            editor.SetRuby(cue.Id, 0, value.Value, cue.Sections[0].RubyText);
            AfterMutation(refreshRows: true);
            OnPropertyChanged();
        }
    }

    /// <summary>선택한 큐 첫 섹션의 루비 텍스트를 가져오거나 설정한다.</summary>
    public string? SelectedRubyText
    {
        get => FirstSelectedSection()?.RubyText;
        set
        {
            if (editor is null)
            {
                return;
            }

            Cue? cue = SingleSelectedCue();
            if (cue is null)
            {
                return;
            }

            editor.SetRuby(cue.Id, 0, cue.Sections[0].Ruby, value);
            AfterMutation(refreshRows: true);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 드래그 스냅 임계값을 픽셀로 가져오거나 설정한다. 기본값은 8 이다.
    /// </summary>
    public double SnapThreshold
    {
        get => snapThreshold;
        set
        {
            double clamped = Math.Clamp(value, 0, 64);
            if (Math.Abs(snapThreshold - clamped) < double.Epsilon)
            {
                return;
            }

            snapThreshold = clamped;
            preferences.SnapThreshold = clamped;
            SavePreferences();
            OnPropertyChanged();
        }
    }

    /// <summary>미리보기에 세이프 에어리어 안내선을 표시할지 결정한다.</summary>
    public bool ShowSafeArea
    {
        get => showSafeArea;
        set
        {
            if (showSafeArea == value)
            {
                return;
            }

            showSafeArea = value;
            OnPropertyChanged();
            RenderSubtitlePreview();
        }
    }

    /// <summary>미리보기에 앵커 마커를 표시할지 결정한다.</summary>
    public bool ShowAnchors
    {
        get => showAnchors;
        set
        {
            if (showAnchors == value)
            {
                return;
            }

            showAnchors = value;
            OnPropertyChanged();
            RenderSubtitlePreview();
        }
    }

    /// <summary>프리뷰에서 선택할 수 있는 데스크톱 뷰포트 모드다.</summary>
    public IReadOnlyList<PreviewViewportMode> ViewportModes { get; } =
    [
        PreviewViewportMode.VideoFrame,
        PreviewViewportMode.YouTubeDefault,
        PreviewViewportMode.YouTubeTheater,
        PreviewViewportMode.YouTubeFullscreen,
    ];

    /// <summary>현재 선택한 프리뷰 뷰포트 모드를 가져오거나 설정한다.</summary>
    public PreviewViewportMode SelectedViewportMode
    {
        get => previewViewportMode;
        set
        {
            PreviewViewportMode normalized = AppPreferences.NormalizePreviewViewportMode(value);
            if (previewViewportMode == normalized)
            {
                return;
            }

            previewViewportMode = normalized;
            preferences.PreviewViewportMode = normalized;
            SavePreferences();
            SetPreviewViewport(CreatePlayerViewport(GetPreviewPlayerSize()));
            OnPropertyChanged(nameof(SelectedViewportMode));
            OnPropertyChanged(nameof(ViewportMode));
            OnPropertyChanged(nameof(SelectedViewportModeDisplayName));
            OnPropertyChanged(nameof(IsVideoFrameViewport));
            OnPropertyChanged(nameof(IsYouTubeDefaultViewport));
            OnPropertyChanged(nameof(IsYouTubeTheaterViewport));
            OnPropertyChanged(nameof(IsYouTubeFullscreenViewport));
            if (!videoLoaded)
            {
                RenderFallbackFrame();
            }

            RenderSubtitlePreview();
            if (validationHasRun)
            {
                RunValidation();
            }
        }
    }

    /// <summary>기존 호출자가 사용할 수 있는 현재 모드 별칭이다.</summary>
    public PreviewViewportMode ViewportMode
    {
        get => SelectedViewportMode;
        set => SelectedViewportMode = value;
    }

    /// <summary>현재 렌더러와 편집 캔버스가 공유하는 플레이어 뷰포트다.</summary>
    public PlayerViewport PreviewViewport => previewViewport;

    /// <summary>프리뷰 오버레이가 사용하는 자막 공간을 Avalonia 좌표로 가져온다.</summary>
    public Rect PreviewSubtitleSpace => ToAvaloniaRect(previewViewport.SubtitleSpace);

    public double PreviewPlayerWidth => previewViewport.PlayerSize.Width;
    public double PreviewPlayerHeight => previewViewport.PlayerSize.Height;
    public double PreviewVideoContentLeft => previewViewport.VideoContentRect.Left;
    public double PreviewVideoContentTop => previewViewport.VideoContentRect.Top;
    public double PreviewVideoContentWidth => previewViewport.VideoContentRect.Width;
    public double PreviewVideoContentHeight => previewViewport.VideoContentRect.Height;

    /// <summary>현재 선택 모드의 표시 이름을 가져온다.</summary>
    public string SelectedViewportModeDisplayName => previewViewportMode switch
    {
        PreviewViewportMode.YouTubeDefault => Loc["ViewportYouTube"],
        PreviewViewportMode.YouTubeTheater => Loc["ViewportTheater"],
        PreviewViewportMode.YouTubeFullscreen => Loc["ViewportFullscreen"],
        _ => Loc["ViewportVideo"],
    };

    /// <summary>검증 메시지에 사용할 현재 모드 표시 이름이다.</summary>
    public string ViewportModeDisplayName => SelectedViewportModeDisplayName;

    public bool IsVideoFrameViewport => previewViewportMode == PreviewViewportMode.VideoFrame;
    public bool IsYouTubeDefaultViewport => previewViewportMode == PreviewViewportMode.YouTubeDefault;
    public bool IsYouTubeTheaterViewport => previewViewportMode == PreviewViewportMode.YouTubeTheater;
    public bool IsYouTubeFullscreenViewport => previewViewportMode == PreviewViewportMode.YouTubeFullscreen;

    /// <summary>선택 가능한 언어를 표시 순서대로 가져온다.</summary>
    public IReadOnlyList<AppLanguage> Languages { get; } =
        [AppLanguage.Korean, AppLanguage.English, AppLanguage.Japanese];

    /// <summary>활성 언어를 가져오거나 설정한다.</summary>
    public AppLanguage Language
    {
        get => Loc.Language;
        set
        {
            if (Loc.Language == value)
            {
                return;
            }

            Loc.Language = value;
            preferences.Language = value;
            SavePreferences();
        }
    }

    public IReadOnlyList<AppThemeMode> ThemeModes { get; } =
        [AppThemeMode.Default, AppThemeMode.Light, AppThemeMode.Dark];

    public AppThemeMode ThemeMode
    {
        get => themeMode;
        set
        {
            if (!SetField(ref themeMode, value))
            {
                return;
            }

            preferences.Theme = value;
            SavePreferences();
            if (Avalonia.Application.Current is App app)
            {
                app.ApplyTheme(value);
            }
        }
    }

    public string MpvPath
    {
        get => mpvPath;
        set => SetField(ref mpvPath, value ?? string.Empty);
    }

    public ObservableCollection<CueRowViewModel> CueRows { get; } = [];
    public ObservableCollection<StyleOption> Styles { get; } = [];
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = [];
    public ObservableCollection<KaraokeSectionViewModel> KaraokeSections { get; } = [];
    public IReadOnlyList<CanvasCueItem> CanvasItems { get; private set; } = [];
    public IReadOnlyCollection<Guid> SelectedCueIds => selectedCueIds;
    public Array AnchorOptions { get; } = Enum.GetValues<AnchorPoint>();
    public Array JustificationOptions { get; } = Enum.GetValues<Justification>();
    public Array DirectionOptions { get; } = Enum.GetValues<TextDirection>();
    public Array ScriptOffsetOptions { get; } = Enum.GetValues<ScriptOffset>();
    public Array FontOptions { get; } = Enum.GetValues<YtFont>();
    public Array EdgeOptions { get; } = Enum.GetValues<EdgeType>();
    public double[] SpeedOptions { get; } = [0.5, 1.0, 1.5, 2.0];
    public IReadOnlyList<KaraokeTypeOption> KaraokeTypeOptions { get; } =
    [
        new(KaraokeType.Simple, "Simple"),
        new(KaraokeType.Fade, "Fade"),
        new(KaraokeType.Glitch, "Glitch"),
        new(KaraokeType.Cursor, "Cursor"),
        new(KaraokeType.LeftCursor, "LeftCursor"),
    ];
    public bool HasProject => project is not null;
    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
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

    /// <summary>선택한 모든 큐에 적용된 스타일이다. 선택이 섞여 있으면 null 이다.</summary>
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

    private SKRect CurrentSubtitleSpace => previewViewport.SubtitleSpace;

    public void ApplySelectedAnchor(AnchorPoint anchor) => ApplyAnchor(anchor);

    public void ApplySelectedJustification(Justification justification)
        => SelectedJustification = justification;

    public void BeginInlineEdit(
        Guid cueId,
        CanvasRect referenceBounds,
        Rect contentRect,
        Rect viewport)
    {
        if (project?.Cues[cueId] is not Cue)
        {
            return;
        }

        InlineEditorStyle style = ResolveInlineEditorStyle(project.Cues[cueId]!);
        InlineEditorPresentation presentation = InlineEditorPresentationMapper.Scale(
            style, referenceBounds, contentRect, PreviewSubtitleSpace);
        Rect requested = presentation.Bounds;
        Rect clamped = InlineEditorPlacement.Clamp(
            new Rect(requested.X, requested.Y,
                Math.Max(140, requested.Width),
                Math.Max(InlineEditorPlacement.DefaultHeight, requested.Height)),
            viewport);
        BeginInlineEdit(cueId, clamped.Left, clamped.Top, clamped.Width);
        if (!isInlineEditing || inlineEditCueId != cueId)
        {
            return;
        }

        inlineEditorStyle = style;
        inlineEditorUsesReferencePlacement = true;
        inlineEditReferenceBounds = referenceBounds;
        RefreshInlineEditorLayout(contentRect, viewport);
    }

    /// <summary>편집기를 거쳐 큐 텍스트의 모든 일치를 치환해 되돌릴 수 있게 한다.</summary>
    private void ReplaceAll()
    {
        if (editor is null || string.IsNullOrEmpty(searchPattern))
        {
            return;
        }

        try
        {
            TextSearchOptions options = new()
            {
                UseRegex = useRegex,
                CaseSensitive = matchCase,
            };
            int replaced = editor.ReplaceText(searchPattern, replacementText, options);
            Status = $"{Loc["ReplaceAll"]}: {replaced}";
            AfterMutation(refreshRows: true);
        }
        catch (ArgumentException exception)
        {
            // 잘못된 정규식은 사용자 입력이지 크래시 사유가 아니다.
            Status = $"{Loc["UseRegex"]} — {exception.Message}";
        }
    }

}
