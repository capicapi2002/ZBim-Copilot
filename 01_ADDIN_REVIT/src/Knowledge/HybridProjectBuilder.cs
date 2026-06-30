using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ZBimCopilot.Knowledge
{
    public class Vector3
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }

        public Vector3() { X = 0; Y = 0; Z = 0; }
        public Vector3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public class OasBoundarySegment
    {
        [JsonPropertyName("start")] public Vector3 Start { get; set; } = new Vector3();
        [JsonPropertyName("end")]   public Vector3 End   { get; set; } = new Vector3();
    }

    public class OasSpace
    {
        [JsonPropertyName("name")]       public string Name { get; set; } = string.Empty;
        [JsonPropertyName("area")]       public double Area { get; set; }
        [JsonPropertyName("contorno")]   public List<OasBoundarySegment> Contorno { get; set; } = new List<OasBoundarySegment>();
        [JsonPropertyName("origin")]     public Vector3 Origin { get; set; } = new Vector3();
        [JsonPropertyName("dimensions")] public Vector3 Dimensions { get; set; } = new Vector3();
    }

    public class OasStair
    {
        [JsonPropertyName("origin")]     public Vector3 Origin { get; set; } = new Vector3();
        [JsonPropertyName("dimensions")] public Vector3 Dimensions { get; set; } = new Vector3();
        [JsonPropertyName("type")]       public string Type { get; set; } = "two_flight";
    }

    public class OasElevatorCore
    {
        [JsonPropertyName("origin")]     public Vector3 Origin { get; set; } = new Vector3();
        [JsonPropertyName("dimensions")] public Vector3 Dimensions { get; set; } = new Vector3();
        [JsonPropertyName("type")]       public string Type { get; set; } = "passenger";
    }

    public class LevelLayout
    {
        [JsonPropertyName("level_name")] public string LevelName { get; set; } = string.Empty;
        [JsonPropertyName("elevation")]  public double Elevation { get; set; }
        [JsonPropertyName("spaces")]     public List<OasSpace> Spaces { get; set; } = new List<OasSpace>();
        [JsonPropertyName("stair")]      public OasStair Stair { get; set; } = new OasStair();
        [JsonPropertyName("elevator")]   public OasElevatorCore Elevator { get; set; } = new OasElevatorCore();
    }

    public class ProjectLayout
    {
        [JsonPropertyName("levels")] public List<LevelLayout> Levels { get; set; } = new List<LevelLayout>();
    }

    public class LevelDefinition
    {
        [JsonPropertyName("level_name")] public string LevelName { get; set; } = string.Empty;
        [JsonPropertyName("elevation")]  public double Elevation { get; set; }
        [JsonPropertyName("spaces")]     public List<OasSpace> Spaces { get; set; } = new List<OasSpace>();
    }

    public class TopologyData
    {
        [JsonPropertyName("levels")] public List<LevelDefinition> Levels { get; set; } = new List<LevelDefinition>();
    }

    public class HybridProjectBuilder
    {
        private const double CoreSideFeet = 15.0;

        public ProjectLayout BuildProjectLayout(TopologyData topology)
        {
            if (topology?.Levels == null)
                throw new ArgumentNullException(nameof(topology));

            var projectLayout = new ProjectLayout();

            foreach (var levelDef in topology.Levels)
            {
                if (levelDef.Spaces == null || levelDef.Spaces.Count == 0)
                    continue;

                var levelLayout = new LevelLayout
                {
                    LevelName = levelDef.LevelName,
                    Elevation = levelDef.Elevation
                };

                double totalArea = levelDef.Spaces.Sum(s => s.Area);
                double C = CoreSideFeet;
                double L = (C + Math.Sqrt(C * C + 2.0 * totalArea)) / 2.0;
                double d = (L - C) / 2.0;
                if (d <= 0.0) d = 1.0;

                var sides = DistributeSpacesToSides(levelDef.Spaces);

                PlaceSpacesOnSide(sides[0], 0, L, C, d, levelLayout.Spaces);
                PlaceSpacesOnSide(sides[1], 1, L, C, d, levelLayout.Spaces);
                PlaceSpacesOnSide(sides[2], 2, L, C, d, levelLayout.Spaces);
                PlaceSpacesOnSide(sides[3], 3, L, C, d, levelLayout.Spaces);

                CreateCore(C, levelDef.Elevation, out var stair, out var elevator);
                levelLayout.Stair = stair;
                levelLayout.Elevator = elevator;

                projectLayout.Levels.Add(levelLayout);
            }

            return projectLayout;
        }

        private List<List<OasSpace>> DistributeSpacesToSides(List<OasSpace> spaces)
        {
            var sides = new List<List<OasSpace>> { new List<OasSpace>(), new List<OasSpace>(), new List<OasSpace>(), new List<OasSpace>() };
            var sorted = spaces.OrderByDescending(s => s.Area).ToList();
            double[] sideAreas = new double[4];

            foreach (var space in sorted)
            {
                int best = 0;
                double min = sideAreas[0];
                for (int i = 1; i < 4; i++)
                    if (sideAreas[i] < min) { min = sideAreas[i]; best = i; }
                sides[best].Add(space);
                sideAreas[best] += space.Area;
            }
            return sides;
        }

        private void PlaceSpacesOnSide(List<OasSpace> spaces, int side, double L, double C, double d, List<OasSpace> output)
        {
            if (spaces.Count == 0) return;

            double startX, startY, dirX, dirY;
            switch (side)
            {
                case 0: startX = -L/2; startY = -L/2; dirX = 1; dirY = 0; break;
                case 1: startX = L/2 - d; startY = -L/2; dirX = 0; dirY = 1; break;
                case 2: startX = L/2; startY = L/2 - d; dirX = -1; dirY = 0; break;
                default: startX = -L/2; startY = L/2; dirX = 0; dirY = -1; break;
            }

            double pos = 0;
            foreach (var sp in spaces)
            {
                double w = sp.Area / d;
                if (pos + w > L) w = L - pos;
                double bx = startX + dirX * pos;
                double by = startY + dirY * pos;

                if (side == 0 || side == 2)
                {
                    double y0 = side == 0 ? by : by - d;
                    sp.Origin = new Vector3(bx, y0, 0);
                    sp.Dimensions = new Vector3(w, d, 0);
                }
                else
                {
                    double x0 = side == 1 ? bx - d : bx;
                    sp.Origin = new Vector3(x0, by, 0);
                    sp.Dimensions = new Vector3(d, w, 0);
                }
                output.Add(sp);
                pos += w;
            }
        }

        private void CreateCore(double coreSide, double elevation, out OasStair stair, out OasElevatorCore elevator)
        {
            double half = coreSide / 2.0;
            stair = new OasStair
            {
                Origin = new Vector3(-half, -half, elevation),
                Dimensions = new Vector3(half, coreSide, 0)
            };
            elevator = new OasElevatorCore
            {
                Origin = new Vector3(0, -half, elevation),
                Dimensions = new Vector3(half, coreSide, 0)
            };
        }
    }
}