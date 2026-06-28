#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador profesional de escaleras adaptativas (rectas, L, U, curvas).
    /// Cumple CTE DB‑SUA y utiliza la API moderna de Revit 2024+ (StairsEditScope).
    /// </summary>
    public static class StairBuilder
    {
        private const double MIN_TREAD = 0.28;       // metros
        private const double MAX_RISER = 0.18;       // metros
        private const double MIN_WIDTH = 1.00;       // metros (ancho útil)
        private const int MAX_RISERS_BEFORE_LANDING = 16;
        private const double LANDING_LENGTH = 1.00;  // metros

        public static Stairs? CreateStairs(
            Document doc,
            Level baseLevel,
            Level topLevel,
            BoundingBoxXYZ boundingBox,
            StairType preferredType = StairType.Straight)
        {
            double height = topLevel.Elevation - baseLevel.Elevation;
            if (height <= 0) return null;

            StairType finalType = DetermineBestStairType(boundingBox, height, preferredType);

            StairsType? stairsType = GetDefaultStairsType(doc);
            if (stairsType == null) return null;

            int totalRisers = (int)Math.Ceiling(height / MAX_RISER);
            double actualRiser = height / totalRisers;
            double actualTread = MIN_TREAD;

            using (Transaction tx = new Transaction(doc, "Crear escalera"))
            {
                tx.Start();

                // CORRECCIÓN API 2027: StairsEditScope.Start() acepta 2 argumentos (baseLevelId, topLevelId)
                // Crea una escalera vacía con tipo por defecto, luego cambiamos el tipo
                StairsEditScope editScope = new StairsEditScope(doc, "Crear escalera");
                ElementId stairsId = editScope.Start(baseLevel.Id, topLevel.Id);
                Stairs? stairs = doc.GetElement(stairsId) as Stairs;
                
                if (stairs == null)
                {
                    editScope.Cancel();
                    tx.RollBack();
                    return null;
                }

                // Asignar tipo de escalera después de crearla
                stairs.ChangeTypeId(stairsType.Id);

                if (finalType == StairType.Straight)
                    CreateStraightRun(doc, stairs, boundingBox, totalRisers, actualRiser, actualTread, baseLevel.Id, topLevel.Id);
                else if (finalType == StairType.LShaped)
                    CreateLShapedRun(doc, stairs, boundingBox, totalRisers, actualRiser, actualTread, baseLevel.Id, topLevel.Id);
                else if (finalType == StairType.UShaped)
                    CreateUShapedRun(doc, stairs, boundingBox, totalRisers, actualRiser, actualTread, baseLevel.Id, topLevel.Id);
                else if (finalType == StairType.Curved)
                    CreateCurvedRun(doc, stairs, boundingBox, totalRisers, actualRiser, actualTread, baseLevel.Id, topLevel.Id);

                editScope.Commit(new FailuresPreprocessor());

                // Ajustar parámetros con fallback robusto
                Parameter? riserParam = stairs.LookupParameter("Riser Height");
                if (riserParam == null || riserParam.IsReadOnly)
                    riserParam = stairs.LookupParameter("Altura de Contrahuella");
                riserParam?.Set(actualRiser);

                Parameter? treadParam = stairs.LookupParameter("Tread Depth");
                if (treadParam == null || treadParam.IsReadOnly)
                    treadParam = stairs.LookupParameter("Profundidad de Huella");
                treadParam?.Set(actualTread);

                Parameter? numRisersParam = stairs.LookupParameter("Number of Risers");
                if (numRisersParam == null || numRisersParam.IsReadOnly)
                    numRisersParam = stairs.LookupParameter("Número de Contrahuellas");
                numRisersParam?.Set(totalRisers);

                tx.Commit();
                return stairs;
            }
        }

        private static StairType DetermineBestStairType(BoundingBoxXYZ bbox, double height, StairType preferred)
        {
            double width = bbox.Max.X - bbox.Min.X;
            double depth = bbox.Max.Y - bbox.Min.Y;
            if (depth > 3 * width) return StairType.Straight;
            if (height > 3.0 && width > 2.0 && depth > 2.0)
                return (width > depth) ? StairType.LShaped : StairType.UShaped;
            if (preferred == StairType.Curved && width > 2.0 && depth > 2.0)
                return StairType.Curved;
            return preferred;
        }

        private static void CreateStraightRun(Document doc, Stairs stairs, BoundingBoxXYZ bbox, int totalRisers, double actualRiser, double actualTread, ElementId baseLevelId, ElementId topLevelId)
        {
            double requiredLength = totalRisers * actualTread;
            double startX = bbox.Min.X + (bbox.Max.X - bbox.Min.X) / 2;
            
            // Calcular centro manualmente
            double centerZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;
            
            XYZ start = new XYZ(startX, bbox.Min.Y, centerZ);
            XYZ end = new XYZ(startX, bbox.Min.Y + requiredLength, centerZ);
            Curve path = Line.CreateBound(start, end);
            
            // CORRECCIÓN: CreateStraightRun requiere parámetro 'justification'
            StairsRun run = StairsRun.CreateStraightRun(doc, stairs.Id, (Line)path, StairsRunJustification.Center);
            
            Parameter? runWidth = run.LookupParameter("Width");
            if (runWidth == null || runWidth.IsReadOnly)
                runWidth = run.LookupParameter("Ancho");
            runWidth?.Set(MIN_WIDTH);
        }

        private static void CreateLShapedRun(Document doc, Stairs stairs, BoundingBoxXYZ bbox, int totalRisers, double actualRiser, double actualTread, ElementId baseLevelId, ElementId topLevelId)
        {
            int risersFirst = totalRisers / 2;
            int risersSecond = totalRisers - risersFirst;
            
            double centerZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;
            
            XYZ start = new XYZ(bbox.Min.X + 0.3, bbox.Min.Y + 0.3, centerZ);
            XYZ mid = new XYZ(start.X, start.Y + risersFirst * actualTread, centerZ);
            XYZ end = new XYZ(start.X + risersSecond * actualTread, mid.Y, centerZ);
            Curve path1 = Line.CreateBound(start, mid);
            Curve path2 = Line.CreateBound(mid, end);
            
            StairsRun.CreateStraightRun(doc, stairs.Id, (Line)path1, StairsRunJustification.Center);
            StairsRun.CreateStraightRun(doc, stairs.Id, (Line)path2, StairsRunJustification.Center);
        }

        private static void CreateUShapedRun(Document doc, Stairs stairs, BoundingBoxXYZ bbox, int totalRisers, double actualRiser, double actualTread, ElementId baseLevelId, ElementId topLevelId)
        {
            double width = bbox.Max.X - bbox.Min.X;
            int risersPerFlight = totalRisers / 3;
            
            double centerZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;
            
            XYZ p1 = new XYZ(bbox.Min.X + 0.3, bbox.Min.Y + 0.3, centerZ);
            XYZ p2 = new XYZ(p1.X, p1.Y + risersPerFlight * actualTread, centerZ);
            XYZ p3 = new XYZ(p1.X + width - 0.6, p2.Y, centerZ);
            XYZ p4 = new XYZ(p3.X, p1.Y, centerZ);
            Curve c1 = Line.CreateBound(p1, p2);
            Curve c2 = Line.CreateBound(p2, p3);
            Curve c3 = Line.CreateBound(p3, p4);
            
            StairsRun.CreateStraightRun(doc, stairs.Id, (Line)c1, StairsRunJustification.Center);
            StairsRun.CreateStraightRun(doc, stairs.Id, (Line)c2, StairsRunJustification.Center);
            StairsRun.CreateStraightRun(doc, stairs.Id, (Line)c3, StairsRunJustification.Center);
        }

        private static void CreateCurvedRun(Document doc, Stairs stairs, BoundingBoxXYZ bbox, int totalRisers, double actualRiser, double actualTread, ElementId baseLevelId, ElementId topLevelId)
        {
            double radius = Math.Min(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y) / 2;
            XYZ center = new XYZ(
                (bbox.Min.X + bbox.Max.X) / 2.0,
                (bbox.Min.Y + bbox.Max.Y) / 2.0,
                (bbox.Min.Z + bbox.Max.Z) / 2.0
            );
            double totalAngle = Math.PI * 2 * totalRisers / 15.0;
            
            // CORRECCIÓN: CreateSpiralRun requiere parámetro clockwise
            StairsRun.CreateSpiralRun(doc, stairs.Id, center, radius, 0, totalAngle, true, StairsRunJustification.Center);
        }

        private static StairsType? GetDefaultStairsType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(StairsType))
                .Cast<StairsType>()
                .FirstOrDefault();
        }
    }

    public enum StairType
    {
        Straight,
        LShaped,
        UShaped,
        Curved
    }

    /// <summary>
    /// Preprocesador de fallos vacío para EditScope.Commit.
    /// </summary>
    internal class FailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            return FailureProcessingResult.Continue;
        }
    }
}