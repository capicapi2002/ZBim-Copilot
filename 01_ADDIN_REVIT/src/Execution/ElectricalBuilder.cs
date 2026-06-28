#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de instalaciones eléctricas (bandejas, luminarias, enchufes, cuadros).
    /// Usa API moderna (ElectricalSystemType en lugar de ElectricalSystemTypeEnum).
    /// </summary>
    public static class ElectricalBuilder
    {
        public static CableTray? CreateCableTray(
            Document doc,
            Level level,
            XYZ startPoint,
            XYZ endPoint,
            double width = 0.30,
            double height = 0.10)
        {
            if (doc == null || level == null || startPoint == null || endPoint == null)
                return null;

            CableTrayType? trayType = GetOrCreateCableTrayType(doc, width, height);
            if (trayType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear bandeja"))
            {
                tx.Start();
                // CORRECCIÓN: CableTray.Create firma correcta (doc, trayType.Id, startPoint, endPoint, level.Id)
                CableTray tray = CableTray.Create(doc, trayType.Id, startPoint, endPoint, level.Id);
                tx.Commit();
                return tray;
            }
        }

        public static FamilyInstance? PlaceLightFixture(
            Document doc,
            Level level,
            XYZ insertionPoint,
            double lightOutput = 3000)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindLightFixtureSymbol(doc);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar luminaria"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                if (lightOutput > 0)
                {
                    // CORRECCIÓN API 2027: RBS_LIGHT_LOSS_FACTOR_PARAM eliminado, usar solo LookupParameter
                    Parameter? fluxParam = instance.LookupParameter("Light Loss Factor");
                    fluxParam?.Set(lightOutput);
                }
                tx.Commit();
                return instance;
            }
        }

        public static FamilyInstance? PlaceElectricalDevice(
            Document doc,
            Wall hostWall,
            XYZ insertionPoint,
            ElectricalDeviceType deviceType = ElectricalDeviceType.Outlet)
        {
            if (doc == null || hostWall == null || insertionPoint == null)
                return null;

            string keyword = deviceType == ElectricalDeviceType.Outlet ? "Enchufe" : "Interruptor";
            FamilySymbol? symbol = FindElectricalDeviceSymbol(doc, keyword);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar dispositivo"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, hostWall, doc.ActiveView.GenLevel,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        public static FamilyInstance? PlaceElectricalPanel(
            Document doc,
            Level level,
            XYZ insertionPoint)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindElectricalPanelSymbol(doc);
            if (symbol == null) return null;
            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar cuadro"))
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
        private static CableTrayType? GetOrCreateCableTrayType(Document doc, double width, double height)
        {
            string typeName = $"Bandeja {width:F2}x{height:F2}";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(CableTrayType))
                .Cast<CableTrayType>()
                .FirstOrDefault(ct => ct.Name.Equals(typeName));
            if (existing != null) return existing;

            var firstType = new FilteredElementCollector(doc)
                .OfClass(typeof(CableTrayType))
                .Cast<CableTrayType>()
                .FirstOrDefault();
            if (firstType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo bandeja"))
            {
                tx.Start();
                CableTrayType? newType = firstType.Duplicate(typeName) as CableTrayType;
                if (newType != null)
                {
                    (newType.LookupParameter("Width") ?? newType.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM))?.Set(width);
                    (newType.LookupParameter("Height") ?? newType.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM))?.Set(height);
                }
                tx.Commit();
                return newType;
            }
        }

        private static FamilySymbol? FindLightFixtureSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
        }

        private static FamilySymbol? FindElectricalDeviceSymbol(Document doc, string keyword)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilySymbol? FindElectricalPanelSymbol(Document doc)
        {
            // CORRECCIÓN API 2027: OST_ElectricalPanelboards eliminado, usar OST_ElectricalEquipment
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
        }
    }

    public enum ElectricalDeviceType { Outlet, Switch }
}