#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de instalaciones de gas (montantes, derivaciones, llaves, contadores, ventilaciones).
    /// Usa API moderna de Revit 2027 (Pipe.Create con puntos XYZ).
    /// </summary>
    public static class GasBuilder
    {
        private const double GAS_PIPE_DIAMETER = 0.025;
        private const double BRANCH_DIAMETER = 0.015;

        public static Pipe? CreateGasRiser(
            Document doc,
            Level baseLevel,
            Level topLevel,
            XYZ insertionPoint)
        {
            if (doc == null || baseLevel == null || topLevel == null || insertionPoint == null)
                return null;

            PipeType? pipeType = GetOrCreateGasPipeType(doc, GAS_PIPE_DIAMETER);
            PipingSystemType? systemType = GetOrCreateGasPipingSystem(doc);
            if (pipeType == null || systemType == null) return null;

            XYZ start = new XYZ(insertionPoint.X, insertionPoint.Y, baseLevel.Elevation);
            XYZ end = new XYZ(insertionPoint.X, insertionPoint.Y, topLevel.Elevation);

            using (Transaction tx = new Transaction(doc, "Crear montante de gas"))
            {
                tx.Start();
                // CORRECCIÓN: Revit 2027 - Pipe.Create con puntos XYZ (5 argumentos)
                Pipe pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, baseLevel.Id, start, end);
                tx.Commit();
                return pipe;
            }
        }

        public static Pipe? CreateGasBranch(
            Document doc,
            Level level,
            XYZ startPoint,
            XYZ endPoint)
        {
            if (doc == null || level == null || startPoint == null || endPoint == null)
                return null;

            PipeType? pipeType = GetOrCreateGasPipeType(doc, BRANCH_DIAMETER);
            PipingSystemType? systemType = GetOrCreateGasPipingSystem(doc);
            if (pipeType == null || systemType == null) return null;

            double ceilingElevation = level.Elevation + 2.4;
            XYZ p1 = new XYZ(startPoint.X, startPoint.Y, ceilingElevation);
            XYZ p2 = new XYZ(endPoint.X, endPoint.Y, ceilingElevation);
            XYZ p3 = new XYZ(endPoint.X, endPoint.Y, level.Elevation + 0.5);

            using (Transaction tx = new Transaction(doc, "Crear derivación de gas"))
            {
                tx.Start();
                // CORRECCIÓN: Revit 2027 - Pipe.Create con puntos XYZ (5 argumentos)
                Pipe pipe1 = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, p1, p2);
                Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, p2, p3);
                tx.Commit();
                return pipe1;
            }
        }

        public static FamilyInstance? PlaceMainShutoffValve(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceValve(doc, level, insertionPoint, "Llave general de gas", 0.5);
        }

        public static FamilyInstance? PlaceFloorShutoffValve(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceValve(doc, level, insertionPoint, "Llave de planta gas", 0.5);
        }

        private static FamilyInstance? PlaceValve(
            Document doc, Level level, XYZ point, string keyword, double height)
        {
            FamilySymbol? symbol = FindPipeAccessorySymbol(doc, keyword);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            XYZ insertion = new XYZ(point.X, point.Y, level.Elevation + height);

            using (Transaction tx = new Transaction(doc, $"Insertar {keyword}"))
            {
                tx.Start();
                FamilyInstance valve = doc.Create.NewFamilyInstance(
                    insertion, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return valve;
            }
        }

        public static void CreateMeterCabinet(
            Document doc,
            Level level,
            XYZ insertionPoint,
            double width = 1.0,
            double depth = 0.4,
            double height = 1.80)
        {
            if (doc == null || level == null || insertionPoint == null) return;

            XYZ p1 = new XYZ(insertionPoint.X, insertionPoint.Y, level.Elevation);
            XYZ p2 = new XYZ(insertionPoint.X + width, insertionPoint.Y, level.Elevation);
            XYZ p3 = new XYZ(insertionPoint.X, insertionPoint.Y + depth, level.Elevation);
            XYZ p4 = new XYZ(insertionPoint.X + width, insertionPoint.Y + depth, level.Elevation);

            using (Transaction tx = new Transaction(doc, "Crear armario de contadores"))
            {
                tx.Start();
                WallBuilder.CreateStraightWall(doc, level, null, null, p1, p2, height);
                WallBuilder.CreateStraightWall(doc, level, null, null, p1, p3, height);
                WallBuilder.CreateStraightWall(doc, level, null, null, p2, p4, height);
                tx.Commit();
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static PipeType? GetOrCreateGasPipeType(Document doc, double diameter)
        {
            string typeName = $"Gas {diameter * 1000:F0}mm";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault(pt => pt.Name.IndexOf("Gas", StringComparison.OrdinalIgnoreCase) >= 0
                                   && Math.Abs((pt.LookupParameter("Diameter")?.AsDouble() ?? 0) - diameter) < 0.001);
            if (existing != null) return existing;

            var firstType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
            if (firstType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo tubería gas"))
            {
                tx.Start();
                PipeType? newType = firstType.Duplicate(typeName) as PipeType;
                if (newType != null)
                {
                    Parameter? diamParam = newType.LookupParameter("Diameter");
                    if (diamParam == null || diamParam.IsReadOnly)
                        diamParam = newType.LookupParameter("Diámetro");
                    diamParam?.Set(diameter);
                }
                tx.Commit();
                return newType;
            }
        }

        private static PipingSystemType? GetOrCreateGasPipingSystem(Document doc)
        {
            string systemName = "Gas";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .FirstOrDefault(ps => ps.Name.IndexOf("Gas", StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null) return existing;

            var firstSystem = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault();
            if (firstSystem == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear sistema de gas"))
            {
                tx.Start();
                PipingSystemType? newSystem = firstSystem.Duplicate(systemName) as PipingSystemType;
                tx.Commit();
                return newSystem;
            }
        }

        private static FamilySymbol? FindPipeAccessorySymbol(Document doc, string keyword)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeAccessory)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}