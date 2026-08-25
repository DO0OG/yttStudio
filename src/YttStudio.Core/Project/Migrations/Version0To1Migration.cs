using System.Text.Json.Nodes;

namespace YttStudio.Core.Project.Migrations;

internal sealed class Version0To1Migration : IProjectMigration
{
    public int FromVersion => 0;

    public JsonObject Apply(JsonObject project)
    {
        if (project.TryGetPropertyValue("mediaPath", out JsonNode? mediaPath) &&
            !project.ContainsKey("videoPath"))
        {
            project["videoPath"] = mediaPath?.DeepClone();
        }
        project.Remove("mediaPath");
        project["schemaVersion"] = 1;
        return project;
    }
}
