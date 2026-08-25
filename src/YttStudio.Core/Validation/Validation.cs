using System.IO.Compression;
using YttStudio.Core.Editing;

namespace YttStudio.Core.Validation;

/// <summary>프로젝트 검증 문제의 심각도다.</summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>검증 결과 하나와 선택적 대상 큐다.</summary>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    string Code,
    string Message,
    Guid? CueId,
    bool HasAutoFix);

/// <summary>안정적인 검증 규칙 식별자다.</summary>
public static class ValidationCodes
{
    public const string E001 = "E001";
    public const string E002 = "E002";
    public const string E003 = "E003";
    public const string E004 = "E004";
    public const string E005 = "E005";
    public const string E006 = "E006";
    public const string W101 = "W101_SIZE_RISK_ESTIMATE";
    public const string W102 = "W102";
    public const string W103 = "W103";
    public const string W104 = "W104";
    public const string W105 = "W105";
    public const string W106 = "W106";
    public const string W107 = "W107";
    public const string W108 = "W108";
    public const string I201 = "I201";
    public const string I202 = "I202";
    public const string I203 = "I203";
}

/// <summary>
/// Core 모델만으로 판단할 수 없는 검증 규칙을 위한 선택적 측정 입력이다.
/// 모델만으로는 알 수 없는 값이다. 예를 들어 렌더 박스 경계와 효과 사용량이다.
/// </summary>
public sealed record ValidationMetrics
{
    public bool? MobileEffectRisk { get; init; }
    public bool? IsOutsideSafeArea { get; init; }
    public bool? HasDarkText { get; init; }
    public bool? BoxWidthExceeded { get; init; }
    public bool? MultipleShadows { get; init; }
    public bool? SizeAtLowerBound { get; init; }
    public bool? UsesPcOnlyFeature { get; init; }
    public bool? FontIgnoredOnAndroid { get; init; }
    public bool? OverlappingZOrderNotPreserved { get; init; }
    public double? TextLuminance { get; init; }
    public double? BoxWidth { get; init; }
    public double? SubtitleSpaceWidth { get; init; }
    public int? ShadowCount { get; init; }

    internal ValidationMetrics Merge(ValidationMetrics? specific) => new()
    {
        MobileEffectRisk = specific?.MobileEffectRisk ?? MobileEffectRisk,
        IsOutsideSafeArea = specific?.IsOutsideSafeArea ?? IsOutsideSafeArea,
        HasDarkText = specific?.HasDarkText ?? HasDarkText,
        BoxWidthExceeded = specific?.BoxWidthExceeded ?? BoxWidthExceeded,
        MultipleShadows = specific?.MultipleShadows ?? MultipleShadows,
        SizeAtLowerBound = specific?.SizeAtLowerBound ?? SizeAtLowerBound,
        UsesPcOnlyFeature = specific?.UsesPcOnlyFeature ?? UsesPcOnlyFeature,
        FontIgnoredOnAndroid = specific?.FontIgnoredOnAndroid ?? FontIgnoredOnAndroid,
        OverlappingZOrderNotPreserved = specific?.OverlappingZOrderNotPreserved ?? OverlappingZOrderNotPreserved,
        TextLuminance = specific?.TextLuminance ?? TextLuminance,
        BoxWidth = specific?.BoxWidth ?? BoxWidth,
        SubtitleSpaceWidth = specific?.SubtitleSpaceWidth ?? SubtitleSpaceWidth,
        ShadowCount = specific?.ShadowCount ?? ShadowCount,
    };
}

/// <summary>내보내기와 렌더 호출자가 검증기에 제공하는 입력이다.</summary>
public sealed class ValidationContext
{
    public ValidationContext() { }

    public ValidationContext(SubtitleProject project) => Project = project;

