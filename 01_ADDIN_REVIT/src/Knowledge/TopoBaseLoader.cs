#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZBIMCopilot.OAS;

namespace ZBIMCopilot.Knowledge
{
    public static class TopoBaseLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static OasProject LoadAndMerge(string topobaseRoot)
        {
            var merged = new OasProject
            {
                ProjectName = "MergedTopoBase",
                Buildings = new List<OasBuilding>()
            };

            if (!Directory.Exists(topobaseRoot))
                return merged;

            var jsonFiles = Directory.GetFiles(topobaseRoot, "*.json", SearchOption.AllDirectories);
            foreach (var file in jsonFiles)
            {
                try
                {
                    string jsonText = File.ReadAllText(file);
                    var topoFile = JsonSerializer.Deserialize<TopoBaseFile>(jsonText, _jsonOptions);
                    if (topoFile == null || topoFile.Plantas == null || topoFile.Plantas.Count == 0)
                        continue;

                    string suffix = Path.GetFileNameWithoutExtension(file).Replace(' ', '_');

                    var building = new OasBuilding
                    {
                        Id = suffix,
                        Name = topoFile.Subtipologia ?? suffix,
                        TipoProyecto = topoFile.TipoProyecto ?? "desconocido",
                        Levels = new List<OasLevel>()
                    };

                    foreach (var planta in topoFile.Plantas)
                    {
                        int repeticiones = planta.Repetir > 0 ? planta.Repetir : 1;
                        for (int r = 0; r < repeticiones; r++)
                        {
                            var level = new OasLevel
                            {
                                Name = repeticiones > 1 ? $"{planta.Nombre}_{r + 1}" : planta.Nombre,
                                Elevation = planta.AlturaNivel + r * (topoFile.Plantas.FirstOrDefault()?.AlturaNivel ?? 3.0),
                                F2F = 3.6,
                                Zones = new List<OasZone>()
                            };

                            var zone = new OasZone
                            {
                                Name = "Zona principal",
                                Spaces = new List<OasSpace>()
                            };

                            if (planta.EspaciosComunes != null)
                                foreach (var esp in planta.EspaciosComunes)
                                    zone.Spaces.Add(ConvertToOasSpace(esp, suffix));

                            if (planta.UnidadesPrivadas != null)
                                foreach (var unidad in planta.UnidadesPrivadas)
                                    zone.Spaces.Add(ConvertToOasSpace(unidad, suffix));

                            if (planta.Espacios != null)
                                foreach (var esp in planta.Espacios)
                                    zone.Spaces.Add(ConvertToOasSpace(esp, suffix));

                            if (zone.Spaces.Count > 0)
                                level.Zones.Add(zone);

                            building.Levels.Add(level);
                        }
                    }

                    merged.Buildings.Add(building);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cargando {file}: {ex.Message}");
                }
            }

            foreach (var building in merged.Buildings)
                ProcessSpacesFromContours(building);

            return merged;
        }

        private static OasSpace ConvertToOasSpace(TopoEspacio esp, string suffix)
        {
            var space = new OasSpace
            {
                Id = $"{suffix}_{esp.Nombre?.Replace(" ", "_") ?? "espacio"}",
                Name = $"{suffix}_{esp.Nombre ?? "sin_nombre"}",
                Type = "general"
            };

            if (esp.Contorno != null && esp.Contorno.Count > 0)
            {
                space.Contorno = esp.Contorno.Select(c => new OasBoundarySegment
                {
                    TipoLimite = c.TipoLimite ?? "muro_ciego",
                    Longitud = c.Longitud
                }).ToList();
            }

            return space;
        }

        private static void ProcessSpacesFromContours(OasBuilding building)
        {
            if (building.Levels == null) return;
            double currentX = 0.0;
            double currentY = 0.0;
            const double spacing = 2.0;

            foreach (var level in building.Levels.OrderBy(l => l.Elevation))
            {
                currentX = 0.0;
                if (level.Zones == null) continue;
                foreach (var zone in level.Zones)
                {
                    if (zone.Spaces == null) continue;
                    foreach (var space in zone.Spaces)
                    {
                        if ((space.Origin == null || space.Dimensions == null)
                            && space.Contorno != null && space.Contorno.Count > 0)
                        {
                            var (w, d) = ComputeBBoxFromContour(space.Contorno);
                            space.Origin = new double[] { currentX, currentY, level.Elevation };
                            space.Dimensions = new double[] { w, d };
                            currentX += w + spacing;
                        }
                    }
                }
                currentY += 20.0;
            }
        }

        private static (double width, double depth) ComputeBBoxFromContour(List<OasBoundarySegment> contour)
        {
            double totalLength = 0;
            foreach (var seg in contour)
                totalLength += seg.Longitud;

            double halfPerimeter = totalLength / 2.0;
            var sorted = contour.Select(s => s.Longitud).OrderBy(l => l).ToList();
            double width = sorted[sorted.Count / 2];
            double depth = halfPerimeter - width;

            if (width <= 0 || depth <= 0)
            {
                width = halfPerimeter / 2.0;
                depth = width;
            }
            return (width, depth);
        }

        // ========== DTOs con mapeo exacto a snake_case ==========

        private class TopoBaseFile
        {
            [JsonPropertyName("tipo_proyecto")]
            public string TipoProyecto { get; set; }

            [JsonPropertyName("subtipologia")]
            public string Subtipologia { get; set; }

            [JsonPropertyName("version_oas")]
            public string VersionOas { get; set; }

            [JsonPropertyName("descripcion")]
            public string Descripcion { get; set; }

            [JsonPropertyName("plantas")]
            public List<TopoPlanta> Plantas { get; set; }
        }

        private class TopoPlanta
        {
            [JsonPropertyName("nombre")]
            public string Nombre { get; set; }

            [JsonPropertyName("altura_nivel")]
            public double AlturaNivel { get; set; }

            [JsonPropertyName("repetir")]
            public int Repetir { get; set; }

            [JsonPropertyName("espacios")]
            public List<TopoEspacio> Espacios { get; set; }

            [JsonPropertyName("espacios_comunes")]
            public List<TopoEspacio> EspaciosComunes { get; set; }

            [JsonPropertyName("unidades_privadas")]
            public List<TopoEspacio> UnidadesPrivadas { get; set; }
        }

        private class TopoEspacio
        {
            [JsonPropertyName("nombre")]
            public string Nombre { get; set; }

            [JsonPropertyName("area")]
            public double Area { get; set; }

            [JsonPropertyName("contorno")]
            public List<TopoContornoSegment> Contorno { get; set; }
        }

        private class TopoContornoSegment
        {
            [JsonPropertyName("tipo_limite")]
            public string TipoLimite { get; set; }

            [JsonPropertyName("longitud")]
            public double Longitud { get; set; }
        }
    }
}