using System.Text.RegularExpressions;

namespace YttStudio.Core.Editing;

/// <summary>Controls how subtitle text is searched.</summary>
public sealed record TextSearchOptions
{
    /// <summary>Gets or sets whether the pattern is interpreted as a regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>Gets or sets whether literal and regular-expression matching is case-sensitive.</summary>
    public bool CaseSensitive { get; init; }
}

/// <summary>Describes one match within a subtitle section.</summary>
public sealed record TextSearchMatch
{
    /// <summary>Initializes a match description.</summary>
    public TextSearchMatch(int index, int length, string value)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ArgumentNullException.ThrowIfNull(value);
        Index = index;
        Length = length;
        Value = value;
    }

    /// <summary>Gets the zero-based UTF-16 index of the match.</summary>
    public int Index { get; }

    /// <summary>Gets the UTF-16 length of the match.</summary>
    public int Length { get; }

    /// <summary>Gets the matched text.</summary>
    public string Value { get; }

    /// <summary>Gets the exclusive zero-based UTF-16 end index of the match.</summary>
    public int End => checked(Index + Length);

    /// <summary>Gets the zero-based UTF-16 start index of the match.</summary>
    public int Start => Index;
}

/// <summary>Contains all matches found in one subtitle section.</summary>
public sealed record TextSearchResult
{
    /// <summary>Initializes a per-section search result.</summary>
    public TextSearchResult(
        Guid cueId,
        int sectionIndex,
        string text,
        IReadOnlyList<TextSearchMatch> matches)
    {
        if (sectionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        }

        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(matches);
        CueId = cueId;
        SectionIndex = sectionIndex;
        Text = text;
        Matches = matches.ToArray();
    }

    /// <summary>Gets the owning cue identifier.</summary>
    public Guid CueId { get; }

    /// <summary>Gets the zero-based section index inside the owning cue.</summary>
    public int SectionIndex { get; }

    /// <summary>Gets the section text at the time of the search.</summary>
    public string Text { get; }

    /// <summary>Gets the non-overlapping matches in section order.</summary>
    public IReadOnlyList<TextSearchMatch> Matches { get; }

    /// <summary>Gets the number of matches in this section.</summary>
    public int MatchCount => Matches.Count;
}

/// <summary>Searches subtitle section text without mutating the project.</summary>
public static class TextSearch
{
    /// <summary>Searches every <see cref="Section.Text"/> value in cue and section order.</summary>
    public static IReadOnlyList<TextSearchResult> Search(
        SubtitleProject project,
        string pattern,
        TextSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        SearchMatcher matcher = SearchMatcher.Create(pattern, options ?? new TextSearchOptions());
        List<TextSearchResult> results = [];

        foreach (Cue cue in project.Cues)
        {
            for (int sectionIndex = 0; sectionIndex < cue.Sections.Count; sectionIndex++)
            {
                Section section = cue.Sections[sectionIndex];
                string text = section.Text;
                IReadOnlyList<TextSearchMatch> matches = matcher.Find(text);
                if (matches.Count > 0)
                {
                    results.Add(new TextSearchResult(cue.Id, sectionIndex, text, matches));
                }
            }
        }

        return results;
    }

    /// <summary>Searches every section using explicit regex and case-sensitivity flags.</summary>
    public static IReadOnlyList<TextSearchResult> Search(
        SubtitleProject project,
        string pattern,
        bool useRegex,
        bool caseSensitive = false)
        => Search(project, pattern, new TextSearchOptions
        {
            UseRegex = useRegex,
            CaseSensitive = caseSensitive,
        });

    /// <summary>Alias for <see cref="Search(SubtitleProject, string, TextSearchOptions?)"/>.</summary>
    public static IReadOnlyList<TextSearchResult> Find(
        SubtitleProject project,
        string pattern,
        TextSearchOptions? options = null)
        => Search(project, pattern, options);

