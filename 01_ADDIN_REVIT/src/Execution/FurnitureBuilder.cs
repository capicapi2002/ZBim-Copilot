#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de mobiliario y equipamiento interior. Usa LookupParameter.
    /// </summary>
    public static class FurnitureBuilder
    {
        private static FamilyInstance? PlaceFurnitureItem(
            Document doc,
            Level level,
            XYZ insertionPoint,
            string keyword,
            BuiltInCategory category)
        {
            if (doc == null || level == null || insertionPoint == null)
                return null;

            FamilySymbol? symbol = FindFamilySymbolByKeyword(doc, category, keyword);
            if (symbol == null) return null;

            if (!symbol.IsActive) symbol.Activate();

            using (Transaction tx = new Transaction(doc, $"Insertar {keyword}"))
            {
                tx.Start();
                FamilyInstance instance = doc.Create.NewFamilyInstance(insertionPoint, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return instance;
            }
        }

        public static List<FamilyInstance> PlaceKitchen(
            Document doc,
            Level level,
            XYZ cornerPoint,
            double length = 3.0,
            double depth = 0.60)
        {
            List<FamilyInstance> items = new List<FamilyInstance>();
            FamilyInstance? counter = PlaceFurnitureItem(doc, level, cornerPoint, "Encimera", BuiltInCategory.OST_Furniture);
            if (counter != null) items.Add(counter);
            return items;
        }

        public static List<FamilyInstance> PlaceBathroom(
            Document doc,
            Level level,
            XYZ insertionPoint,
            bool includeShower = true)
        {
            List<FamilyInstance> items = new List<FamilyInstance>();
            FamilyInstance? sink = PlaceFurnitureItem(doc, level, insertionPoint, "Lavabo", BuiltInCategory.OST_PlumbingFixtures);
            if (sink != null) items.Add(sink);
            FamilyInstance? toilet = PlaceFurnitureItem(doc, level, insertionPoint + new XYZ(0.8, 0, 0), "Inodoro", BuiltInCategory.OST_PlumbingFixtures);
            if (toilet != null) items.Add(toilet);
            if (includeShower)
            {
                FamilyInstance? shower = PlaceFurnitureItem(doc, level, insertionPoint + new XYZ(1.6, 0, 0), "Ducha", BuiltInCategory.OST_PlumbingFixtures);
                if (shower != null) items.Add(shower);
            }
            return items;
        }

        public static FamilyInstance? PlaceWardrobe(
            Document doc,
            Level level,
            XYZ insertionPoint,
            double width = 1.20,
            double depth = 0.60,
            double height = 2.40)
        {
            FamilySymbol? wardrobeSymbol = FindFamilySymbolByKeyword(doc, BuiltInCategory.OST_Furniture, "Armario", "Wardrobe");
            if (wardrobeSymbol == null)
                wardrobeSymbol = CreateGenericFurnitureSymbol(doc, $"Armario {width:F2}x{depth:F2}", width, depth, height);
            if (wardrobeSymbol == null) return null;
            if (!wardrobeSymbol.IsActive) wardrobeSymbol.Activate();

            using (Transaction tx = new Transaction(doc, "Insertar armario"))
            {
                tx.Start();
                FamilyInstance wardrobe = doc.Create.NewFamilyInstance(insertionPoint, wardrobeSymbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                tx.Commit();
                return wardrobe;
            }
        }

        public static List<FamilyInstance> PlaceOfficeSet(
            Document doc,
            Level level,
            XYZ deskPosition,
            int numberOfChairs = 1)
        {
            List<FamilyInstance> items = new List<FamilyInstance>();
            FamilyInstance? desk = PlaceFurnitureItem(doc, level, deskPosition, "Mesa", BuiltInCategory.OST_Furniture);
            if (desk != null) items.Add(desk);
            for (int i = 0; i < numberOfChairs; i++)
            {
                XYZ chairPos = deskPosition + new XYZ(i * 1.2 + 0.5, -1.0, 0);
                FamilyInstance? chair = PlaceFurnitureItem(doc, level, chairPos, "Silla", BuiltInCategory.OST_Furniture);
                if (chair != null) items.Add(chair);
            }
            return items;
        }

        private static FamilySymbol? FindFamilySymbolByKeyword(Document doc, BuiltInCategory category, params string[] keywords)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(category)
                .Cast<FamilySymbol>();
            foreach (string kw in keywords)
            {
                var match = symbols.FirstOrDefault(s => s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            return symbols.FirstOrDefault();
        }

        private static FamilySymbol? CreateGenericFurnitureSymbol(Document doc, string typeName, double width, double depth, double height)
        {
            var basicFurniture = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Furniture)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            if (basicFurniture == null) return null;

            using (Transaction tx = new Transaction(doc, "Crear tipo mobiliario genérico"))
            {
                tx.Start();
                FamilySymbol? newSymbol = basicFurniture.Duplicate(typeName) as FamilySymbol;
                if (newSymbol != null)
                {
                    Parameter? widthParam = newSymbol.LookupParameter("Width");
                    if (widthParam == null || widthParam.IsReadOnly)
                        widthParam = newSymbol.LookupParameter("Ancho");
                    widthParam?.Set(width);

                    // CORRECCIÓN: Revit 2027 - FURNITURE_DEPTH no existe
                    Parameter? depthParam = newSymbol.LookupParameter("Depth");
                    if (depthParam == null || depthParam.IsReadOnly)
                        depthParam = newSymbol.LookupParameter("Profundidad");
                    depthParam?.Set(depth);

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
}