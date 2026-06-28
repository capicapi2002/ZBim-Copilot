using System;
using System.Collections.Generic;
using ZBimCopilot.Knowledge;

namespace ZBIMCopilot.Execution
{
    public static class ProjectConfigEngine
    {
        private const double M_TO_FT = 3.280839895;

        public static TopologyData GenerateTopology(ProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (config.TotalFloors <= 0)
                throw new ArgumentException("TotalFloors debe ser mayor que 0", nameof(config));

            if (config.FloorHeight <= 0)
                throw new ArgumentException("FloorHeight debe ser mayor que 0", nameof(config));

            var topology = new TopologyData();

            double totalAreaM2 = 0;
            if (config.ProgramRequirements != null)
            {
                foreach (var req in config.ProgramRequirements)
                    totalAreaM2 += req.DesiredArea;
            }

            if (totalAreaM2 <= 0)
                totalAreaM2 = 100.0;

            double totalAreaFt2 = totalAreaM2 * 10.7639;

            for (int i = 0; i < config.TotalFloors; i++)
            {
                double elevationM = i * config.FloorHeight;
                double elevationFt = elevationM * M_TO_FT;

                string levelName = GetLevelName(i, config.TotalFloors);

                var levelDef = new LevelDefinition
                {
                    LevelName = levelName,
                    Elevation = elevationFt,
                    Spaces = new List<OasSpace>()
                };

                if (config.ProgramRequirements != null && config.ProgramRequirements.Count > 0)
                {
                    foreach (var req in config.ProgramRequirements)
                    {
                        double areaFt2 = req.DesiredArea * 10.7639;
                        var space = new OasSpace
                        {
                            Name = req.Name,
                            Area = areaFt2,
                            Origin = new Vector3(0, 0, 0),
                            Dimensions = new Vector3(0, 0, 0),
                            Contorno = new List<OasBoundarySegment>()
                        };
                        levelDef.Spaces.Add(space);
                    }
                }
                else
                {
                    var space = new OasSpace
                    {
                        Name = $"Espacio_Planta_{i + 1}",
                        Area = totalAreaFt2,
                        Origin = new Vector3(0, 0, 0),
                        Dimensions = new Vector3(0, 0, 0),
                        Contorno = new List<OasBoundarySegment>()
                    };
                    levelDef.Spaces.Add(space);
                }

                topology.Levels.Add(levelDef);
            }

            return topology;
        }

        // ========== NUEVO MÉTODO PARA FullProjectConfig ==========
        public static TopologyData GenerateTopologyFromFullConfig(FullProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var topology = new TopologyData();
            int totalFloors = Math.Max(1, config.FloorsAbove + config.FloorsBelow);
            double floorHeight = 3.0; // Altura por defecto; puede ser configurable

            for (int i = 0; i < totalFloors; i++)
            {
                double elevationM = i * floorHeight;
                double elevationFt = elevationM * M_TO_FT;

                string levelName = i == 0 ? "Planta_Baja" : $"Planta_{i}";

                var levelDef = new LevelDefinition
                {
                    LevelName = levelName,
                    Elevation = elevationFt,
                    Spaces = new List<OasSpace>()
                };

                // Espacio placeholder genérico para topología
                var space = new OasSpace
                {
                    Name = $"Espacio_Planta_{i + 1}",
                    Area = 100.0,
                    Origin = new Vector3(0, 0, 0),
                    Dimensions = new Vector3(0, 0, 0),
                    Contorno = new List<OasBoundarySegment>()
                };

                levelDef.Spaces.Add(space);
                topology.Levels.Add(levelDef);
            }

            return topology;
        }

        private static string GetLevelName(int floorIndex, int totalFloors)
        {
            if (floorIndex == 0)
                return "Planta_Baja";
            else if (floorIndex == totalFloors - 1)
                return $"Planta_{floorIndex}_Cubierta";
            else
                return $"Planta_{floorIndex}";
        }

        // ========== MÉTODOS DE ALMACENAMIENTO ==========
        private static FullProjectConfig? _lastConfig;
        public static void StoreLastConfig(FullProjectConfig config) { _lastConfig = config; }
        public static FullProjectConfig? GetLastConfig() => _lastConfig;
    }
}