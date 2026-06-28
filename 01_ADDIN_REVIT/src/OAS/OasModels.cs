#nullable disable
using System.Collections.Generic;

namespace ZBIMCopilot.OAS
{
    public class OasProject
    {
        public string ProjectName { get; set; }
        public string TipoProyecto { get; set; }
        public List<OasBuilding> Buildings { get; set; }
    }

    public class OasBuilding
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TipoProyecto { get; set; }
        public double[] Origin { get; set; }
        public object StructuralGrid { get; set; }
        public List<OasCore> Cores { get; set; }
        public List<OasLevel> Levels { get; set; }
        public List<OasStair> Stairs { get; set; }
        public List<OasElevatorCore> ElevatorCores { get; set; }
    }

    public class OasLevel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Elevation { get; set; }
        public double F2F { get; set; }
        public string Use { get; set; }
        public List<OasZone> Zones { get; set; }
    }

    public class OasZone
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string PrivacyGradient { get; set; }
        public bool FireSector { get; set; }
        public List<OasSpace> Spaces { get; set; }
    }

    public class OasSpace
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public double[] Origin { get; set; }
        public double[] Dimensions { get; set; }
        public string Boundary { get; set; }
        public List<string> AdjacentTo { get; set; }
        public List<OasFixture> Fixtures { get; set; }
        public List<OasFurniture> Furniture { get; set; }
        public List<OasOpening> Openings { get; set; }
        public List<OasBoundarySegment> Contorno { get; set; }
    }

    public class OasBoundarySegment
    {
        public string TipoLimite { get; set; }
        public double Longitud { get; set; }
    }

    public class OasFixture
    {
        public string Id { get; set; }
        public string SubType { get; set; }
        public double[] Origin { get; set; }
        public double Rotation { get; set; }
        public double[] Dimensions { get; set; }
    }

    public class OasFurniture
    {
        public string Id { get; set; }
        public string SubType { get; set; }
        public double[] Origin { get; set; }
        public double Rotation { get; set; }
        public double[] Dimensions { get; set; }
    }

    public class OasOpening
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string WallOrientation { get; set; }
        public double[] Origin { get; set; }
    }

    public class OasStair
    {
        public string Id { get; set; }
        public string BaseLevelName { get; set; }
        public string TopLevelName { get; set; }
        public double RiserHeight { get; set; }
        public double TreadDepth { get; set; }
        public double Width { get; set; }
        public double[] Origin { get; set; }
        public double Rotation { get; set; }
    }

    public class OasCore
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double[] Origin { get; set; }
        public double[] Dimensions { get; set; }
    }

    public class OasElevatorCore
    {
        public string Id { get; set; }
        public string BaseLevelName { get; set; }
        public string TopLevelName { get; set; }
        public double LobbyWidth { get; set; }
        public double LobbyDepth { get; set; }
        public List<OasElevator> Elevators { get; set; }
    }

    public class OasElevator
    {
        public string Id { get; set; }
        public double ShaftWidth { get; set; }
        public double ShaftDepth { get; set; }
        public double[] Origin { get; set; }
    }
}