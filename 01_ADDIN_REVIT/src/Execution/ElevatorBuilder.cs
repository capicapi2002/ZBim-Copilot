#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de ascensores y montacargas. Usa LookupParameter como fallback.
    /// </summary>
    public static class ElevatorBuilder
    {
        private const double MIN_SHAFT_WALL_THICKNESS = 0.15;

        private static Dictionary<int, (double Width, double Depth, double Height)> CabinDimensions = new()
        {
            { 4,  (1.10, 1.40, 2.20) },
            { 6,  (1.10, 2.00, 2.20) },
            { 8,  (1.40, 2.40, 2.30) },
            { 10, (1.80, 2.70, 2.30) },
            { 12, (2.10, 2.70, 2.30) }
        };

        public enum ElevatorType { ElectricTraction, Hydraulic, Pneumatic, Panoramic, Freight }

        public static FamilyInstance? CreateElevator(
            Document doc,
            Level baseLevel,
            Level topLevel,
            XYZ insertionPoint,
            int capacity = 6,
            ElevatorType elevatorType = ElevatorType.ElectricTraction,
            int numPanoramicFaces = 0,
            bool requiresSeparateShaftForServices = false)
        {
            if (doc == null || baseLevel == null || topLevel == null || insertionPoint == null)
                return null;

            if (!CabinDimensions.TryGetValue(capacity, out var cabin))
            {
                int closest = CabinDimensions.Keys.OrderBy(k => Math.Abs(k - capacity)).First();
                cabin = CabinDimensions[closest];
            }

            double cabinWidth = cabin.Width;
            double cabinDepth = cabin.Depth;
            double wallThickness = MIN_SHAFT_WALL_THICKNESS;
            if (elevatorType == ElevatorType.Freight) wallThickness = 0.20;

            double shaftClearWidth = cabinWidth + 0.20;
            double shaftClearDepth = cabinDepth + 0.30;
            if (elevatorType == ElevatorType.Freight)
            {
                shaftClearWidth = cabinWidth + 0.30;
                shaftClearDepth = cabinDepth + 0.40;
            }

            double extraWidth = requiresSeparateShaftForServices ? wallThickness + 0.10 : 0;
            double totalShaftWidth = shaftClearWidth + 2 * wallThickness + extraWidth;
            double totalShaftDepth = shaftClearDepth + 2 * wallThickness;

            using (Transaction tx = new Transaction(doc, "Crear ascensor"))
            {
                tx.Start();

                double x0 = insertionPoint.X - totalShaftWidth / 2;
                double y0 = insertionPoint.Y - totalShaftDepth / 2;
                double x1 = x0 + totalShaftWidth;
                double y1 = y0 + totalShaftDepth;

                // Muros del hueco
                WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x1, y0, baseLevel.Elevation));
                WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(x0, y1, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation));
                WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x0, y1, baseLevel.Elevation));
                WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(x1, y0, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation));

                if (requiresSeparateShaftForServices)
                {
                    double xService = x0 + wallThickness;
                    WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(xService, y0, baseLevel.Elevation), new XYZ(xService, y1, baseLevel.Elevation));
                }

                // CAMBIO: Muro host creado fuera del bucle para evitar bug lógico y CS8604
                Wall? hostWall = WallBuilder.CreateStraightWall(doc, baseLevel, topLevel, null, new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x1, y0, baseLevel.Elevation));

                // Puertas de ascensor
                FamilySymbol? doorSymbol = FindElevatorDoorSymbol(doc, elevatorType);
                FamilyInstance? firstDoor = null;
                if (doorSymbol != null && hostWall != null)
                {
                    List<Level> allLevels = GetAllIntermediateLevels(doc, baseLevel, topLevel);
                    double centerX = x0 + totalShaftWidth / 2;
                    double centerY = y0;
                    foreach (Level lvl in allLevels)
                    {
                        XYZ doorPoint = new XYZ(centerX, centerY, lvl.Elevation);
                        FamilyInstance door = doc.Create.NewFamilyInstance(doorPoint, doorSymbol, hostWall, lvl, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        if (firstDoor == null) firstDoor = door;
                    }
                }

                if (elevatorType == ElevatorType.ElectricTraction || elevatorType == ElevatorType.Panoramic)
                    CreateMachineRoom(doc, topLevel, insertionPoint, totalShaftWidth, totalShaftDepth);
                else if (elevatorType == ElevatorType.Hydraulic)
                    CreateHydraulicCompressorRoom(doc, baseLevel, insertionPoint, totalShaftWidth, totalShaftDepth);

                if (elevatorType == ElevatorType.Panoramic && numPanoramicFaces > 0)
                    MakePanoramicGlassFaces(doc, baseLevel, topLevel, x0, y0, x1, y1, numPanoramicFaces, wallThickness);

                tx.Commit();
                return firstDoor;
            }
        }

        private static List<Level> GetAllIntermediateLevels(Document doc, Level baseLevel, Level topLevel)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(l => l.Elevation >= baseLevel.Elevation && l.Elevation <= topLevel.Elevation)
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        private static FamilySymbol? FindElevatorDoorSymbol(Document doc, ElevatorType type)
        {
            // Buscar familia de puerta de ascensor
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf("Ascensor", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void CreateMachineRoom(Document doc, Level topLevel, XYZ center, double width, double depth)
        {
            double roomHeight = 2.50;
            double x0 = center.X - width / 2;
            double y0 = center.Y - depth / 2;
            double x1 = center.X + width / 2;
            double y1 = center.Y + depth / 2;

            using (Transaction tx = new Transaction(doc, "Sala de máquinas"))
            {
                tx.Start();
                WallBuilder.CreateStraightWall(doc, topLevel, null, null, new XYZ(x0, y0, topLevel.Elevation), new XYZ(x1, y0, topLevel.Elevation), roomHeight);
                WallBuilder.CreateStraightWall(doc, topLevel, null, null, new XYZ(x0, y1, topLevel.Elevation), new XYZ(x1, y1, topLevel.Elevation), roomHeight);
                WallBuilder.CreateStraightWall(doc, topLevel, null, null, new XYZ(x0, y0, topLevel.Elevation), new XYZ(x0, y1, topLevel.Elevation), roomHeight);
                WallBuilder.CreateStraightWall(doc, topLevel, null, null, new XYZ(x1, y0, topLevel.Elevation), new XYZ(x1, y1, topLevel.Elevation), roomHeight);
                CurveArray roofFootprint = new CurveArray();
                roofFootprint.Append(Line.CreateBound(new XYZ(x0, y0, topLevel.Elevation + roomHeight), new XYZ(x1, y0, topLevel.Elevation + roomHeight)));
                roofFootprint.Append(Line.CreateBound(new XYZ(x1, y0, topLevel.Elevation + roomHeight), new XYZ(x1, y1, topLevel.Elevation + roomHeight)));
                roofFootprint.Append(Line.CreateBound(new XYZ(x1, y1, topLevel.Elevation + roomHeight), new XYZ(x0, y1, topLevel.Elevation + roomHeight)));
                roofFootprint.Append(Line.CreateBound(new XYZ(x0, y1, topLevel.Elevation + roomHeight), new XYZ(x0, y0, topLevel.Elevation + roomHeight)));
                RoofBuilder.CreateFootprintRoof(doc, topLevel, null, roofFootprint);
                tx.Commit();
            }
        }

        private static void CreateHydraulicCompressorRoom(Document doc, Level baseLevel, XYZ center, double shaftWidth, double shaftDepth)
        {
            double roomWidth = 2.0, roomDepth = 2.0;
            double x0 = center.X + shaftWidth / 2 + 0.2;
            double y0 = center.Y - roomDepth / 2;
            double x1 = x0 + roomWidth;
            double y1 = y0 + roomDepth;
            using (Transaction tx = new Transaction(doc, "Cuarto compresor"))
            {
                tx.Start();
                WallBuilder.CreateStraightWall(doc, baseLevel, null, null, new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x1, y0, baseLevel.Elevation), 3.0);
                WallBuilder.CreateStraightWall(doc, baseLevel, null, null, new XYZ(x0, y1, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation), 3.0);
                WallBuilder.CreateStraightWall(doc, baseLevel, null, null, new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x0, y1, baseLevel.Elevation), 3.0);
                WallBuilder.CreateStraightWall(doc, baseLevel, null, null, new XYZ(x1, y0, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation), 3.0);
                tx.Commit();
            }
        }

        private static void MakePanoramicGlassFaces(Document doc, Level baseLevel, Level topLevel, double x0, double y0, double x1, double y1, int numFaces, double wallThickness)
        {
            WallType? glassType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name.IndexOf("Vidrio", StringComparison.OrdinalIgnoreCase) >= 0);
            if (glassType == null)
            {
                WallType? basic = WallBuilder.GetDefaultWallType(doc);
                if (basic != null)
                {
                    using (Transaction tx = new Transaction(doc, "Crear tipo muro vidrio"))
                    {
                        tx.Start();
                        glassType = basic.Duplicate("Muro_Vidrio_Templado") as WallType;
                        if (glassType != null)
                        {
                            Material? glassMat = new FilteredElementCollector(doc)
                                .OfClass(typeof(Material))
                                .Cast<Material>()
                                .FirstOrDefault(m => m.Name.IndexOf("Vidrio", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (glassMat != null)
                                glassType.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(glassMat.Id);
                        }
                        tx.Commit();
                    }
                }
            }
            if (glassType == null) return;

            double height = topLevel.Elevation - baseLevel.Elevation;
            using (Transaction tx = new Transaction(doc, "Caras panorámicas"))
            {
                tx.Start();
                if (numFaces >= 1) WallBuilder.CreateWall(doc, baseLevel, topLevel, glassType, Line.CreateBound(new XYZ(x0, y0, baseLevel.Elevation), new XYZ(x1, y0, baseLevel.Elevation)), height);
                if (numFaces >= 2) WallBuilder.CreateWall(doc, baseLevel, topLevel, glassType, Line.CreateBound(new XYZ(x1, y0, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation)), height);
                if (numFaces >= 3) WallBuilder.CreateWall(doc, baseLevel, topLevel, glassType, Line.CreateBound(new XYZ(x0, y1, baseLevel.Elevation), new XYZ(x1, y1, baseLevel.Elevation)), height);
                tx.Commit();
            }
        }
    }
}