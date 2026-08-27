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

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
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
        InitializeVideoSource();
        RenderFallbackFrame();
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

    /// <summary>스타일 식별자를 표시용 이름으로 바꾼다. 알 수 없으면 기본 스타일 이름을 쓴다.</summary>
    internal string StyleNameOf(Guid? styleId)
    {
        if (project is null)
        {
            return "Default";
        }

        StylePreset style = project.GetStyle(styleId);
        return string.IsNullOrWhiteSpace(style.Name) ? "Default" : style.Name;
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

    private static void RequestShutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }

        Window? owner = GetMainWindow();
        if (owner is null)
        {
            return;
        }

        await ShowSettingsDialogAsync(owner);
    }

    private static Window? GetMainWindow()
        => Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    private SettingsViewModel CreateSettingsViewModel()
        => new(
            Loc,
            preferences,
            dialogs,
            language => Language = language,
            theme => ThemeMode = theme,
            ApplyMpvPathFromSettingsAsync,
            () => SnapThreshold,
            value => SnapThreshold = value,
            ApplyAutosaveSettings,
            () => VideoStatus,
            mpvAutoInstaller is null ? null : InstallMpvAndApplyAsync);

    private async Task ShowSettingsDialogAsync(Window owner)
    {
        SettingsViewModel settingsViewModel = CreateSettingsViewModel();
        SettingsWindow window = new() { DataContext = settingsViewModel };
        settingsWindow = window;
        settingsViewModel.CloseRequested += window.Close;
        window.Closed += (_, _) =>
        {
            settingsViewModel.Dispose();
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        };

        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            settingsViewModel.Dispose();
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        }
    }

    private async Task ShowAboutAsync()
    {
        string body = string.Join(
            Environment.NewLine + Environment.NewLine,
            Loc["AboutBody"],
            $"{Loc["AboutVersion"]} v{AppVersion.Current}");
        await dialogs.ConfirmAsync(Loc["MenuAbout"], body, Loc["Close"]);
    }

    private Cue? SingleSelectedCue()
    {
        if (project is null || selectedCueIds.Count != 1)
        {
            return null;
        }

        Cue? cue = project.Cues[selectedCueIds.First()];
        return cue is null || cue.Sections.Count == 0 ? null : cue;
    }

    private Section? FirstSelectedSection() => SingleSelectedCue()?.Sections[0];

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

    private void OnLanguageChanged()
    {
        // 인덱서 바인딩은 인덱서 자체가 무효화될 때만 갱신된다.
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(PlayPauseActionText));
        OnPropertyChanged(nameof(SelectedViewportModeDisplayName));
        OnPropertyChanged(nameof(ViewportModeDisplayName));
        OnPropertyChanged(string.Empty);
    }

    public DelegateCommand ExitCommand { get; }
    public AsyncCommand AboutCommand { get; }
    public AsyncCommand OpenProjectCommand { get; }
    public AsyncCommand SaveProjectCommand { get; }
    public DelegateCommand ReplaceAllCommand { get; }
    public DelegateCommand ShiftSelectedCommand { get; }
    public DelegateCommand ShiftAllCommand { get; }
    public AsyncCommand OpenSubtitleCommand { get; }
    public AsyncCommand OpenVideoCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public DelegateCommand PlayPauseCommand { get; }
    public DelegateCommand StepBackCommand { get; }
    public DelegateCommand StepForwardCommand { get; }
    public DelegateCommand SelectVideoFrameViewportCommand { get; }
    public DelegateCommand SelectYouTubeDefaultViewportCommand { get; }
    public DelegateCommand SelectYouTubeTheaterViewportCommand { get; }
    public DelegateCommand SelectYouTubeFullscreenViewportCommand { get; }
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
    public DelegateCommand AutoSplitKaraokeCommand { get; }
    public AsyncCommand SelectMpvPathCommand { get; }
    public AsyncCommand ApplyMpvPathCommand { get; }
    public DelegateCommand OpenMpvInstallationGuideCommand { get; }
    public AsyncCommand OpenSettingsCommand { get; }
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
    public bool HasVideo => videoLoaded;
    public bool IsPlaying => videoSource?.IsPlaying == true;
    public string PlayPauseLabel => IsPlaying ? PauseIcon : PlayIcon;
    public string PlayPauseActionText => IsPlaying ? Loc["Pause"] : Loc["Play"];

    public Guid? SelectedKaraokeCueId => selectedCueIds.Count == 1 ? lastSelectedCueId : null;

    public bool HasKaraokeCue => SelectedKaraokeCueId.HasValue && editor is not null;

    public double SelectedKaraokeCueDurationMilliseconds
        => SelectedCue is Cue cue ? Math.Max(1, (cue.End - cue.Start).TotalMilliseconds) : 1;

    public KaraokeTypeOption? SelectedKaraokeTypeOption
    {
        get
        {
            KaraokeType type = SelectedCue?.Effects.OfType<KaraokeSettings>().LastOrDefault()?.Type
                ?? KaraokeType.Simple;
            return KaraokeTypeOptions.FirstOrDefault(option => option.Value == type);
        }
        set
        {
            if (value is null || editor is null || SelectedKaraokeCueId is not Guid cueId)
            {
                return;
            }

            editor.SetKaraokeType(cueId, value.Value);
            AfterMutation();
        }
    }

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

    public bool IsInlineEditing
    {
        get => isInlineEditing;
        private set
        {
            if (!SetField(ref isInlineEditing, value))
            {
                return;
            }

            CommitInlineEditCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            DeleteCueCommand.NotifyCanExecuteChanged();
        }
    }

    public string InlineText
    {
        get => inlineText;
        set
        {
            string next = value ?? string.Empty;
            if (inlineText == next)
            {
                return;
            }

            bool appliedToModel = false;
            if (isInlineEditing && editor is not null && inlineEditCueId is Guid cueId &&
                project?.Cues[cueId] is Cue cue &&
                (uint)inlineEditSectionIndex < (uint)cue.Sections.Count)
            {
                Section section = cue.Sections[inlineEditSectionIndex];
                if (section.Text != next)
                {
                    editor.SetText(cueId, inlineEditSectionIndex, next);
                    appliedToModel = true;
                }
            }

            if (SetField(ref inlineText, next) && appliedToModel)
            {
                // SetText is part of the active transaction, while rendering is
                // intentionally immediate so typing is visible in the preview.
                // This path must not mark the document dirty: canceling the
                // session must preserve its pre-session autosave state.
                RefreshInlinePreview();
            }
        }
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

    public double InlineEditorHeight
    {
        get => inlineEditorHeight;
        private set => SetField(ref inlineEditorHeight, value);
    }

    public FontFamily InlineEditorFontFamily
    {
        get => inlineEditorFontFamily;
        private set => SetField(ref inlineEditorFontFamily, value);
    }

    public double InlineEditorFontSize
    {
        get => inlineEditorFontSize;
        private set => SetField(ref inlineEditorFontSize, value);
    }

    public IBrush InlineEditorForeground
    {
        get => inlineEditorForeground;
        private set => SetField(ref inlineEditorForeground, value);
    }

    public TextAlignment InlineEditorTextAlignment
    {
        get => inlineEditorTextAlignment;
        private set => SetField(ref inlineEditorTextAlignment, value);
    }

    public Thickness InlineEditorPadding
    {
        get => inlineEditorPadding;
        private set => SetField(ref inlineEditorPadding, value);
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

        validationHasRun = true;
        SKRect subtitleSpace = previewViewport.SubtitleSpace;
        double horizontalInset = subtitleSpace.Width * EditorSafeAreaInsetPercent / 100.0;
        double verticalInset = subtitleSpace.Height * EditorSafeAreaInsetPercent / 100.0;
        Dictionary<Guid, ValidationMetrics> metrics = [];
        foreach (Cue cue in project.Cues)
        {
            CanvasCueItem? item = CanvasItems.FirstOrDefault(candidate => candidate.Id == cue.Id);
            bool outside = item is not null && (
                item.Bounds.Left < subtitleSpace.Left + horizontalInset ||
                item.Bounds.Top < subtitleSpace.Top + verticalInset ||
                item.Bounds.Right > subtitleSpace.Right - horizontalInset ||
                item.Bounds.Bottom > subtitleSpace.Bottom - verticalInset);
            metrics[cue.Id] = new ValidationMetrics
            {
                MobileEffectRisk = cue.Effects.Count >= 3,
                IsOutsideSafeArea = outside,
                ViewportModeDisplayName = SelectedViewportModeDisplayName,
                BoxWidth = item?.Bounds.Width,
                SubtitleSpaceWidth = subtitleSpace.Width,
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

    public int KaraokeSectionCount(Guid cueId)
        => project?.Cues[cueId]?.Sections.Count ?? 0;

    public void SplitKaraokeSection(Guid cueId, int sectionIndex, int textOffset)
    {
        if (editor is null)
        {
            return;
        }

        editor.SplitKaraokeSection(cueId, sectionIndex, textOffset);
        AfterMutation(refreshRows: true);
    }

    public void RemoveKaraokeSection(Guid cueId, int sectionIndex)
    {
        int count = KaraokeSectionCount(cueId);
        if (count <= 1)
        {
            return;
        }

        MergeKaraokeSections(cueId, sectionIndex > 0 ? sectionIndex - 1 : 0);
    }

    public void MergeKaraokeSections(Guid cueId, int leftSectionIndex)
    {
        if (editor is null)
        {
            return;
        }

        editor.MergeKaraokeSections(cueId, leftSectionIndex);
        AfterMutation(refreshRows: true);
    }

    public void SetKaraokeOffset(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void SetKaraokeOffsetFromTimeline(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void BeginKaraokeTimelineAdjustment()
    {
        if (editor is null || karaokeTimelineTransaction)
        {
            return;
        }

        editor.BeginTransaction("가라오케 타이밍 미세 조정");
        karaokeTimelineTransaction = true;
    }

    public void PreviewKaraokeTimelineOffset(Guid cueId, int sectionIndex, double milliseconds)
        => ApplyKaraokeOffset(cueId, sectionIndex, milliseconds);

    public void EndKaraokeTimelineAdjustment()
    {
        if (editor is null || !karaokeTimelineTransaction)
        {
            return;
        }

        editor.EndTransaction();
        karaokeTimelineTransaction = false;
        NotifyCommandStates();
    }

    public void RecordKaraokeTabForSelectedCue()
    {
        if (editor is null || SelectedCue is not Cue cue)
        {
            return;
        }

        try
        {
            KaraokeEditResult result = editor.RecordKaraokeTab(
                cue.Id,
                TimeSpan.FromMilliseconds(Math.Max(0, PositionMilliseconds - cue.Start.TotalMilliseconds)));
            LogKaraokeCorrections(result);
            AfterMutation();
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
    }

    public void CancelLastKaraokeTabForSelectedCue()
    {
        if (editor is null || SelectedKaraokeCueId is not Guid cueId)
        {
            return;
        }

        try
        {
            editor.CancelLastKaraokeTab(cueId);
            AfterMutation();
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
    }

    private void AutoSplitSelectedKaraokeCue()
    {
        if (editor is null || SelectedKaraokeCueId is not Guid cueId)
        {
            return;
        }

        KaraokeEditResult result = editor.SplitCueIntoKaraokeSections(cueId);
        LogKaraokeCorrections(result);
        AfterMutation(refreshRows: true);
    }

    private void ApplyKaraokeOffset(Guid cueId, int sectionIndex, double milliseconds)
    {
        if (editor is null || !double.IsFinite(milliseconds))
        {
            return;
        }

        KaraokeEditResult result = editor.SetKaraokeOffset(
            cueId,
            sectionIndex,
            TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));
        LogKaraokeCorrections(result);
        AfterMutation();
    }

    private static void LogKaraokeCorrections(KaraokeEditResult result)
    {
        foreach (KaraokeOffsetCorrection correction in result.OffsetCorrections)
        {
            Serilog.Log.Information(
                "Karaoke offset auto-corrected for cue {CueId}, section {SectionIndex}: {PreviousOffset} -> {CorrectedOffset}",
                result.CueId,
                correction.SectionIndex,
                correction.PreviousOffset,
                correction.CorrectedOffset);
        }
    }

    public bool BeginCanvasResize(Guid primaryCueId, int grabbedRow, int grabbedColumn)
    {
        if (canvasResizeActive || isInlineEditing || editor is null || editor.IsTransactionActive || project is null ||
            selectedCueIds.Count == 0 || !selectedCueIds.Contains(primaryCueId) ||
            CanvasItems.FirstOrDefault(item => item.Id == primaryCueId) is not CanvasCueItem primary)
        {
            return false;
        }

        canvasResizeBaselines.Clear();
        canvasResizeGeometry.Clear();
        (int pivotRow, int pivotColumn) = PreviewResizeGeometry.GetPivotCell(grabbedRow, grabbedColumn);
        foreach (Guid cueId in selectedCueIds)
        {
            if (project.Cues[cueId] is not Cue cue)
            {
                continue;
            }

            if (CanvasItems.FirstOrDefault(item => item.Id == cueId) is CanvasCueItem canvasItem &&
                double.IsFinite(canvasItem.Bounds.Width) && double.IsFinite(canvasItem.Bounds.Height))
            {
                canvasResizeGeometry[cueId] = new CanvasResizeGeometry(
                    ToCanvasPoint(cue.PositionX, cue.PositionY),
                    canvasItem.Bounds.Width,
                    canvasItem.Bounds.Height,
                    CanvasResizeGeometry.CellFraction((int)canvasItem.AnchorKind % 3),
                    CanvasResizeGeometry.CellFraction((int)canvasItem.AnchorKind / 3),
                    CanvasResizeGeometry.CellFraction(pivotColumn),
                    CanvasResizeGeometry.CellFraction(pivotRow));
            }

            for (int sectionIndex = 0; sectionIndex < cue.Sections.Count; sectionIndex++)
            {
                Section section = cue.Sections[sectionIndex];
                canvasResizeBaselines[(cue.Id, sectionIndex)] =
                    ResolveSectionFormat(cue, section).SizePercent;
            }
        }

        if (canvasResizeBaselines.Count == 0 ||
            !double.IsFinite(primary.Bounds.X) || !double.IsFinite(primary.Bounds.Y) ||
            !double.IsFinite(primary.Bounds.Width) || !double.IsFinite(primary.Bounds.Height) ||
            primary.Bounds.Width <= 0 || primary.Bounds.Height <= 0)
        {
            canvasResizeBaselines.Clear();
            canvasResizeGeometry.Clear();
            return false;
        }

        try
        {
            canvasResizeOriginalUnsavedChanges = unsavedChanges;
            editor.BeginTransaction("자막 크기 변경");
        }
        catch
        {
            canvasResizeBaselines.Clear();
            canvasResizeGeometry.Clear();
            throw;
        }

        canvasResizeActive = true;
        canvasResizeChanged = false;
        return true;
    }

    public void PreviewCanvasResize(double multiplier)
    {
        if (!canvasResizeActive || editor is null || project is null)
        {
            return;
        }

        double normalizedMultiplier = double.IsFinite(multiplier) ? Math.Max(0, multiplier) : 1.0;
        bool applied = false;
        foreach (KeyValuePair<(Guid CueId, int SectionIndex), int> baseline in canvasResizeBaselines)
        {
            Guid cueId = baseline.Key.CueId;
            int sectionIndex = baseline.Key.SectionIndex;
            int baselineSizePercent = baseline.Value;
            if (project.Cues[cueId] is not Cue cue ||
                (uint)sectionIndex >= (uint)cue.Sections.Count)
            {
                continue;
            }

            Section section = cue.Sections[sectionIndex];
            int targetSizePercent = PreviewResizeGeometry.ComputeSizePercent(
                baselineSizePercent, normalizedMultiplier);
            if (ResolveSectionFormat(cue, section).SizePercent == targetSizePercent)
            {
                continue;
            }

            // The copy keeps every existing override (including style, color,
            // edge, and pack values) and changes only the explicit size.
            editor.SetFormatOverrides(
                cueId,
                sectionIndex,
                section.Overrides.WithSizePercent(targetSizePercent));
            applied = true;
        }

        canvasResizeChanged = canvasResizeBaselines.Any(item =>
            PreviewResizeGeometry.ComputeSizePercent(item.Value, normalizedMultiplier) != item.Value);
        applied |= ApplyCanvasResizeCompensation(normalizedMultiplier);
        if (applied)
        {
            AfterMutation();
        }
    }

    /// <summary>
    /// 글자만 키우면 박스가 앵커를 중심으로 양쪽으로 자란다. 맞은편 고정점이 제자리에
    /// 머무르도록 앵커 좌표를 되밀어, 잡은 조절점 방향으로만 자라는 것처럼 보이게 한다.
    /// </summary>
    private bool ApplyCanvasResizeCompensation(double multiplier)
    {
        if (editor is null || project is null || canvasResizeGeometry.Count == 0)
        {
            return false;
        }

        Dictionary<Guid, CanvasPoint> positions = [];
        foreach (KeyValuePair<Guid, CanvasResizeGeometry> entry in canvasResizeGeometry)
        {
            if (project.Cues[entry.Key] is not Cue cue ||
                !canvasResizeBaselines.TryGetValue((entry.Key, 0), out int baselineSizePercent) ||
                baselineSizePercent <= 0)
            {
                continue;
            }

            // The clamp can refuse part of the requested multiplier, so pin the
            // box against the size that was actually applied, not the request.
            double achievedScale =
                (double)PreviewResizeGeometry.ComputeSizePercent(baselineSizePercent, multiplier) /
                baselineSizePercent;
            CanvasPoint compensated = entry.Value.GetCompensatedAnchor(achievedScale);
            CanvasPoint target = ToYttPoint(compensated.X, compensated.Y);
            if (Math.Abs(cue.PositionX - target.X) > double.Epsilon ||
                Math.Abs(cue.PositionY - target.Y) > double.Epsilon)
            {
                positions[entry.Key] = target;
            }
        }

        if (positions.Count == 0)
        {
            return false;
        }

        editor.MoveCues(positions);
        return true;
    }

    public void EndCanvasResize(double multiplier)
    {
        if (!canvasResizeActive || editor is null)
        {
            return;
        }

        PreviewCanvasResize(multiplier);
        bool changed = canvasResizeChanged;
        bool originalUnsavedChanges = canvasResizeOriginalUnsavedChanges;
        if (editor.IsTransactionActive)
        {
            if (changed)
            {
                editor.EndTransaction();
            }
            else
            {
                editor.CancelTransaction();
            }
        }

        ClearCanvasResizeState();
        if (!changed)
        {
            RefreshAfterCanvasResizeCancel(originalUnsavedChanges);
        }
        else
        {
            NotifyCommandStates();
        }
    }

    public void CancelCanvasResize()
    {
        if (!canvasResizeActive || editor is null)
        {
            return;
        }

        bool originalUnsavedChanges = canvasResizeOriginalUnsavedChanges;
        if (editor.IsTransactionActive)
        {
            editor.CancelTransaction();
        }

        ClearCanvasResizeState();
        RefreshAfterCanvasResizeCancel(originalUnsavedChanges);
    }

    private void RefreshAfterCanvasResizeCancel(bool originalUnsavedChanges)
    {
        RefreshRowsAndStyles();
        ReconcileSelection();
        UpdateMaximum();
        RenderSubtitlePreview();
        NotifySelectionProperties();
        unsavedChanges = originalUnsavedChanges;
        NotifyCommandStates();
    }

    private void ClearCanvasResizeState()
    {
        canvasResizeBaselines.Clear();
        canvasResizeGeometry.Clear();
        canvasResizeActive = false;
        canvasResizeChanged = false;
        canvasResizeOriginalUnsavedChanges = false;
    }

    private ResolvedFormat ResolveSectionFormat(Cue cue, Section section)
    {
        ArgumentNullException.ThrowIfNull(project);
        StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
        return FormatResolver.Resolve(style.BaseFormat, section.Overrides);
    }

    private SKRect CurrentSubtitleSpace => previewViewport.SubtitleSpace;

    private CanvasPoint ToCanvasPoint(double positionX, double positionY)
    {
        SKRect space = CurrentSubtitleSpace;
        CanvasPoint point = CanvasGeometry.ToCanvasPoint(
            positionX, positionY, space.Width, space.Height);
        return new CanvasPoint(point.X + space.Left, point.Y + space.Top);
    }

    private CanvasPoint ToYttPoint(double pixelX, double pixelY)
    {
        SKRect space = CurrentSubtitleSpace;
        return CanvasGeometry.ToYttPoint(
            pixelX - space.Left, pixelY - space.Top, space.Width, space.Height);
    }

    private CanvasPoint PreserveBoxForAnchor(CanvasRect box, AnchorPoint anchor)
    {
        SKRect space = CurrentSubtitleSpace;
        CanvasRect relative = new(
            box.X - space.Left,
            box.Y - space.Top,
            box.Width,
            box.Height);
        return CanvasGeometry.PreserveBoxForAnchor(
            relative, anchor, space.Width, space.Height);
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
        SKRect subtitleSpace = CurrentSubtitleSpace;
        List<SnapGuide> relativeGuides = guides
            .Select(guide => guide with
            {
                Position = guide.Position - (guide.Vertical ? subtitleSpace.Left : subtitleSpace.Top),
            })
            .ToList();
        CanvasPoint relativeRequested = new(
            requested.X - subtitleSpace.Left,
            requested.Y - subtitleSpace.Top);
        SnapResult snapped = CanvasGeometry.Snap(relativeRequested, subtitleSpace.Width,
            subtitleSpace.Height, altPressed, relativeGuides);
        SnapGuide[] absoluteGuides = snapped.Guides
            .Select(guide => guide with
            {
                Position = guide.Position + (guide.Vertical ? subtitleSpace.Left : subtitleSpace.Top),
            })
            .ToArray();
        return new CanvasMovePreview(
            snapped.Point.X + subtitleSpace.Left - primary.Anchor.X,
            snapped.Point.Y + subtitleSpace.Top - primary.Anchor.Y,
            absoluteGuides);
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
            CanvasPoint current = ToCanvasPoint(cue.PositionX, cue.PositionY);
            positions[id] = ToYttPoint(current.X + preview.DeltaX, current.Y + preview.DeltaY);
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

        CanvasPoint ytt = PreserveBoxForAnchor(item.Bounds, anchor);
        editor.SetAnchor(cueId, anchor, ytt.X, ytt.Y);
        AfterMutation();
    }

    public void ApplySelectedAnchor(AnchorPoint anchor) => ApplyAnchor(anchor);

    public void ApplySelectedJustification(Justification justification)
        => SelectedJustification = justification;

    public Guid? AddCueAtCanvasPoint(double canvasX, double canvasY)
    {
        if (isInlineEditing || editor?.IsTransactionActive == true)
        {
            return null;
        }

        if (editor is null)
        {
            project ??= new SubtitleProject();
            editor = new DocumentEditor(project);
        }

        bool wasUnsaved = unsavedChanges;
        CanvasPoint position = ToYttPoint(canvasX, canvasY);
        TimeSpan start = TimeSpan.FromMilliseconds(PositionMilliseconds);
        Cue cue;
        editor.BeginTransaction("자막 추가 및 위치 지정");
        try
        {
            cue = editor.AddCue(start, start + TimeSpan.FromSeconds(2), "새 자막");
            editor.MoveCue(cue.Id, position.X, position.Y);
        }
        catch
        {
            editor.CancelTransaction();
            throw;
        }

        // The transaction remains open until BeginInlineEdit commits or
        // cancels it, so a new cue and its initial typing are one session.
        pendingInlineEditCueId = cue.Id;
        inlineEditOriginalUnsavedChanges = wasUnsaved;
        RefreshRowsAndStyles();
        SelectCue(cue.Id, toggle: false);
        RefreshInlinePreview();
        return cue.Id;
    }

    public void BeginInlineEdit(Guid cueId, double left, double top, double width)
    {
        if (isInlineEditing || project?.Cues[cueId] is not Cue cue || cue.Sections.Count == 0)
        {
            return;
        }

        if (editor is null)
        {
            editor = new DocumentEditor(project);
        }

        bool pendingNewCue = pendingInlineEditCueId == cueId && editor.IsTransactionActive;
        if (editor.IsTransactionActive && !pendingNewCue)
        {
            // Do not absorb an unrelated transaction into this edit session.
            return;
        }

        if (!pendingNewCue)
        {
            editor.BeginTransaction("인라인 텍스트 편집");
        }

        pendingInlineEditCueId = null;
        SelectCue(cueId, toggle: false);
        inlineEditCueId = cueId;
        inlineEditSectionIndex = 0;
        inlineEditOriginalText = cue.Sections[inlineEditSectionIndex].Text;
        inlineEditOriginalUnsavedChanges = pendingNewCue
            ? inlineEditOriginalUnsavedChanges
            : unsavedChanges;
        inlineEditIncludesNewCue = pendingNewCue;
        ApplyInlineEditorStyle(ResolveInlineEditorStyle(cue));
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        SetField(ref inlineText, inlineEditOriginalText, nameof(InlineText));
        InlineEditorLeft = double.IsFinite(left) ? left : 0;
        InlineEditorTop = double.IsFinite(top) ? top : 0;
        InlineEditorWidth = Math.Max(0, double.IsFinite(width) ? width : 180);
        InlineEditorHeight = InlineEditorPlacement.DefaultHeight;
        IsInlineEditing = true;
        RefreshInlinePreview();
    }

    public void BeginInlineEdit(Guid cueId, Rect placement, Rect viewport)
    {
        Rect clamped = InlineEditorPlacement.Clamp(placement, viewport);
        BeginInlineEdit(cueId, clamped.Left, clamped.Top, clamped.Width);
    }

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

    public void RefreshInlineEditorLayout(Rect contentRect, Rect viewport)
    {
        if (!isInlineEditing || !inlineEditorUsesReferencePlacement ||
            inlineEditorStyle is not InlineEditorStyle style || inlineEditCueId is not Guid cueId)
        {
            return;
        }

        inlineEditorContentBounds = contentRect;
        inlineEditorViewport = viewport;
        if (CanvasItems.FirstOrDefault(item => item.Id == cueId) is CanvasCueItem item)
        {
            inlineEditReferenceBounds = item.Bounds;
        }

        if (inlineEditReferenceBounds is not CanvasRect referenceBounds)
        {
            return;
        }

        InlineEditorPresentation presentation = InlineEditorPresentationMapper.Scale(
            style, referenceBounds, contentRect, PreviewSubtitleSpace);
        Rect requested = new(
            presentation.Bounds.X,
            presentation.Bounds.Y,
            Math.Max(140, presentation.Bounds.Width),
            Math.Max(InlineEditorPlacement.DefaultHeight, presentation.Bounds.Height));
        Rect clamped = InlineEditorPlacement.Clamp(requested, viewport);
        InlineEditorLeft = clamped.Left;
        InlineEditorTop = clamped.Top;
        InlineEditorWidth = clamped.Width;
        InlineEditorHeight = clamped.Height;
        InlineEditorFontSize = presentation.FontSize;
        InlineEditorPadding = presentation.Padding;
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
                    // 화면 기준 정렬 단축키 네 개만 이 메서드에 도달한다.
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
            positions[item.Id] = ToYttPoint(item.Anchor.X + delta.X, item.Anchor.Y + delta.Y);
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
        Loc.LanguageChanged -= OnLanguageChanged;
        settingsWindow?.Close();
        settingsWindow = null;
        if (autosave is not null)
        {
            autosave.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // 정상 종료는 스냅샷을 남기지 않아야 한다. 남기면 다음 실행에서
        // 필요하지도 않은 복구를 제안하게 된다.
        AutosaveService.ClearSnapshots();

        DisposeVideoSource();

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

    private void DisposeVideoSource()
    {
        MpvVideoSource? source = videoSource;
        videoSource = null;
        videoLoaded = false;
        if (source is not null)
        {
            source.FrameReady -= OnVideoFrameReady;
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        NotifyVideoState();
    }

    private async Task SelectMpvPathAsync()
    {
        string? selectedPath = await dialogs.OpenMpvLibraryAsync();
        if (selectedPath is null)
        {
            return;
        }

        MpvPath = selectedPath;
        await ApplyMpvPathAsync();
    }

    private Task ApplyMpvPathAsync()
        => ApplyMpvPathFromSettingsAsync(MpvPath);

    internal async Task<string> ApplyMpvPathFromSettingsAsync(string path)
    {
        MpvPath = path;
        string selectedPath = NormalizeMpvPath(path);
        if (!string.IsNullOrWhiteSpace(selectedPath)
            && !File.Exists(selectedPath)
            && !Directory.Exists(selectedPath))
        {
            Status = Loc["MpvPathInvalid"];
            return Status;
        }

        MpvPath = selectedPath;
        preferences.MpvPath = selectedPath;
        SavePreferences();
        Environment.SetEnvironmentVariable(
            "YTTSTUDIO_MPV_PATH",
            string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath);

        string? videoToReload = loadedVideoPath;
        double positionToRestore = PositionMilliseconds;
        DisposeVideoSource();
        InitializeVideoSource();
        RenderFallbackFrame();

        if (videoToReload is not null && videoSource is not null && File.Exists(videoToReload))
        {
            await LoadVideoAsync(videoToReload);
            if (videoLoaded)
            {
                await SeekAsync(positionToRestore, exact: false);
            }
        }

        Status = videoSource is null ? Loc["MpvReloadFailed"] : Loc["MpvReloaded"];
        return Status;
    }

    private void OpenMpvInstallationGuide()
    {
        if (!MpvInstallationGuide.TryOpen(out string? error))
        {
            Status = $"{Loc["MpvGuide"]}: {error}";
        }
    }

    public double Volume
    {
        get => volume;
        set
        {
            double clamped = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 100;
            if (!SetField(ref volume, clamped))
            {
                return;
            }

            preferences.Volume = clamped;
            SavePreferences();
            if (videoLoaded)
            {
                videoSource?.SetVolume(clamped);
            }
        }
    }

    public bool IsMuted
    {
        get => isMuted;
        set
        {
            if (!SetField(ref isMuted, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MuteLabel));
            preferences.IsMuted = value;
            SavePreferences();
            if (videoLoaded)
            {
                videoSource?.SetMuted(value);
            }
        }
    }

    public string MuteLabel => IsMuted ? Loc["Unmute"] : Loc["Mute"];

    private async Task<string?> InstallMpvAndApplyAsync(IProgress<MpvInstallProgress> progress)
    {
        if (mpvAutoInstaller is null)
        {
            return Loc["MpvAutoInstallUnavailable"];
        }

        try
        {
            Status = Loc["MpvAutoInstall"];
            string installedPath = await mpvAutoInstaller.InstallAsync(progress);
            return await ApplyMpvPathFromSettingsAsync(installedPath);
        }
        catch (MpvAutoInstallException exception)
        {
            Serilog.Log.Warning(exception, "libmpv 자동 설치 실패: {Kind}", exception.Kind);
            Status = Loc["MpvAutoInstallFailed"];
            return Status;
        }
        catch (OperationCanceledException)
        {
            Status = Loc["MpvAutoInstallCanceled"];
            return Status;
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "libmpv 자동 설치 중 처리되지 않은 오류");
            Status = Loc["MpvAutoInstallFailed"];
            return Status;
        }
    }

    private void SavePreferences()
    {
        if (!preferencesStore.TrySave(preferences, out string? error))
        {
            Status = $"{Loc["Settings"]}: {error}";
        }
    }

    private void ApplyAutosaveSettings(bool enabled, int intervalSeconds)
    {
        int normalizedInterval = NormalizeAutosaveIntervalSeconds(intervalSeconds);
        if (preferences.AutosaveEnabled == enabled
            && preferences.AutosaveIntervalSeconds == normalizedInterval)
        {
            return;
        }

        preferences.AutosaveEnabled = enabled;
        preferences.AutosaveIntervalSeconds = normalizedInterval;
        RestartAutosave(enabled, normalizedInterval);
        SavePreferences();
    }

    private void RestartAutosave(bool enabled, int intervalSeconds)
    {
        AutosaveService? previous = autosave;
        autosave = null;
        previous?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        if (!enabled)
        {
            return;
        }

        autosave = new AutosaveService(
            () => project,
            () => unsavedChanges,
            message => Serilog.Log.Warning("{Autosave}", message),
            TimeSpan.FromSeconds(NormalizeAutosaveIntervalSeconds(intervalSeconds)));
        autosave.Start();
    }

    private static int NormalizeAutosaveIntervalSeconds(int seconds)
        => seconds is 15 or 30 or 60 or 120 or 300 or 600 ? seconds : 60;

    private static string NormalizeMpvPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');

    private async Task OpenSubtitleAsync()
    {
        string? path = await dialogs.OpenSubtitleAsync();
        if (path is null)
        {
            return;
        }

        await OpenPathAsync(path);
    }

    /// <summary>
    /// 명령줄 인자나 파일 연결로 전달된 경로를 연다.
    /// 확장자로 프로젝트 패키지와 자막 파일을 구분한다.
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        OpenPathKind kind = OpenPathClassifier.Classify(path);
        if (kind == OpenPathKind.Project)
        {
            await LoadProjectPackageAsync(path, clearSnapshots: true);
            return;
        }

        if (kind == OpenPathKind.Video)
        {
            await LoadVideoAsync(path);
            return;
        }

        if (kind == OpenPathKind.Subtitle)
        {
            ImportSubtitle(path);
        }
    }

    /// <summary>드롭된 자막·영상 파일을 전달된 순서대로 기존 열기 경로로 처리한다.</summary>
    public async Task OpenDroppedPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            if (!OpenPathClassifier.IsDropSupported(path))
            {
                continue;
            }

            await OpenPathAsync(path);
        }
    }

    private void ImportSubtitle(string path)
    {
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

        await LoadVideoAsync(path);
    }

    /// <summary>공유 소스에 영상을 불러온다. 열기 명령과 프로젝트 재연결이 함께 쓴다.</summary>
    private async Task LoadVideoAsync(string path)
    {
        if (videoSource is null)
        {
            return;
        }

        try
        {
            Status = "영상 메타데이터 읽는 중…";
            await videoSource.LoadAsync(path, CancellationToken.None);
            videoLoaded = true;
            videoSource.SetVolume(volume);
            videoSource.SetMuted(isMuted);
            loadedVideoPath = Path.GetFullPath(path);
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

    /// <summary>큐 타이밍을 일괄 이동한다. 편집기가 어떤 큐도 1 ms 이전에 시작하지 않도록 보정한다.</summary>
    private void ShiftTimes(bool selectedOnly)
    {
        if (editor is null || project is null)
        {
            return;
        }

        IEnumerable<Guid> targets = selectedOnly
            ? selectedCueIds.ToArray()
            : project.Cues.Select(cue => cue.Id).ToArray();

        TimeSpan applied = editor.ShiftCueTimes(targets, TimeSpan.FromMilliseconds(shiftMilliseconds));
        Status = $"{Loc["TimeShift"]}: {applied.TotalMilliseconds:0} ms";
        UpdateMaximum();
        AfterMutation(refreshRows: true);
    }

    /// <summary><c>.yttproj</c> 패키지를 열고 필요하면 사라진 영상을 다시 연결한다.</summary>
    private async Task OpenProjectAsync()
    {
        string? path = await dialogs.OpenProjectAsync();
        if (path is null)
        {
            return;
        }

        await LoadProjectPackageAsync(path, clearSnapshots: true);
    }

    /// <summary>열려 있는 프로젝트를 <c>.yttproj</c> 패키지로 저장한다.</summary>
    private async Task SaveProjectAsync()
    {
        if (project is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(projectPath ?? sourcePath ?? "project") + ".yttproj";
        string? path = await dialogs.SaveProjectAsync(suggested);
        if (path is null)
        {
            return;
        }

        try
        {
            ProjectPackage.Save(project, path, RenderThumbnailPng());
            projectPath = path;
            unsavedChanges = false;
            // 정상 저장은 크래시 스냅샷을 무효화한다.
            AutosaveService.ClearSnapshots();
            Status = $"{Loc["SaveProject"]}: {path}";
        }
        catch (Exception exception)
        {
            Status = $"{Loc["SaveProject"]} — {exception.Message}";
        }
    }

    private async Task LoadProjectPackageAsync(string path, bool clearSnapshots)
    {
        try
        {
            ProjectPackageReadResult result = ProjectPackage.Read(path);
            project = result.Project;
            // 패키지 로드는 undo 를 만들지 않는 문맥이므로 편집기를 새로 시작한다.
            editor = new DocumentEditor(project);
            projectPath = clearSnapshots ? path : null;
            sourcePath = path;
            unsavedChanges = false;

            await RelinkVideoIfMissingAsync();

            UpdateMaximum();
            selectedCueIds.Clear();
            lastSelectedCueId = null;
            RefreshRowsAndStyles();
            AfterMutation(refreshRows: false);

            string migrated = result.WasMigrated
                ? $" (v{result.SourceSchemaVersion} → v{result.SchemaVersion})"
                : string.Empty;
            Status = $"{Loc["OpenProject"]}: {Path.GetFileName(path)}{migrated}";
            if (clearSnapshots)
            {
                AutosaveService.ClearSnapshots();
            }
        }
        catch (Exception exception)
        {
            Status = $"{Loc["OpenProject"]} — {exception.Message}";
        }
    }

    /// <summary>
    /// 패키지는 영상 경로만 저장하므로 끊어진 연결을 복구할 수 있어야 하고
    /// 조용히 영상 없는 프로젝트로 두지 않는다.
    /// </summary>
    private async Task RelinkVideoIfMissingAsync()
    {
        string? recorded = project?.VideoPath;
        if (project is null || string.IsNullOrEmpty(recorded) || File.Exists(recorded))
        {
            return;
        }

        bool relink = await dialogs.ConfirmAsync(
            Loc["VideoMissingTitle"],
            $"{Loc["VideoMissingPrompt"]}\n\n{recorded}",
            Loc["Relink"]);
        if (!relink)
        {
            return;
        }

        string? replacement = await dialogs.RelinkVideoAsync(recorded);
        if (replacement is not null)
        {
            await LoadVideoAsync(replacement);
        }
    }

    /// <summary>
    /// 비정상 종료가 남긴 스냅샷의 복구를 제안한다.
    /// 시작 시점에 실행되므로 작업 중인 문서의 실행 취소 기록을 지우지 않는다.
    /// </summary>
    public async Task OfferCrashRecoveryAsync()
    {
        string? snapshot = AutosaveService.FindLatestSnapshot();
        if (snapshot is null)
        {
            return;
        }

        bool recover = await dialogs.ConfirmAsync(
            Loc["RecoveryTitle"],
            Loc["RecoveryPrompt"],
            Loc["Recover"]);
        if (!recover)
        {
            AutosaveService.ClearSnapshots();
            return;
        }

        await LoadProjectPackageAsync(snapshot, clearSnapshots: false);
        // 복구된 문서는 정의상 저장되지 않은 상태다.
        unsavedChanges = true;
    }

    /// <summary>현재 프레임을 썸네일로 렌더한다. 아직 그릴 것이 없으면 <c>null</c> 이다.</summary>
    private byte[]? RenderThumbnailPng()
    {
        if (project is null)
        {
            return null;
        }

        try
        {
            const int width = 320;
            const int height = 180;
            PlayerViewport viewport = CreatePlayerViewport(new SKSize(width, height));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(
                ToBitmapDimension(viewport.PlayerSize.Width),
                ToBitmapDimension(viewport.PlayerSize.Height)));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(24, 24, 24));
            renderer.Render(
                canvas,
                viewport,
                project,
                TimeSpan.FromMilliseconds(PositionMilliseconds),
                new SubtitleRenderOptions());
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
        catch (Exception exception)
        {
            // 썸네일은 부가 정보다. 이것 때문에 저장을 막지 말고, 가짜로 만들지도 마라.
            Serilog.Log.Warning("{ThumbnailFailure}", exception.Message);
            return null;
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
        if (isInlineEditing || editor is null || project is null || selectedCueIds.Count == 0)
        {
            return;
        }

        Guid[] deletedIds = selectedCueIds.ToArray();
        Cue[] orderedCues = project.Cues
            .OrderBy(cue => cue.Start)
            .ToArray();
        int firstSelected = Array.FindIndex(orderedCues, cue => selectedCueIds.Contains(cue.Id));
        int lastSelected = Array.FindLastIndex(orderedCues, cue => selectedCueIds.Contains(cue.Id));
        Cue? neighbor = lastSelected >= 0
            ? orderedCues.Skip(lastSelected + 1).FirstOrDefault(cue => !selectedCueIds.Contains(cue.Id))
            : null;
        neighbor ??= firstSelected > 0
            ? orderedCues.Take(firstSelected).LastOrDefault(cue => !selectedCueIds.Contains(cue.Id))
            : null;

        editor.RemoveCues(deletedIds);
        selectedCueIds.Clear();
        lastSelectedCueId = neighbor?.Id;
        if (neighbor is not null)
        {
            selectedCueIds.Add(neighbor.Id);
        }

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

    private InlineEditorStyle ResolveInlineEditorStyle(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(project);
        Section section = cue.Sections[0];
        StylePreset style = project.GetStyle(section.StyleIdOverride ?? cue.StyleId);
        ResolvedFormat format = FormatResolver.Resolve(style.BaseFormat, section.Overrides);
        FontResolution resolution = renderer.ResolveFont(format.Font);
        return InlineEditorPresentationMapper.Map(format, resolution, cue.Justify);
    }

    private void ApplyInlineEditorStyle(InlineEditorStyle style)
    {
        inlineEditorStyle = style;
        InlineEditorFontFamily = style.FontFamily;
        InlineEditorFontSize = style.ReferenceFontSize;
        InlineEditorForeground = style.ForegroundBrush;
        InlineEditorTextAlignment = style.TextAlignment;
        InlineEditorPadding = style.ReferencePadding;
    }

    public void CommitInlineEdit()
    {
        if (!isInlineEditing)
        {
            return;
        }

        bool hasChanges = inlineEditIncludesNewCue ||
            (inlineEditCueId is Guid cueId &&
             project?.Cues[cueId] is Cue cue &&
             (uint)inlineEditSectionIndex < (uint)cue.Sections.Count &&
             cue.Sections[inlineEditSectionIndex].Text != inlineEditOriginalText);
        if (editor?.IsTransactionActive == true)
        {
            if (hasChanges)
            {
                editor.EndTransaction();
            }
            else
            {
                editor.CancelTransaction();
            }
        }

        inlineEditCueId = null;
        pendingInlineEditCueId = null;
        inlineEditIncludesNewCue = false;
        inlineEditOriginalText = string.Empty;
        inlineEditOriginalUnsavedChanges = false;
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        IsInlineEditing = false;
        RefreshRowsAndStyles();
        if (hasChanges)
        {
            AfterMutation(refreshRows: false);
        }
        else
        {
            RefreshInlinePreview();
        }
    }

    public void CancelInlineEdit()
    {
        if (!isInlineEditing)
        {
            return;
        }

        if (editor?.IsTransactionActive == true)
        {
            editor.CancelTransaction();
        }

        inlineEditCueId = null;
        pendingInlineEditCueId = null;
        inlineEditIncludesNewCue = false;
        inlineEditorUsesReferencePlacement = false;
        inlineEditReferenceBounds = null;
        IsInlineEditing = false;
        SetField(ref inlineText, inlineEditOriginalText, nameof(InlineText));
        inlineEditOriginalText = string.Empty;
        RefreshRowsAndStyles();
        RefreshInlinePreview();
        unsavedChanges = inlineEditOriginalUnsavedChanges;
        inlineEditOriginalUnsavedChanges = false;
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

            CanvasPoint ytt = PreserveBoxForAnchor(item.Bounds, anchor);
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
        if (videoSource is null || !videoLoaded)
        {
            return;
        }

        try
        {
            await videoSource.SeekAsync(TimeSpan.FromMilliseconds(milliseconds), exact);
        }
        catch (Exception exception)
        {
            if (videoLoaded)
            {
                Status = $"{Loc["SeekFailed"]}: {exception.Message}";
            }
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

    /// <summary>화면에 실제 표시되는 전체화면 플레이어 크기를 갱신한다.</summary>
    public void UpdatePreviewPlayerSize(double width, double height)
    {
        if (previewViewportMode != PreviewViewportMode.YouTubeFullscreen ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            width > float.MaxValue || height > float.MaxValue)
        {
            return;
        }

        SKSize value = new((float)width, (float)height);
        if (Math.Abs(fullscreenPlayerSize.Width - value.Width) < 0.5f &&
            Math.Abs(fullscreenPlayerSize.Height - value.Height) < 0.5f)
        {
            return;
        }

        fullscreenPlayerSize = value;
        SetPreviewViewport(CreatePlayerViewport(value));
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

    private SKSize GetPreviewPlayerSize()
        => previewViewportMode == PreviewViewportMode.YouTubeFullscreen
            ? fullscreenPlayerSize
            : ReferencePlayerSize;

    private PlayerViewport CreatePlayerViewport(SKSize playerSize)
    {
        SKSize? videoSize = GetVideoSize();
        PlayerViewport viewport = previewViewportMode switch
        {
            PreviewViewportMode.YouTubeDefault => videoSize is SKSize defaultVideoSize
                ? PlayerViewport.YouTubeDefault(defaultVideoSize)
                : PlayerViewport.YouTubeDefault(),
            PreviewViewportMode.YouTubeTheater => videoSize is SKSize theaterVideoSize
                ? PlayerViewport.YouTubeTheater(theaterVideoSize)
                : PlayerViewport.YouTubeTheater(),
            PreviewViewportMode.YouTubeFullscreen => videoSize is SKSize fullscreenVideoSize
                ? PlayerViewport.YouTubeFullscreen(playerSize, fullscreenVideoSize)
                : PlayerViewport.YouTubeFullscreen(playerSize),
            _ => PlayerViewport.VideoFrame(playerSize),
        };

        // 일반과 극장 팩터리가 주는 크기는 측정 당시의 창 크기라 기준 너비보다 작다.
        // 그대로 그리면 프리뷰 비트맵 해상도가 모드에 따라 낮아져 흐릿해진다. 두 모드는
        // 서로 닮음이라 배치가 달라지지 않으므로 기준 너비로 맞춰 선명도를 일정하게 둔다.
        // 전체화면과 VideoFrame 은 호출자가 실제 크기를 정하므로 건드리지 않는다.
        return viewport.Mode is PreviewViewportMode.YouTubeDefault or PreviewViewportMode.YouTubeTheater
            ? viewport.ScaleToWidth(ReferencePlayerSize.Width)
            : viewport;
    }

    private SKSize? GetVideoSize()
    {
        if (videoLoaded && videoSource is not null)
        {
            var info = videoSource.Info;
            if (info.Width > 0 && info.Height > 0)
            {
                return new SKSize(info.Width, info.Height);
            }
        }

        if (project?.Video is { Width: > 0, Height: > 0 } video)
        {
            return new SKSize(video.Width, video.Height);
        }

        return null;
    }

    private void SetPreviewViewport(PlayerViewport value)
    {
        if (previewViewport == value)
        {
            return;
        }

        previewViewport = value;
        OnPropertyChanged(nameof(PreviewViewport));
        OnPropertyChanged(nameof(PreviewSubtitleSpace));
        OnPropertyChanged(nameof(PreviewPlayerWidth));
        OnPropertyChanged(nameof(PreviewPlayerHeight));
        OnPropertyChanged(nameof(PreviewVideoContentLeft));
        OnPropertyChanged(nameof(PreviewVideoContentTop));
        OnPropertyChanged(nameof(PreviewVideoContentWidth));
        OnPropertyChanged(nameof(PreviewVideoContentHeight));
    }

    private static int ToBitmapDimension(float value)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static Rect ToAvaloniaRect(SKRect value)
        => new(value.Left, value.Top, value.Width, value.Height);

    private void RenderFallbackFrame()
    {
        if (videoLoaded || disposed)
        {
            return;
        }

        SKSize playerSize = previewViewport.PlayerSize;
        using SKBitmap bitmap = new(new SKImageInfo(
            ToBitmapDimension(playerSize.Width),
            ToBitmapDimension(playerSize.Height),
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
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

        PlayerViewport viewport = CreatePlayerViewport(GetPreviewPlayerSize());
        SetPreviewViewport(viewport);
        int width = ToBitmapDimension(viewport.PlayerSize.Width);
        int height = ToBitmapDimension(viewport.PlayerSize.Height);

        // 이 경로는 재생 중 프레임마다 돈다. 매번 비트맵을 새로 만들고 PNG 로 압축했다가
        // 곧바로 되읽으면 프레임당 수 MB 할당과 무손실 압축 한 번이 통째로 낭비된다.
        // 영상 프레임과 같은 방식으로 비트맵을 재사용하고 Skia 가 그 화소 버퍼에 직접
        // 그리게 한다. 같은 인스턴스를 고쳐 쓰므로 변경 알림은 아래에서 직접 올린다.
        WriteableBitmap target;
        if (SubtitleImage is WriteableBitmap existing &&
            existing.PixelSize.Width == width && existing.PixelSize.Height == height)
        {
            target = existing;
        }
        else
        {
            target = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            SubtitleImage = target;
        }

        TimeSpan time = TimeSpan.FromMilliseconds(PositionMilliseconds);
        double framesPerSecond = project.Video?.NominalFps is > 0 ? project.Video.NominalFps : 30;
        long frameIndex = checked((long)Math.Floor(time.TotalSeconds * framesPerSecond));
        IReadOnlyList<CueHitBox> hitBoxes;
        using (ILockedFramebuffer framebuffer = target.Lock())
        {
            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            hitBoxes = renderer.RenderAndMeasure(canvas, viewport, project, time, new SubtitleRenderOptions
            {
                FrameIndex = frameIndex,
                ShowSafeArea = showSafeArea,
                ShowAnchorPoints = showAnchors,
                EditingCueId = isInlineEditing ? inlineEditCueId : null,
            });
        }

        OnPropertyChanged(nameof(SubtitleImage));
        CanvasItems = hitBoxes
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
        // 자동 저장은 복구할 내용이 있을 때만 기록한다.
        unsavedChanges = true;
        if (refreshRows)
        {
            RefreshRowsAndStyles();
        }

        ReconcileSelection();

        UpdateMaximum();
        RenderSubtitlePreview();
        NotifySelectionProperties();
        NotifyCommandStates();
    }

    private void RefreshInlinePreview()
    {
        // Live typing is a visual/model notification only. The open editor
        // transaction owns the eventual dirty/history transition at commit.
        RefreshRowsAndStyles();
        ReconcileSelection();
        RenderSubtitlePreview();
        if (inlineEditorUsesReferencePlacement && inlineEditCueId is not null)
        {
            RefreshInlineEditorLayout(inlineEditorContentBounds, inlineEditorViewport);
        }
        NotifySelectionProperties();
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

    private void ReconcileSelection()
    {
        lastSelectedCueId = ReconcileCueSelection(project, selectedCueIds, lastSelectedCueId);

        selectedCueRow = lastSelectedCueId is Guid selectedId
            ? CueRows.FirstOrDefault(row => row.Id == selectedId)
            : null;
        OnPropertyChanged(nameof(SelectedCueRow));
    }

    internal static Guid? ReconcileCueSelection(
        SubtitleProject? project,
        HashSet<Guid> selectedCueIds,
        Guid? lastSelectedCueId)
    {
        ArgumentNullException.ThrowIfNull(selectedCueIds);
        if (project is null)
        {
            selectedCueIds.Clear();
            return null;
        }

        selectedCueIds.RemoveWhere(id => project.Cues[id] is null);
        return lastSelectedCueId is Guid selected && selectedCueIds.Contains(selected)
            ? selected
            : selectedCueIds.Count == 0 ? null : selectedCueIds.Last();
    }

    private void RefreshCanvasSelection()
    {
        CanvasItems = CanvasItems.Select(item => item with { Selected = selectedCueIds.Contains(item.Id) }).ToArray();
        OnPropertyChanged(nameof(CanvasItems));
    }

    private void NotifySelectionProperties()
    {
        RefreshKaraokePresentation();
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
        OnPropertyChanged(nameof(SelectedKaraokeCueId));
        OnPropertyChanged(nameof(HasKaraokeCue));
        OnPropertyChanged(nameof(SelectedKaraokeCueDurationMilliseconds));
        OnPropertyChanged(nameof(SelectedKaraokeTypeOption));

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
        AutoSplitKaraokeCommand.NotifyCanExecuteChanged();
    }

    private void RefreshKaraokePresentation()
    {
        KaraokeSections.Clear();
        if (SelectedKaraokeCueId is not Guid cueId || project?.Cues[cueId] is not Cue cue)
        {
            return;
        }

        for (int index = 0; index < cue.Sections.Count; index++)
        {
            Section section = cue.Sections[index];
            KaraokeSections.Add(new KaraokeSectionViewModel(
                this,
                cue.Id,
                index,
                section.Text,
                section.KaraokeOffset));
        }
    }

    private void NotifyVideoState()
    {
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
        OnPropertyChanged(nameof(PlayPauseActionText));
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
