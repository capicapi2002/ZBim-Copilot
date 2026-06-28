#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de fontanería y saneamiento (montantes, derivaciones, bajantes, aparatos).
    /// Usa API moderna de Revit (Pipe.Create).
    /// </summary>
    public static class WaterSystemsBuilder
    {
        private const string DEFAULT_PIPE_TYPE = "Estándar";

        public static Pipe? CreateRiser(
            Document doc,
            Level baseLevel,
            Level topLevel,
            XYZ insertionPoint,
            WaterSystemType systemType = WaterSystemType.ColdWater,
            double diameter = 1.0)
        {
            if (doc == null || baseLevel == null || topLevel == null || insertionPoint == null)
                return null;

            PipeType? pipeType = GetOrCreatePipeType(doc, systemType, diameter);
            PipingSystemType? pipingSystemType = GetOrCreatePipingSystemType(doc, systemType);
            if (pipeType == null || pipingSystemType == null) return null;

            XYZ startPoint = new XYZ(insertionPoint.X, insertionPoint.Y, baseLevel.Elevation);
            XYZ endPoint = new XYZ(insertionPoint.X, insertionPoint.Y, topLevel.Elevation);

            using (Transaction tx = new Transaction(doc, "Crear montante"))
            {
                tx.Start();
                // CORRECCIÓN: En Revit 2027, Pipe.Create usa la firma legacy con puntos XYZ
                Pipe pipe = Pipe.Create(doc, pipingSystemType.Id, pipeType.Id, baseLevel.Id, startPoint, endPoint);
                tx.Commit();
                return pipe;
            }
        }

        public static Pipe? CreateHorizontalBranch(
            Document doc,
            Level level,
            XYZ startPoint,
            XYZ fixtureLocation,
            WaterSystemType systemType = WaterSystemType.ColdWater,
            double diameter = 0.5)
        {
            if (doc == null || level == null || startPoint == null || fixtureLocation == null)
                return null;

            PipeType? pipeType = GetOrCreatePipeType(doc, systemType, diameter);
            PipingSystemType? pipingSystemType = GetOrCreatePipingSystemType(doc, systemType);
            if (pipeType == null || pipingSystemType == null) return null;

            double ceilingHeight = level.Elevation + 2.4;
            XYZ p1 = new XYZ(startPoint.X, startPoint.Y, ceilingHeight);
            XYZ p2 = new XYZ(fixtureLocation.X, fixtureLocation.Y, ceilingHeight);
            XYZ p3 = new XYZ(fixtureLocation.X, fixtureLocation.Y, level.Elevation + 0.5);

            using (Transaction tx = new Transaction(doc, "Crear derivación"))
            {
                tx.Start();
                Pipe pipe1 = Pipe.Create(doc, pipingSystemType.Id, pipeType.Id, level.Id, p1, p2);
                Pipe.Create(doc, pipingSystemType.Id, pipeType.Id, level.Id, p2, p3);
                tx.Commit();
                return pipe1;
            }
        }

        public static Pipe? CreateStack(
            Document doc,
            Level topLevel,
            Level baseLevel,
            XYZ insertionPoint,
            DrainageType drainageType = DrainageType.Sanitary,
            double diameter = 4.0)
        {
            if (doc == null || topLevel == null || baseLevel == null || insertionPoint == null)
                return null;

            PipeType? pipeType = GetOrCreatePipeType(doc, WaterSystemType.ColdWater, diameter);
            PipingSystemType? pipingSystemType = GetOrCreateDrainageSystemType(doc, drainageType);
            if (pipeType == null || pipingSystemType == null) return null;

            XYZ startPoint = new XYZ(insertionPoint.X, insertionPoint.Y, topLevel.Elevation);
            XYZ endPoint = new XYZ(insertionPoint.X, insertionPoint.Y, baseLevel.Elevation - 1.0);

            using (Transaction tx = new Transaction(doc, "Crear bajante"))
            {
                tx.Start();
                Pipe pipe = Pipe.Create(doc, pipingSystemType.Id, pipeType.Id, baseLevel.Id, startPoint, endPoint);
                tx.Commit();
                return pipe;
            }
        }

        public static FamilyInstance? PlaceFixture(
            Document doc,
            Level level,
            XYZ insertionPoint,
            FixtureType fixtureType = FixtureType.Lavabo)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindFixtureSymbol(doc, fixtureType);
            if (symbol == null) return null;

            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar aparato sanitario"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static PipeType? GetOrCreatePipeType(Document doc, WaterSystemType systemType, double diameterInches)
        {
            string typeName = $"{DEFAULT_PIPE_TYPE} {diameterInches}\"";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault(pt => pt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var firstType = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault();
            if (firstType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo de tubería"))
            {
                tx.Start();
                PipeType? newType = firstType.Duplicate(typeName) as PipeType;
                if (newType != null)
                {
                    Parameter? diameterParam = newType.LookupParameter("Diameter")
                        ?? newType.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    diameterParam?.Set(diameterInches * 0.0254);
                }
                tx.Commit();
                return newType;
            }
        }

        private static PipingSystemType? GetOrCreatePipingSystemType(Document doc, WaterSystemType systemType)
        {
            string systemName = systemType switch
            {
                WaterSystemType.HotWater => "Agua caliente sanitaria",
                WaterSystemType.ColdWater => "Agua fría",
                _ => "Agua fría"
            };

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .FirstOrDefault(ps => ps.Name.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var firstSystem = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .FirstOrDefault();
            if (firstSystem == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear sistema de tuberías"))
            {
                tx.Start();
                PipingSystemType? newSystem = firstSystem.Duplicate(systemName) as PipingSystemType;
                tx.Commit();
                return newSystem;
            }
        }

        private static PipingSystemType? GetOrCreateDrainageSystemType(Document doc, DrainageType drainageType)
        {
            string systemName = drainageType == DrainageType.Sanitary ? "Desagüe cloacal" : "Desagüe pluvial";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .FirstOrDefault(ps => ps.Name.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            return GetOrCreatePipingSystemType(doc, WaterSystemType.ColdWater);
        }

        private static FamilySymbol? FindFixtureSymbol(Document doc, FixtureType fixtureType)
        {
            string keyword = fixtureType switch
            {
                FixtureType.Lavabo => "Lavabo", FixtureType.Inodoro => "Inodoro", FixtureType.Ducha => "Ducha",
                FixtureType.Bañera => "Bañera", FixtureType.Fregadero => "Fregadero", _ => "Lavabo"
            };

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    public enum WaterSystemType { ColdWater, HotWater, ReturnWater }
    public enum DrainageType { Sanitary, Storm }
    public enum FixtureType { Lavabo, Inodoro, Ducha, Bañera, Fregadero, Bidé }
}