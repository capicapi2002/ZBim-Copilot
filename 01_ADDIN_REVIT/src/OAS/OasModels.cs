using System;
using System.Collections.Generic;

namespace ZBIMCopilot.OAS
{
    public class OasProject
    {
        public string ProjectName { get; set; } = "";
        public List<OasBuilding>? Buildings { get; set; }
    }

    public class OasBuilding
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double[]? Origin { get; set; }
        public object? StructuralGrid { get; set; } 
        public List<object>? Cores { get; set; } 
        public List<OasLevel>? Levels { get; set; }
    }

    public class OasLevel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double Elevation { get; set; }
        public double F2f { get; set; }
        public string Use { get; set; } = "";
        public List<OasZone>? Zones { get; set; }
    }

    public class OasZone
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string PrivacyGradient { get; set; } = "";
        public bool FireSector { get; set; }
        public List<OasSpace>? Spaces { get; set; }
    }

    public class OasSpace
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public double[]? Origin { get; set; }
        public double[]? Dimensions { get; set; }
        public string Boundary { get; set; } = "";
        public List<string>? AdjacentTo { get; set; }

        public List<OasOpening>? Openings { get; set; }
        public List<OasFixture>? Fixtures { get; set; }
        public List<OasFurniture>? Furniture { get; set; }
        
        // ⭐ AGREGADO PARA ESCALERAS
        public List<OasStair>? Stairs { get; set; }
    }

    public class OasOpening
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public double SillHeight { get; set; } 
        public double[]? Origin { get; set; } 
        public double Rotation { get; set; }
    }

    public class OasFixture
    {
        public string Id { get; set; } = "";
        public string SubType { get; set; } = "";
        public double[]? Origin { get; set; }
        public double Rotation { get; set; }
    }

    public class OasFurniture
    {
        public string Id { get; set; } = "";
        public string SubType { get; set; } = "";
        public double[]? Origin { get; set; }
        public double[]? Dimensions { get; set; }
        public double Rotation { get; set; }
    }

    // ⭐ CLASE AGREGADA PARA ESCALERAS
    public class OasStair
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string BaseLevelName { get; set; } = "";
        public string TopLevelName { get; set; } = "";
        public double[]? Origin { get; set; } // Punto de inicio [X, Y]
        public double[]? EndPoint { get; set; } // Punto final [X, Y]
        public double Width { get; set; } = 1.2;
        public double Rotation { get; set; }
    }
}