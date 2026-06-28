#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de revestimientos y falsos techos.
    /// </summary>
    public static class InteriorFinishBuilder
    {
        // Helper para convertir CurveArray a IList<CurveLoop> (Requerido en Revit 2024+)
        private static IList<CurveLoop> ConvertToCurveLoops(CurveArray curves)
        {
            var loops = new List<CurveLoop>();
            var loop = new CurveLoop();
            foreach (Curve c in curves) loop.Append(c);
            loops.Add(loop);
            return loops;
        }

        public static Floor? ApplyFloorFinish(
            Document doc, Level baseLevel, CurveArray curves, string materialName,
            double thickness = 0.02, double offsetFromLevel = 0)
        {
            if (doc == null || baseLevel == null || curves == null || curves.Size == 0) return null;
            FloorType? floorType = GetOrCreateFloorType(doc, materialName, thickness);
            if (floorType == null) return null;

            using (Transaction tx = new Transaction(doc, $"Aplicar pavimento {materialName}"))
            {
                tx.Start();
                // CORRECCIÓN: Floor.Create requiere IList<CurveLoop>
                Floor floor = Floor.Create(doc, ConvertToCurveLoops(curves), floorType.Id, baseLevel.Id);
                if (offsetFromLevel != 0)
                {
                    Parameter? offsetParam = floor.LookupParameter("Height Offset From Level");
                    offsetParam?.Set(offsetFromLevel);
                }
                tx.Commit();
                return floor;
            }
        }

        public static void ApplyWallFinish(Document doc, Wall wall, string materialName)
        {
            if (doc == null || wall == null) return;
            ElementId materialId = GetOrCreateMaterial(doc, materialName);
            if (materialId == ElementId.InvalidElementId) return;

            using (Transaction tx = new Transaction(doc, $"Aplicar revestimiento {materialName}"))
            {
                tx.Start();
                Parameter? materialParam = wall.LookupParameter("Material") ?? wall.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (materialParam != null && !materialParam.IsReadOnly) materialParam.Set(materialId);
                tx.Commit();
            }
        }

        public static Ceiling? CreateSuspendedCeiling(
            Document doc, Level level, CurveArray curves,
            double ceilingHeight = 2.60, string materialName = "Yeso laminado")
        {
            if (doc == null || level == null || curves == null || curves.Size == 0) return null;
            CeilingType? ceilingType = GetOrCreateCeilingType(doc, materialName);
            if (ceilingType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear falso techo"))
            {
                tx.Start();
                // CORRECCIÓN: Ceiling.Create requiere IList<CurveLoop>
                Ceiling ceiling = Ceiling.Create(doc, ConvertToCurveLoops(curves), ceilingType.Id, level.Id);
                Parameter? heightParam = ceiling.LookupParameter("Height Above Level") ?? ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                heightParam?.Set(ceilingHeight);
                tx.Commit();
                return ceiling;
            }
        }

        public static Wall? CreateBaseboard(
            Document doc, Level baseLevel, Curve baseCurve,
            double height = 0.10, double thickness = 0.02, string materialName = "Madera zócalo")
        {
            WallType? wallType = GetOrCreateWallType(doc, materialName, thickness);
            if (wallType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear zócalo"))
            {
                tx.Start();
                Wall baseboard = Wall.Create(doc, baseCurve, wallType.Id, baseLevel.Id, height, 0, false, false);
                tx.Commit();
                return baseboard;
            }
        }

        private static ElementId GetOrCreateMaterial(Document doc, string materialName)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            using (Transaction tx = new Transaction(doc, $"Crear material {materialName}"))
            {
                tx.Start();
                ElementId materialId = Material.Create(doc, materialName);
                tx.Commit();
                return materialId;
            }
        }

        private static FloorType? GetOrCreateFloorType(Document doc, string typeName, double thickness)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(ft => ft.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var firstFloor = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
            if (firstFloor == null) return null;
            using (Transaction tx = new Transaction(doc, "Crear tipo de suelo"))
            {
                tx.Start();
                FloorType? newFloor = firstFloor.Duplicate(typeName) as FloorType;
                if (newFloor != null)
                {
                    newFloor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.Set(thickness);
                    ElementId matId = GetOrCreateMaterial(doc, typeName);
                    if (matId != ElementId.InvalidElementId) newFloor.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(matId);
                }
                tx.Commit();
                return newFloor;
            }
        }

        private static CeilingType? GetOrCreateCeilingType(Document doc, string typeName)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>().FirstOrDefault(ct => ct.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var firstCeiling = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>().FirstOrDefault();
            if (firstCeiling == null) return null;
            using (Transaction tx = new Transaction(doc, "Crear tipo de techo"))
            {
                tx.Start();
                CeilingType? newCeiling = firstCeiling.Duplicate(typeName) as CeilingType;
                if (newCeiling != null)
                {
                    ElementId matId = GetOrCreateMaterial(doc, typeName);
                    if (matId != ElementId.InvalidElementId) newCeiling.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(matId);
                }
                tx.Commit();
                return newCeiling;
            }
        }

        private static WallType? GetOrCreateWallType(Document doc, string typeName, double thickness)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(wt => wt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var firstWall = WallBuilder.GetDefaultWallType(doc);
            if (firstWall == null) return null;
            using (Transaction tx = new Transaction(doc, "Crear tipo de muro"))
            {
                tx.Start();
                WallType? newWall = firstWall.Duplicate(typeName) as WallType;
                if (newWall != null)
                {
                    ElementId matId = GetOrCreateMaterial(doc, typeName);
                    if (matId != ElementId.InvalidElementId) newWall.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(matId);
                }
                tx.Commit();
                return newWall;
            }
        }
    }
}