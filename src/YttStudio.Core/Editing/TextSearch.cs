using System.Text.RegularExpressions;

namespace YttStudio.Core.Editing;

/// <summary>자막 텍스트 검색 방식을 제어한다.</summary>
public sealed record TextSearchOptions
{
    /// <summary>패턴을 정규식으로 해석할지 가져오거나 설정한다.</summary>
    public bool UseRegex { get; init; }

    /// <summary>리터럴과 정규식 일치가 대소문자를 구분하는지 가져오거나 설정한다.</summary>
    public bool CaseSensitive { get; init; }
}

/// <summary>자막 섹션 안의 일치 하나를 기술한다.</summary>
public sealed record TextSearchMatch
{
    /// <summary>일치 정보를 초기화한다.</summary>
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

    /// <summary>일치의 0 기반 UTF-16 인덱스를 가져온다.</summary>
    public int Index { get; }

    /// <summary>일치의 UTF-16 길이를 가져온다.</summary>
    public int Length { get; }

    /// <summary>일치한 텍스트를 가져온다.</summary>
    public string Value { get; }

    /// <summary>일치의 0 기반 UTF-16 끝 인덱스를 가져온다. 끝은 포함하지 않는다.</summary>
    public int End => checked(Index + Length);

    /// <summary>일치의 0 기반 UTF-16 시작 인덱스를 가져온다.</summary>
    public int Start => Index;
}

/// <summary>자막 섹션 하나에서 찾은 모든 일치를 담는다.</summary>
public sealed record TextSearchResult
{
    /// <summary>섹션별 검색 결과를 초기화한다.</summary>
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

    /// <summary>소유 큐 식별자를 가져온다.</summary>
    public Guid CueId { get; }

    /// <summary>소유 큐 안에서의 0 기반 섹션 인덱스를 가져온다.</summary>
    public int SectionIndex { get; }

    /// <summary>검색 시점의 섹션 텍스트를 가져온다.</summary>
    public string Text { get; }

    /// <summary>겹치지 않는 일치를 섹션 순서로 가져온다.</summary>
    public IReadOnlyList<TextSearchMatch> Matches { get; }

    /// <summary>이 섹션의 일치 개수를 가져온다.</summary>
    public int MatchCount => Matches.Count;
}

/// <summary>프로젝트를 바꾸지 않고 자막 섹션 텍스트를 검색한다.</summary>
public static class TextSearch
{
    /// <summary>큐와 섹션 순서로 모든 <see cref="Section.Text"/> 값을 검색한다.</summary>
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

    /// <summary>정규식과 대소문자 구분 플래그를 명시해 모든 섹션을 검색한다.</summary>
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

    /// <summary><see cref="Search(SubtitleProject, string, TextSearchOptions?)"/> 의 별칭이다.</summary>
    public static IReadOnlyList<TextSearchResult> Find(
        SubtitleProject project,
        string pattern,
        TextSearchOptions? options = null)
        => Search(project, pattern, options);

    /// <summary><see cref="Search(SubtitleProject, string, bool, bool)"/> 의 별칭이다.</summary>
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

                // DocumentEditor 가 커맨드를 실행하기 전에 모든 치환을 계산한다. 그래야
                // 잘못된 치환 템플릿이나 정규식 타임아웃이 프로젝트를 일부만 바꾸는 일이 없다.
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
