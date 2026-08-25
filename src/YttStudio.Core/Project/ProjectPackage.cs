using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YttStudio.Core.Project.Migrations;

namespace YttStudio.Core.Project;

/// <summary>버전이 있는 <c>.yttproj</c> 프로젝트 패키지를 읽고 쓴다.</summary>
/// <remarks>
/// 패키지에는 항상 <c>manifest.json</c> 과 <c>project.json</c> 과
/// <c>thumbnail.png</c> 가 들어간다. 썸네일이 없으면 0 바이트 항목으로 표현하며
/// 패키지 작성기는 이미지 데이터를 지어내지 않는다.
/// </remarks>
public static class ProjectPackage
{
    /// <summary>현재 프로젝트 JSON 과 매니페스트 스키마 버전이다.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>매니페스트 항목 이름이다.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>프로젝트 JSON 항목 이름이다.</summary>
    public const string ProjectEntryName = "project.json";

    /// <summary>선택적 이미지 항목 이름이다. 항목 자체는 항상 존재한다.</summary>
    public const string ThumbnailEntryName = "thumbnail.png";

    private const long MaximumManifestBytes = 64 * 1024;
    private const long MaximumProjectBytes = 16 * 1024 * 1024;
    private const long MaximumThumbnailBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>프로젝트 패키지를 쓰기 가능한 스트림에 저장한다.</summary>
    /// <param name="project">직렬화할 프로젝트다.</param>
    /// <param name="destination">대상 스트림이다.</param>
    /// <param name="thumbnailPng">호출자가 준 PNG 바이트다. 빈 항목이면 <see langword="null"/> 이다.</param>
    public static void Save(SubtitleProject project, Stream destination, byte[]? thumbnailPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (thumbnailPng is not null && thumbnailPng.LongLength > MaximumThumbnailBytes)
        {
            throw new ArgumentException("The thumbnail is larger than the package limit.", nameof(thumbnailPng));
        }

        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(
            new ManifestJsonDto(CurrentSchemaVersion), JsonOptions);
        byte[] projectJson = JsonSerializer.SerializeToUtf8Bytes(
            ProjectJsonDto.FromModel(project, CurrentSchemaVersion), JsonOptions);

        if (destination.CanSeek)
        {
            destination.Position = 0;
            destination.SetLength(0);
        }

        using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, ManifestEntryName, manifest);
        WriteEntry(archive, ProjectEntryName, projectJson);
        // [CONTRACT] thumbnail.png 항목은 필수지만 가짜 이미지를 만들어서는 안 된다.
        WriteEntry(archive, ThumbnailEntryName, thumbnailPng ?? []);
    }

    /// <summary>프로젝트 패키지를 파일 경로에 저장한다.</summary>
    /// <param name="project">직렬화할 프로젝트다.</param>
    /// <param name="filePath">출력 파일 경로다.</param>
    /// <param name="thumbnailPng">호출자가 준 PNG 바이트다. 빈 항목이면 <see langword="null"/> 이다.</param>
    public static void Save(SubtitleProject project, string filePath, byte[]? thumbnailPng = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.Create(filePath);
        Save(project, stream, thumbnailPng);
    }

    /// <summary>패키지 스트림에서 프로젝트 모델을 불러온다.</summary>
    /// <param name="source">읽을 수 있는 패키지 스트림이다.</param>
    /// <returns>새로 만들어진 프로젝트 모델이다. 실행 취소 기록은 만들지 않는다.</returns>
    public static SubtitleProject Load(Stream source) => Read(source).Project;

    /// <summary>패키지 파일에서 프로젝트 모델을 불러온다.</summary>
    /// <param name="filePath">패키지 파일 경로다.</param>
    /// <returns>새로 만들어진 프로젝트 모델이다. 실행 취소 기록은 만들지 않는다.</returns>
    public static SubtitleProject Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>패키지를 불러오고 썸네일과 마이그레이션 메타데이터를 노출한다.</summary>
    /// <param name="source">읽을 수 있는 패키지 스트림이다.</param>
    /// <returns>새로 만들어진 모델과 패키지 메타데이터다.</returns>
    public static ProjectPackageReadResult Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        }

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using ZipArchive archive = new(source, ZipArchiveMode.Read, leaveOpen: true);
        Dictionary<string, ZipArchiveEntry> entries = CollectAndValidateEntries(archive);
        byte[] manifestBytes = ReadEntry(entries[ManifestEntryName], MaximumManifestBytes);
        byte[] projectBytes = ReadEntry(entries[ProjectEntryName], MaximumProjectBytes);
        byte[] thumbnailBytes = ReadEntry(entries[ThumbnailEntryName], MaximumThumbnailBytes);

        int sourceVersion = ReadManifestVersion(manifestBytes);
        if (sourceVersion < 0 || sourceVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported project schema version {sourceVersion}.");
        }

        JsonNode projectJson;
        try
        {
            projectJson = JsonNode.Parse(projectBytes)
                ?? throw new InvalidDataException("project.json must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("project.json is not valid JSON.", exception);
        }

        if (projectJson is not JsonObject projectObject)
        {
            throw new InvalidDataException("project.json must contain a JSON object.");
        }

        int? embeddedVersion = JsonNodeHelpers.TryGetInt32(projectObject, "schemaVersion");
        if (embeddedVersion is > CurrentSchemaVersion || embeddedVersion is < 0)
        {
            throw new InvalidDataException("project.json declares an unsupported schema version.");
        }

        if (embeddedVersion is int version && version > sourceVersion)
        {
            throw new InvalidDataException("manifest.json is older than project.json.");
        }

        JsonNode migrated = ProjectMigrationPipeline.Default.Migrate(projectJson, sourceVersion);
        if (migrated is not JsonObject migratedObject ||
            JsonNodeHelpers.TryGetInt32(migratedObject, "schemaVersion") != CurrentSchemaVersion)
        {
            throw new InvalidDataException("Project migration did not produce the current schema.");
        }

        ProjectJsonDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProjectJsonDto>(migrated.ToJsonString(), JsonOptions)
                ?? throw new InvalidDataException("project.json is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("project.json does not match the project schema.", exception);
        }

        SubtitleProject project = dto.ToModel();
        return new ProjectPackageReadResult(project, thumbnailBytes, sourceVersion);
    }

    /// <summary>패키지를 불러오고 썸네일과 마이그레이션 메타데이터를 노출한다.</summary>
    /// <param name="filePath">패키지 파일 경로다.</param>
    /// <returns>새로 만들어진 모델과 패키지 메타데이터다.</returns>
    public static ProjectPackageReadResult Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.OpenRead(filePath);
        return Read(stream);
    }

    /// <summary>열기 동작을 선호하는 호출자를 위한 <see cref="Read(Stream)"/> 별칭이다.</summary>
    public static ProjectPackageReadResult Open(Stream source) => Read(source);

    internal static JsonSerializerOptions SerializationOptions => JsonOptions;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static Dictionary<string, ZipArchiveEntry> CollectAndValidateEntries(ZipArchive archive)
    {
        HashSet<string> allowed = [ManifestEntryName, ProjectEntryName, ThumbnailEntryName];
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName);
            if (!allowed.Contains(entry.FullName))
            {
                throw new InvalidDataException($"Unexpected project package entry '{entry.FullName}'.");
            }

            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException($"Duplicate project package entry '{entry.FullName}'.");
            }
        }

        foreach (string required in allowed)
        {
            if (!entries.ContainsKey(required))
            {
                throw new InvalidDataException($"Required project package entry '{required}' is missing.");
            }
        }

        return entries;
    }

    private static void ValidateEntryName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Contains('\0') ||
            fullName.Contains('\\') || fullName.StartsWith("/", StringComparison.Ordinal) ||
            fullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(fullName) ||
            fullName.Contains(":", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsafe project package entry name '{fullName}'.");
        }

        if (fullName.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Directory project package entry '{fullName}' is not allowed.");
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumBytes)
    {
        if (entry.Length > maximumBytes || entry.Length < 0)
        {
            throw new InvalidDataException($"Project package entry '{entry.FullName}' exceeds its size limit.");
        }

        using Stream source = entry.Open();
        using MemoryStream destination = new();
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"Project package entry '{entry.FullName}' exceeds its size limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static int ReadManifestVersion(byte[] manifestBytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(manifestBytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int version))
            {
                throw new InvalidDataException("manifest.json must contain an integer schemaVersion.");
            }

            return version;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("manifest.json is not valid JSON.", exception);
        }
    }
}

