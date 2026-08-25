namespace YttStudio.Core.Format;

/// <summary>가져온 프로젝트와 치명적이지 않은 모든 손실 경고를 담는다.</summary>
public sealed record ImportResult(SubtitleProject Project, IReadOnlyList<ImportWarning> Warnings);

/// <summary>가져오는 동안 표현하지 못한 정보를 기술한다.</summary>
public sealed record ImportWarning(string Message, string? TagName = null, int? LineNumber = null);