    /// <summary>Alias for <see cref="Search(SubtitleProject, string, bool, bool)"/>.</summary>
    public static IReadOnlyList<TextSearchResult> Find(
        SubtitleProject project,
        string pattern,
        bool useRegex,
        bool caseSensitive = false)
        => Search(project, pattern, useRegex, caseSensitive);

    internal static IReadOnlyList<TextReplacementPlan> PlanReplacement(
        SubtitleProject project,
        string pattern,
        string replacement,
        TextSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(replacement);
        SearchMatcher matcher = SearchMatcher.Create(pattern, options ?? new TextSearchOptions());
        List<TextReplacementPlan> plans = [];

        foreach (Cue cue in project.Cues)
        {
            for (int sectionIndex = 0; sectionIndex < cue.Sections.Count; sectionIndex++)
            {
                Section section = cue.Sections[sectionIndex];
                string text = section.Text;
                IReadOnlyList<TextSearchMatch> matches = matcher.Find(text);
                if (matches.Count == 0)
                {
                    continue;
                }

                // Compute every replacement before DocumentEditor executes any command. This keeps
                // invalid replacement templates and regex timeouts from partially mutating a project.
                string nextText = matcher.Replace(text, replacement, matches);
                plans.Add(new TextReplacementPlan(cue.Id, section, nextText, matches.Count));
            }
        }

        return plans;
    }

    private sealed class SearchMatcher
    {
        private readonly string pattern;
        private readonly Regex? regex;
        private readonly StringComparison comparison;

        private SearchMatcher(string pattern, Regex? regex, StringComparison comparison)
        {
            this.pattern = pattern;
            this.regex = regex;
            this.comparison = comparison;
        }

        public static SearchMatcher Create(string pattern, TextSearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            ArgumentNullException.ThrowIfNull(options);
            if (pattern.Length == 0)
            {
                throw new ArgumentException("Search pattern cannot be empty.", nameof(pattern));
            }

            if (pattern.Length > YttConstants.MaximumSearchPatternLength)
            {
                throw new ArgumentException(
                    $"Search pattern cannot exceed {YttConstants.MaximumSearchPatternLength} characters.",
                    nameof(pattern));
            }

            if (options.UseRegex)
            {
                RegexOptions regexOptions = RegexOptions.CultureInvariant;
                if (!options.CaseSensitive)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                Regex regex = new(
                    pattern,
                    regexOptions,
                    TimeSpan.FromMilliseconds(YttConstants.SearchRegexTimeoutMilliseconds));
                return new SearchMatcher(pattern, regex, StringComparison.Ordinal);
            }

            StringComparison comparison = options.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return new SearchMatcher(pattern, regex: null, comparison);
        }

        public IReadOnlyList<TextSearchMatch> Find(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (regex is Regex expression)
            {
                return expression.Matches(text)
                    .Select(match => new TextSearchMatch(match.Index, match.Length, match.Value))
                    .ToArray();
            }

            List<TextSearchMatch> matches = [];
            int offset = 0;
            while (offset <= text.Length - pattern.Length)
            {
                int index = text.IndexOf(pattern, offset, comparison);
                if (index < 0)
                {
                    break;
                }

                matches.Add(new TextSearchMatch(index, pattern.Length, text.Substring(index, pattern.Length)));
                offset = index + pattern.Length;
            }

            return matches;
        }

        public string Replace(
            string text,
            string replacement,
            IReadOnlyList<TextSearchMatch> matches)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(replacement);
            ArgumentNullException.ThrowIfNull(matches);
            if (regex is Regex expression)
            {
                return expression.Replace(text, replacement);
            }

            System.Text.StringBuilder builder = new(text.Length);
            int previousEnd = 0;
            foreach (TextSearchMatch match in matches)
            {
                builder.Append(text, previousEnd, match.Index - previousEnd);
                builder.Append(replacement);
                previousEnd = match.End;
            }

            builder.Append(text, previousEnd, text.Length - previousEnd);
            return builder.ToString();
        }
    }

    internal sealed record TextReplacementPlan(
        Guid CueId,
        Section Section,
        string ReplacementText,
        int MatchCount);
}
