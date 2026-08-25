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

    private ProjectMigrationPipeline(IEnumerable<IProjectMigration> migrations)
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
