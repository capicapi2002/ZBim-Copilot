#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZBimCopilot.Knowledge
{
    /// <summary>Requisito de espacio definido por el usuario (programa de necesidades).</summary>
    public class SpaceRequirement
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("function")]
        public string Function { get; set; } = string.Empty;

        [JsonPropertyName("desired_area")]
        public double DesiredArea { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }

    public class Retreats
    {
        [JsonPropertyName("front")]
        public double Front { get; set; }

        [JsonPropertyName("side")]
        public double Side { get; set; }

        [JsonPropertyName("back")]
        public double Back { get; set; }
    }

    public class NormativeFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public string DataBase64 { get; set; } = string.Empty;

        [JsonIgnore]
        public byte[]? RawData
        {
            get
            {
                if (string.IsNullOrEmpty(DataBase64)) return null;
                try { return System.Convert.FromBase64String(DataBase64); }
                catch { return null; }
            }
        }
    }

    public class StyleReferences
    {
        [JsonPropertyName("materials")]
        public List<string> Materials { get; set; } = new();

        [JsonPropertyName("style")]
        public string Style { get; set; } = string.Empty;

        [JsonPropertyName("max_height")]
        public double MaxHeight { get; set; }

        [JsonPropertyName("climate")]
        public string Climate { get; set; } = string.Empty;
    }

    public class ReferenceImage
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public string DataBase64 { get; set; } = string.Empty;

        [JsonIgnore]
        public byte[]? RawData
        {
            get
            {
                if (string.IsNullOrEmpty(DataBase64)) return null;
                try { return System.Convert.FromBase64String(DataBase64); }
                catch { return null; }
            }
        }
    }

    public class FullProjectConfig
    {
        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;

        [JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = string.Empty;

        [JsonPropertyName("site_description")]
        public string SiteDescription { get; set; } = string.Empty;

        [JsonPropertyName("site_plan_base64")]
        public string SitePlanBase64 { get; set; } = string.Empty;

        [JsonPropertyName("retreats")]
        public Retreats Retreats { get; set; } = new();

        [JsonPropertyName("implantation_area")]
        public double ImplantationArea { get; set; }

        [JsonPropertyName("floors_above")]
        public int FloorsAbove { get; set; } = 1;

        [JsonPropertyName("floors_below")]
        public int FloorsBelow { get; set; }

        [JsonPropertyName("program_text")]
        public string ProgramText { get; set; } = string.Empty;

        [JsonPropertyName("program_doc_base64")]
        public string ProgramDocBase64 { get; set; } = string.Empty;

        [JsonPropertyName("applicable_normative")]
        public List<NormativeFile> ApplicableNormative { get; set; } = new();

        [JsonPropertyName("style_references")]
        public StyleReferences StyleReferences { get; set; } = new();

        [JsonPropertyName("images_references")]
        public List<ReferenceImage> ImagesReferences { get; set; } = new();

        [JsonIgnore]
        public string EffectiveProjectType =>
            string.IsNullOrEmpty(ProjectType) ? "Genérico" : ProjectType;

        [JsonIgnore]
        public List<byte[]> RawImageData
        {
            get
            {
                var result = new List<byte[]>();
                foreach (var img in ImagesReferences)
                {
                    var data = img.RawData;
                    if (data != null && data.Length > 0)
                        result.Add(data);
                }
                return result;
            }
        }
    }
}