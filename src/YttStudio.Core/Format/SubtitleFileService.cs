using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YTSubConverter.Shared.Formats.Ass;

namespace YttStudio.Core.Format;

/// <summary>고정된 변환기를 통해 자막 형식을 가져오고 내보낸다.</summary>
public sealed partial class SubtitleFileService
{
    /// <summary>.ytt 나 .srv3 나 .ass 문서를 가져온다.</summary>
    /// <summary>자막 파일 하나가 가질 수 있는 최대 바이트다.</summary>
    /// <remarks>
    /// 두 포맷 모두 파일을 통째로 메모리에 올린 뒤 다시 모델로 펼친다. 상한이 없으면 아주 큰
    /// 파일 하나로 메모리를 소진시킬 수 있다. 유튜브 자막은 압축 전 기준으로도 수백 KB 를
    /// 넘기 어렵다. 64MB 는 그 천 배가 넘어 정상적인 작업을 막지 않으면서 폭주만 끊는다.
    /// </remarks>
    public const long MaximumImportBytes = 64L * 1024 * 1024;

    public ImportResult Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureImportSizeWithinLimit(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ytt" or ".srv3" => ImportYtt(path),
            ".ass" => ImportAss(path),
            _ => throw new NotSupportedException($"The '{extension}' format is outside the M1 import scope."),
        };
    }

    /// <summary>upstream 의 Save 메서드로 프로젝트를 .ytt 나 .srv3 나 .ass 로 내보낸다.</summary>
    public void Export(SubtitleProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string extension = Path.GetExtension(path).ToLowerInvariant();
        Cue[] orderedCues = project.Cues.OrderBy(cue => cue.ZOrder).ThenBy(cue => cue.Start).ToArray();
        IReadOnlyList<ExportCue> exportCues = extension is ".ytt" or ".srv3"
            ? ExpandMotionCues(orderedCues)
            : orderedCues.Select(ExportCue.FromCue).ToArray();
        AdapterDocument adapterDocument = ToExternalDocument(project, exportCues);
        AssDocument assDocument = new(adapterDocument);
        bool hasEffects = exportCues.Any(cue => cue.Effects.Count > 0);
        if (hasEffects)
        {
            ExportWithEffects(assDocument, exportCues, path, extension);
            return;
        }

        switch (extension)
        {
            case ".ytt":
            case ".srv3":
                // [UPSTREAM] 전처리와 풀 ID 와 더미 항목과 head 순서는 모두 Save 가 소유한다.
                // 근거: YttDocument.Save()/WriteHead(), docs/YTT-VERIFICATION.md
                new YttDocument(assDocument).Save(path);
                break;
            case ".ass":
                assDocument.Save(path);
                break;
            default:
                throw new NotSupportedException($"The '{extension}' format is outside the M1 export scope.");
        }
    }
}