    public SubtitleProject? Project { get; init; }
    public TimeSpan? VideoDuration { get; init; }
    public byte[]? ExportedXmlBytes { get; init; }
    public long? GzipXmlSizeBytes { get; init; }
    public double? EstimatedBitsPerSecond { get; init; }
    public ValidationMetrics Metrics { get; init; } = new();
    public IReadOnlyDictionary<Guid, ValidationMetrics> CueMetrics { get; init; } =
        new Dictionary<Guid, ValidationMetrics>();

    public ValidationMetrics ForCue(Guid cueId)
        => Metrics.Merge(CueMetrics.GetValueOrDefault(cueId));

    public static ValidationContext FromExportedXml(SubtitleProject project, byte[] xmlBytes, TimeSpan duration)
        => new(project) { ExportedXmlBytes = xmlBytes, VideoDuration = duration };

    public static ValidationContext FromGzipSize(SubtitleProject project, long gzipXmlSizeBytes, TimeSpan duration)
        => new(project) { GzipXmlSizeBytes = gzipXmlSizeBytes, VideoDuration = duration };
}

/// <summary>유튜브 제약 검증 규칙을 실행한다.</summary>
public class DocumentValidator
{
    private const string W101Message =
        "브라우저의 실제 JSON3 기준과 다른 근사치입니다. 실제 표시 여부는 업로드 후 확인하세요.";

