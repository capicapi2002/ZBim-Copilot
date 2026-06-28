#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador universal de cubiertas para ZBIM‑Copilot.
    /// Cubre: planas, inclinadas, bóvedas, diente de sierra, vidrio, etc.
    /// </summary>
    public static class RoofBuilder
    {
        /// <summary>
        /// Crea una cubierta plana o inclinada por huella (Footprint).
        /// </summary>
        public static FootPrintRoof? CreateFootprintRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            CurveArray curves,
            double slopeAngleRadians = 0,
            double overhang = 0)
        {
            if (doc == null || baseLevel == null || curves == null || curves.Size == 0)
                return null;

            roofType ??= GetDefaultRoofType(doc);
            if (roofType == null) return null;

            ModelCurveArray footPrintToModelCurveMapping = new ModelCurveArray();
            
            // CORRECCIÓN: doc.Create.NewFootPrintRoof requiere Level y RoofType, no sus IDs
            FootPrintRoof roof = doc.Create.NewFootPrintRoof(curves, baseLevel, roofType, out footPrintToModelCurveMapping);

            if (roof == null) return null;

            // Configurar pendiente usando parámetros de las curvas del perfil
            if (slopeAngleRadians > 0)
            {
                foreach (ModelCurve modelCurve in footPrintToModelCurveMapping)
                {
                    if (modelCurve is ModelCurve curve)
                    {
                        Parameter slopeParam = curve.get_Parameter(BuiltInParameter.CURVE_IS_SLOPE_DEFINING);
                        if (slopeParam == null || slopeParam.IsReadOnly)
                            slopeParam = curve.LookupParameter("Defines Slope");
                        if (slopeParam != null && !slopeParam.IsReadOnly)
                            slopeParam.Set(1);

                        Parameter angleParam = curve.get_Parameter(BuiltInParameter.ROOF_SLOPE);
                        if (angleParam == null || angleParam.IsReadOnly)
                            angleParam = curve.LookupParameter("Slope");
                        if (angleParam != null && !angleParam.IsReadOnly)
                            angleParam.Set(slopeAngleRadians);
                    }
                }
            }

            return roof;
        }

        /// <summary>
        /// Crea una cubierta extruida (bóveda de cañón, perfil personalizado).
        /// </summary>
        public static ExtrusionRoof? CreateExtrusionRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            CurveArray profileCurves,
            XYZ extrusionDirection,
            double extrusionLength)
        {
            if (doc == null || baseLevel == null || profileCurves == null || profileCurves.Size == 0)
                return null;

            roofType ??= GetDefaultRoofType(doc);
            if (roofType == null) return null;

            // Crear un plano de referencia para la extrusión
            Plane plane = Plane.CreateByNormalAndOrigin(
                extrusionDirection.CrossProduct(XYZ.BasisZ).Normalize(),
                new XYZ(0, 0, baseLevel.Elevation));

            ReferencePlane refPlane = doc.Create.NewReferencePlane(
                new XYZ(0, 0, baseLevel.Elevation),
                new XYZ(0, 0, baseLevel.Elevation + 1),
                extrusionDirection.CrossProduct(XYZ.BasisZ).Normalize(),
                doc.ActiveView);

            ExtrusionRoof roof = doc.Create.NewExtrusionRoof(
                profileCurves,
                refPlane,
                baseLevel,
                roofType,
                extrusionLength,
                extrusionLength);

            return roof;
        }

        /// <summary>
        /// Crea una cubierta a dos aguas simétrica.
        /// </summary>
        public static FootPrintRoof? CreateGableRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            CurveArray footprint,
            double ridgeAngleRadians,
            double overhang = 0)
        {
            return CreateFootprintRoof(doc, baseLevel, roofType, footprint, ridgeAngleRadians, overhang);
        }

        /// <summary>
        /// Crea una cubierta a cuatro aguas.
        /// </summary>
        public static FootPrintRoof? CreateHipRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            CurveArray footprint,
            double slopeAngleRadians,
            double overhang = 0)
        {
            return CreateFootprintRoof(doc, baseLevel, roofType, footprint, slopeAngleRadians, overhang);
        }

        /// <summary>
        /// Crea una cubierta tipo "diente de sierra" para naves industriales.
        /// </summary>
        public static List<ExtrusionRoof> CreateSawtoothRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            XYZ startPoint,
            double bayWidth,
            double bayDepth,
            double ridgeHeight,
            double eavesHeight,
            int numBays)
        {
            List<ExtrusionRoof> roofs = new List<ExtrusionRoof>();
            for (int i = 0; i < numBays; i++)
            {
                XYZ origin = startPoint + new XYZ(i * bayWidth, 0, 0);
                CurveArray profile = new CurveArray();
                XYZ p1 = new XYZ(0, 0, eavesHeight);
                XYZ p2 = new XYZ(bayWidth / 2, 0, ridgeHeight);
                XYZ p3 = new XYZ(bayWidth, 0, eavesHeight);
                profile.Append(Line.CreateBound(p1, p2));
                profile.Append(Line.CreateBound(p2, p3));
                profile.Append(Line.CreateBound(p3, p1));

                XYZ extrusionDir = new XYZ(0, 1, 0);
                ExtrusionRoof? roof = CreateExtrusionRoof(doc, baseLevel, roofType, profile, extrusionDir, bayDepth);
                if (roof != null) roofs.Add(roof);
            }
            return roofs;
        }

        /// <summary>
        /// Crea una cubierta de vidrio con estructura metálica.
        /// </summary>
        public static FootPrintRoof? CreateGlassRoof(
            Document doc,
            Level baseLevel,
            CurveArray footprint,
            double slopeAngleRadians = 0,
            double overhang = 0)
        {
            RoofType? glassRoofType = GetRoofTypeByName(doc, "Vidrio");
            if (glassRoofType == null)
            {
                RoofType? defaultType = GetDefaultRoofType(doc);
                if (defaultType != null)
                {
                    glassRoofType = defaultType.Duplicate("Vidrio") as RoofType;
                    Material? glassMat = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name.Equals("Glass", StringComparison.OrdinalIgnoreCase));
                    if (glassMat != null && glassRoofType != null)
                        glassRoofType.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM).Set(glassMat.Id);
                }
            }

            return CreateFootprintRoof(doc, baseLevel, glassRoofType, footprint, slopeAngleRadians, overhang);
        }

        /// <summary>
        /// Crea una bóveda de cañón mediante extrusión de un perfil curvo.
        /// </summary>
        public static ExtrusionRoof? CreateBarrelVaultRoof(
            Document doc,
            Level baseLevel,
            RoofType? roofType,
            XYZ startPoint,
            double spanWidth,
            double length,
            double riseHeight)
        {
            CurveArray profile = new CurveArray();
            double radius = (spanWidth * spanWidth) / (8 * riseHeight) + riseHeight / 2;
            XYZ center = new XYZ(startPoint.X + spanWidth / 2, startPoint.Y, startPoint.Z + riseHeight - radius);
            Arc arc = Arc.Create(center, radius, Math.PI, 0, XYZ.BasisX, XYZ.BasisZ);
            profile.Append(arc);

            XYZ extrusionDir = new XYZ(0, 1, 0);
            return CreateExtrusionRoof(doc, baseLevel, roofType, profile, extrusionDir, length);
        }

        // ============================================================
        // MÉTODOS AUXILIARES
        // ============================================================
        private static RoofType? GetDefaultRoofType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault();
        }

        private static RoofType? GetRoofTypeByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault(rt => rt.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}