#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de climatización y ventilación (conductos, difusores, equipos).
    /// Usa API moderna de Revit 2027 (Duct.Create con puntos XYZ).
    /// </summary>
    public static class HVACBuilder
    {
        private const string SUPPLY_AIR_SYSTEM = "Impulsión";
        private const string RETURN_AIR_SYSTEM = "Retorno";
        private const string EXHAUST_AIR_SYSTEM = "Extracción";

        /// <summary>
        /// Crea un tramo de conducto entre dos puntos.
        /// </summary>
        public static Duct? CreateDuct(
            Document doc,
            XYZ startPoint,
            XYZ endPoint,
            HVACSystemType systemType = HVACSystemType.Supply,
            double width = 0,
            double height = 0,
            double diameter = 0)
        {
            if (doc == null || startPoint == null || endPoint == null)
                return null;

            DuctType? ductType = GetOrCreateDuctType(doc, width, height, diameter);
            MechanicalSystemType? mechSystemType = GetOrCreateMechanicalSystemType(doc, systemType);
            if (ductType == null || mechSystemType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear conducto"))
            {
                tx.Start();
                // CORRECCIÓN: Revit 2027 - Duct.Create con puntos XYZ (5 argumentos)
                Duct duct = Duct.Create(doc, mechSystemType.Id, ductType.Id, mechSystemType.Id, startPoint, endPoint);
                tx.Commit();
                return duct;
            }
        }

        /// <summary>
        /// Crea un difusor o rejilla de ventilación.
        /// </summary>
        public static FamilyInstance? PlaceAirTerminal(
            Document doc,
            Level level,
            XYZ insertionPoint,
            AirTerminalType airTerminalType = AirTerminalType.Diffuser,
            double airFlow = 0)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindAirTerminalSymbol(doc, airTerminalType);
            if (symbol == null) return null;

            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar difusor"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                if (airFlow > 0)
                {
                    Parameter? flowParam = instance.LookupParameter("Air Flow");
                    if (flowParam == null || flowParam.IsReadOnly)
                        flowParam = instance.LookupParameter("Flujo de aire");
                    if (flowParam == null || flowParam.IsReadOnly)
                        flowParam = instance.LookupParameter("Caudal");
                    flowParam?.Set(airFlow / 3600); // convertir m³/h a m³/s
                }
                tx.Commit();
                return instance;
            }
        }

        /// <summary>
        /// Coloca un equipo de climatización (UTA, caldera, etc.).
        /// </summary>
        public static FamilyInstance? PlaceEquipment(
            Document doc,
            Level level,
            XYZ insertionPoint,
            HVACEquipmentType equipmentType = HVACEquipmentType.AHU)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindEquipmentSymbol(doc, equipmentType);
            if (symbol == null) return null;

            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar equipo HVAC"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(
                    insertionPoint, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        /// <summary>
        /// Conecta un difusor al conducto más cercano mediante un tramo flexible.
        /// </summary>
        public static void ConnectTerminalToDuct(Document doc, FamilyInstance terminal, Duct duct)
        {
            if (doc == null || terminal == null || duct == null) return;

            using (Transaction tx = new Transaction(doc, "Conectar difusor"))
            {
                tx.Start();
                ConnectorSet? terminalConnectors = terminal.MEPModel?.ConnectorManager?.Connectors;
                ConnectorSet? ductConnectors = duct.ConnectorManager?.Connectors;
                if (terminalConnectors != null && ductConnectors != null)
                {
                    foreach (Connector tc in terminalConnectors)
                    {
                        foreach (Connector dc in ductConnectors)
                        {
                            if (tc.IsConnected || dc.IsConnected) continue;
                            try { doc.Create.NewElbowFitting(tc, dc); } catch { }
                        }
                    }
                }
                tx.Commit();
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        public static MechanicalSystemType? GetOrCreateMechanicalSystemType(Document doc, HVACSystemType systemType)
        {
            string systemName = systemType switch
            {
                HVACSystemType.Supply => SUPPLY_AIR_SYSTEM,
                HVACSystemType.Return => RETURN_AIR_SYSTEM,
                HVACSystemType.Exhaust => EXHAUST_AIR_SYSTEM,
                _ => SUPPLY_AIR_SYSTEM
            };

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .FirstOrDefault(ms => ms.Name.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var firstSystem = new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .FirstOrDefault();
            if (firstSystem == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear sistema de conductos"))
            {
                tx.Start();
                MechanicalSystemType? newSystem = firstSystem.Duplicate(systemName) as MechanicalSystemType;
                tx.Commit();
                return newSystem;
            }
        }

        private static DuctType? GetOrCreateDuctType(Document doc, double width, double height, double diameter)
        {
            string typeName = (diameter > 0) ? $"Circular {diameter:F2}m" : $"Rectangular {width:F2}x{height:F2}m";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>()
                .FirstOrDefault(dt => dt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var firstType = new FilteredElementCollector(doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>()
                .FirstOrDefault();
            if (firstType == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo de conducto"))
            {
                tx.Start();
                DuctType? newType = firstType.Duplicate(typeName) as DuctType;
                if (newType != null)
                {
                    if (diameter > 0)
                    {
                        Parameter? diamParam = newType.LookupParameter("Diameter");
                        if (diamParam == null || diamParam.IsReadOnly)
                            diamParam = newType.LookupParameter("Diámetro");
                        diamParam?.Set(diameter);
                    }
                    else
                    {
                        Parameter? widthParam = newType.LookupParameter("Width");
                        if (widthParam == null || widthParam.IsReadOnly)
                            widthParam = newType.LookupParameter("Ancho");
                        widthParam?.Set(width);

                        Parameter? heightParam = newType.LookupParameter("Height");
                        if (heightParam == null || heightParam.IsReadOnly)
                            heightParam = newType.LookupParameter("Altura");
                        heightParam?.Set(height);
                    }
                }
                tx.Commit();
                return newType;
            }
        }

        private static FamilySymbol? FindAirTerminalSymbol(Document doc, AirTerminalType type)
        {
            string keyword = type switch { AirTerminalType.Diffuser => "Difusor", AirTerminalType.Grille => "Rejilla", _ => "Difusor" };
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DuctTerminal)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static FamilySymbol? FindEquipmentSymbol(Document doc, HVACEquipmentType type)
        {
            string keyword = type switch
            {
                HVACEquipmentType.AHU => "UTA", HVACEquipmentType.Boiler => "Caldera", HVACEquipmentType.Chiller => "Enfriadora",
                HVACEquipmentType.HeatPump => "Aerotermia", HVACEquipmentType.Split => "Split", _ => "UTA"
            };
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    public enum HVACSystemType { Supply, Return, Exhaust }
    public enum AirTerminalType { Diffuser, Grille }
    public enum HVACEquipmentType { AHU, Boiler, Chiller, HeatPump, Split }
}