/// <summary>프로젝트 패키지를 연 결과다. 모델 외 패키지 메타데이터를 포함한다.</summary>
public sealed class ProjectPackageReadResult
{
    internal ProjectPackageReadResult(SubtitleProject project, byte[] thumbnailPng, int sourceSchemaVersion)
    {
        Project = project;
        ThumbnailPng = thumbnailPng.ToArray();
        SourceSchemaVersion = sourceSchemaVersion;
    }

    /// <summary>새로 만들어진 프로젝트 모델을 가져온다.</summary>
    public SubtitleProject Project { get; }

    /// <summary>호출자가 준 썸네일 바이트를 가져온다. 저장된 썸네일이 없으면 빈 배열이다.</summary>
    public byte[] ThumbnailPng { get; }

    /// <summary>패키지 매니페스트가 선언한 스키마 버전을 가져온다.</summary>
    public int SourceSchemaVersion { get; }

    /// <summary>패키지가 마이그레이션을 거쳤는지 가져온다.</summary>
    public bool WasMigrated => SourceSchemaVersion != ProjectPackage.CurrentSchemaVersion;

    /// <summary>마이그레이션 후 스키마 버전을 가져온다.</summary>
    public int SchemaVersion => ProjectPackage.CurrentSchemaVersion;
}

internal sealed record ManifestJsonDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion);

internal static class JsonNodeHelpers
{
    public static int? TryGetInt32(JsonObject objectNode, string propertyName)
    {
        if (!objectNode.TryGetPropertyValue(propertyName, out JsonNode? node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<int>(out int intValue))
        {
            return intValue;
        }

        return null;
    }
}
