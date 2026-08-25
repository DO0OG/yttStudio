using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YttStudio.Core.Project.Migrations;

namespace YttStudio.Core.Project;

/// <summary>Reads and writes the versioned <c>.yttproj</c> project package.</summary>
/// <remarks>
/// A package always contains <c>manifest.json</c>, <c>project.json</c>, and
/// <c>thumbnail.png</c>. A missing thumbnail is represented by a zero-byte entry;
/// the package writer never invents image data.
/// </remarks>
public static class ProjectPackage
{
    /// <summary>Current project JSON and manifest schema version.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The manifest entry name.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>The project JSON entry name.</summary>
    public const string ProjectEntryName = "project.json";

    /// <summary>The optional-image entry name (the entry itself is always present).</summary>
    public const string ThumbnailEntryName = "thumbnail.png";

    private const long MaximumManifestBytes = 64 * 1024;
    private const long MaximumProjectBytes = 16 * 1024 * 1024;
    private const long MaximumThumbnailBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Saves a project package to a writable stream.</summary>
    /// <param name="project">The project to serialize.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="thumbnailPng">Caller-provided PNG bytes, or <see langword="null"/> for an empty entry.</param>
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
        // SPEC §12 [CONTRACT]: thumbnail.png is required, but no fake image may be generated.
        WriteEntry(archive, ThumbnailEntryName, thumbnailPng ?? []);
    }

    /// <summary>Saves a project package to a file path.</summary>
    /// <param name="project">The project to serialize.</param>
    /// <param name="filePath">The output file path.</param>
    /// <param name="thumbnailPng">Caller-provided PNG bytes, or <see langword="null"/> for an empty entry.</param>
    public static void Save(SubtitleProject project, string filePath, byte[]? thumbnailPng = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.Create(filePath);
        Save(project, stream, thumbnailPng);
    }

    /// <summary>Loads the project model from a package stream.</summary>
    /// <param name="source">A readable package stream.</param>
    /// <returns>A newly created project model; no undo stack is involved.</returns>
    public static SubtitleProject Load(Stream source) => Read(source).Project;

    /// <summary>Loads the project model from a package file.</summary>
    /// <param name="filePath">The package file path.</param>
    /// <returns>A newly created project model; no undo stack is involved.</returns>
    public static SubtitleProject Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>Loads a package and exposes its thumbnail and migration metadata.</summary>
    /// <param name="source">A readable package stream.</param>
    /// <returns>The newly created model and package metadata.</returns>
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

    /// <summary>Loads a package and exposes its thumbnail and migration metadata.</summary>
    /// <param name="filePath">The package file path.</param>
    /// <returns>The newly created model and package metadata.</returns>
    public static ProjectPackageReadResult Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using FileStream stream = File.OpenRead(filePath);
        return Read(stream);
    }

    /// <summary>Alias for <see cref="Read(Stream)"/> for callers that prefer an open operation.</summary>
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

/// <summary>Result of opening a project package, including non-model package metadata.</summary>
public sealed class ProjectPackageReadResult
{
    internal ProjectPackageReadResult(SubtitleProject project, byte[] thumbnailPng, int sourceSchemaVersion)
    {
        Project = project;
        ThumbnailPng = thumbnailPng.ToArray();
        SourceSchemaVersion = sourceSchemaVersion;
    }

    /// <summary>Gets the newly created project model.</summary>
    public SubtitleProject Project { get; }

    /// <summary>Gets the caller-provided thumbnail bytes, or an empty array when no thumbnail was saved.</summary>
    public byte[] ThumbnailPng { get; }

    /// <summary>Gets the schema version declared by the package manifest.</summary>
    public int SourceSchemaVersion { get; }

    /// <summary>Gets whether the package passed through a migration.</summary>
    public bool WasMigrated => SourceSchemaVersion != ProjectPackage.CurrentSchemaVersion;

    /// <summary>Gets the schema version after migration.</summary>
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
