#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de sistemas especiales (solar, telecomunicaciones, domótica, CCTV).
    /// </summary>
    public static class SpecialSystemsBuilder
    {
        public static FamilyInstance? PlaceSolarPanel(
            Document doc,
            Level roofLevel,
            XYZ insertionPoint)
        {
            return PlaceSpecialDevice(doc, roofLevel, insertionPoint,
                BuiltInCategory.OST_ElectricalEquipment, "Panel solar", "Fotovoltaico");
        }

        public static FamilyInstance? PlaceSolarCollector(
            Document doc,
            Level roofLevel,
            XYZ insertionPoint)
        {
            return PlaceSpecialDevice(doc, roofLevel, insertionPoint,
                BuiltInCategory.OST_MechanicalEquipment, "Colector solar", "Térmico");
        }

        public static FamilyInstance? PlaceTelecomOutlet(
            Document doc,
            Wall hostWall,
            XYZ insertionPoint,
            TelecomType telecomType = TelecomType.Data)
        {
            string keyword = telecomType switch
            {
                TelecomType.TV => "TV",
                TelecomType.Telephone => "Teléfono",
                TelecomType.Data => "RJ45",
                _ => "RJ45"
            };
            return PlaceWallMountedDevice(doc, hostWall, insertionPoint,
                BuiltInCategory.OST_ElectricalFixtures, keyword);
        }

        public static FamilyInstance? PlaceVideoIntercom(
            Document doc,
            Wall hostWall,
            XYZ insertionPoint)
        {
            return PlaceWallMountedDevice(doc, hostWall, insertionPoint,
                BuiltInCategory.OST_ElectricalFixtures, "Videoportero");
        }

        public static FamilyInstance? PlaceCCTVCamera(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            return PlaceSpecialDevice(doc, level, insertionPoint,
                BuiltInCategory.OST_ElectricalEquipment, "Cámara", "CCTV");
        }

        public static FamilyInstance? PlaceHomeAutomationSensor(
            Document doc,
            Level level,
            XYZ insertionPoint,
            string sensorKeyword = "Sensor multifunción")
        {
            return PlaceSpecialDevice(doc, level, insertionPoint,
                BuiltInCategory.OST_ElectricalEquipment, sensorKeyword);
        }

        private static FamilyInstance? PlaceSpecialDevice(
            Document doc,
            Level level,
            XYZ point,
            BuiltInCategory category,
            params string[] keywords)
        {
            FamilySymbol? symbol = FindSymbol(doc, category, keywords);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, $"Insertar {keywords[0]}"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        private static FamilyInstance? PlaceWallMountedDevice(
            Document doc,
            Wall hostWall,
            XYZ point,
            BuiltInCategory category,
            params string[] keywords)
        {
            FamilySymbol? symbol = FindSymbol(doc, category, keywords);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, $"Insertar {keywords[0]}"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, hostWall, doc.ActiveView.GenLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        private static FamilySymbol? FindSymbol(Document doc, BuiltInCategory category, params string[] keywords)
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
    }

    public enum TelecomType { TV, Telephone, Data }
}