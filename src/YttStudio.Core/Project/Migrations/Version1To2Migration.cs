using System.Text.Json.Nodes;

namespace YttStudio.Core.Project.Migrations;

internal sealed class Version1To2Migration : IProjectMigration
{
    public int FromVersion => 1;

    public JsonObject Apply(JsonObject project)
    {
        project["settings"] ??= new JsonObject
        {
            ["previewBackground"] = new JsonObject
            {
                ["red"] = 32,
                ["green"] = 32,
                ["blue"] = 32,
                ["alpha"] = 255,
            },
            ["useCheckerboard"] = false,
        };
        project["styles"] ??= new JsonArray();
        project["cues"] ??= new JsonArray();
        if (project["cues"] is JsonArray cues)
        {
            foreach (JsonNode? item in cues)
            {
                if (item is JsonObject cue)
                {
                    cue["effects"] ??= new JsonArray();
                }
            }
        }
        project["schemaVersion"] = 2;
        return project;
    }
}
