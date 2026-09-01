using System.IO.Compression;
using System.Text;
using System.Drawing;
using YTSubConverter.Shared;
using YTSubConverter.Shared.Formats;
using YttStudio.Core.Editing;
using YttStudio.Core.Format;
using YttStudio.Core.Project;

namespace YttStudio.Core.Tests;

public sealed class MotionKeyframeTests
{
    [Fact]
    public void TimedMovesOnOneAssLineMergeIntoOneContinuousPath()
    {
        IReadOnlyList<CueEffect> effects = AssEffectCodec.Parse(
            "{\\move(10,20,30,40,0,1000)\\move(30,40,50,60,1000,2000)}");

        MoveEffect move = Assert.IsType<MoveEffect>(Assert.Single(effects));
        Assert.Equal(3, move.Keyframes.Count);
        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            move.Keyframes.Select(keyframe => keyframe.RelativeTime));
        Assert.Equal([10, 30, 50], move.Keyframes.Select(keyframe => keyframe.X));
        Assert.Equal([20, 40, 60], move.Keyframes.Select(keyframe => keyframe.Y));
    }

    [Fact]
    public void NonContinuousMovesRemainSeparateRuns()
    {
        IReadOnlyList<CueEffect> effects = AssEffectCodec.Parse(
            "{\\move(10,20,30,40,0,1000)\\move(31,40,50,60,1000,2000)}");

        Assert.Equal(2, effects.OfType<MoveEffect>().Count());
        Assert.All(effects.OfType<MoveEffect>(), move => Assert.Equal(2, move.Keyframes.Count));
    }

    [Fact]
    public void UntimedLegacyMoveRetainsScalarApi()
    {
        MoveEffect move = Assert.IsType<MoveEffect>(Assert.Single(AssEffectCodec.Parse(
            "{\\move(10,20,30,40)}")));

        Assert.Equal(10, move.FromX);
        Assert.Equal(20, move.FromY);
        Assert.Equal(30, move.ToX);
        Assert.Equal(40, move.ToY);
        Assert.Empty(move.Keyframes);
        Assert.Null(move.StartTime);
        Assert.Null(move.EndTime);
    }

    [Fact]
    public void KeyframeAssRoundTripPreservesThreePointPath()
    {
        SubtitleProject project = new() { Video = new VideoInfo(1280, 720, TimeSpan.FromSeconds(3), 30) };
        Cue cue = new(Guid.NewGuid()) { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(3) };
        cue.AddSection(new Section { Text = "motion" });
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 100, 200),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 300, 400),
            new MotionKeyframe(TimeSpan.FromSeconds(2), 500, 600),
        ]));
        project.Cues.Add(cue);

        string path = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ass");
        try
        {
            SubtitleFileService service = new();
            service.Export(project, path);
            string ass = File.ReadAllText(path);
            Assert.Equal(2, CountOccurrences(ass, "\\move("));
            Assert.Contains("\\ytmotion(v1.", ass, StringComparison.Ordinal);

            Cue importedCue = Assert.Single(service.Import(path).Project.Cues);
            MoveEffect imported = Assert.IsType<MoveEffect>(Assert.Single(importedCue.Effects));
            Assert.Equal(3, imported.Keyframes.Count);
            Assert.Equal(cue.Effects.OfType<MoveEffect>().Single().Keyframes,
                imported.Keyframes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NonLinearKeyframeMetadataIsAuthoritativeOverCompanionMoves()
    {
        MoveEffect source = new(
        [
            new MotionKeyframe(TimeSpan.Zero, 10, 20, MotionInterpolation.EaseIn, 2),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 30, 40, MotionInterpolation.EaseOut, 3),
            new MotionKeyframe(TimeSpan.FromSeconds(2), 50, 60, MotionInterpolation.EaseInOut, 4),
        ]);

        string encoded = AssEffectCodec.Encode([source]);
        IReadOnlyList<CueEffect> parsed = AssEffectCodec.Parse(encoded);
        MoveEffect result = Assert.IsType<MoveEffect>(Assert.Single(parsed));

        Assert.Equal(source.Keyframes, result.Keyframes);
        Assert.Equal(2, CountOccurrences(encoded, "\\move("));
    }

    [Fact]
    public void MetadataCanBeStrippedWhileStandardMovesRemainForUpstreamReaders()
    {
        MoveEffect source = new(
        [
            new MotionKeyframe(TimeSpan.Zero, 10, 20, MotionInterpolation.EaseIn, 2),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 30, 40, MotionInterpolation.EaseOut, 3),
        ]);

        string encoded = AssEffectCodec.Encode([source]);
        string stripped = AssEffectCodec.Strip(encoded);

        Assert.DoesNotContain("\\ytmotion", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("\\move", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyframesSurviveProjectJsonRoundTrip()
    {
        SubtitleProject project = new();
        Cue cue = new(Guid.NewGuid()) { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(2) };
        cue.AddSection(new Section { Text = "motion" });
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(TimeSpan.Zero, 10, 20, MotionInterpolation.EaseIn, 2),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 30, 40, MotionInterpolation.EaseOut, 3),
        ]));
        project.Cues.Add(cue);

        using MemoryStream stream = new();
        ProjectPackage.Save(project, stream);
        Cue loadedCue = Assert.Single(ProjectPackage.Read(stream).Project.Cues);
        MoveEffect loaded = Assert.IsType<MoveEffect>(Assert.Single(loadedCue.Effects));

        Assert.Equal(cue.Effects.OfType<MoveEffect>().Single().Keyframes, loaded.Keyframes);
    }

    [Fact]
    public void VersionTwoProjectMigratesToKeyframeSchema()
    {
        using MemoryStream package = CreateLegacyPackage(
            2,
            "{\"schemaVersion\":2,\"videoPath\":null,\"video\":null,\"settings\":{\"previewBackground\":{\"red\":32,\"green\":32,\"blue\":32,\"alpha\":255},\"useCheckerboard\":false},\"styles\":[],\"cues\":[{\"id\":\"01234567-89ab-cdef-0123-456789abcdef\",\"start\":\"00:00:00\",\"end\":\"00:00:01\",\"track\":0,\"zOrder\":0,\"anchor\":\"bottomCenter\",\"positionX\":50,\"positionY\":90,\"justify\":\"center\",\"direction\":\"horizontal\",\"styleId\":null,\"sections\":[{\"text\":\"legacy\",\"karaokeOffset\":null,\"overrides\":{},\"ruby\":\"none\",\"rubyText\":null,\"styleIdOverride\":null}],\"effects\":[{\"kind\":\"move\",\"fromX\":10,\"fromY\":20,\"toX\":30,\"toY\":40,\"startTime\":\"00:00:00\",\"endTime\":\"00:00:01\"}]}]}");

        ProjectPackageReadResult result = ProjectPackage.Read(package);

        Assert.True(result.WasMigrated);
        Assert.Equal(2, result.SourceSchemaVersion);
        MoveEffect move = Assert.IsType<MoveEffect>(Assert.Single(Assert.Single(result.Project.Cues).Effects));
        Assert.Equal(10, move.FromX);
        Assert.Equal(30, move.ToX);
    }

    [Fact]
    public void EditorOperationsReplacePathsAndUndoWithoutAliasing()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), "motion");
        List<MotionKeyframe> source =
        [
            new MotionKeyframe(TimeSpan.Zero, 10, 20),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 30, 40),
        ];

        editor.ReplaceKeyframes(cue.Id, source);
        source[0] = new MotionKeyframe(TimeSpan.Zero, 999, 999);
        Assert.Equal(10, Assert.IsType<MoveEffect>(Assert.Single(cue.Effects)).Keyframes[0].X);

        editor.AddKeyframe(cue.Id, new MotionKeyframe(TimeSpan.FromSeconds(2), 50, 60));
        Assert.Equal(3, Assert.IsType<MoveEffect>(Assert.Single(cue.Effects)).Keyframes.Count);
        editor.Undo();
        Assert.Equal(2, Assert.IsType<MoveEffect>(Assert.Single(cue.Effects)).Keyframes.Count);
        editor.Redo();

        Cue duplicate = Assert.Single(editor.DuplicateCues([cue.Id]));
        MoveEffect originalMove = Assert.IsType<MoveEffect>(Assert.Single(cue.Effects));
        MoveEffect duplicateMove = Assert.IsType<MoveEffect>(Assert.Single(duplicate.Effects));
        Assert.Equal(originalMove.Keyframes, duplicateMove.Keyframes);
        Assert.NotSame(originalMove, duplicateMove);
        Assert.NotSame(originalMove.Keyframes[0], duplicateMove.Keyframes[0]);
    }

    [Fact]
    public void DeletingFinalKeyframeRemovesMoveEffect()
    {
        SubtitleProject project = new();
        DocumentEditor editor = new(project);
        Cue cue = editor.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "motion");
        editor.ReplaceKeyframes(cue.Id, [new MotionKeyframe(TimeSpan.Zero, 10, 20)]);

        editor.DeleteKeyframe(cue.Id, 0);

        Assert.Empty(cue.Effects);
        editor.Undo();
        Assert.Single(cue.Effects);
        editor.Redo();
        Assert.Empty(cue.Effects);
    }

    [Fact]
    public void KeyframesCannotBeMutatedThroughReadOnlyListCast()
    {
        MoveEffect move = new(
        [
            new MotionKeyframe(TimeSpan.Zero, 10, 20),
            new MotionKeyframe(TimeSpan.FromSeconds(1), 30, 40),
        ]);

        Assert.False(move.Keyframes is MotionKeyframe[]);
        IList<MotionKeyframe> list = Assert.IsAssignableFrom<IList<MotionKeyframe>>(move.Keyframes);
        Assert.Throws<NotSupportedException>(() => list[0] = new MotionKeyframe(TimeSpan.Zero, 999, 999));
        Assert.Equal(10, move.Keyframes[0].X);
    }

    [Fact]
    public void YttFlattenTreatsAccelerationExponentToleranceBoundaryAsLinear()
    {
        double tolerance = YttConstants.MotionAccelerationExponentTolerance;
        double[] accelerations = [1 - tolerance, 1 + tolerance];
        double expectedX = ExportFlattenedMidpoint(1.0).X;
        foreach (double acceleration in accelerations)
        {
            PointF midpoint = ExportFlattenedMidpoint(acceleration);

            Assert.InRange(Math.Abs(midpoint.X - expectedX), 0, 1);
        }
    }

    [Fact]
    public void YttFlattenUsesPowerOutsideAccelerationExponentTolerance()
    {
        double tolerance = YttConstants.MotionAccelerationExponentTolerance;
        PointF midpoint = ExportFlattenedMidpoint(1 + (2 * tolerance));

        Assert.True(
            Math.Abs(midpoint.X - (YttConstants.ReferenceWidth / 2f)) > 5,
            $"Expected a power-eased midpoint, but got {midpoint.X} px.");
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static MemoryStream CreateLegacyPackage(int version, string projectJson)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", $"{{\"schemaVersion\":{version}}}");
            Write(archive, "project.json", projectJson);
            Write(archive, "thumbnail.png", string.Empty);
        }

        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
    }

    private static PointF ExportFlattenedMidpoint(double acceleration)
    {
        const double midpoint = YttConstants.ReferenceWidth / 2.0;
        // YTT는 정수 좌표로 저장하므로 공개 출력에서 경계 밖의 차이를 관찰할 수 있게 경로만 확대한다.
        const double span = 10_000_000;
        SubtitleProject project = new()
        {
            Video = new VideoInfo(
                YttConstants.ReferenceWidth,
                YttConstants.ReferenceHeight,
                TimeSpan.FromSeconds(2),
                30),
        };
        Cue cue = new(Guid.NewGuid())
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(2),
        };
        cue.AddSection(new Section { Text = "motion" });
        cue.AddEffect(new MoveEffect(
        [
            new MotionKeyframe(
                TimeSpan.Zero,
                midpoint - span,
                midpoint,
                MotionInterpolation.Linear,
                acceleration),
            new MotionKeyframe(TimeSpan.FromSeconds(1), midpoint + span, midpoint),
        ]));
        cue.AddEffect(new MoveEffect(
            midpoint,
            midpoint,
            midpoint,
            midpoint,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1500)));
        project.Cues.Add(cue);

        string path = Path.Combine(Path.GetTempPath(), $"yttstudio-{Guid.NewGuid():N}.ytt");
        try
        {
            new SubtitleFileService().Export(project, path);
            YttDocument external = new(path);
            DateTime expectedStart = SubtitleDocument.TimeBase.AddMilliseconds(500);
            Line midpointLine = external.Lines
                .Where(line => line.Text.Contains("motion", StringComparison.Ordinal))
                .Where(line => Math.Abs((line.Start - expectedStart).TotalMilliseconds) <= 50)
                .OrderBy(line => Math.Abs((line.Start - expectedStart).TotalMilliseconds))
                .First();
            return Assert.IsType<PointF>(midpointLine.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
