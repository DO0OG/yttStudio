using Avalonia;
using YttStudio.Core;
using YttStudio.Core.Editing;

namespace YttStudio.App;

public sealed partial class MainWindowViewModel
{
    private int? selectedMotionKeyframeIndex;
    private Guid? selectedMotionKeyframeCueId;

    /// <summary>현재 단일 선택 큐에 표시할 모션 키프레임 경로다.</summary>
    /// <remarks>
    /// 기존 두 점짜리 MoveEffect는 편집 가능한 경로로 보이도록 가상 키프레임
    /// 두 개로 투영한다. 여러 큐가 선택되면 한 큐만 편집한다는 규칙에 따라 비운다.
    /// </remarks>
    public IReadOnlyList<MotionKeyframe> SelectedCueKeyframes
        => IsMotionPathEditing && SelectedCue is Cue cue
            ? GetMotionKeyframes(cue)
            : [];

    /// <summary>모션 편집 경로를 표시할 수 있는 단일 선택 상태인지 나타낸다.</summary>
    public bool IsMotionPathEditing
        => selectedCueIds.Count == 1 && MoveEffectEnabled;

    public bool HasSelectedMotionPath => SelectedCueKeyframes.Count > 0;

    /// <summary>프리뷰에서 선택된 키프레임의 인덱스다.</summary>
    public int? SelectedMotionKeyframeIndex => selectedMotionKeyframeIndex;

    /// <summary>현재 선택 큐의 키프레임을 cue 시작 기준의 타임라인 마커로 투영한다.</summary>
    public IReadOnlyList<MotionTimelineMarker> SelectedCueKeyframeMarkers
    {
        get
        {
            if (!IsMotionPathEditing || SelectedCue is not Cue cue)
            {
                return [];
            }

            return GetMotionKeyframes(cue)
                .Select((keyframe, index) => new MotionTimelineMarker(
                    cue.Id,
                    index,
                    keyframe.RelativeTime,
                    cue.Start.TotalMilliseconds + keyframe.RelativeTime.TotalMilliseconds,
                    cue.Track))
                .ToArray();
        }
    }

    /// <summary>타임 드래그는 아직 지원하지 않는다는 계약을 명시한다.</summary>
    public bool SupportsMotionKeyframeTimeDrag => false;

    /// <summary>선택 모션 경로를 주어진 프리뷰 콘텐츠 사각형으로 투영한다.</summary>
    public MotionPathPresentation GetSelectedMotionPath(Rect contentRect)
        => MotionPathGeometry.CreatePresentation(
            SelectedCueKeyframes,
            contentRect,
            PreviewSubtitleSpace,
            selectedMotionKeyframeIndex);

    /// <summary>마커를 선택하고 이후 Delete 입력의 대상을 정한다.</summary>
    public bool SelectMotionKeyframe(int keyframeIndex)
    {
        IReadOnlyList<MotionKeyframe> path = SelectedCueKeyframes;
        if ((uint)keyframeIndex >= (uint)path.Count)
        {
            return false;
        }

        if (selectedMotionKeyframeIndex == keyframeIndex)
        {
            return true;
        }

        selectedMotionKeyframeIndex = keyframeIndex;
        selectedMotionKeyframeCueId = SelectedCue?.Id;
        OnPropertyChanged(nameof(SelectedMotionKeyframeIndex));
        return true;
    }

    public void ClearMotionKeyframeSelection()
    {
        if (selectedMotionKeyframeIndex is null)
        {
            return;
        }

        selectedMotionKeyframeIndex = null;
        selectedMotionKeyframeCueId = null;
        OnPropertyChanged(nameof(SelectedMotionKeyframeIndex));
    }

    /// <summary>
    /// 캔버스 좌표를 자막 좌표로 변환해 현재 playhead의 상대 시간에 키프레임을
    /// 추가한다. 기존 빈 캔버스의 새 큐 추가와 분리하기 위해 경로 편집 상태에서만 동작한다.
    /// </summary>
    public bool AddMotionKeyframeAtScreenPoint(Point screenPoint, Rect contentRect)
    {
        Point subtitlePoint = PreviewCanvasGeometry.ToSubtitle(
            screenPoint, contentRect, PreviewSubtitleSpace);
        return AddMotionKeyframeAtCurrentTime(subtitlePoint.X, subtitlePoint.Y);
    }

    public bool AddMotionKeyframeAtCurrentTime(double x, double y)
    {
        Cue? cue = SingleSelectedCue();
        if (editor is null || cue is null || !IsMotionPathEditing)
        {
            return false;
        }

        TimeSpan relativeTime = GetCurrentRelativeTime(cue);
        return AddMotionKeyframe(cue.Id, relativeTime, x, y);
    }

    /// <summary>명시한 cue-relative 시간과 자막 좌표로 경로에 키프레임을 추가한다.</summary>
    public bool AddMotionKeyframe(Guid cueId, TimeSpan relativeTime, double x, double y)
    {
        if (editor is null || !IsMotionPathEditing || SelectedCue?.Id != cueId)
        {
            return false;
        }

        Cue cue = SelectedCue;
        MotionKeyframe[] path = GetMotionKeyframes(cue).ToArray();
        TimeSpan normalizedTime = ClampRelativeTime(cue, relativeTime);
        Point normalizedPoint = NormalizeMotionPoint(x, y);
        MotionKeyframe added = new(normalizedTime, normalizedPoint.X, normalizedPoint.Y);
        editor.AddKeyframe(cueId, added);

        MotionKeyframe[] updated = [.. path, added];
        Array.Sort(updated, static (first, second) => first.RelativeTime.CompareTo(second.RelativeTime));
        selectedMotionKeyframeIndex = Array.FindLastIndex(updated,
            keyframe => keyframe.RelativeTime == added.RelativeTime &&
                Math.Abs(keyframe.X - added.X) < double.Epsilon &&
                Math.Abs(keyframe.Y - added.Y) < double.Epsilon);
        selectedMotionKeyframeCueId = cueId;
        AfterMutation(refreshRows: false);
        return true;
    }

