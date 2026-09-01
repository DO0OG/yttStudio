using System.Text.Json.Nodes;

namespace YttStudio.Core.Project.Migrations;

/// <summary>
/// 모션 경로에서 사용하는 선택적 키프레임 컬렉션을 추가한다.
/// 기존 스칼라 이동 필드는 그대로 두며 컬렉션이 비었을 때 계속 읽으므로
/// 이전 프로젝트의 동작을 보존한다.
/// </summary>
internal sealed class Version2To3Migration : IProjectMigration
{
    public int FromVersion => 2;

    public JsonObject Apply(JsonObject project)
    {
        if (project["cues"] is JsonArray cues)
        {
            foreach (JsonNode? item in cues)
            {
                if (item is not JsonObject cue || cue["effects"] is not JsonArray effects)
                {
                    continue;
                }

                foreach (JsonNode? effectItem in effects)
                {
                    if (effectItem is JsonObject effect)
                    {
                        effect["keyframes"] ??= new JsonArray();
                    }
                }
            }
        }

        project["schemaVersion"] = 3;
        return project;
    }
}
