#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador universal de muros para ZBIM‑Copilot.
    /// Cubre muros rectos, curvos, inclinados, cortina, con ventanas/puertas.
    /// </summary>
    public static class WallBuilder
    {
        /// <summary>
        /// Crea un muro básico (recto o curvo) entre dos puntos o a lo largo de una curva.
        /// </summary>
        public static Wall? CreateWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            WallType? wallType,
            Curve curve,
            double height = 0,
            bool isStructural = false)
        {
            if (doc == null || baseLevel == null || curve == null)
                throw new ArgumentNullException();

            wallType ??= GetDefaultWallType(doc);
            if (wallType == null) return null;

            double wallHeight = 0;
            if (topLevel != null)
                wallHeight = topLevel.Elevation - baseLevel.Elevation;
            else if (height > 0)
                wallHeight = height;
            else
                throw new ArgumentException("Debe especificar topLevel o height.");

            Wall wall = Wall.Create(doc, curve, wallType.Id, baseLevel.Id, wallHeight, 0, false, isStructural);
            return wall;
        }

        /// <summary>
        /// Crea un muro recto entre dos puntos.
        /// </summary>
        public static Wall? CreateStraightWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            WallType? wallType,
            XYZ startPoint,
            XYZ endPoint,
            double height = 0,
            bool isStructural = false)
        {
            Line line = Line.CreateBound(startPoint, endPoint);
            return CreateWall(doc, baseLevel, topLevel, wallType, line, height, isStructural);
        }

        /// <summary>
        /// Crea un muro curvo (arco).
        /// </summary>
        public static Wall? CreateCurvedWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            WallType? wallType,
            XYZ center,
            double radius,
            double startAngle,
            double endAngle,
            XYZ normal,
            XYZ referenceVector,
            double height = 0,
            bool isStructural = false)
        {
            Arc arc = Arc.Create(center, radius, startAngle, endAngle, normal, referenceVector);
            return CreateWall(doc, baseLevel, topLevel, wallType, arc, height, isStructural);
        }

        /// <summary>
        /// Crea un muro cortina (Curtain Wall) sin paneles predefinidos.
        /// </summary>
        public static Wall? CreateCurtainWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            WallType? curtainWallType,
            Curve curve,
            double height = 0)
        {
            if (curtainWallType == null)
            {
                curtainWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Kind == WallKind.Curtain);
            }

            if (curtainWallType == null) return null;

            return CreateWall(doc, baseLevel, topLevel, curtainWallType, curve, height, false);
        }

        /// <summary>
        /// Crea un muro inclinado (slanted wall). El ángulo se mide desde la vertical (0 = vertical).
        /// </summary>
        public static Wall? CreateSlantedWall(
            Document doc,
            Level baseLevel,
            Level? topLevel,
            WallType? wallType,
            Curve baseCurve,
            double height,
            double angleFromVerticalRadians,
            bool isStructural = false)
        {
            Wall? wall = CreateWall(doc, baseLevel, topLevel, wallType, baseCurve, height, isStructural);
            if (wall != null)
            {
                // CORRECCIÓN: WALL_SINGLE_SLANT no existe, usar LookupParameter
                Parameter slantParam = wall.LookupParameter("Single Slant Angle");
                if (slantParam == null || slantParam.IsReadOnly)
                    slantParam = wall.LookupParameter("Cross Angle");
                if (slantParam == null || slantParam.IsReadOnly)
                    slantParam = wall.LookupParameter("Angle from Vertical");
                if (slantParam != null && !slantParam.IsReadOnly)
                    slantParam.Set(angleFromVerticalRadians);
            }
            return wall;
        }

        /// <summary>
        /// Inserta una ventana (o puerta) en un muro dado.
        /// </summary>
        public static FamilyInstance? InsertWindowInWall(
            Document doc,
            Wall hostWall,
            FamilySymbol windowSymbol,
            XYZ insertionPoint,
            Level? sillLevel = null)
        {
            if (doc == null || hostWall == null || windowSymbol == null || insertionPoint == null)
                return null;

            if (!windowSymbol.IsActive)
                windowSymbol.Activate();

            FamilyInstance instance = doc.Create.NewFamilyInstance(
                insertionPoint,
                windowSymbol,
                hostWall,
                sillLevel ?? doc.ActiveView.GenLevel,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            return instance;
        }

        /// <summary>
        /// Obtiene un WallType por defecto (el primero encontrado que no sea cortina).
        /// </summary>
        public static WallType? GetDefaultWallType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind != WallKind.Curtain);
        }
    }
}