    public IReadOnlyList<ValidationIssue> Validate(SubtitleProject project, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        context ??= new ValidationContext(project);
        List<ValidationIssue> issues = [];
        TimeSpan? duration = context.VideoDuration ?? project.Video?.Duration;

        if (TryGetBitsPerSecond(context, duration, out double bitsPerSecond) &&
            bitsPerSecond > YttConstants.SizeRiskBitsPerSecondThreshold)
        {
            issues.Add(new(IssueSeverity.Warning, ValidationCodes.W101, W101Message, null, false));
        }

        foreach (Cue cue in project.Cues)
        {
            if (cue.Start < TimeSpan.FromMilliseconds(YttConstants.MinimumCueStartMilliseconds))
            {
                issues.Add(new(IssueSeverity.Error, ValidationCodes.E001, "시작 시각은 1ms 이상이어야 합니다.", cue.Id, true));
            }

            if (cue.End <= cue.Start)
            {
                issues.Add(new(IssueSeverity.Error, ValidationCodes.E002, "끝 시각은 시작 시각보다 늦어야 합니다.", cue.Id, false));
            }

            if (duration is TimeSpan videoDuration && cue.End > videoDuration)
            {
                issues.Add(new(IssueSeverity.Error, ValidationCodes.E006, "큐 시각이 영상 길이를 초과합니다.", cue.Id, false));
            }

            ValidationMetrics metrics = context.ForCue(cue.Id);
            bool hasPcOnly = metrics.UsesPcOnlyFeature ?? HasPcOnlyFeature(project, cue);
            bool androidFontIgnored = metrics.FontIgnoredOnAndroid ?? HasAndroidOnlyUnsupportedFont(project, cue);
            bool hasShadowWarning = metrics.MultipleShadows ?? HasMultipleShadows(project, cue, metrics);
            bool hasOverlap = metrics.OverlappingZOrderNotPreserved ?? false;

            for (int index = 1; index < cue.Sections.Count; index++)
            {
                TimeSpan? previous = cue.Sections[index - 1].KaraokeOffset;
                TimeSpan? current = cue.Sections[index].KaraokeOffset;
                if (previous is TimeSpan p && current is TimeSpan c && p == c)
                {
                    issues.Add(new(IssueSeverity.Error, ValidationCodes.E003,
                        "인접 가라오케 섹션의 오프셋이 같습니다.", cue.Id, true));
                    break;
                }
            }

            bool white = false;
            bool opacity255 = false;
            bool lowerBound = false;
            bool large = false;
            bool dark = false;
            foreach (Section section in cue.Sections)
            {
                ResolvedFormat format = Resolve(project, cue, section);
                white |= IsPureWhite(format.Foreground);
                opacity255 |= Has255Alpha(format);
                lowerBound |= format.SizePercent <= YttConstants.MinimumFontSizePercent;
                large |= format.SizePercent > YttConstants.RecommendedFontSizePercent;
                dark |= Luminance(format.Foreground) < YttConstants.DarkTextLuminanceThreshold;
            }

            if (white)
            {
                issues.Add(new(IssueSeverity.Error, ValidationCodes.E004, "전경색 순백(#FFFFFF)은 허용되지 않습니다.", cue.Id, true));
            }

            if (opacity255)
            {
                issues.Add(new(IssueSeverity.Error, ValidationCodes.E005, "불투명도 255는 허용되지 않습니다.", cue.Id, true));
            }

            if (metrics.MobileEffectRisk == true)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W102, "효과가 많아 모바일에서 자막 선택지에 표시되지 않을 수 있습니다.", cue.Id, false));
            }

            if (metrics.IsOutsideSafeArea == true)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W103, "세이프 에어리어 밖에 있어 극장 모드에서 화면 밖으로 밀릴 수 있습니다.", cue.Id, false));
            }

            if (metrics.HasDarkText ?? dark)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W104, "어두운 텍스트는 안드로이드 검은 배경에서 판독하기 어렵습니다.", cue.Id, false));
            }

            if (metrics.BoxWidthExceeded == true || (metrics.BoxWidth is double width && metrics.SubtitleSpaceWidth is double space && width > space))
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W105, "박스 너비가 자막 좌표 공간 폭을 초과합니다.", cue.Id, false));
            }

            if (hasShadowWarning)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W106, "pen 하나에 그림자 2종 이상이 적용되어 파일 크기가 증가합니다.", cue.Id, false));
            }

            if (metrics.SizeAtLowerBound ?? lowerBound)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W107, "폰트 크기가 75% 하한에 걸렸습니다.", cue.Id, false));
            }

            if (large)
            {
                issues.Add(new(IssueSeverity.Warning, ValidationCodes.W108, "폰트 크기가 UX 권장 상한(200%)을 초과합니다.", cue.Id, false));
            }

            if (hasPcOnly)
            {
                issues.Add(new(IssueSeverity.Info, ValidationCodes.I201, "PC 전용 기능이 사용되었습니다.", cue.Id, false));
            }

            if (androidFontIgnored)
            {
                issues.Add(new(IssueSeverity.Info, ValidationCodes.I202, "이 폰트는 안드로이드에서 무시될 수 있습니다.", cue.Id, false));
            }
        }

        foreach (Cue left in project.Cues)
        {
            foreach (Cue right in project.Cues)
            {
                if (left.Id == right.Id || left.Id.CompareTo(right.Id) >= 0 || !Overlaps(left, right))
                {
                    continue;
                }

                if (context.ForCue(left.Id).OverlappingZOrderNotPreserved == true ||
                    context.ForCue(right.Id).OverlappingZOrderNotPreserved == true ||
                    left.ZOrder != right.ZOrder)
                {
                    issues.Add(new(IssueSeverity.Info, ValidationCodes.I203,
                        "겹치는 큐의 ZOrder는 .ytt 왕복에서 보존되지 않습니다.", left.Id, false));
                }
            }
        }

        return issues;
    }

    public IReadOnlyList<ValidationIssue> Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Project is null ? throw new ArgumentException("ValidationContext.Project is required.", nameof(context)) : Validate(context.Project, context);
    }

    public static double CalculateGzipBitsPerSecond(byte[] exportedXmlBytes, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(exportedXmlBytes);
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(exportedXmlBytes);
        }

        return CalculateGzipBitsPerSecond(output.Length, duration);
    }

    public static double CalculateGzipBitsPerSecond(long gzipXmlSizeBytes, TimeSpan duration)
    {
        if (gzipXmlSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(gzipXmlSizeBytes));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        return gzipXmlSizeBytes * 8.0 / duration.TotalSeconds;
    }

    public bool ApplyAutoFix(DocumentEditor editor, ValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(issue);
        return editor.ApplyValidationFix(issue);
    }

    private static bool TryGetBitsPerSecond(ValidationContext context, TimeSpan? duration, out double bitsPerSecond)
    {
        if (context.EstimatedBitsPerSecond is double explicitRate)
        {
            bitsPerSecond = explicitRate;
            return true;
        }

        if (duration is not TimeSpan validDuration || validDuration <= TimeSpan.Zero)
        {
            bitsPerSecond = 0;
            return false;
        }

        if (context.GzipXmlSizeBytes is long gzipSize)
        {
            bitsPerSecond = CalculateGzipBitsPerSecond(gzipSize, validDuration);
            return true;
        }

        if (context.ExportedXmlBytes is byte[] xml)
        {
            bitsPerSecond = CalculateGzipBitsPerSecond(xml, validDuration);
            return true;
        }

        bitsPerSecond = 0;
        return false;
    }

    private static ResolvedFormat Resolve(SubtitleProject project, Cue cue, Section section)
        => FormatResolver.Resolve(project.GetStyle(section.StyleIdOverride ?? cue.StyleId).BaseFormat, section.Overrides);

    private static bool HasPcOnlyFeature(SubtitleProject project, Cue cue)
    {
        if (cue.Direction is TextDirection.VerticalRightToLeft or TextDirection.VerticalLeftToRight or TextDirection.RotatedLeftToRight or TextDirection.RotatedRightToLeft)
            return true;
        foreach (Section section in cue.Sections)
        {
            ResolvedFormat format = Resolve(project, cue, section);
            if (section.Ruby != RubyRole.None || format.Pack || format.Offset != ScriptOffset.Regular || format.Edge != EdgeType.None)
                return true;
        }
        return false;
    }

    private static bool HasAndroidOnlyUnsupportedFont(SubtitleProject project, Cue cue)
        => cue.Sections.Any(section => Resolve(project, cue, section).Font != YtFont.Default);

    private static bool HasMultipleShadows(SubtitleProject project, Cue cue, ValidationMetrics metrics)
    {
        int count = metrics.ShadowCount ?? 0;
        StylePreset style = project.GetStyle(cue.StyleId);
        foreach (Section section in cue.Sections)
        {
            ResolvedFormat format = Resolve(project, cue, section);
            count = Math.Max(count, (format.Edge == EdgeType.None ? 0 : 1) + style.ExtraEdges.Count);
        }
        return count > 1;
    }

    private static bool Has255Alpha(ResolvedFormat format)
        => format.Foreground.Alpha == byte.MaxValue || format.Background.Alpha == byte.MaxValue ||
           format.SecondaryColor.Alpha == byte.MaxValue || format.EdgeColor.Alpha == byte.MaxValue;

    private static bool IsPureWhite(RgbaColor color) => color.Red == byte.MaxValue && color.Green == byte.MaxValue && color.Blue == byte.MaxValue;

    private static double Luminance(RgbaColor color)
        => (0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue) / byte.MaxValue;

    private static bool Overlaps(Cue left, Cue right) => left.Start < right.End && right.Start < left.End;
}

/// <summary>짧은 검증기 이름을 선호하는 호출자를 위한 호환 별칭이다.</summary>
public sealed class Validator : DocumentValidator { }

/// <summary>검증과 W101 계산을 위한 정적 편의 진입점이다.</summary>
public static class ValidationService
{
    public static IReadOnlyList<ValidationIssue> Validate(SubtitleProject project, ValidationContext? context = null)
        => new DocumentValidator().Validate(project, context);

    public static double CalculateGzipBitsPerSecond(byte[] exportedXmlBytes, TimeSpan duration)
        => DocumentValidator.CalculateGzipBitsPerSecond(exportedXmlBytes, duration);

    public static double CalculateGzipBitsPerSecond(long gzipXmlSizeBytes, TimeSpan duration)
        => DocumentValidator.CalculateGzipBitsPerSecond(gzipXmlSizeBytes, duration);
}
