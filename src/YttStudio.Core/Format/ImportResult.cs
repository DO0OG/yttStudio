namespace YttStudio.Core.Format;

/// <summary>Contains an imported project and all non-fatal fidelity warnings.</summary>
public sealed record ImportResult(SubtitleProject Project, IReadOnlyList<ImportWarning> Warnings);

/// <summary>Describes information that could not be represented during import.</summary>
public sealed record ImportWarning(string Message, string? TagName = null, int? LineNumber = null);
