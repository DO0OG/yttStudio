using YttStudio.Core.Editing;
using YttStudio.Core.Validation;

namespace YttStudio.Core.Tests;

public sealed class ValidationTests
{
    [Theory]
    [MemberData(nameof(RuleCases))]
    public void ReportsEachRule(string expectedCode, Action<SubtitleProject, Cue, ValidationContextBuilder> arrange)
    {
        (SubtitleProject project, Cue cue) = CreateProject();
        ValidationContextBuilder context = new(project);
        arrange(project, cue, context);

        IReadOnlyList<ValidationIssue> issues = new DocumentValidator().Validate(project, context.Build());

        Assert.Contains(issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void AutoFixIsUndoable()
    {
        (SubtitleProject project, Cue cue) = CreateProject();
        const string code = "E003";
        cue.Sections[0].KaraokeOffset = TimeSpan.Zero;
        cue.AddSection(new Section { Text = "b", KaraokeOffset = TimeSpan.Zero });
        DocumentValidator validator = new();
        ValidationIssue issue = Assert.Single(validator.Validate(project), item => item.Code == code);
        DocumentEditor editor = new(project);

        Assert.True(validator.ApplyAutoFix(editor, issue));
        Assert.DoesNotContain(validator.Validate(project), item => item.Code == code);
        editor.Undo();
        Assert.Contains(validator.Validate(project), item => item.Code == code);
    }

    [Theory]
    [InlineData(7168, false)]
    [InlineData(7168.01, true)]
    [InlineData(10240, true)]
    public void W101UsesEarlyApproximateThreshold(double bitsPerSecond, bool expected)
    {
        (SubtitleProject project, _) = CreateProject();
        ValidationContext context = new(project) { EstimatedBitsPerSecond = bitsPerSecond };
        ValidationIssue? issue = new DocumentValidator().Validate(project, context)
            .SingleOrDefault(item => item.Code == ValidationCodes.W101);
        Assert.Equal(expected, issue is not null);
        if (issue is not null)
        {
            Assert.Contains("근사치", issue.Message);
            Assert.Contains("업로드 후 확인", issue.Message);
        }
    }

    [Theory]
    [InlineData("일반 플레이어")]
    [InlineData("극장 모드")]
    [InlineData("전체화면")]
    public void W103UsesViewportModeDisplayName(string viewportModeDisplayName)
    {
        (SubtitleProject project, Cue cue) = CreateProject();
        ValidationContext context = new(project)
        {
            Metrics = new ValidationMetrics
            {
                IsOutsideSafeArea = true,
                ViewportModeDisplayName = viewportModeDisplayName,
            },
        };

        ValidationIssue issue = Assert.Single(new DocumentValidator().Validate(project, context), item => item.Code == ValidationCodes.W103);

        Assert.Equal($"세이프 에어리어 밖에 있어 {viewportModeDisplayName}에서 화면 밖으로 밀릴 수 있습니다.", issue.Message);
        Assert.Equal(cue.Id, issue.CueId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void W103FallsBackToTheaterModeWhenViewportModeDisplayNameIsMissing(string? viewportModeDisplayName)
    {
        (SubtitleProject project, _) = CreateProject();
        ValidationContext context = new(project)
        {
            Metrics = new ValidationMetrics
            {
                IsOutsideSafeArea = true,
                ViewportModeDisplayName = viewportModeDisplayName,
            },
        };

        ValidationIssue issue = Assert.Single(new DocumentValidator().Validate(project, context), item => item.Code == ValidationCodes.W103);

        Assert.Equal("세이프 에어리어 밖에 있어 극장 모드에서 화면 밖으로 밀릴 수 있습니다.", issue.Message);
    }

    public static IEnumerable<object[]> RuleCases()
    {
        yield return Case("E001", (_, cue, _) => cue.Start = TimeSpan.Zero);
        yield return Case("E002", (_, cue, _) => cue.End = cue.Start);
        yield return Case("E003", (_, cue, _) => { cue.Sections[0].KaraokeOffset = TimeSpan.Zero; cue.AddSection(new Section { Text = "b", KaraokeOffset = TimeSpan.Zero }); });
        yield return Case("E004", (_, cue, _) => cue.Sections[0].Overrides.Foreground = new RgbaColor(255, 255, 255, 254));
        yield return Case("E005", (_, cue, _) => cue.Sections[0].Overrides.Foreground = new RgbaColor(1, 2, 3, 255));
        yield return Case("E006", (project, cue, _) => project.Video = new VideoInfo(1280, 720, cue.End - TimeSpan.FromMilliseconds(1), 30));
        yield return MetricCase("W102", new ValidationMetrics { MobileEffectRisk = true });
        yield return MetricCase("W103", new ValidationMetrics { IsOutsideSafeArea = true });
        yield return MetricCase("W104", new ValidationMetrics { HasDarkText = true });
        yield return MetricCase("W105", new ValidationMetrics { BoxWidthExceeded = true });
        yield return MetricCase("W106", new ValidationMetrics { MultipleShadows = true });
        yield return MetricCase("W107", new ValidationMetrics { SizeAtLowerBound = true });
        yield return Case("W108", (_, cue, _) => cue.Sections[0].Overrides.SizePercent = 201);
        yield return MetricCase("I201", new ValidationMetrics { UsesPcOnlyFeature = true });
        yield return MetricCase("I202", new ValidationMetrics { FontIgnoredOnAndroid = true });
        yield return Case("I203", (project, cue, _) => { Cue other = new(Guid.NewGuid()) { Start = cue.Start, End = cue.End, ZOrder = 2 }; other.AddSection(new Section { Text = "overlap" }); project.Cues.Add(other); });
    }

    private static object[] MetricCase(string code, ValidationMetrics metrics)
        => Case(code, (_, cue, context) => context.SetCue(cue.Id, metrics));

    private static object[] Case(string code, Action<SubtitleProject, Cue, ValidationContextBuilder> arrange)
        => [code, arrange];

    private static (SubtitleProject Project, Cue Cue) CreateProject()
    {
        SubtitleProject project = new() { Video = new VideoInfo(1280, 720, TimeSpan.FromSeconds(10), 30) };
        Cue cue = new(Guid.NewGuid()) { Start = TimeSpan.FromMilliseconds(1), End = TimeSpan.FromSeconds(2) };
        cue.AddSection(new Section { Text = "text", Overrides = new SectionOverrides { Edge = EdgeType.None } });
        project.Cues.Add(cue);
        return (project, cue);
    }

    public sealed class ValidationContextBuilder(SubtitleProject project)
    {
        private readonly Dictionary<Guid, ValidationMetrics> cueMetrics = [];
        public void SetCue(Guid cueId, ValidationMetrics metrics) => cueMetrics[cueId] = metrics;
        public ValidationContext Build() => new(project) { CueMetrics = cueMetrics };
    }
}
