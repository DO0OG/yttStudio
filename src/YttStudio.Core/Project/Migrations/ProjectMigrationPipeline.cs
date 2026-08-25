using System.Text.Json.Nodes;

namespace YttStudio.Core.Project.Migrations;

internal interface IProjectMigration
{
    int FromVersion { get; }
    JsonObject Apply(JsonObject project);
}

internal sealed class ProjectMigrationPipeline
{
    private readonly IReadOnlyDictionary<int, IProjectMigration> migrations;

    // Default 싱글턴을 통해 만들어 마이그레이션 순서를 한 곳에 모은다.
    // internal 로 두면 그 의도를 유지하면서 어셈블리 안에서는 생성할 수 있다.
    internal ProjectMigrationPipeline(IEnumerable<IProjectMigration> migrations)
    {
        this.migrations = migrations.ToDictionary(migration => migration.FromVersion);
    }

    public static ProjectMigrationPipeline Default { get; } = new(
        [new Version0To1Migration(), new Version1To2Migration()]);

    public JsonNode Migrate(JsonNode source, int sourceVersion)
    {
        if (source is not JsonObject project)
        {
            throw new InvalidDataException("project.json must contain a JSON object.");
        }

        JsonObject current = (JsonObject)project.DeepClone();
        int version = sourceVersion;
        while (version < ProjectPackage.CurrentSchemaVersion)
        {
            if (!migrations.TryGetValue(version, out IProjectMigration? migration))
            {
                throw new InvalidDataException($"No project migration exists for schema {version}.");
            }

            current = migration.Apply(current);
            version++;
        }

        return current;
    }
}
