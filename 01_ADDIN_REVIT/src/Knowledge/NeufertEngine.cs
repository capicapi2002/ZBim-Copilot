#nullable enable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ZBimCopilot.Knowledge
{
    public class NeufertMatch
    {
        public string SpaceName { get; set; } = string.Empty;
        public string Function { get; set; } = string.Empty;
        public double AreaMin { get; set; }
        public double AreaMax { get; set; }
        public double WidthMin { get; set; }
        public double DepthMin { get; set; }
        public double Height { get; set; }
        public List<string> TypicalFurniture { get; set; } = new List<string>();
        public string NormativeReference { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public static class NeufertEngine
    {
        private static readonly object _dbLock = new object();
        private const double DEFAULT_FLOOR_HEIGHT_M = 2.70;
        private const double DEFAULT_MIN_ROOM_DIMENSION_M = 2.40;

        public static List<NeufertMatch> QuerySpaces(string projectType, List<SpaceRequirement> requirements)
        {
            var results = new List<NeufertMatch>();

            if (string.IsNullOrWhiteSpace(projectType) && (requirements == null || requirements.Count == 0))
            {
                return GetFallbackMatches("Genérico");
            }

            string? dbPath = FindDatabasePath();
            bool dbAvailable = dbPath != null && File.Exists(dbPath);

            if (dbAvailable)
            {
                try
                {
                    results = QueryDatabase(dbPath!, projectType, requirements);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NeufertEngine] Error consultando DB: {ex.Message}");
                    results = new List<NeufertMatch>();
                }
            }

            if (results.Count == 0 || (requirements != null && results.Count < requirements.Count))
            {
                var fallback = GetFallbackMatches(projectType, requirements);
                var existingNames = new HashSet<string>(results.Select(r => r.SpaceName), StringComparer.OrdinalIgnoreCase);
                foreach (var fb in fallback)
                {
                    if (!existingNames.Contains(fb.SpaceName))
                        results.Add(fb);
                }
            }

            return results;
        }

        public static List<SpaceRequirement> ParseProgramText(string programText)
        {
            var requirements = new List<SpaceRequirement>();
            if (string.IsNullOrWhiteSpace(programText)) return requirements;

            var spaceDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dormitorio", "Dormitorio" }, { "bedroom", "Dormitorio" },
                { "habitación", "Dormitorio" }, { "cuarto", "Dormitorio" },
                { "cocina", "Cocina" }, { "kitchen", "Cocina" },
                { "baño", "Baño_Completo" }, { "bathroom", "Baño_Completo" },
                { "aseo", "Baño_Visitas" }, { "toilet", "Baño_Visitas" },
                { "living", "Living" }, { "sala", "Living" },
                { "comedor", "Comedor" }, { "dining", "Comedor" },
                { "garaje", "Garaje" }, { "garage", "Garaje" },
                { "lavadero", "Lavadero" }, { "laundry", "Lavadero" },
                { "oficina", "Oficina" }, { "office", "Oficina" },
                { "reuniones", "Sala_Reuniones" }, { "meeting", "Sala_Reuniones" },
                { "recepción", "Recepcion" }, { "reception", "Recepcion" },
                { "tienda", "Area_Ventas" }, { "shop", "Area_Ventas" },
                { "almacén", "Almacen" }, { "storage", "Almacen" },
                { "aula", "Aula" }, { "classroom", "Aula" },
                { "biblioteca", "Biblioteca" }, { "library", "Biblioteca" }
            };

            string text = programText.ToLowerInvariant();

            var pattern = new Regex(@"(\b(?:\d+|dos|tres|cuatro|cinco|seis|siete|ocho)\b)\s+(?:de\s+)?(" +
                                    string.Join("|", spaceDictionary.Keys.Select(Regex.Escape)) +
                                    @")[s]?\b", RegexOptions.IgnoreCase);

            foreach (Match match in pattern.Matches(text))
            {
                string numStr = match.Groups[1].Value.ToLowerInvariant();
                string spaceWord = match.Groups[2].Value.ToLowerInvariant();

                int quantity = ParseNumber(numStr);
                if (spaceDictionary.TryGetValue(spaceWord, out string? canonicalName))
                {
                    requirements.Add(new SpaceRequirement
                    {
                        Name = canonicalName,
                        Function = canonicalName,
                        Quantity = quantity,
                        DesiredArea = 0
                    });
                }
            }

            if (requirements.Count == 0)
            {
                foreach (var kvp in spaceDictionary)
                {
                    if (text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !requirements.Any(r => r.Name.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        requirements.Add(new SpaceRequirement
                        {
                            Name = kvp.Value,
                            Function = kvp.Value,
                            Quantity = 1,
                            DesiredArea = 0
                        });
                    }
                }
            }

            return requirements;
        }

        private static List<NeufertMatch> QueryDatabase(string dbPath, string projectType, List<SpaceRequirement> requirements)
        {
            var results = new List<NeufertMatch>();

            lock (_dbLock)
            {
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;Read Only=True;");
                conn.Open();

                using var cmd = new SQLiteCommand();
                cmd.Connection = conn;
                cmd.CommandText = @"
                    SELECT 
                        COALESCE(name, ''),
                        COALESCE(function, name, ''),
                        COALESCE(min_area, 0),
                        COALESCE(max_area, 0),
                        COALESCE(min_width, 0),
                        COALESCE(min_depth, 0),
                        COALESCE(height, @defHeight),
                        COALESCE(furniture, ''),
                        COALESCE(norm_ref, '')
                    FROM spaces
                    WHERE project_type LIKE @ptype
                    ORDER BY min_area DESC";

                cmd.Parameters.AddWithValue("@ptype", $"%{projectType}%");
                cmd.Parameters.AddWithValue("@defHeight", DEFAULT_FLOOR_HEIGHT_M);

                using var reader = cmd.ExecuteReader();
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (reader.Read())
                {
                    try
                    {
                        var match = new NeufertMatch
                        {
                            SpaceName = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            Function = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            AreaMin = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                            AreaMax = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                            WidthMin = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                            DepthMin = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                            Height = reader.IsDBNull(6) ? DEFAULT_FLOOR_HEIGHT_M : reader.GetDouble(6),
                            NormativeReference = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            Confidence = 0.85
                        };

                        string furnitureStr = reader.IsDBNull(7) ? "" : reader.GetString(7);
                        if (!string.IsNullOrWhiteSpace(furnitureStr))
                        {
                            match.TypicalFurniture = furnitureStr
                                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }

                        if (match.AreaMin <= 0) match.AreaMin = 6.0;
                        if (match.AreaMax <= match.AreaMin) match.AreaMax = match.AreaMin * 1.5;
                        if (match.WidthMin <= 0) match.WidthMin = DEFAULT_MIN_ROOM_DIMENSION_M;
                        if (match.DepthMin <= 0) match.DepthMin = DEFAULT_MIN_ROOM_DIMENSION_M;
                        if (match.Height <= 0) match.Height = DEFAULT_FLOOR_HEIGHT_M;

                        if (!string.IsNullOrWhiteSpace(match.SpaceName) && !seenNames.Contains(match.SpaceName))
                        {
                            seenNames.Add(match.SpaceName);
                            results.Add(match);
                        }
                    }
                    catch { }
                }
            }

            return results;
        }

        private static List<NeufertMatch> GetFallbackMatches(string projectType, List<SpaceRequirement>? requirements = null)
        {
            var all = new Dictionary<string, NeufertMatch>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dormitorio"] = new NeufertMatch { SpaceName = "Dormitorio", Function = "Dormitorio", AreaMin = 10, AreaMax = 16, WidthMin = 2.8, DepthMin = 3.0, Height = 2.7, TypicalFurniture = new List<string> { "Bed", "Wardrobe", "Nightstand" } },
                ["Cocina"] = new NeufertMatch { SpaceName = "Cocina", Function = "Cocina", AreaMin = 8, AreaMax = 16, WidthMin = 2.4, DepthMin = 3.0, Height = 2.7, TypicalFurniture = new List<string> { "BaseCabinet", "Refrigerator", "Stove", "Sink" } },
                ["Baño_Completo"] = new NeufertMatch { SpaceName = "Baño_Completo", Function = "Baño", AreaMin = 4.5, AreaMax = 7, WidthMin = 1.8, DepthMin = 2.0, Height = 2.4, TypicalFurniture = new List<string> { "Toilet", "Sink", "Shower" } },
                ["Baño_Visitas"] = new NeufertMatch { SpaceName = "Baño_Visitas", Function = "Aseo", AreaMin = 2.5, AreaMax = 4, WidthMin = 1.5, DepthMin = 1.6, Height = 2.4, TypicalFurniture = new List<string> { "Toilet", "Sink" } },
                ["Living"] = new NeufertMatch { SpaceName = "Living", Function = "Sala de estar", AreaMin = 18, AreaMax = 35, WidthMin = 3.5, DepthMin = 4.0, Height = 2.7, TypicalFurniture = new List<string> { "Sofa", "Table", "TV_Stand" } },
                ["Comedor"] = new NeufertMatch { SpaceName = "Comedor", Function = "Comedor", AreaMin = 10, AreaMax = 18, WidthMin = 3.0, DepthMin = 3.2, Height = 2.7, TypicalFurniture = new List<string> { "DiningTable", "Chairs" } },
                ["Garaje"] = new NeufertMatch { SpaceName = "Garaje", Function = "Estacionamiento", AreaMin = 18, AreaMax = 36, WidthMin = 3.5, DepthMin = 5.0, Height = 2.4, TypicalFurniture = new List<string>() },
                ["Lavadero"] = new NeufertMatch { SpaceName = "Lavadero", Function = "Lavadero", AreaMin = 3, AreaMax = 6, WidthMin = 1.5, DepthMin = 2.0, Height = 2.4, TypicalFurniture = new List<string> { "WashingMachine" } },
                ["Oficina"] = new NeufertMatch { SpaceName = "Oficina", Function = "Oficina", AreaMin = 8, AreaMax = 12, WidthMin = 3.0, DepthMin = 3.0, Height = 2.7, TypicalFurniture = new List<string> { "Desk", "OfficeChair" } },
                ["Sala_Reuniones"] = new NeufertMatch { SpaceName = "Sala_Reuniones", Function = "Reuniones", AreaMin = 15, AreaMax = 30, WidthMin = 3.5, DepthMin = 4.5, Height = 2.7, TypicalFurniture = new List<string> { "MeetingTable", "Chairs" } },
                ["Recepcion"] = new NeufertMatch { SpaceName = "Recepcion", Function = "Recepción", AreaMin = 10, AreaMax = 20, WidthMin = 3.0, DepthMin = 3.5, Height = 2.7, TypicalFurniture = new List<string> { "ReceptionDesk" } },
                ["Area_Ventas"] = new NeufertMatch { SpaceName = "Area_Ventas", Function = "Venta", AreaMin = 50, AreaMax = 200, WidthMin = 6.0, DepthMin = 8.0, Height = 3.0, TypicalFurniture = new List<string> { "DisplayShelf", "Counter" } },
                ["Almacen"] = new NeufertMatch { SpaceName = "Almacen", Function = "Almacén", AreaMin = 15, AreaMax = 40, WidthMin = 3.0, DepthMin = 4.0, Height = 3.0, TypicalFurniture = new List<string> { "ShelvingUnit" } },
                ["Aula"] = new NeufertMatch { SpaceName = "Aula", Function = "Aula", AreaMin = 45, AreaMax = 70, WidthMin = 6.0, DepthMin = 7.5, Height = 3.0, TypicalFurniture = new List<string> { "StudentDesk", "Blackboard" } },
                ["Biblioteca"] = new NeufertMatch { SpaceName = "Biblioteca", Function = "Biblioteca", AreaMin = 40, AreaMax = 80, WidthMin = 6.0, DepthMin = 6.5, Height = 3.0, TypicalFurniture = new List<string> { "Bookshelf", "ReadingTable" } }
            };

            var result = new List<NeufertMatch>();

            if (requirements != null && requirements.Count > 0)
            {
                foreach (var req in requirements)
                {
                    if (all.TryGetValue(req.Name, out var match))
                    {
                        var enriched = CloneMatch(match);
                        enriched.Confidence = 0.9;
                        if (req.DesiredArea > 0)
                        {
                            enriched.AreaMin = req.DesiredArea * 0.9;
                            enriched.AreaMax = req.DesiredArea * 1.1;
                        }
                        for (int i = 0; i < Math.Max(1, req.Quantity); i++)
                        {
                            var copy = CloneMatch(enriched);
                            copy.SpaceName = req.Quantity > 1 ? $"{enriched.SpaceName}_{i + 1}" : enriched.SpaceName;
                            result.Add(copy);
                        }
                    }
                }
            }

            if (result.Count == 0)
            {
                string pt = (projectType ?? "").ToLowerInvariant();
                if (pt.IndexOf("viv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pt.IndexOf("casa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pt.IndexOf("residencial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(CloneMatch(all["Living"]));
                    result.Add(CloneMatch(all["Cocina"]));
                    result.Add(CloneMatch(all["Comedor"]));
                    result.Add(CloneMatch(all["Dormitorio"]));
                    result.Add(CloneMatch(all["Baño_Completo"]));
                }
                else if (pt.IndexOf("ofic", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(CloneMatch(all["Oficina"]));
                    result.Add(CloneMatch(all["Sala_Reuniones"]));
                    result.Add(CloneMatch(all["Recepcion"]));
                    result.Add(CloneMatch(all["Baño_Visitas"]));
                }
                else if (pt.IndexOf("comer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(CloneMatch(all["Area_Ventas"]));
                    result.Add(CloneMatch(all["Almacen"]));
                    result.Add(CloneMatch(all["Baño_Visitas"]));
                }
                else
                {
                    result.Add(CloneMatch(all["Living"]));
                    result.Add(CloneMatch(all["Baño_Visitas"]));
                }
            }

            return result;
        }

        private static NeufertMatch CloneMatch(NeufertMatch source)
        {
            return new NeufertMatch
            {
                SpaceName = source.SpaceName,
                Function = source.Function,
                AreaMin = source.AreaMin,
                AreaMax = source.AreaMax,
                WidthMin = source.WidthMin,
                DepthMin = source.DepthMin,
                Height = source.Height,
                TypicalFurniture = new List<string>(source.TypicalFurniture),
                NormativeReference = source.NormativeReference,
                Confidence = source.Confidence
            };
        }

        private static int ParseNumber(string text)
        {
            text = text.ToLowerInvariant().Trim();
            if (int.TryParse(text, out int n)) return n;
            return text switch
            {
                "dos" => 2,
                "tres" => 3,
                "cuatro" => 4,
                "cinco" => 5,
                "seis" => 6,
                "siete" => 7,
                "ocho" => 8,
                _ => 1
            };
        }

        private static string? FindDatabasePath()
        {
            string? dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dllDir != null)
            {
                string localPath = Path.Combine(dllDir, "neufert_data.db");
                if (File.Exists(localPath)) return localPath;

                string[] relativePaths = new[]
                {
                    Path.Combine(dllDir, "..", "..", "..", "..", "..", "02_SERVICIOS_LOCALES", "brain", "neufert_data.db"),
                    Path.Combine(dllDir, "..", "..", "..", "..", "02_SERVICIOS_LOCALES", "brain", "neufert_data.db"),
                    Path.Combine(dllDir, "..", "..", "..", "02_SERVICIOS_LOCALES", "brain", "neufert_data.db")
                };
                foreach (var rel in relativePaths)
                {
                    string full = Path.GetFullPath(rel);
                    if (File.Exists(full)) return full;
                }
            }

            string knownPath = @"D:\1WORK\00 HPI\00 COPILOT\kwen-bimcopilot\02_SERVICIOS_LOCALES\brain\neufert_data.db";
            if (File.Exists(knownPath)) return knownPath;

            return null;
        }
    }
}