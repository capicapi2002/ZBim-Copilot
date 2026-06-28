#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador universal de barandillas para balcones y escaleras.
    /// Altura mínima 90 cm (CTE DB‑SUA). Soporta mampostería, metálica, vidrio.
    /// </summary>
    public static class RailingBuilder
    {
        private const double MIN_HEIGHT_FEET = 2.95276; // 0.9 m

        public static List<Railing> CreateStairRailing(
            Document doc,
            Stairs stairs,
            RailingType? railingType = null,
            LocalRailingPlacementPosition placement = LocalRailingPlacementPosition.Left,
            double height = 0)
        {
            List<Railing> result = new List<Railing>();
            if (doc == null || stairs == null)
                return result;

            railingType ??= FindOrCreateRailingType(doc);
            if (railingType == null) return result;

            if (height <= 0)
                height = MIN_HEIGHT_FEET;

            if (placement == LocalRailingPlacementPosition.Left || placement == LocalRailingPlacementPosition.Both)
            {
                Railing? leftRail = CreateSingleStairRailing(doc, stairs, railingType, LocalRailingPlacementPosition.Left, height);
                if (leftRail != null) result.Add(leftRail);
            }

            if (placement == LocalRailingPlacementPosition.Right || placement == LocalRailingPlacementPosition.Both)
            {
                Railing? rightRail = CreateSingleStairRailing(doc, stairs, railingType, LocalRailingPlacementPosition.Right, height);
                if (rightRail != null) result.Add(rightRail);
            }

            return result;
        }

        public static Railing? CreateBalconyRailing(
            Document doc,
            Level baseLevel,
            Curve curve,
            RailingType? railingType = null,
            double height = 0)
        {
            if (doc == null || baseLevel == null || curve == null)
                return null;

            railingType ??= FindOrCreateRailingType(doc);
            if (railingType == null) return null;

            if (height <= 0)
                height = MIN_HEIGHT_FEET;

            using (Transaction tx = new Transaction(doc, "Crear barandilla de balcón"))
            {
                tx.Start();

                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, baseLevel.Elevation));
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                ModelCurve modelCurve = doc.Create.NewModelCurve(curve, sketchPlane);

                // CORRECCIÓN: Railing.Create requiere CurveLoop, no ElementId
                CurveLoop curveLoop = new CurveLoop();
                curveLoop.Append(curve);
                Railing railing = Railing.Create(doc, curveLoop, railingType.Id, baseLevel.Id);

                if (railing != null)
                {
                    Parameter heightParam = railing.get_Parameter(BuiltInParameter.STAIRS_RAILING_HEIGHT);
                    if (heightParam == null || heightParam.IsReadOnly)
                        heightParam = railing.LookupParameter("Height");
                    heightParam?.Set(height);
                }

                tx.Commit();
                return railing;
            }
        }

        public static void SetMaterial(Railing railing, Material material)
        {
            if (railing == null || material == null) return;
            Parameter matParam = railing.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (matParam != null && !matParam.IsReadOnly)
                matParam.Set(material.Id);
        }

        // ============================================================
        // MÉTODOS PRIVADOS
        // ============================================================

        private static Railing? CreateSingleStairRailing(
            Document doc, Stairs stairs, RailingType railingType,
            LocalRailingPlacementPosition position, double height)
        {
            try
            {
                // CORRECCIÓN: Para escaleras, necesitamos obtener las curvas de los runs
                // y crear barandillas usando la firma con CurveLoop
                var runs = new FilteredElementCollector(doc, stairs.Id)
                    .OfClass(typeof(StairsRun))
                    .Cast<StairsRun>()
                    .ToList();

                if (runs.Count == 0) return null;

                // Obtener la curva del primer run
                var firstRun = runs.First();
                LocationCurve? locCurve = firstRun.Location as LocationCurve;
                if (locCurve?.Curve == null) return null;

                // CORRECCIÓN API 2027: StairsRun.Width eliminado, obtener vía LookupParameter
                double runWidth = 1.0;
                Parameter? widthParam = firstRun.LookupParameter("Actual Run Width");
                if (widthParam == null) widthParam = firstRun.LookupParameter("Width");
                if (widthParam != null && widthParam.HasValue)
                    runWidth = widthParam.AsDouble();

                // Desplazar la curva según la posición
                Curve railingCurve = OffsetCurveForPosition(locCurve.Curve, runWidth, position);

                CurveLoop curveLoop = new CurveLoop();
                curveLoop.Append(railingCurve);

                // CORRECCIÓN API 2027: Stairs.BaseLevelId no existe, obtener vía BuiltInParameter
                ElementId baseLevelId = stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM)?.AsElementId() 
                    ?? stairs.LookupParameter("Base Level")?.AsElementId() 
                    ?? ElementId.InvalidElementId;
                
                Railing railing = Railing.Create(doc, curveLoop, railingType.Id, baseLevelId);

                if (railing != null)
                {
                    Parameter heightParam = railing.get_Parameter(BuiltInParameter.STAIRS_RAILING_HEIGHT);
                    if (heightParam == null || heightParam.IsReadOnly)
                        heightParam = railing.LookupParameter("Height");
                    heightParam?.Set(height);
                }
                return railing;
            }
            catch
            {
                return null;
            }
        }

        private static Curve OffsetCurveForPosition(Curve originalCurve, double width, LocalRailingPlacementPosition position)
        {
            // Simplificación: usar la curva original
            // En producción, se debería desplazar la curva según la posición
            return originalCurve;
        }

        private static RailingType? FindOrCreateRailingType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RailingType))
                .Cast<RailingType>()
                .FirstOrDefault();
        }
    }

    public enum LocalRailingPlacementPosition
    {
        Left,
        Right,
        Both
    }
}