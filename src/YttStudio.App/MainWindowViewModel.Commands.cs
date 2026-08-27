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
}
