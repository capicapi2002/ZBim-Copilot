#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de ventanas, puertas y puertas‑ventana para ZBIM‑Copilot.
    /// </summary>
    public static class WindowDoorBuilder
    {
        public static FamilyInstance? InsertWindow(
            Document doc,
            Wall hostWall,
            FamilySymbol? windowSymbol = null,
            XYZ? insertionPoint = null,
            double sillHeight = 1.0,
            double width = 0,
            double height = 0)
        {
            return InsertOpening(doc, hostWall, windowSymbol, insertionPoint, sillHeight, width, height,
                BuiltInCategory.OST_Windows, "Ventana");
        }

        public static FamilyInstance? InsertDoor(
            Document doc,
            Wall hostWall,
            FamilySymbol? doorSymbol = null,
            XYZ? insertionPoint = null,
            double sillHeight = 0,
            double width = 0,
            double height = 0)
        {
            return InsertOpening(doc, hostWall, doorSymbol, insertionPoint, sillHeight, width, height,
                BuiltInCategory.OST_Doors, "Puerta");
        }

        public static FamilyInstance? InsertDoorWindow(
            Document doc,
            Wall hostWall,
            FamilySymbol? symbol = null,
            XYZ? insertionPoint = null,
            double width = 0,
            double height = 0)
        {
            if (symbol == null)
            {
                symbol = FindFamilySymbol(doc, BuiltInCategory.OST_Doors, "vidrio", "cristal", "glass", "doorwindow");
                symbol ??= FindFamilySymbol(doc, BuiltInCategory.OST_Windows, "puerta", "door", "ventana puerta");
                symbol ??= FindFamilySymbol(doc, BuiltInCategory.OST_Windows);
            }

            return InsertOpening(doc, hostWall, symbol, insertionPoint, 0, width, height,
                BuiltInCategory.OST_Windows, "Puerta-ventana");
        }

        private static FamilyInstance? InsertOpening(
            Document doc,
            Wall hostWall,
            FamilySymbol? symbol,
            XYZ? insertionPoint,
            double sillHeight,
            double width,
            double height,
            BuiltInCategory category,
            string description)
        {
            if (doc == null || hostWall == null)
                throw new ArgumentNullException();

            if (symbol == null)
            {
                symbol = FindFamilySymbol(doc, category);
                if (symbol == null) return null;
            }

            if (!symbol.IsActive)
                symbol.Activate();

            XYZ point = insertionPoint ?? hostWall.Orientation.Normalize() * hostWall.Width / 2;
            if (insertionPoint == null)
            {
                LocationCurve? locCurve = hostWall.Location as LocationCurve;
                if (locCurve != null)
                    point = locCurve.Curve.Evaluate(0.5, true);
            }

            Level? wallLevel = doc.GetElement(hostWall.LevelId) as Level;
            if (wallLevel == null)
                throw new InvalidOperationException("El muro no tiene un nivel base válido.");

            FamilyInstance instance = doc.Create.NewFamilyInstance(
                point,
                symbol,
                hostWall,
                wallLevel,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            if (instance == null) return null;

            if (sillHeight > 0)
            {
                Parameter sillParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                if (sillParam == null || sillParam.IsReadOnly)
                    sillParam = instance.LookupParameter("Sill Height");
                if (sillParam != null && !sillParam.IsReadOnly)
                    sillParam.Set(sillHeight);
            }

            if (height > 0)
            {
                // CORRECCIÓN API 2027: INSTANCE_HEIGHT_PARAM eliminado, usar solo LookupParameter
                Parameter heightParam = instance.LookupParameter("Height");
                if (heightParam == null || heightParam.IsReadOnly)
                    heightParam = instance.LookupParameter("Generic Height");
                heightParam?.Set(height);
            }

            if (width > 0)
            {
                // CORRECCIÓN: INSTANCE_WIDTH_PARAM no existe, usar LookupParameter
                Parameter widthParam = instance.LookupParameter("Width");
                if (widthParam == null || widthParam.IsReadOnly)
                    widthParam = instance.LookupParameter("Generic Width");
                widthParam?.Set(width);
            }

            return instance;
        }

        public static void AlignWindowSillsToDoorHead(
            Document doc,
            Wall hostWall,
            FamilyInstance referenceDoor,
            List<FamilyInstance> windowsOnSameWall)
        {
            if (doc == null || hostWall == null || referenceDoor == null || windowsOnSameWall == null)
                return;

            double doorHeadHeight = 0;
            Parameter doorSill = referenceDoor.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
            // CORRECCIÓN API 2027: INSTANCE_HEIGHT_PARAM eliminado, usar solo LookupParameter
            Parameter doorHeight = referenceDoor.LookupParameter("Height");
            if (doorSill != null && doorHeight != null)
                doorHeadHeight = doorSill.AsDouble() + doorHeight.AsDouble();

            if (doorHeadHeight <= 0) return;

            using (Transaction tx = new Transaction(doc, "Alinear ventanas"))
            {
                tx.Start();
                foreach (FamilyInstance window in windowsOnSameWall)
                {
                    // CORRECCIÓN API 2027: INSTANCE_HEIGHT_PARAM eliminado, usar solo LookupParameter
                    Parameter winHeight = window.LookupParameter("Height");
                    Parameter winSill = window.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                    if (winHeight != null && winSill != null && !winSill.IsReadOnly)
                    {
                        double newSill = doorHeadHeight - winHeight.AsDouble();
                        if (newSill >= 0)
                            winSill.Set(newSill);
                    }
                }
                tx.Commit();
            }
        }

        public static Family? LoadFamily(Document doc, string familyFilePath)
        {
            if (doc == null || string.IsNullOrEmpty(familyFilePath) || !System.IO.File.Exists(familyFilePath))
                return null;

            using (Transaction tx = new Transaction(doc, "Cargar familia"))
            {
                tx.Start();
                // CORRECCIÓN: doc.LoadFamily devuelve bool, usar out parameter
                Family? family = null;
                bool loaded = doc.LoadFamily(familyFilePath, out family);
                tx.Commit();
                return loaded ? family : null;
            }
        }

        private static FamilySymbol? FindFamilySymbol(Document doc, BuiltInCategory category, params string[] nameFilters)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(category)
                .Cast<FamilySymbol>();

            if (nameFilters.Length > 0)
            {
                foreach (var filter in nameFilters)
                {
                    var match = symbols.FirstOrDefault(s =>
                        s.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return match;
                }
                return null;
            }

            return symbols.FirstOrDefault();
        }
    }
}