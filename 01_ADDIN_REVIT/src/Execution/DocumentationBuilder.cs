#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de documentación automática (vistas, etiquetas, tablas, planos).
    /// Usa API moderna de Revit (ViewPlan.Create, RoomTag, IndependentTag, ViewSchedule, ViewSheet).
    /// </summary>
    public static class DocumentationBuilder
    {
        public static ViewPlan? CreateFloorPlan(
            Document doc,
            Level level,
            ElementId? viewFamilyTypeId = null)
        {
            if (doc == null || level == null)
                return null;

            if (viewFamilyTypeId == null || viewFamilyTypeId == ElementId.InvalidElementId)
            {
                viewFamilyTypeId = GetDefaultViewFamilyTypeId(doc, ViewFamily.FloorPlan);
                if (viewFamilyTypeId == null || viewFamilyTypeId == ElementId.InvalidElementId)
                    return null;
            }

            using (Transaction tx = new Transaction(doc, $"Crear vista de planta {level.Name}"))
            {
                tx.Start();
                // CORRECCIÓN: ViewPlan.Create requiere ElementId, no long
                ViewPlan viewPlan = ViewPlan.Create(doc, new ElementId(viewFamilyTypeId.Value), level.Id);
                viewPlan.Name = $"Planta {level.Name}";
                tx.Commit();
                return viewPlan;
            }
        }

        public static List<ViewPlan> CreateAllFloorPlans(Document doc)
        {
            List<ViewPlan> views = new List<ViewPlan>();
            if (doc == null) return views;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation);

            foreach (Level level in levels)
            {
                ViewPlan? view = CreateFloorPlan(doc, level);
                if (view != null) views.Add(view);
            }
            return views;
        }

        public static void TagAllRooms(Document doc, ViewPlan viewPlan)
        {
            if (doc == null || viewPlan == null) return;

            RoomTagType? roomTagType = GetDefaultRoomTagType(doc);
            if (roomTagType == null) return;

            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .Cast<SpatialElement>()
                .Where(r => r is Room)
                .ToList();

            using (Transaction tx = new Transaction(doc, "Etiquetar habitaciones"))
            {
                tx.Start();
                foreach (Room room in rooms)
                {
                    try
                    {
                        LocationPoint? loc = room.Location as LocationPoint;
                        if (loc != null)
                        {
                            XYZ point = loc.Point;
                            RoomTag tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(point.X, point.Y), viewPlan.Id);
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }
        }

        public static void TagAllDoorsAndWindows(Document doc, ViewPlan viewPlan)
        {
            if (doc == null || viewPlan == null) return;

            using (Transaction tx = new Transaction(doc, "Etiquetar puertas/ventanas"))
            {
                tx.Start();

                var doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>();
                foreach (FamilyInstance door in doors)
                {
                    try
                    {
                        LocationPoint? loc = door.Location as LocationPoint;
                        if (loc != null)
                        {
                            // CORRECCIÓN API 2027: ElementTypeGroup.DoorTagType eliminado, buscar por categoría
                            ElementId defaultTagTypeId = GetDefaultTagTypeId(doc, BuiltInCategory.OST_DoorTags);
                            if (defaultTagTypeId != ElementId.InvalidElementId)
                                IndependentTag.Create(doc, defaultTagTypeId, viewPlan.Id, new Reference(door), false, TagOrientation.Horizontal, loc.Point);
                        }
                    }
                    catch { }
                }

                var windows = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>();
                foreach (FamilyInstance window in windows)
                {
                    try
                    {
                        LocationPoint? loc = window.Location as LocationPoint;
                        if (loc != null)
                        {
                            // CORRECCIÓN API 2027: ElementTypeGroup.WindowTagType eliminado, buscar por categoría
                            ElementId defaultTagTypeId = GetDefaultTagTypeId(doc, BuiltInCategory.OST_WindowTags);
                            if (defaultTagTypeId != ElementId.InvalidElementId)
                                IndependentTag.Create(doc, defaultTagTypeId, viewPlan.Id, new Reference(window), false, TagOrientation.Horizontal, loc.Point);
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }
        }

        public static ViewSchedule? CreateElementSchedule(
            Document doc,
            BuiltInCategory category,
            string scheduleName)
        {
            if (doc == null) return null;

            using (Transaction tx = new Transaction(doc, $"Crear tabla {scheduleName}"))
            {
                tx.Start();
                ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(category));
                schedule.Name = scheduleName;

                AddScheduleField(schedule, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Tipo");
                AddScheduleField(schedule, BuiltInParameter.ELEM_FAMILY_PARAM, "Familia");
                AddScheduleField(schedule, BuiltInParameter.ELEM_TYPE_PARAM, "Tipo de elemento");

                tx.Commit();
                return schedule;
            }
        }

        private static void AddScheduleField(ViewSchedule schedule, BuiltInParameter param, string header)
        {
            try
            {
                ScheduleField field = schedule.Definition.AddField(ScheduleFieldType.Instance, new ElementId(param));
                field.ColumnHeading = header;
            }
            catch { }
        }

        public static ViewSheet? CreateSheet(
            Document doc,
            string sheetNumber,
            string sheetName,
            View view,
            XYZ? origin = null)
        {
            if (doc == null || view == null) return null;

            ElementId? titleBlockId = GetDefaultTitleBlockId(doc);
            if (titleBlockId == null) return null;

            XYZ position = origin ?? new XYZ(0.5, 0.5, 0);

            using (Transaction tx = new Transaction(doc, $"Crear plano {sheetNumber}"))
            {
                tx.Start();
                // CORRECCIÓN: ViewSheet.Create requiere ElementId, no long
                ViewSheet sheet = ViewSheet.Create(doc, new ElementId(titleBlockId.Value));
                sheet.SheetNumber = sheetNumber;
                sheet.Name = sheetName;
                Viewport.Create(doc, sheet.Id, view.Id, position);
                tx.Commit();
                return sheet;
            }
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static ElementId? GetDefaultViewFamilyTypeId(Document doc, ViewFamily viewFamily)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == viewFamily)?.Id;
        }

        private static RoomTagType? GetDefaultRoomTagType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RoomTagType))
                .Cast<RoomTagType>()
                .FirstOrDefault();
        }

        private static ElementId? GetDefaultTitleBlockId(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault()?.Id;
        }

        /// <summary>
        /// CORRECCIÓN API 2027: Reemplaza GetDefaultElementTypeId(ElementTypeGroup.XxxTagType)
        /// Busca el tipo de etiqueta por defecto mediante FilteredElementCollector por categoría.
        /// Devuelve ElementId (no nullable) para evitar confusión con ElementId.Value (long).
        /// </summary>
        private static ElementId GetDefaultTagTypeId(Document doc, BuiltInCategory tagCategory)
        {
            var symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            return symbol?.Id ?? ElementId.InvalidElementId;
        }
    }
}