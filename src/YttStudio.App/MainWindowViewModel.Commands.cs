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

/// <summary>화면에서 실행하는 명령들의 선언을 모은다.</summary>
public sealed partial class MainWindowViewModel
{

    public DelegateCommand ExitCommand { get; private set; } = null!;
    public AsyncCommand AboutCommand { get; private set; } = null!;
    public AsyncCommand OpenProjectCommand { get; private set; } = null!;
    public AsyncCommand SaveProjectCommand { get; private set; } = null!;
    public DelegateCommand ReplaceAllCommand { get; private set; } = null!;
    public DelegateCommand ShiftSelectedCommand { get; private set; } = null!;
    public DelegateCommand ShiftAllCommand { get; private set; } = null!;
    public AsyncCommand OpenSubtitleCommand { get; private set; } = null!;
    public AsyncCommand OpenVideoCommand { get; private set; } = null!;
    public AsyncCommand SaveCommand { get; private set; } = null!;
    public DelegateCommand PlayPauseCommand { get; private set; } = null!;
    public DelegateCommand StepBackCommand { get; private set; } = null!;
    public DelegateCommand StepForwardCommand { get; private set; } = null!;
    public DelegateCommand SelectVideoFrameViewportCommand { get; private set; } = null!;
    public DelegateCommand SelectYouTubeDefaultViewportCommand { get; private set; } = null!;
    public DelegateCommand SelectYouTubeTheaterViewportCommand { get; private set; } = null!;
    public DelegateCommand SelectYouTubeFullscreenViewportCommand { get; private set; } = null!;
    public DelegateCommand UndoCommand { get; private set; } = null!;
    public DelegateCommand RedoCommand { get; private set; } = null!;
    public DelegateCommand AddCueCommand { get; private set; } = null!;
    public DelegateCommand DeleteCueCommand { get; private set; } = null!;
    public DelegateCommand DuplicateCueCommand { get; private set; } = null!;
    public DelegateCommand AddStyleCommand { get; private set; } = null!;
    public AsyncCommand DeleteStyleCommand { get; private set; } = null!;
    public DelegateCommand RenameStyleCommand { get; private set; } = null!;
    public DelegateCommand SaveCueAsStyleCommand { get; private set; } = null!;
    public DelegateCommand ApplySelectedStyleCommand { get; private set; } = null!;
    public DelegateCommand AlignLeftCommand { get; private set; } = null!;
    public DelegateCommand AlignCenterCommand { get; private set; } = null!;
    public DelegateCommand AlignRightCommand { get; private set; } = null!;
    public DelegateCommand AlignTopCommand { get; private set; } = null!;
    public DelegateCommand AlignMiddleCommand { get; private set; } = null!;
    public DelegateCommand AlignBottomCommand { get; private set; } = null!;
    public DelegateCommand DistributeHorizontalCommand { get; private set; } = null!;
    public DelegateCommand DistributeVerticalCommand { get; private set; } = null!;
    public DelegateCommand BringToFrontCommand { get; private set; } = null!;
    public DelegateCommand SendToBackCommand { get; private set; } = null!;
    public DelegateCommand CommitInlineEditCommand { get; private set; } = null!;
    public DelegateCommand ValidateCommand { get; private set; } = null!;
    public DelegateCommand ApplyValidationFixCommand { get; private set; } = null!;
    public DelegateCommand GoToValidationIssueCommand { get; private set; } = null!;
    public DelegateCommand AutoSplitKaraokeCommand { get; private set; } = null!;
    public AsyncCommand SelectMpvPathCommand { get; private set; } = null!;
    public AsyncCommand ApplyMpvPathCommand { get; private set; } = null!;
    public DelegateCommand OpenMpvInstallationGuideCommand { get; private set; } = null!;
    public AsyncCommand OpenSettingsCommand { get; private set; } = null!;
}
