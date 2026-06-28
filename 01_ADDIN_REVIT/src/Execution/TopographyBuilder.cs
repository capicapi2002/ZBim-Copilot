#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Generador de superficies topográficas a partir de puntos de elevación.
    /// [FASE E] Integración con datos de OpenTopography.
    /// NOTA: TopographySurface está marcado como obsoleto en Revit 2024+ 
    /// (recomendado: Toposolid), pero se mantiene por compatibilidad con la lógica existente.
    /// </summary>
    public static class TopographyBuilder
    {
        /// <summary>
        /// Crea una TopographySurface en el documento activo.
        /// </summary>
        /// <param name="doc">Documento de Revit.</param>
        /// <param name="points">Lista de puntos XYZ con elevación.</param>
        /// <returns>Elemento TopographySurface creado.</returns>
        public static TopographySurface CreateFromPoints(Document doc, List<XYZ> points)
        {
            if (points == null || points.Count < 3)
                throw new ArgumentException("Se necesitan al menos 3 puntos para crear una topografía.");

            using var transaction = new Transaction(doc, "Crear Topografía");
            transaction.Start();
            
            // CS0618: TopographySurface.Create está obsoleto en Revit 2024+ (recomendado: Toposolid).
            // Se suprime el warning localmente para mantener la funcionalidad existente.
            // TODO Fase futura: migrar a Toposolid.CreateByPoints() cuando se refactorice la lógica.
#pragma warning disable CS0618 // Type or member is obsolete
            TopographySurface surface = TopographySurface.Create(doc, points);
#pragma warning restore CS0618 // Type or member is obsolete
            
            transaction.Commit();
            return surface;
        }
    }
}