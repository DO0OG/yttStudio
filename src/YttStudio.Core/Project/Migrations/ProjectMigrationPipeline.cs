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

    // Built through the Default singleton so the migration order stays in one place.
    // Internal keeps that intent while letting the type be instantiated within the assembly.
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
