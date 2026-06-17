#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using ZBIMCopilot.OAS;

namespace ZBIMCopilot.Execution
{
    public class Text2MblOrchestrator
    {
        public static Result Execute(OasProject project, UIApplication app)
        {
            if (project == null || app?.ActiveUIDocument?.Document == null)
            {
                ZBIMApp.OnServerStatus?.Invoke("❌ Error: Proyecto o Documento nulo.");
                return Result.Failed;
            }

            Document doc = app.ActiveUIDocument.Document;
            Action<string> log = msg => ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            using (Transaction tx = new Transaction(doc, "ZBIM: Generar Modelo (Fase C.5)"))
            {
                try
                {
                    tx.Start();
                    log("🏗️ Iniciando generación de modelo...");

                    var levelMap = new Dictionary<string, Level>();
                    
                    if (project.Buildings != null)
                    {
                        foreach (var building in project.Buildings)
                        {
                            if (building.Levels != null)
                            {
                                foreach (var levelData in building.Levels)
                                {
                                    var level = GetOrCreateLevel(doc, levelData.Name, levelData.Elevation, log);
                                    if (level != null && !levelMap.ContainsKey(levelData.Name))
                                    {
                                        levelMap[levelData.Name] = level;
                                    }
                                }
                            }
                        }
                    }

                    if (project.Buildings != null)
                    {
                        foreach (var building in project.Buildings)
                        {
                            if (building.Levels == null) continue;

                            foreach (var level in building.Levels)
                            {
                                if (level.Zones == null) continue;

                                foreach (var zone in level.Zones)
                                {
                                    if (zone.Spaces == null) continue;

                                    foreach (var space in zone.Spaces)
                                    {
                                        if (!levelMap.TryGetValue(level.Name, out Level? currentLevel) || currentLevel == null)
                                        {
                                            log($"⚠️ Nivel '{level.Name}' no encontrado para espacio '{space.Name}'");
                                            continue;
                                        }

                                        if (space.Stairs != null)
                                        {
                                            foreach (var stair in space.Stairs)
                                            {
                                                CreateStairAsDirectShape(doc, stair, levelMap, log);
                                            }
                                        }

                                        if (space.Fixtures != null)
                                        {
                                            foreach (var fixture in space.Fixtures)
                                            {
                                                CreateFamilyInstanceOrFallback(doc, fixture, BuiltInCategory.OST_PlumbingFixtures, currentLevel, log);
                                            }
                                        }

                                        if (space.Furniture != null)
                                        {
                                            foreach (var furniture in space.Furniture)
                                            {
                                                CreateFamilyInstanceOrFallback(doc, furniture, BuiltInCategory.OST_Furniture, currentLevel, log);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    tx.Commit();
                    log("✅ Generación de modelo completada exitosamente.");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    log($"❌ Error fatal durante la transacción: {ex.Message}");
                    return Result.Failed;
                }
            }
        }

        #region Lógica de Escaleras (DirectShape)

        private static void CreateStairAsDirectShape(Document doc, OasStair stair, Dictionary<string, Level> levelMap, Action<string> log)
        {
            try
            {
                if (!levelMap.TryGetValue(stair.BaseLevelName, out Level? baseLevel) || baseLevel == null ||
                    !levelMap.TryGetValue(stair.TopLevelName, out Level? topLevel) || topLevel == null)
                {
                    log($"⚠️ Niveles no encontrados para escalera '{stair.Name}'. Omitiendo.");
                    return;
                }

                if (stair.Origin == null || stair.Origin.Length < 2 || stair.EndPoint == null || stair.EndPoint.Length < 2)
                {
                    log($"⚠️ Coordenadas inválidas para escalera '{stair.Name}'. Omitiendo.");
                    return;
                }

                XYZ start = new XYZ(stair.Origin[0], stair.Origin[1], baseLevel.Elevation);
                XYZ end = new XYZ(stair.EndPoint[0], stair.EndPoint[1], topLevel.Elevation);
                
                double totalHeight = end.Z - start.Z;
                if (totalHeight <= 0) return;

                int numSteps = Math.Max(1, (int)Math.Round(totalHeight / 0.18));
                double stepHeight = totalHeight / numSteps;
                
                XYZ direction = end - start;
                double totalLength = direction.GetLength();
                XYZ stepVector = direction.Normalize() * (totalLength / numSteps);
                
                double width = stair.Width > 0 ? stair.Width : 1.2;
                double stepDepth = stepVector.GetLength();

                List<GeometryObject> solids = new List<GeometryObject>();

                for (int i = 0; i < numSteps; i++)
                {
                    XYZ currentPos = start + stepVector * i;
                    currentPos = new XYZ(currentPos.X, currentPos.Y, currentPos.Z + (stepHeight * i));
                    
                    XYZ min = new XYZ(currentPos.X - width / 2, currentPos.Y - stepDepth / 2, currentPos.Z);
                    XYZ max = new XYZ(currentPos.X + width / 2, currentPos.Y + stepDepth / 2, currentPos.Z + stepHeight);
                    
                    Solid? box = CreateBoxSolid(min, max);
                    if (box != null) solids.Add(box);
                }

                if (solids.Count > 0)
                {
                    DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_Stairs));
                    ds.Name = $"Escalera_{stair.Name}";
                    ds.SetShape(solids);
                    log($"🪜 Escalera creada: {stair.Name} con {numSteps} peldaños.");
                }
            }
            catch (Exception ex)
            {
                log($"❌ Error al crear escalera '{stair.Name}': {ex.Message}");
            }
        }

        private static Solid? CreateBoxSolid(XYZ min, XYZ max)
        {
            try
            {
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(new XYZ(min.X, min.Y, min.Z), new XYZ(max.X, min.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(min.X, max.Y, min.Z), new XYZ(min.X, min.Y, min.Z)));
                
                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, max.Z - min.Z);
            }
            catch { return null; }
        }

        #endregion

        #region Lógica de Familias y Fallback

        private static void CreateFamilyInstanceOrFallback(Document doc, dynamic item, BuiltInCategory category, Level targetLevel, Action<string> log)
        {
            try
            {
                string subType = item.SubType ?? "Generic";
                
                double x = item.Origin != null && item.Origin.Length > 0 ? item.Origin[0] : 0;
                double y = item.Origin != null && item.Origin.Length > 1 ? item.Origin[1] : 0;
                
                XYZ insertionPoint = new XYZ(x, y, targetLevel.Elevation);

                FamilySymbol? symbol = FindFamilySymbolBySubstring(doc, category, subType);

                if (symbol != null)
                {
                    if (!symbol.IsActive) symbol.Activate();
                    
                    var instance = doc.Create.NewFamilyInstance(
                        insertionPoint, 
                        symbol, 
                        StructuralType.NonStructural);
                    
                    if (item.Rotation != 0)
                    {
                        Line axis = Line.CreateBound(insertionPoint, new XYZ(insertionPoint.X, insertionPoint.Y, insertionPoint.Z + 1));
                        ElementTransformUtils.RotateElement(doc, instance.Id, axis, item.Rotation * Math.PI / 180);
                    }
                    
                    log($"✅ Instanciado: {subType} usando familia '{symbol.FamilyName}'");
                }
                else
                {
                    log($"⚠️ Familia para '{subType}' no encontrada. Usando fallback geométrico.");
                    CreateGenericFallbackBox(doc, item, insertionPoint, category, log);
                }
            }
            catch (Exception ex)
            {
                log($"❌ Error al procesar elemento '{item.SubType}': {ex.Message}");
            }
        }

        private static FamilySymbol? FindFamilySymbolBySubstring(Document doc, BuiltInCategory category, string subType)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            return collector.FirstOrDefault(fs => 
                (fs.FamilyName != null && fs.FamilyName.IndexOf(subType, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (fs.Name != null && fs.Name.IndexOf(subType, StringComparison.OrdinalIgnoreCase) >= 0)
            );
        }

        private static void CreateGenericFallbackBox(Document doc, dynamic item, XYZ basePoint, BuiltInCategory category, Action<string> log)
        {
            try
            {
                double width = 1.0, depth = 1.0, height = 1.0;
                
                if (item.Dimensions != null && item.Dimensions.Length >= 3)
                {
                    width = item.Dimensions[0] > 0 ? item.Dimensions[0] : width;
                    depth = item.Dimensions[1] > 0 ? item.Dimensions[1] : depth;
                    height = item.Dimensions[2] > 0 ? item.Dimensions[2] : height;
                }

                XYZ minPoint = new XYZ(basePoint.X - width / 2, basePoint.Y - depth / 2, basePoint.Z);
                XYZ maxPoint = new XYZ(basePoint.X + width / 2, basePoint.Y + depth / 2, basePoint.Z + height);
                
                Solid? boxSolid = CreateBoxSolid(minPoint, maxPoint);

                if (boxSolid != null)
                {
                    DirectShape ds = DirectShape.CreateElement(doc, new ElementId((int)category));
                    ds.Name = $"Fallback_{item.SubType ?? "Generic"}";
                    ds.SetShape(new GeometryObject[] { boxSolid });
                }
            }
            catch (Exception ex)
            {
                log($"❌ Error al crear fallback genérico: {ex.Message}");
            }
        }

        #endregion

        #region Helpers de Niveles

        private static Level? GetOrCreateLevel(Document doc, string name, double elevation, Action<string> log)
        {
            Level? level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (level == null)
            {
                level = Level.Create(doc, elevation);
                if (level != null)
                {
                    level.Name = name;
                    log($"📐 Nivel creado: {name} @ {elevation}m");
                }
            }
            return level;
        }

        #endregion
    }
}