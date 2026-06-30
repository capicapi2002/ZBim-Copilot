using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZBimCopilot.Knowledge
{
    public class ProjectConfig
    {
        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;

        [JsonPropertyName("total_floors")]
        public int TotalFloors { get; set; } = 1;

        [JsonPropertyName("floor_height")]
        public double FloorHeight { get; set; } = 3.0;

        [JsonPropertyName("normative")]
        public string Normative { get; set; } = "CTE";

        [JsonPropertyName("style_references")]
        public List<string> StyleReferences { get; set; } = new List<string>();

        [JsonPropertyName("program_requirements")]
        public List<SpaceRequirement> ProgramRequirements { get; set; } = new List<SpaceRequirement>();
    }
}