using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace ZBIMCopilot
{
    public class TopographyHandler : IExternalEventHandler
    {
        private List<XYZ> _pendingPoints = new List<XYZ>();
        private List<XYZ>? _pendingContour = null;   // contorno opcional
        private readonly object _lock = new object();

        /// <summary>
        /// Recibe los puntos de relieve y, opcionalmente, el contorno del polígono.
        /// </summary>
        public void SetPointsAndRaise(List<XYZ> points, List<XYZ>? contourPoints = null)
        {
            lock (_lock)
            {
                _pendingPoints = new List<XYZ>(points);
                _pendingContour = contourPoints != null ? new List<XYZ>(contourPoints) : null;
            }
            ZBIMApp.TopographyEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            List<XYZ> rawPoints;
            List<XYZ>? rawContour;
            lock (_lock)
            {
                if (_pendingPoints.Count == 0) return;
                rawPoints = new List<XYZ>(_pendingPoints);
                rawContour = _pendingContour != null ? new List<XYZ>(_pendingContour) : null;
                _pendingPoints.Clear();
                _pendingContour = null;
            }

            UIDocument? uidoc = app.ActiveUIDocument;
            Document? doc = uidoc?.Document;
            if (doc == null) return;

            try
            {
                // 1. Trasladar al origen local (evita la regla de las 20 millas)
                double avgX = rawPoints.Average(p => p.X);
                double avgY = rawPoints.Average(p => p.Y);
                double avgZ = rawPoints.Average(p => p.Z);
                XYZ centroid = new XYZ(avgX, avgY, avgZ);

                List<XYZ> projectedPoints = new List<XYZ>();
                foreach (var p in rawPoints)
                {
                    double x = UnitUtils.ConvertToInternalUnits(p.X - centroid.X, UnitTypeId.Meters);
                    double y = UnitUtils.ConvertToInternalUnits(p.Y - centroid.Y, UnitTypeId.Meters);
                    double z = UnitUtils.ConvertToInternalUnits(p.Z - centroid.Z, UnitTypeId.Meters);
                    projectedPoints.Add(new XYZ(x, y, z));
                }

                // 2. Filtrar puntos con XY duplicados (tolerancia ~3 mm)
                List<XYZ> cleanedPoints = projectedPoints
                    .GroupBy(p => new { X = Math.Round(p.X, 2), Y = Math.Round(p.Y, 2) })
                    .Select(g => g.First())
                    .ToList();

                if (cleanedPoints.Count < 3)
                {
                    TaskDialog.Show("Error", "Se necesitan al menos 3 puntos únicos para la topografía.");
                    return;
                }

                // 3. Obtener recursos del documento
                ElementId levelId = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstElementId();
                if (levelId == ElementId.InvalidElementId)
                    levelId = Level.Create(doc, 0.0).Id;

                Level level = doc.GetElement(levelId) as Level;
                double baseElevation = level?.Elevation ?? 0.0;

                ElementId topoTypeId = new FilteredElementCollector(doc)
                    .OfClass(typeof(ToposolidType))
                    .FirstElementId();
                if (topoTypeId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("Error", "No se encontró un tipo de Toposolid en la plantilla.");
                    return;
                }

                // 4. Procesar contorno (si existe)
                List<XYZ>? projectedContour = null;
                if (rawContour != null && rawContour.Count >= 3)
                {
                    projectedContour = new List<XYZ>();
                    foreach (var cp in rawContour)
                    {
                        double x = UnitUtils.ConvertToInternalUnits(cp.X - centroid.X, UnitTypeId.Meters);
                        double y = UnitUtils.ConvertToInternalUnits(cp.Y - centroid.Y, UnitTypeId.Meters);
                        // Z del contorno: se coloca a la elevación base (0 relativo)
                        double z = UnitUtils.ConvertToInternalUnits(0.0, UnitTypeId.Meters);
                        projectedContour.Add(new XYZ(x, y, z));
                    }
                }

                // 5. Transacción: crear Toposolid y dibujar contorno
                using (Transaction t = new Transaction(doc, "Crear Topografía y contorno"))
                {
                    t.Start();

                    // Crear Toposolid con los puntos, el tipo y el nivel
                    Toposolid toposolid = Toposolid.Create(doc, cleanedPoints, topoTypeId, levelId);

                    // Dibujar contorno como líneas de modelo si existe
                    if (projectedContour != null && projectedContour.Count >= 3)
                    {
                        // Crear un SketchPlane en el plano horizontal a la elevación base
                        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ,
                            new XYZ(0, 0, baseElevation));
                        SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                        for (int i = 0; i < projectedContour.Count; i++)
                        {
                            XYZ start = projectedContour[i];
                            XYZ end = projectedContour[(i + 1) % projectedContour.Count]; // cerrar polígono
                            if (start.DistanceTo(end) > 0.001) // evitar líneas de longitud cero
                            {
                                Line line = Line.CreateBound(start, end);
                                doc.Create.NewModelCurve(line, sketchPlane);
                            }
                        }
                    }

                    t.Commit();
                }

                string msg = $"Toposolid creado con {cleanedPoints.Count} puntos únicos.";
                if (projectedContour != null)
                    msg += $" Contorno dibujado con {projectedContour.Count} vértices.";
                TaskDialog.Show("Éxito", msg);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error en TopographyHandler", ex.ToString());
            }
        }

        public string GetName() => "ZBIM_TopographyHandler";
    }
}