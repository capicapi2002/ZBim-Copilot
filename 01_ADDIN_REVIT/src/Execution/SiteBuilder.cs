#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de urbanización y entorno exterior para ZBIM‑Copilot.
    /// Cubre: calles, aceras, bordillos, aparcamientos, muros de contención, jardinería.
    /// </summary>
    public static class SiteBuilder
    {
        private const double STREET_WIDTH = 6.00;
        private const double SIDEWALK_WIDTH = 1.50;
        private const double CURB_HEIGHT = 0.15;
        private const double PARKING_SPOT_WIDTH = 2.50;
        private const double PARKING_SPOT_LENGTH = 5.00;
        private const double RETAINING_WALL_THICKNESS = 0.30;

        // Helper para conversión de CurveArray a IList<CurveLoop>
        private static IList<CurveLoop> ToCurveLoops(CurveArray curves)
        {
            var loop = new CurveLoop();
            foreach (Curve c in curves) loop.Append(c);
            return new List<CurveLoop> { loop };
        }

        public static Floor? CreateStreet(
            Document doc,
            Level baseLevel,
            XYZ startPoint,
            XYZ endPoint,
            double width = STREET_WIDTH,
            double thickness = 0.10)
        {
            if (doc == null || baseLevel == null || startPoint == null || endPoint == null)
                return null;

            XYZ direction = (endPoint - startPoint).Normalize();
            XYZ perpendicular = new XYZ(-direction.Y, direction.X, 0).Normalize();
            double halfWidth = width / 2;

            XYZ p1 = startPoint + perpendicular * halfWidth;
            XYZ p2 = endPoint + perpendicular * halfWidth;
            XYZ p3 = endPoint - perpendicular * halfWidth;
            XYZ p4 = startPoint - perpendicular * halfWidth;

            CurveArray curves = new CurveArray();
            curves.Append(Line.CreateBound(p1, p2));
            curves.Append(Line.CreateBound(p2, p3));
            curves.Append(Line.CreateBound(p3, p4));
            curves.Append(Line.CreateBound(p4, p1));

            FloorType? floorType = GetDefaultFloorType(doc);
            if (floorType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear calle"))
            {
                tx.Start();
                Floor street = Floor.Create(doc, ToCurveLoops(curves), floorType.Id, baseLevel.Id);
                street.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.Set(thickness);
                tx.Commit();
                return street;
            }
        }

        public static Floor? CreateSidewalk(
            Document doc,
            Level baseLevel,
            Curve streetEdge,
            Side side = Side.Left,
            double width = SIDEWALK_WIDTH,
            double height = CURB_HEIGHT)
        {
            if (doc == null || baseLevel == null || streetEdge == null)
                return null;

            XYZ edgeDir = (streetEdge.GetEndPoint(0) - streetEdge.GetEndPoint(1)).Normalize();
            XYZ perp = (side == Side.Left) ? new XYZ(-edgeDir.Y, edgeDir.X, 0) : new XYZ(edgeDir.Y, -edgeDir.X, 0);

            XYZ p1 = streetEdge.GetEndPoint(0);
            XYZ p2 = streetEdge.GetEndPoint(1);
            XYZ p3 = p2 + perp * width;
            XYZ p4 = p1 + perp * width;

            CurveArray curves = new CurveArray();
            curves.Append(Line.CreateBound(p1, p2));
            curves.Append(Line.CreateBound(p2, p3));
            curves.Append(Line.CreateBound(p3, p4));
            curves.Append(Line.CreateBound(p4, p1));

            FloorType? floorType = GetDefaultFloorType(doc);
            if (floorType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear acera"))
            {
                tx.Start();
                Floor sidewalk = Floor.Create(doc, ToCurveLoops(curves), floorType.Id, baseLevel.Id);
                if (height > 0)
                {
                    Parameter offsetParam = sidewalk.LookupParameter("Height Offset From Level");
                    offsetParam?.Set(height);
                }
                tx.Commit();
                return sidewalk;
            }
        }

        public static Floor? CreateParkingSpot(
            Document doc,
            Level baseLevel,
            XYZ insertionPoint,
            double width = PARKING_SPOT_WIDTH,
            double length = PARKING_SPOT_LENGTH)
        {
            if (doc == null || baseLevel == null || insertionPoint == null)
                return null;

            XYZ p1 = insertionPoint;
            XYZ p2 = new XYZ(insertionPoint.X + length, insertionPoint.Y, insertionPoint.Z);
            XYZ p3 = new XYZ(insertionPoint.X + length, insertionPoint.Y + width, insertionPoint.Z);
            XYZ p4 = new XYZ(insertionPoint.X, insertionPoint.Y + width, insertionPoint.Z);

            CurveArray curves = new CurveArray();
            curves.Append(Line.CreateBound(p1, p2));
            curves.Append(Line.CreateBound(p2, p3));
            curves.Append(Line.CreateBound(p3, p4));
            curves.Append(Line.CreateBound(p4, p1));

            FloorType? floorType = GetDefaultFloorType(doc);
            if (floorType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear plaza parking"))
            {
                tx.Start();
                Floor spot = Floor.Create(doc, ToCurveLoops(curves), floorType.Id, baseLevel.Id);
                DrawParkingLines(doc, baseLevel, p1, p2, p3, p4);
                tx.Commit();
                return spot;
            }
        }

        private static void DrawParkingLines(Document doc, Level baseLevel, XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            using (Transaction tx = new Transaction(doc, "Líneas parking"))
            {
                tx.Start();
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, baseLevel.Elevation + 0.01));
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                doc.Create.NewModelCurve(Line.CreateBound(p1, p2), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p2, p3), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p3, p4), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p4, p1), sketchPlane);
                tx.Commit();
            }
        }

        public static Wall? CreateRetainingWall(
            Document doc,
            Level baseLevel,
            Curve alignmentCurve,
            double height = 2.0,
            double thickness = RETAINING_WALL_THICKNESS)
        {
            WallType? retainingType = GetOrCreateRetainingWallType(doc);
            if (retainingType == null) return null;

            return WallBuilder.CreateWall(doc, baseLevel, null, retainingType, alignmentCurve, height);
        }

        private static WallType? GetOrCreateRetainingWallType(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name.IndexOf("Contención", StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null) return existing;

            var basicWall = WallBuilder.GetDefaultWallType(doc);
            if (basicWall == null) return null;
            using (Transaction tx = new Transaction(doc, "Crear tipo muro contención"))
            {
                tx.Start();
                WallType? newType = basicWall.Duplicate("Muro_Contención") as WallType;
                tx.Commit();
                return newType;
            }
        }

        public static void CreateGarden(
            Document doc,
            Level baseLevel,
            CurveArray boundaryCurves,
            List<XYZ> treePositions,
            int shrubCount = 10)
        {
            FloorType? floorType = GetDefaultFloorType(doc);
            if (floorType == null) return;

            using (Transaction tx = new Transaction(doc, "Crear jardín"))
            {
                tx.Start();

                Floor grass = Floor.Create(doc, ToCurveLoops(boundaryCurves), floorType.Id, baseLevel.Id);
                grass.LookupParameter("Material")?.Set(GetMaterialId(doc, "Hierba", "Grass", "Césped"));

                FamilySymbol? treeSymbol = FindTreeSymbol(doc);
                if (treeSymbol != null)
                {
                    foreach (XYZ pos in treePositions)
                        doc.Create.NewFamilyInstance(pos, treeSymbol, baseLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }

                FamilySymbol? shrubSymbol = FindShrubSymbol(doc);
                if (shrubSymbol != null)
                {
                    Random rnd = new Random();
                    BoundingBoxXYZ bbox = GetBoundingBox(boundaryCurves);
                    for (int i = 0; i < shrubCount; i++)
                    {
                        double x = bbox.Min.X + rnd.NextDouble() * (bbox.Max.X - bbox.Min.X);
                        double y = bbox.Min.Y + rnd.NextDouble() * (bbox.Max.Y - bbox.Min.Y);
                        XYZ shrubPos = new XYZ(x, y, baseLevel.Elevation);
                        doc.Create.NewFamilyInstance(shrubPos, shrubSymbol, baseLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    }
                }

                tx.Commit();
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static FloorType? GetDefaultFloorType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
        }

        private static ElementId GetMaterialId(Document doc, params string[] names)
        {
            foreach (string name in names)
            {
                var mat = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (mat != null) return mat.Id;
            }
            return ElementId.InvalidElementId;
        }

        private static FamilySymbol? FindTreeSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Planting)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf("Árbol", StringComparison.OrdinalIgnoreCase) >= 0
                                  || s.Name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilySymbol? FindShrubSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Planting)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf("Arbusto", StringComparison.OrdinalIgnoreCase) >= 0
                                  || s.Name.IndexOf("Shrub", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static BoundingBoxXYZ GetBoundingBox(CurveArray curves)
        {
            BoundingBoxXYZ bbox = new BoundingBoxXYZ();
            bool first = true;
            foreach (Curve c in curves)
            {
                IList<XYZ> pts = c.Tessellate();
                foreach (XYZ p in pts)
                {
                    if (first)
                    {
                        bbox.Min = p;
                        bbox.Max = p;
                        first = false;
                    }
                    else
                    {
                        bbox.Min = new XYZ(Math.Min(bbox.Min.X, p.X), Math.Min(bbox.Min.Y, p.Y), Math.Min(bbox.Min.Z, p.Z));
                        bbox.Max = new XYZ(Math.Max(bbox.Max.X, p.X), Math.Max(bbox.Max.Y, p.Y), Math.Max(bbox.Max.Z, p.Z));
                    }
                }
            }
            return bbox;
        }
    }

    /// <summary>
    /// Lado de la calle para la acera.
    /// </summary>
    public enum Side
    {
        Left,
        Right
    }
}