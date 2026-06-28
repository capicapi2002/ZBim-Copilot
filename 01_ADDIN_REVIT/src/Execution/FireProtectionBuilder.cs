#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de protección contra incendios (columna seca, BIE, rociadores, detectores, puertas cortafuego).
    /// Usa API moderna (Pipe.Create con puntos XYZ, LookupParameter).
    /// </summary>
    public static class FireProtectionBuilder
    {
        private const double DRY_RISER_DIAMETER = 0.10;
        private const double HOSE_CABINET_HEIGHT = 1.20;
        private const double EXTINGUISHER_HEIGHT = 1.20;
        private const double SPRINKLER_SPACING = 3.50;
        private const double SMOKE_DETECTOR_SPACING = 8.00;

        public static Pipe? CreateDryRiser(
            Document doc,
            Level baseLevel,
            Level topLevel,
            XYZ insertionPoint)
        {
            if (doc == null || baseLevel == null || topLevel == null || insertionPoint == null)
                return null;

            PipeType? steelPipeType = GetOrCreateSteelPipeType(doc, DRY_RISER_DIAMETER);
            PipingSystemType? fireSystem = GetOrCreateFirePipingSystem(doc);
            if (steelPipeType == null || fireSystem == null) return null;

            XYZ startPoint = new XYZ(insertionPoint.X, insertionPoint.Y, baseLevel.Elevation);
            XYZ endPoint = new XYZ(insertionPoint.X, insertionPoint.Y, topLevel.Elevation + 1.0);

            using (Transaction tx = new Transaction(doc, "Crear columna seca"))
            {
                tx.Start();
                // CORRECCIÓN: Revit 2027 - Pipe.Create con puntos XYZ (5 argumentos)
                Pipe pipe = Pipe.Create(doc, fireSystem.Id, steelPipeType.Id, baseLevel.Id, startPoint, endPoint);
                tx.Commit();
                return pipe;
            }
        }

        public static FamilyInstance? PlaceHoseCabinet(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceFamilyInstance(doc, FindFireProtectionSymbol(doc, BuiltInCategory.OST_FireProtection, "BIE", "Manguera"), level, insertionPoint, HOSE_CABINET_HEIGHT);
        }

        public static FamilyInstance? PlaceExtinguisher(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceFamilyInstance(doc, FindFireProtectionSymbol(doc, BuiltInCategory.OST_FireProtection, "Extintor"), level, insertionPoint, EXTINGUISHER_HEIGHT);
        }

        public static FamilyInstance? PlaceSprinkler(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceFamilyInstance(doc, FindFireProtectionSymbol(doc, BuiltInCategory.OST_Sprinklers, "Sprinkler", "Rociador"), level, insertionPoint, 2.40);
        }

        public static FamilyInstance? PlaceSmokeDetector(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceFamilyInstance(doc, FindFireProtectionSymbol(doc, BuiltInCategory.OST_FireAlarmDevices, "Detector", "Humo"), level, insertionPoint, 2.40);
        }

        public static FamilyInstance? PlaceFireAlarm(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceFamilyInstance(doc, FindFireProtectionSymbol(doc, BuiltInCategory.OST_FireAlarmDevices, "Sirena", "Alarma"), level, insertionPoint, 2.50);
        }

        public static FamilyInstance? CreateFireReserveTank(
            Document doc,
            Level baseLevel,
            XYZ insertionPoint,
            double capacityLiters = 12000)
        {
            FamilySymbol? symbol = FindFireProtectionSymbol(doc, BuiltInCategory.OST_MechanicalEquipment, "Tanque", "Reserva", "Agua");
            return PlaceFamilyInstance(doc, symbol, baseLevel, insertionPoint, 0);
        }

        public static FamilyInstance? PlaceFireDoor(
            Document doc,
            Wall hostWall,
            XYZ insertionPoint,
            FireResistanceRating rating = FireResistanceRating.RF60,
            double width = 0.90,
            double height = 2.10)
        {
            FamilySymbol? symbol = FindFireProtectionSymbol(doc, BuiltInCategory.OST_Doors, "Cortafuego", "RF");
            if (symbol == null)
                symbol = CreateFireDoorSymbol(doc, width, height, rating);

            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar puerta cortafuego"))
            {
                tx.Start();
                FamilyInstance door = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, hostWall, doc.ActiveView.GenLevel,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                Parameter? ratingParam = door.LookupParameter("Resistencia al fuego");
                if (ratingParam == null || ratingParam.IsReadOnly)
                    ratingParam = door.LookupParameter("Fire Rating");
                if (ratingParam != null && !ratingParam.IsReadOnly)
                {
                    ratingParam.Set(rating.ToString().Replace("RF", "RF"));
                }

                tx.Commit();
                return door;
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static FamilyInstance? PlaceFamilyInstance(Document doc, FamilySymbol? symbol, Level level, XYZ insertionPoint, double elevationOffset)
        {
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            XYZ point = new XYZ(insertionPoint.X, insertionPoint.Y, level.Elevation + elevationOffset);

            using (Transaction tx = new Transaction(doc, $"Insertar {symbol.Name}"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        private static FamilySymbol? FindFireProtectionSymbol(Document doc, BuiltInCategory category, params string[] keywords)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(category)
                .Cast<FamilySymbol>();
            foreach (var kw in keywords)
            {
                var match = symbols.FirstOrDefault(s => s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            return symbols.FirstOrDefault();
        }

        private static PipeType? GetOrCreateSteelPipeType(Document doc, double diameter)
        {
            string typeName = $"Acero {diameter * 1000:F0}mm";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault(pt => pt.Name.IndexOf("Acero", StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null) return existing;

            var firstType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
            if (firstType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo tubería acero"))
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

        private static PipingSystemType? GetOrCreateFirePipingSystem(Document doc)
        {
            string systemName = "PCI (Columna Seca)";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .FirstOrDefault(ps => ps.Name.IndexOf("PCI", StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null) return existing;

            var firstSystem = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault();
            if (firstSystem == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear sistema PCI"))
            {
                tx.Start();
                PipingSystemType? newSystem = firstSystem.Duplicate(systemName) as PipingSystemType;
                tx.Commit();
                return newSystem;
            }
        }

        private static FamilySymbol? CreateFireDoorSymbol(Document doc, double width, double height, FireResistanceRating rating)
        {
            var firstDoor = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            if (firstDoor == null) return null;

            string typeName = $"Cortafuego {rating}";
            using (Transaction tx = new Transaction(doc, "Crear puerta cortafuego"))
            {
                tx.Start();
                FamilySymbol? newSymbol = firstDoor.Duplicate(typeName) as FamilySymbol;
                if (newSymbol != null)
                {
                    // CORRECCIÓN: Revit 2027 - INSTANCE_WIDTH_PARAM e INSTANCE_HEIGHT_PARAM no existen
                    Parameter? widthParam = newSymbol.LookupParameter("Width");
                    if (widthParam == null || widthParam.IsReadOnly)
                        widthParam = newSymbol.LookupParameter("Ancho");
                    widthParam?.Set(width);

                    Parameter? heightParam = newSymbol.LookupParameter("Height");
                    if (heightParam == null || heightParam.IsReadOnly)
                        heightParam = newSymbol.LookupParameter("Altura");
                    heightParam?.Set(height);
                }
                tx.Commit();
                return newSymbol;
            }
        }
    }

    public enum FireResistanceRating { RF30, RF60, RF90, RF120 }
}