    /// <summary>마커 드래그 종료 시 위치만 하나의 undo 명령으로 커밋한다.</summary>
    public bool CommitMotionKeyframeDrag(int keyframeIndex, double x, double y)
    {
        Cue? cue = SingleSelectedCue();
        IReadOnlyList<MotionKeyframe> path = SelectedCueKeyframes;
        if (editor is null || cue is null || !IsMotionPathEditing ||
            (uint)keyframeIndex >= (uint)path.Count)
        {
            return false;
        }

        Point normalizedPoint = NormalizeMotionPoint(x, y);
        MotionKeyframe current = path[keyframeIndex];
        if (Math.Abs(current.X - normalizedPoint.X) < double.Epsilon &&
            Math.Abs(current.Y - normalizedPoint.Y) < double.Epsilon)
        {
            selectedMotionKeyframeIndex = keyframeIndex;
            selectedMotionKeyframeCueId = cue.Id;
            return false;
        }

        MotionKeyframe replacement = current with
        {
            X = normalizedPoint.X,
            Y = normalizedPoint.Y,
        };
        editor.MoveKeyframe(cue.Id, 0, keyframeIndex, replacement);
        selectedMotionKeyframeIndex = keyframeIndex;
        selectedMotionKeyframeCueId = cue.Id;
        AfterMutation(refreshRows: false);
        return true;
    }

    /// <summary>선택 마커를 삭제한다. 유효 경로가 되도록 최소 두 점은 보존한다.</summary>
    public bool DeleteSelectedMotionKeyframe()
    {
        Cue? cue = SingleSelectedCue();
        IReadOnlyList<MotionKeyframe> path = SelectedCueKeyframes;
        if (editor is null || cue is null || !IsMotionPathEditing ||
            selectedMotionKeyframeIndex is not int index ||
            (uint)index >= (uint)path.Count || path.Count <= 2)
        {
            return false;
        }

        editor.DeleteKeyframe(cue.Id, 0, index);
        selectedMotionKeyframeIndex = null;
        selectedMotionKeyframeCueId = null;
        AfterMutation(refreshRows: false);
        return true;
    }

    /// <summary>Delete 입력을 소비해 큐 삭제보다 키프레임 삭제를 우선할지 판단한다.</summary>
    public bool HasSelectedMotionKeyframe
        => IsMotionPathEditing && selectedMotionKeyframeIndex is not null;

    /// <summary>선택 경로를 갱신하고 오래된 마커 인덱스를 제거한다.</summary>
    private void NotifyMotionPresentationProperties()
    {
        IReadOnlyList<MotionKeyframe> path = SelectedCueKeyframes;
        if (selectedMotionKeyframeIndex is not null &&
            selectedMotionKeyframeCueId != SelectedCue?.Id)
        {
            selectedMotionKeyframeIndex = null;
            selectedMotionKeyframeCueId = null;
            OnPropertyChanged(nameof(SelectedMotionKeyframeIndex));
        }
        else if (selectedMotionKeyframeIndex is int index && (uint)index >= (uint)path.Count)
        {
            selectedMotionKeyframeIndex = null;
            selectedMotionKeyframeCueId = null;
            OnPropertyChanged(nameof(SelectedMotionKeyframeIndex));
        }

        OnPropertyChanged(nameof(SelectedCueKeyframes));
        OnPropertyChanged(nameof(IsMotionPathEditing));
        OnPropertyChanged(nameof(HasSelectedMotionPath));
        OnPropertyChanged(nameof(SelectedCueKeyframeMarkers));
        OnPropertyChanged(nameof(SupportsMotionKeyframeTimeDrag));
    }

    private TimeSpan GetCurrentRelativeTime(Cue cue)
    {
        double milliseconds = PositionMilliseconds - cue.Start.TotalMilliseconds;
        return ClampRelativeTime(cue, TimeSpan.FromMilliseconds(milliseconds));
    }

    private static TimeSpan ClampRelativeTime(Cue cue, TimeSpan value)
    {
        TimeSpan duration = cue.End - cue.Start;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return value < TimeSpan.Zero
            ? TimeSpan.Zero
            : value > duration ? duration : value;
    }

    private Point NormalizeMotionPoint(double x, double y)
    {
        Rect space = PreviewCanvasGeometry.NormalizeSubtitleSpace(PreviewSubtitleSpace);
        double normalizedX = double.IsFinite(x) ? x : space.X;
        double normalizedY = double.IsFinite(y) ? y : space.Y;
        return new Point(
            Math.Clamp(normalizedX, space.Left, space.Right),
            Math.Clamp(normalizedY, space.Top, space.Bottom));
    }

    internal static MotionKeyframe[] GetMotionKeyframes(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        MoveEffect? move = cue.Effects.OfType<MoveEffect>().FirstOrDefault();
        if (move is null)
        {
            return [];
        }

        if (move.Keyframes.Count > 0)
        {
            return move.Keyframes.ToArray();
        }

        TimeSpan start = move.StartTime ?? TimeSpan.Zero;
        TimeSpan end = move.EndTime ?? (cue.End - cue.Start);
        return
        [
            new MotionKeyframe(start, move.FromX, move.FromY),
            new MotionKeyframe(end, move.ToX, move.ToY),
        ];
    }
}
