#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de estructuras portantes y cimentaciones para ZBIM‑Copilot.
    /// Cubre columnas, vigas, losas, muros portantes, zapatas, plateas y pilotes.
    /// </summary>
    public static class StructureBuilder
    {
        private const double DEFAULT_COLUMN_WIDTH = 0.30;
        private const double DEFAULT_COLUMN_DEPTH = 0.30;
        private const double DEFAULT_SLAB_THICKNESS = 0.20;

        // Helper para conversión de CurveArray a IList<CurveLoop>
        private static IList<CurveLoop> ToCurveLoops(CurveArray curves)
        {
            var loop = new CurveLoop();
            foreach (Curve c in curves) loop.Append(c);
            return new List<CurveLoop> { loop };
        }

        public static FamilyInstance? CreateColumn(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            XYZ insertionPoint,
            StructuralMaterial material = StructuralMaterial.Concrete,
            double width = DEFAULT_COLUMN_WIDTH,
            double depth = DEFAULT_COLUMN_DEPTH,
            double height = 0)
        {
            if (doc == null || baseLevel == null || insertionPoint == null)
                return null;

            FamilySymbol? columnSymbol = FindColumnSymbol(doc, material, width, depth);
            if (columnSymbol == null) return null;

            if (!columnSymbol.IsActive) columnSymbol.Activate();

            double columnHeight = (topLevel != null) ? topLevel.Elevation - baseLevel.Elevation : height;
            if (columnHeight <= 0) return null;

            using (Transaction tx = new Transaction(doc, "Crear columna"))
            {
                tx.Start();
                FamilyInstance column = doc.Create.NewFamilyInstance(
                    insertionPoint, columnSymbol, baseLevel, StructuralType.Column);
                Parameter lengthParam = column.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM);
                if (lengthParam == null || lengthParam.IsReadOnly)
                    lengthParam = column.LookupParameter("Length");
                lengthParam?.Set(columnHeight);
                tx.Commit();
                return column;
            }
        }

        public static FamilyInstance? CreateBeam(
            Document doc,
            Level baseLevel,
            XYZ startPoint,
            XYZ endPoint,
            StructuralMaterial material = StructuralMaterial.Concrete,
            double width = DEFAULT_COLUMN_WIDTH,
            double height = 0)
        {
            if (doc == null || baseLevel == null || startPoint == null || endPoint == null)
                return null;

            bool isFlushBeam = (height <= 0);
            double beamHeight = isFlushBeam ? DEFAULT_SLAB_THICKNESS : height;

            FamilySymbol? beamSymbol = FindBeamSymbol(doc, material, width, beamHeight);
            if (beamSymbol == null) return null;

            if (!beamSymbol.IsActive) beamSymbol.Activate();

            Line curve = Line.CreateBound(startPoint, endPoint);

            using (Transaction tx = new Transaction(doc, "Crear viga"))
            {
                tx.Start();
                FamilyInstance beam = doc.Create.NewFamilyInstance(curve, beamSymbol, baseLevel, StructuralType.Beam);
                if (isFlushBeam)
                {
                    // CORRECCIÓN: INSTANCE_HEIGHT_PARAM no existe, usar LookupParameter
                    Parameter heightParam = beam.LookupParameter("Height");
                    if (heightParam == null || heightParam.IsReadOnly)
                        heightParam = beam.LookupParameter("Beam Height");
                    heightParam?.Set(beamHeight);
                }
                tx.Commit();
                return beam;
            }
        }

        public static Floor? CreateSlab(
            Document doc,
            Level baseLevel,
            CurveArray boundaryCurves,
            double thickness = DEFAULT_SLAB_THICKNESS,
            bool isFungiform = false,
            double offsetFromLevel = 0)
        {
            if (doc == null || baseLevel == null || boundaryCurves == null || boundaryCurves.Size == 0)
                return null;

            FloorType? floorType = GetStructuralFloorType(doc, thickness);
            if (floorType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear losa"))
            {
                tx.Start();
                // CORRECCIÓN: Floor.Create requiere IList<CurveLoop>
                Floor slab = Floor.Create(doc, ToCurveLoops(boundaryCurves), floorType.Id, baseLevel.Id);
                slab.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.Set(thickness);
                if (offsetFromLevel != 0)
                {
                    // CORRECCIÓN: FLOOR_ATTR_HEIGHT_OFFSET_FROM_LEVEL_PARAM no existe
                    Parameter offsetParam = slab.LookupParameter("Height Offset From Level");
                    if (offsetParam == null || offsetParam.IsReadOnly)
                        offsetParam = slab.LookupParameter("Elevation Offset");
                    offsetParam?.Set(offsetFromLevel);
                }

                if (!isFungiform)
                    CreateBeamsForSlab(doc, baseLevel, boundaryCurves, thickness);

                tx.Commit();
                return slab;
            }
        }

        private static void CreateBeamsForSlab(Document doc, Level baseLevel, CurveArray curves, double slabThickness)
        {
            foreach (Curve c in curves)
            {
                XYZ start = c.GetEndPoint(0);
                XYZ end = c.GetEndPoint(1);
                CreateBeam(doc, baseLevel, start, end, StructuralMaterial.Concrete, 0.25, 0.40);
            }
        }

        public static Wall? CreateStructuralWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            Curve alignmentCurve,
            StructuralMaterial material = StructuralMaterial.Concrete,
            double thickness = 0.25,
            double height = 0)
        {
            WallType? wallType = GetStructuralWallType(doc, material, thickness);
            if (wallType == null) return null;

            return WallBuilder.CreateWall(doc, baseLevel, topLevel, wallType, alignmentCurve, height, true);
        }

        public static FamilyInstance? CreateIsolatedFooting(
            Document doc,
            Level baseLevel,
            XYZ insertionPoint,
            double width = 1.0,
            double depth = 1.0,
            double thickness = 0.50)
        {
            FamilySymbol? footingSymbol = FindFootingSymbol(doc, "Zapata_Aislada", width, depth, thickness);
            if (footingSymbol == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear zapata"))
            {
                tx.Start();
                FamilyInstance footing = doc.Create.NewFamilyInstance(insertionPoint, footingSymbol, baseLevel, StructuralType.Footing);
                tx.Commit();
                return footing;
            }
        }

        public static Floor? CreateMatFoundation(
            Document doc,
            Level baseLevel,
            CurveArray boundaryCurves,
            double thickness = 0.60)
        {
            FloorType? matType = GetStructuralFloorType(doc, thickness);
            if (matType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear platea"))
            {
                tx.Start();
                // CORRECCIÓN: Floor.Create requiere IList<CurveLoop>
                Floor mat = Floor.Create(doc, ToCurveLoops(boundaryCurves), matType.Id, baseLevel.Id);
                mat.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.Set(thickness);
                // CORRECCIÓN: FLOOR_ATTR_HEIGHT_OFFSET_FROM_LEVEL_PARAM no existe
                Parameter offsetParam = mat.LookupParameter("Height Offset From Level");
                if (offsetParam == null || offsetParam.IsReadOnly)
                    offsetParam = mat.LookupParameter("Elevation Offset");
                offsetParam?.Set(-thickness);
                tx.Commit();
                return mat;
            }
        }

        public static FamilyInstance? CreatePile(
            Document doc,
            Level baseLevel,
            XYZ insertionPoint,
            double diameter = 0.50,
            double length = 10.0)
        {
            FamilySymbol? pileSymbol = FindPileSymbol(doc, diameter, length);
            if (pileSymbol == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear pilote"))
            {
                tx.Start();
                FamilyInstance pile = doc.Create.NewFamilyInstance(insertionPoint, pileSymbol, baseLevel, StructuralType.Footing);
                tx.Commit();
                return pile;
            }
        }

        // ============================================================
        // MÉTODOS DE BÚSQUEDA DE FAMILIAS
        // ============================================================
        private static FamilySymbol? FindColumnSymbol(Document doc, StructuralMaterial material, double width, double depth)
        {
            string familyKeyword = material == StructuralMaterial.Steel ? "Acero" : "Hormigón";
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(familyKeyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilySymbol? FindBeamSymbol(Document doc, StructuralMaterial material, double width, double height)
        {
            string familyKeyword = material == StructuralMaterial.Steel ? "Acero" : "Hormigón";
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(familyKeyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FloorType? GetStructuralFloorType(Document doc, double thickness)
        {
            var floorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(ft => ft.Name.IndexOf("Estructural", StringComparison.OrdinalIgnoreCase) >= 0);
            if (floorType != null) return floorType;

            var basicFloor = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
            if (basicFloor == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo losa estructural"))
            {
                tx.Start();
                FloorType? newFloor = basicFloor.Duplicate($"Losa_Estructural_{thickness}m") as FloorType;
                tx.Commit();
                return newFloor;
            }
        }

        private static WallType? GetStructuralWallType(Document doc, StructuralMaterial material, double thickness)
        {
            string keyword = material == StructuralMaterial.Concrete ? "Hormigón Armado" : "Mampostería";
            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            if (wallType != null) return wallType;

            var basicWall = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault();
            if (basicWall == null) return null;
            using (Transaction tx = new Transaction(doc, "Crear tipo muro estructural"))
            {
                tx.Start();
                WallType? newWall = basicWall.Duplicate($"Muro_Portante_{keyword}") as WallType;
                tx.Commit();
                return newWall;
            }
        }

        private static FamilySymbol? FindFootingSymbol(Document doc, string familyName, double width, double depth, double thickness)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilySymbol? FindPileSymbol(Document doc, double diameter, double length)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf("Pilote", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>
    /// Material estructural.
    /// </summary>
    public enum StructuralMaterial
    {
        Concrete,
        Steel,
        Masonry
    }
}