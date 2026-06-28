#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using ZBIMCopilot.OAS;

namespace ZBIMCopilot.Execution
{
    public class Text2MblOrchestrator
    {
        private const double M_TO_FT = 3.280839895;
        private const double MM_TO_FT = 1.0 / 304.8;
        private const double DEFAULT_WALL_HEIGHT_FT = 9.84;
        private const double DEFAULT_FLOOR_THICKNESS_FT = 0.3;
        private const double DEFAULT_RISER_FT = 0.18;
        private const double DEFAULT_TREAD_FT = 0.28;

        private readonly Dictionary<string, Level> _levelCache = new();
        private readonly Dictionary<string, WallType> _wallTypeCache = new();
        private readonly Dictionary<string, FloorType> _floorTypeCache = new();
        private readonly Dictionary<string, RoofType> _roofTypeCache = new();
        private readonly Dictionary<string, FamilySymbol> _familySymbolCache = new();

        private static readonly Dictionary<string, Dictionary<string, List<string>>> _mappingCache = new();
        private static readonly object _cacheLock = new();

        private Document _doc = null!;
        private UIApplication _uiApp = null!;
        private Action<string> _log = null!;

        // ========================================================================
        // CONSTRUCTORES
        // ========================================================================

        public Text2MblOrchestrator() { }

        public Text2MblOrchestrator(UIApplication app)
        {
            _uiApp = app ?? throw new ArgumentNullException(nameof(app));
            _doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No hay documento activo");
            _log = msg => ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        // ========================================================================
        // MÉTODO EXISTENTE: Execute (flujo OAS v2.0)
        // ========================================================================

        public Result Execute(OasProject project, UIApplication app)
        {
            if (project == null || app?.ActiveUIDocument?.Document == null)
            {
                ZBIMApp.OnServerStatus?.Invoke("❌ Error: Proyecto o Documento nulo.");
                return Result.Failed;
            }

            _doc = app.ActiveUIDocument.Document;
            _uiApp = app;
            _log = msg => ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            string languageCode = GetLanguageCode();
            _log($"🌐 Idioma detectado: {languageCode}");

            var familyMapping = LoadFamilyMapping(languageCode, _log);
            _log($"📋 Mapeo de familias cargado: {familyMapping.Count} categorías.");

            _log("🏗️ Iniciando generación con motor OAS v2.0 (fusión A+B)");

            using (Transaction tx = new Transaction(_doc, "ZBIM: Generar Modelo OAS v2.0"))
            {
                try
                {
                    tx.Start();

                    DeleteAllLevels();

                    CreateLevels(project);
                    CreatePlanViews();
                    CreateSpaces(project);
                    CreateStairs(project);
                    CreateFurnitureAndFixtures(project, familyMapping);

                    tx.Commit();
                    _log("✅ Generación completada exitosamente.");

                    LogGenerationSummary();

                    ResetDefault3DView();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    _log($"❌ Error fatal: {ex.Message}");
                    return Result.Failed;
                }
            }
        }

        // ========================================================================
        // NUEVO MÉTODO: BuildFromLayout (flujo HybridProjectBuilder)
        // ========================================================================

        public void BuildFromLayout(ZBimCopilot.Knowledge.ProjectLayout layout)
        {
            if (layout?.Levels == null)
            {
                _log?.Invoke("❌ Layout o niveles nulos.");
                return;
            }

            if (_doc == null)
            {
                _log?.Invoke("❌ Documento no inicializado. Use el constructor con UIApplication.");
                return;
            }

            _log?.Invoke("🏗️ Iniciando generación desde ProjectLayout...");

            using (Transaction tx = new Transaction(_doc, "ZBIM: Generar desde Layout"))
            {
                try
                {
                    tx.Start();

                    DeleteAllLevels();

                    var sortedLevels = layout.Levels.OrderBy(l => l.Elevation).ToList();

                    foreach (var levelLayout in sortedLevels)
                    {
                        CreateLevelFromLayout(levelLayout);
                    }

                    CreatePlanViews();

                    for (int i = 0; i < sortedLevels.Count; i++)
                    {
                        var levelLayout = sortedLevels[i];
                        
                        var topLevel = (i < sortedLevels.Count - 1)
                            ? _levelCache.GetValueOrDefault(sortedLevels[i + 1].LevelName)
                            : null;

                        if (!_levelCache.TryGetValue(levelLayout.LevelName, out Level? currentLevel) || currentLevel == null)
                        {
                            _log?.Invoke($"⚠️ Nivel '{levelLayout.LevelName}' no encontrado en caché. Saltando.");
                            continue;
                        }

                        CreateFloorFromLayout(levelLayout, currentLevel);

                        if (topLevel == null)
                        {
                            CreateRoofFromLayout(levelLayout, currentLevel);
                        }

                        if (levelLayout.Spaces != null)
                        {
                            foreach (var space in levelLayout.Spaces)
                            {
                                CreateWallsFromLayout(space, currentLevel, topLevel);
                            }
                        }

                        if (levelLayout.Stair != null)
                        {
                            CreateStairFromLayout(levelLayout.Stair, currentLevel, topLevel);
                        }

                        if (levelLayout.Elevator != null)
                        {
                            CreateElevatorFromLayout(levelLayout.Elevator, currentLevel, topLevel);
                        }
                    }

                    tx.Commit();
                    _log?.Invoke("✅ Generación desde Layout completada.");

                    LogGenerationSummary();
                    ResetDefault3DView();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    _log?.Invoke($"❌ Error fatal en BuildFromLayout: {ex.Message}");
                }
            }
        }

        // ========================================================================
        // MÉTODOS AUXILIARES PARA BuildFromLayout (CORREGIDOS - SIN CONVERSIÓN)
        // ========================================================================

        private void CreateLevelFromLayout(ZBimCopilot.Knowledge.LevelLayout levelLayout)
        {
            try
            {
                // CORRECCIÓN: Elevation ya está en pies, no multiplicar
                double elevationFt = levelLayout.Elevation;

                Level level = Level.Create(_doc, elevationFt);
                level.Name = levelLayout.LevelName;
                _levelCache[levelLayout.LevelName] = level;
                _log?.Invoke($"📐 Nivel creado: {levelLayout.LevelName} @ {elevationFt:F2} ft");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error creando nivel '{levelLayout.LevelName}': {ex.Message}");
            }
        }

        private void CreateFloorFromLayout(ZBimCopilot.Knowledge.LevelLayout levelLayout, Level level)
        {
            if (levelLayout.Spaces == null || levelLayout.Spaces.Count == 0)
            {
                _log?.Invoke($"⚠️ No hay espacios en el nivel '{levelLayout.LevelName}'. Omitiendo losa.");
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var space in levelLayout.Spaces)
            {
                if (space.Origin == null || space.Dimensions == null) continue;

                // CORRECCIÓN: Coordenadas ya están en pies, no multiplicar
                double ox = space.Origin.X;
                double oy = space.Origin.Y;
                double w = space.Dimensions.X;
                double d = space.Dimensions.Y;

                minX = Math.Min(minX, ox);
                minY = Math.Min(minY, oy);
                maxX = Math.Max(maxX, ox + w);
                maxY = Math.Max(maxY, oy + d);
            }

            if (minX >= maxX || minY >= maxY)
            {
                _log?.Invoke($"⚠️ Bounding box inválido para nivel '{levelLayout.LevelName}'. Omitiendo losa.");
                return;
            }

            FloorType? floorType = GetDefaultFloorType();
            if (floorType == null)
            {
                _log?.Invoke("❌ No se encontró ningún tipo de losa.");
                return;
            }

            try
            {
                CurveLoop profile = CreateRectangularProfile(minX, minY, maxX, maxY, level.Elevation);
                Floor floor = Floor.Create(_doc, new List<CurveLoop> { profile }, floorType.Id, level.Id);
                
                Parameter? thickParam = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (thickParam != null && !thickParam.IsReadOnly)
                    thickParam.Set(DEFAULT_FLOOR_THICKNESS_FT);

                _log?.Invoke($"🏗️ Losa creada: {levelLayout.LevelName} ({(maxX - minX) * 304.8:F0} x {(maxY - minY) * 304.8:F0} mm)");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error creando losa para '{levelLayout.LevelName}': {ex.Message}");
            }
        }

        private void CreateRoofFromLayout(ZBimCopilot.Knowledge.LevelLayout levelLayout, Level level)
        {
            if (levelLayout.Spaces == null || levelLayout.Spaces.Count == 0) return;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var space in levelLayout.Spaces)
            {
                if (space.Origin == null || space.Dimensions == null) continue;

                // CORRECCIÓN: Coordenadas ya están en pies, no multiplicar
                double ox = space.Origin.X;
                double oy = space.Origin.Y;
                double w = space.Dimensions.X;
                double d = space.Dimensions.Y;

                minX = Math.Min(minX, ox);
                minY = Math.Min(minY, oy);
                maxX = Math.Max(maxX, ox + w);
                maxY = Math.Max(maxY, oy + d);
            }

            if (minX >= maxX || minY >= maxY) return;

            RoofType? roofType = GetDefaultRoofType();
            if (roofType != null)
            {
                try
                {
                    CurveLoop profile = CreateRectangularProfile(minX, minY, maxX, maxY, level.Elevation);
                    Floor roof = Floor.Create(_doc, new List<CurveLoop> { profile }, roofType.Id, level.Id);
                    _log?.Invoke($"🏠 Techo creado: {levelLayout.LevelName} ({(maxX - minX) * 304.8:F0} x {(maxY - minY) * 304.8:F0} mm)");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"❌ Error creando techo: {ex.Message}");
                }
            }
            else
            {
                CreateFloorFromLayout(levelLayout, level);
                _log?.Invoke($"🏠 Techo (fallback losa) creado: {levelLayout.LevelName}");
            }
        }

        private void CreateWallsFromLayout(ZBimCopilot.Knowledge.OasSpace space, Level baseLevel, Level? topLevel)
        {
            if (space.Origin == null || space.Dimensions == null) return;

            // CORRECCIÓN: Coordenadas ya están en pies, no multiplicar
            double ox = space.Origin.X;
            double oy = space.Origin.Y;
            double w = space.Dimensions.X;
            double d = space.Dimensions.Y;

            WallType? wallType = GetDefaultWallType();
            if (wallType == null)
            {
                _log?.Invoke("❌ No se encontró tipo de muro.");
                return;
            }

            double height = DEFAULT_WALL_HEIGHT_FT;
            if (topLevel != null)
            {
                height = topLevel.Elevation - baseLevel.Elevation;
                if (height <= 0) height = DEFAULT_WALL_HEIGHT_FT;
            }

            XYZ p1 = new XYZ(ox, oy, 0);
            XYZ p2 = new XYZ(ox + w, oy, 0);
            XYZ p3 = new XYZ(ox + w, oy + d, 0);
            XYZ p4 = new XYZ(ox, oy + d, 0);

            CreateWall(p1, p2, baseLevel, height, wallType);
            CreateWall(p2, p3, baseLevel, height, wallType);
            CreateWall(p3, p4, baseLevel, height, wallType);
            CreateWall(p4, p1, baseLevel, height, wallType);
        }

        private void CreateStairFromLayout(ZBimCopilot.Knowledge.OasStair stair, Level baseLevel, Level? topLevel)
        {
            if (stair.Origin == null || stair.Dimensions == null) return;

            // CORRECCIÓN: Coordenadas ya están en pies, no multiplicar
            double ox = stair.Origin.X;
            double oy = stair.Origin.Y;
            double w = stair.Dimensions.X;
            double d = stair.Dimensions.Y;

            double height = DEFAULT_WALL_HEIGHT_FT;
            if (topLevel != null)
            {
                height = topLevel.Elevation - baseLevel.Elevation;
                if (height <= 0) height = DEFAULT_WALL_HEIGHT_FT;
            }

            try
            {
                // CORRECCIÓN: Reutilizar lógica de peldaños en lugar de caja opaca
                int numRisers = (int)Math.Ceiling(height / DEFAULT_RISER_FT);
                if (numRisers <= 0) numRisers = 1;

                List<Solid> solids = new();
                
                // Ajustar escalera para que quepa dentro de las dimensiones del núcleo
                double availableRun = d;
                double adjustedTread = availableRun / numRisers;
                if (adjustedTread < DEFAULT_TREAD_FT) adjustedTread = DEFAULT_TREAD_FT;

                if (numRisers <= 20)
                {
                    BuildSingleFlight(solids, w, adjustedTread, DEFAULT_RISER_FT, numRisers, baseLevel.Elevation, ox, oy);
                }
                else
                {
                    int first = (int)Math.Ceiling(numRisers / 2.0);
                    int second = numRisers - first;
                    BuildTwoFlight(solids, w, adjustedTread, DEFAULT_RISER_FT, first, second, baseLevel.Elevation, ox, oy);
                }

                if (solids.Count > 0)
                {
                    DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_Stairs));
                    ds.Name = $"Stair_{baseLevel.Name}_{topLevel?.Name ?? "Top"}";
                    ds.SetShape(solids.Cast<GeometryObject>().ToList());
                    _log?.Invoke($"🪜 Escalera creada: {baseLevel.Name} ({numRisers} peldaños, {w * 304.8:F0}x{d * 304.8:F0}mm)");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error creando escalera: {ex.Message}");
            }
        }

        private void CreateElevatorFromLayout(ZBimCopilot.Knowledge.OasElevatorCore elevator, Level baseLevel, Level? topLevel)
        {
            if (elevator.Origin == null || elevator.Dimensions == null) return;

            // CORRECCIÓN: Coordenadas ya están en pies, no multiplicar
            double ox = elevator.Origin.X;
            double oy = elevator.Origin.Y;
            double w = elevator.Dimensions.X;
            double d = elevator.Dimensions.Y;

            double height = DEFAULT_WALL_HEIGHT_FT;
            if (topLevel != null)
            {
                height = topLevel.Elevation - baseLevel.Elevation;
                if (height <= 0) height = DEFAULT_WALL_HEIGHT_FT;
            }

            try
            {
                XYZ min = new XYZ(ox, oy, baseLevel.Elevation);
                XYZ max = new XYZ(ox + w, oy + d, baseLevel.Elevation + height);

                Solid? box = CreateBox(min, max);
                if (box != null)
                {
                    DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    ds.Name = $"Elevator_{baseLevel.Name}";
                    ds.SetShape(new GeometryObject[] { box });
                    _log?.Invoke($"🛗 Ascensor creado: {baseLevel.Name} ({w * 304.8:F0}x{d * 304.8:F0}mm, altura: {height * 304.8:F0}mm)");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error creando ascensor: {ex.Message}");
            }
        }

        // ========================================================================
        // MÉTODOS AUXILIARES EXISTENTES (CORREGIDOS - TIPOS OAS CUALIFICADOS)
        // ========================================================================

        private void DeleteAllLevels()
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            foreach (var level in levels)
            {
                try
                {
                    _doc.Delete(level.Id);
                    _log?.Invoke($"🗑️ Nivel eliminado: {level.Name}");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"⚠️ No se pudo eliminar nivel '{level.Name}': {ex.Message}");
                }
            }
        }

        private void CreateLevels(OasProject project)
        {
            if (project.Buildings == null) return;
            foreach (var building in project.Buildings)
            {
                if (building.Levels == null) continue;
                foreach (var levelData in building.Levels)
                {
                    string name = levelData.Name;
                    double elevationFt = levelData.Elevation * M_TO_FT;

                    Level level = Level.Create(_doc, elevationFt);
                    level.Name = name;
                    _levelCache[name] = level;
                    _log?.Invoke($"📐 Nivel creado: {name} @ {elevationFt:F2} ft");
                }
            }
        }

        private void CreatePlanViews()
        {
            ViewFamilyType? viewFamType = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

            if (viewFamType == null)
            {
                _log?.Invoke("⚠️ No se encontró ViewFamilyType de planta. No se crearán vistas.");
                return;
            }

            foreach (var kvp in _levelCache)
            {
                Level level = kvp.Value;
                var existingView = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => v.Name.Equals(level.Name, StringComparison.OrdinalIgnoreCase));
                if (existingView != null) continue;

                ViewPlan view = ViewPlan.Create(_doc, viewFamType.Id, level.Id);
                view.Name = level.Name;
                _log?.Invoke($"📄 Vista de planta creada: {view.Name}");
            }
        }

        private void CreateSpaces(OasProject project)
        {
            if (project.Buildings == null) return;
            foreach (var building in project.Buildings)
            {
                if (building.Levels == null) continue;
                var sortedLevels = building.Levels.OrderBy(l => l.Elevation).ToList();

                foreach (var levelData in sortedLevels)
                {
                    if (!_levelCache.TryGetValue(levelData.Name, out Level? currentLevel))
                    {
                        _log?.Invoke($"⚠️ Nivel '{levelData.Name}' no encontrado en caché. Saltando.");
                        continue;
                    }

                    int idx = sortedLevels.IndexOf(levelData);
                    Level? topLevel = (idx < sortedLevels.Count - 1)
                        ? _levelCache.GetValueOrDefault(sortedLevels[idx + 1].Name)
                        : null;

                    CreateFloorForLevel(levelData, currentLevel);

                    if (topLevel == null)
                        CreateRoofForLevel(levelData, currentLevel);

                    if (levelData.Zones != null)
                    {
                        foreach (var zone in levelData.Zones)
                        {
                            if (zone.Spaces == null) continue;
                            foreach (var space in zone.Spaces)
                            {
                                CreateWallsForSpace(space, currentLevel, topLevel);
                            }
                        }
                    }
                }
            }
        }

        private void CreateFloorForLevel(OasLevel levelData, Level level)
        {
            var (minX, minY, maxX, maxY) = ComputeBoundingBox(levelData);
            if (minX >= maxX || minY >= maxY)
            {
                _log?.Invoke($"⚠️ Bounding box inválido para nivel '{levelData.Name}'. Omitiendo losa.");
                return;
            }

            FloorType? floorType = GetDefaultFloorType();
            if (floorType == null)
            {
                _log?.Invoke("❌ No se encontró ningún tipo de losa.");
                return;
            }

            CurveLoop profile = CreateRectangularProfile(minX, minY, maxX, maxY, level.Elevation);
            Floor floor = Floor.Create(_doc, new List<CurveLoop> { profile }, floorType.Id, level.Id);
            Parameter? thickParam = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
            if (thickParam != null && !thickParam.IsReadOnly)
                thickParam.Set(DEFAULT_FLOOR_THICKNESS_FT);

            _log?.Invoke($"🏗️ Losa creada: {levelData.Name} ({(maxX - minX) * 304.8:F0} x {(maxY - minY) * 304.8:F0} mm)");
        }

        private void CreateRoofForLevel(OasLevel levelData, Level level)
        {
            var (minX, minY, maxX, maxY) = ComputeBoundingBox(levelData);
            if (minX >= maxX || minY >= maxY) return;

            RoofType? roofType = GetDefaultRoofType();
            if (roofType != null)
            {
                CurveLoop profile = CreateRectangularProfile(minX, minY, maxX, maxY, level.Elevation);
                Floor roof = Floor.Create(_doc, new List<CurveLoop> { profile }, roofType.Id, level.Id);
                _log?.Invoke($"🏠 Techo creado: {levelData.Name} ({(maxX - minX) * 304.8:F0} x {(maxY - minY) * 304.8:F0} mm)");
            }
            else
            {
                CreateFloorForLevel(levelData, level);
                _log?.Invoke($"🏠 Techo (fallback losa) creado: {levelData.Name}");
            }
        }

        private (double minX, double minY, double maxX, double maxY) ComputeBoundingBox(OasLevel levelData)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            if (levelData.Zones != null)
            {
                foreach (var zone in levelData.Zones)
                {
                    if (zone.Spaces == null) continue;
                    foreach (var space in zone.Spaces)
                    {
                        if (space.Origin == null || space.Origin.Length < 2 ||
                            space.Dimensions == null || space.Dimensions.Length < 2)
                            continue;
                        double ox = space.Origin[0] * M_TO_FT;
                        double oy = space.Origin[1] * M_TO_FT;
                        double w = space.Dimensions[0] * M_TO_FT;
                        double d = space.Dimensions[1] * M_TO_FT;

                        minX = Math.Min(minX, ox);
                        minY = Math.Min(minY, oy);
                        maxX = Math.Max(maxX, ox + w);
                        maxY = Math.Max(maxY, oy + d);
                    }
                }
            }

            return (minX, minY, maxX, maxY);
        }

        private CurveLoop CreateRectangularProfile(double minX, double minY, double maxX, double maxY, double elevation)
        {
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(minX, minY, elevation), new XYZ(maxX, minY, elevation)));
            loop.Append(Line.CreateBound(new XYZ(maxX, minY, elevation), new XYZ(maxX, maxY, elevation)));
            loop.Append(Line.CreateBound(new XYZ(maxX, maxY, elevation), new XYZ(minX, maxY, elevation)));
            loop.Append(Line.CreateBound(new XYZ(minX, maxY, elevation), new XYZ(minX, minY, elevation)));
            return loop;
        }

        private void CreateWallsForSpace(OasSpace space, Level baseLevel, Level? topLevel)
        {
            if (space.Origin == null || space.Dimensions == null) return;
            if (space.Origin.Length < 2 || space.Dimensions.Length < 2) return;

            double ox = space.Origin[0] * M_TO_FT;
            double oy = space.Origin[1] * M_TO_FT;
            double w = space.Dimensions[0] * M_TO_FT;
            double d = space.Dimensions[1] * M_TO_FT;

            WallType? wallType = GetDefaultWallType();
            if (wallType == null) return;

            double height = DEFAULT_WALL_HEIGHT_FT;
            if (topLevel != null)
            {
                height = topLevel.Elevation - baseLevel.Elevation;
                if (height <= 0) height = DEFAULT_WALL_HEIGHT_FT;
            }

            XYZ p1 = new XYZ(ox, oy, 0);
            XYZ p2 = new XYZ(ox + w, oy, 0);
            XYZ p3 = new XYZ(ox + w, oy + d, 0);
            XYZ p4 = new XYZ(ox, oy + d, 0);

            CreateWall(p1, p2, baseLevel, height, wallType);
            CreateWall(p2, p3, baseLevel, height, wallType);
            CreateWall(p3, p4, baseLevel, height, wallType);
            CreateWall(p4, p1, baseLevel, height, wallType);
        }

        private void CreateWall(XYZ start, XYZ end, Level baseLevel, double height, WallType wallType)
        {
            try
            {
                Line line = Line.CreateBound(start, end);
                Wall.Create(_doc, line, wallType.Id, baseLevel.Id, height, 0.0, false, false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error creando muro: {ex.Message}");
            }
        }

        private void CreateStairs(OasProject project)
        {
            if (project.Buildings == null) return;
            foreach (var building in project.Buildings)
            {
                if (building.Stairs == null) continue;
                foreach (var stair in building.Stairs)
                    CreateSingleStair(stair);
            }
        }

        private void CreateSingleStair(OasStair stair)
        {
            if (!_levelCache.TryGetValue(stair.BaseLevelName, out Level? baseLevel) ||
                !_levelCache.TryGetValue(stair.TopLevelName, out Level? topLevel))
            {
                _log?.Invoke($"⚠️ Niveles no encontrados para escalera {stair.Id}. Omitiendo.");
                return;
            }

            double height = topLevel.Elevation - baseLevel.Elevation;
            if (height <= 0) return;

            double riser = stair.RiserHeight > 0 ? stair.RiserHeight : 0.18;
            double tread = stair.TreadDepth > 0 ? stair.TreadDepth : 0.28;
            double width = stair.Width > 0 ? stair.Width * MM_TO_FT : 1.2;
            int numRisers = (int)Math.Ceiling(height / riser);

            List<Solid> solids = new();
            if (numRisers <= 20)
                BuildSingleFlight(solids, width, tread, riser, numRisers, baseLevel.Elevation);
            else
            {
                int first = (int)Math.Ceiling(numRisers / 2.0);
                int second = numRisers - first;
                BuildTwoFlight(solids, width, tread, riser, first, second, baseLevel.Elevation);
            }

            if (solids.Count > 0)
            {
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_Stairs));
                ds.Name = $"Stair_{stair.Id ?? baseLevel.Name}_{topLevel.Name}";
                ds.SetShape(solids.Cast<GeometryObject>().ToList());
                _log?.Invoke($"🪜 Escalera creada: {stair.Id} ({numRisers} peldaños)");
            }
        }

        private void BuildSingleFlight(List<Solid> solids, double width, double tread, double riser, int risers, double baseElev, double offsetX = 0, double offsetY = 0)
        {
            for (int i = 0; i < risers; i++)
            {
                double z = baseElev + i * riser;
                double x = offsetX + i * tread;
                Solid? box = CreateBox(new XYZ(x, offsetY - width / 2, z), new XYZ(x + tread, offsetY + width / 2, z + riser));
                if (box != null) solids.Add(box);
            }
        }

        private void BuildTwoFlight(List<Solid> solids, double width, double tread, double riser, int risers1, int risers2, double baseElev, double offsetX = 0, double offsetY = 0)
        {
            double run1 = (risers1 - 1) * tread;
            double landingWidth = 1.2;

            for (int i = 0; i < risers1; i++)
            {
                double z = baseElev + i * riser;
                double x = offsetX + i * tread;
                Solid? box = CreateBox(new XYZ(x, offsetY - width / 2, z), new XYZ(x + tread, offsetY + width / 2, z + riser));
                if (box != null) solids.Add(box);
            }

            Solid? landing = CreateBox(new XYZ(offsetX + run1, offsetY - width / 2, baseElev + risers1 * riser),
                                      new XYZ(offsetX + run1 + landingWidth, offsetY + width / 2, baseElev + risers1 * riser + riser));
            if (landing != null) solids.Add(landing);

            for (int i = 0; i < risers2; i++)
            {
                double z = baseElev + risers1 * riser + i * riser;
                double x = offsetX + run1 + landingWidth + (risers2 - 1 - i) * tread;
                Solid? box = CreateBox(new XYZ(x, offsetY - width / 2, z), new XYZ(x + tread, offsetY + width / 2, z + riser));
                if (box != null) solids.Add(box);
            }
        }

        private void CreateFurnitureAndFixtures(OasProject project, Dictionary<string, List<string>> familyMapping)
        {
            if (project.Buildings == null) return;
            foreach (var building in project.Buildings)
            {
                if (building.Levels == null) continue;
                foreach (var levelData in building.Levels)
                {
                    if (!_levelCache.TryGetValue(levelData.Name, out Level? level)) continue;
                    if (levelData.Zones == null) continue;

                    foreach (var zone in levelData.Zones)
                    {
                        if (zone.Spaces == null) continue;
                        foreach (var space in zone.Spaces)
                        {
                            if (space.Fixtures != null)
                                foreach (var fixture in space.Fixtures)
                                    PlaceFamilyInstance(fixture, BuiltInCategory.OST_PlumbingFixtures, level, familyMapping);
                            if (space.Furniture != null)
                                foreach (var furniture in space.Furniture)
                                    PlaceFamilyInstance(furniture, BuiltInCategory.OST_Furniture, level, familyMapping);
                        }
                    }
                }
            }
        }

        private void PlaceFamilyInstance(OasFixture fixture, BuiltInCategory cat, Level level, Dictionary<string, List<string>> familyMapping)
        {
            PlaceFamilyInstanceInternal(fixture.SubType, fixture.Origin, fixture.Rotation, fixture.Dimensions, cat, level, familyMapping);
        }

        private void PlaceFamilyInstance(OasFurniture furniture, BuiltInCategory cat, Level level, Dictionary<string, List<string>> familyMapping)
        {
            PlaceFamilyInstanceInternal(furniture.SubType, furniture.Origin, furniture.Rotation, furniture.Dimensions, cat, level, familyMapping);
        }

        private void PlaceFamilyInstanceInternal(string subType, double[] origin, double rotation, double[] dimensions, BuiltInCategory cat, Level level, Dictionary<string, List<string>> familyMapping)
        {
            try
            {
                string typeName = subType ?? "Generic";
                double x = origin != null && origin.Length > 0 ? origin[0] * MM_TO_FT : 0;
                double y = origin != null && origin.Length > 1 ? origin[1] * MM_TO_FT : 0;
                XYZ insertionPoint = new XYZ(x, y, level.Elevation);

                FamilySymbol? symbol = FindFamilySymbol(cat, typeName, familyMapping);
                if (symbol != null)
                {
                    if (!symbol.IsActive) symbol.Activate();
                    FamilyInstance instance = _doc.Create.NewFamilyInstance(insertionPoint, symbol, StructuralType.NonStructural);
                    if (rotation != 0)
                    {
                        Line axis = Line.CreateBound(insertionPoint, new XYZ(insertionPoint.X, insertionPoint.Y, insertionPoint.Z + 1));
                        ElementTransformUtils.RotateElement(_doc, instance.Id, axis, rotation * Math.PI / 180);
                    }
                    _log?.Invoke($"✅ Instanciado: {typeName} usando '{symbol.FamilyName}'");
                }
                else
                {
                    CreateFallbackBox(typeName, insertionPoint, dimensions, cat);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Error al colocar '{subType}': {ex.Message}");
            }
        }

        private FamilySymbol? FindFamilySymbol(BuiltInCategory cat, string subType, Dictionary<string, List<string>> familyMapping)
        {
            if (string.IsNullOrEmpty(subType)) return null;

            string cacheKey = $"{cat}_{subType}";
            if (_familySymbolCache.TryGetValue(cacheKey, out FamilySymbol? cached))
                return cached;

            var allSymbols = new FilteredElementCollector(_doc)
                .OfCategory(cat)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            if (allSymbols.Count == 0) return null;

            if (familyMapping.TryGetValue(subType, out var synonyms))
            {
                foreach (var syn in synonyms)
                {
                    var match = allSymbols.FirstOrDefault(fs => fs.Name != null &&
                        fs.Name.Equals(syn, StringComparison.OrdinalIgnoreCase));
                    if (match != null) { _familySymbolCache[cacheKey] = match; return match; }
                }
                foreach (var syn in synonyms)
                {
                    var match = allSymbols.FirstOrDefault(fs => fs.Name != null &&
                        fs.Name.IndexOf(syn, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) { _familySymbolCache[cacheKey] = match; return match; }
                }
            }

            var fallback = allSymbols.FirstOrDefault(fs => fs.Name != null &&
                fs.Name.IndexOf(subType, StringComparison.OrdinalIgnoreCase) >= 0);
            if (fallback != null) { _familySymbolCache[cacheKey] = fallback; return fallback; }

            var first = allSymbols.First();
            _familySymbolCache[cacheKey] = first;
            return first;
        }

        private void CreateFallbackBox(string subType, XYZ basePoint, double[] dimensions, BuiltInCategory cat)
        {
            double w = 0.6, d = 0.6, h = 0.9;
            if (dimensions != null && dimensions.Length >= 3)
            {
                w = dimensions[0] > 0 ? dimensions[0] * MM_TO_FT : w;
                d = dimensions[1] > 0 ? dimensions[1] * MM_TO_FT : d;
                h = dimensions[2] > 0 ? dimensions[2] * MM_TO_FT : h;
            }
            else
            {
                if (subType.Contains("Toilet", StringComparison.OrdinalIgnoreCase) || subType.Contains("Inodoro", StringComparison.OrdinalIgnoreCase))
                    (w, d, h) = (0.4, 0.6, 0.4);
                else if (subType.Contains("Sink", StringComparison.OrdinalIgnoreCase) || subType.Contains("Lavabo", StringComparison.OrdinalIgnoreCase))
                    (w, d, h) = (0.5, 0.4, 0.8);
                else if (subType.Contains("Shower", StringComparison.OrdinalIgnoreCase) || subType.Contains("Ducha", StringComparison.OrdinalIgnoreCase))
                    (w, d, h) = (0.9, 0.9, 2.0);
                else if (subType.Contains("Refrigerator", StringComparison.OrdinalIgnoreCase) || subType.Contains("Refrigerador", StringComparison.OrdinalIgnoreCase))
                    (w, d, h) = (0.7, 0.7, 1.8);
                else if (subType.Contains("Stove", StringComparison.OrdinalIgnoreCase) || subType.Contains("Cocina", StringComparison.OrdinalIgnoreCase))
                    (w, d, h) = (0.6, 0.6, 0.9);
            }

            Solid? box = CreateBox(basePoint + new XYZ(-w / 2, -d / 2, 0), basePoint + new XYZ(w / 2, d / 2, h));
            if (box != null)
            {
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId((int)cat));
                ds.Name = $"Fallback_{subType}";
                ds.SetShape(new GeometryObject[] { box });
                _log?.Invoke($"📦 Fallback: {subType} ({w * 304.8:F0}x{d * 304.8:F0}x{h * 304.8:F0}mm)");
            }
        }

        private WallType? GetDefaultWallType()
        {
            if (_wallTypeCache.TryGetValue("default", out var wt)) return wt;
            wt = new FilteredElementCollector(_doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault();
            if (wt != null) _wallTypeCache["default"] = wt;
            return wt;
        }

        private FloorType? GetDefaultFloorType()
        {
            if (_floorTypeCache.TryGetValue("default", out var ft)) return ft;
            ft = new FilteredElementCollector(_doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
            if (ft != null) _floorTypeCache["default"] = ft;
            return ft;
        }

        private RoofType? GetDefaultRoofType()
        {
            if (_roofTypeCache.TryGetValue("default", out var rt)) return rt;
            rt = new FilteredElementCollector(_doc).OfClass(typeof(RoofType)).Cast<RoofType>().FirstOrDefault();
            if (rt != null) _roofTypeCache["default"] = rt;
            return rt;
        }

        private static string GetLanguageCode()
        {
            try { string? code = CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName; if (!string.IsNullOrEmpty(code)) return code; } catch { }
            try { string? code = CultureInfo.InstalledUICulture?.TwoLetterISOLanguageName; if (!string.IsNullOrEmpty(code)) return code; } catch { }
            return "en";
        }

        private static Dictionary<string, List<string>> LoadFamilyMapping(string languageCode, Action<string> log)
        {
            lock (_cacheLock)
            {
                if (_mappingCache.TryGetValue(languageCode, out var cached))
                    return cached;
            }

            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? dllDir = Path.GetDirectoryName(dllPath);
            string mappingPath = Path.Combine(dllDir ?? "", $"family_mapping_{languageCode}.json");

            Dictionary<string, List<string>>? mapping = null;

            if (File.Exists(mappingPath))
            {
                try
                {
                    string json = File.ReadAllText(mappingPath);
                    mapping = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (mapping != null && mapping.Count > 0)
                    {
                        lock (_cacheLock) { _mappingCache[languageCode] = mapping; }
                        return mapping;
                    }
                }
                catch (Exception ex) { log($"⚠️ Error cargando mapeo: {ex.Message}. Creando por defecto."); }
            }

            log($"📝 Creando mapeo por defecto para {languageCode}...");
            mapping = GetDefaultMapping(languageCode);
            try
            {
                string json = JsonSerializer.Serialize(mapping, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(mappingPath, json);
            }
            catch { }

            lock (_cacheLock) { _mappingCache[languageCode] = mapping; }
            return mapping;
        }

        private static Dictionary<string, List<string>> GetDefaultMapping(string languageCode)
        {
            var baseSynonyms = new Dictionary<string, List<string>>
            {
                ["Toilet"] = new() { "toilet", "wc", "water closet" },
                ["Sink"] = new() { "sink", "lavatory", "washbasin", "under mount sink" },
                ["Shower"] = new() { "shower", "shower tray" },
                ["Bathtub"] = new() { "bathtub", "tub" },
                ["Bed"] = new() { "bed", "divan", "hospital bed" },
                ["Refrigerator"] = new() { "refrigerator", "fridge" },
                ["Dishwasher"] = new() { "dishwasher" },
                ["Oven"] = new() { "oven", "microwave" },
                ["Stove"] = new() { "stove", "cooktop", "hob", "gas hob", "induction hob" },
                ["BaseCabinet"] = new() { "base cabinet", "lower cabinet" },
                ["WallCabinet"] = new() { "wall cabinet", "upper cabinet" },
                ["Countertop"] = new() { "countertop", "worktop" },
                ["Sofa"] = new() { "sofa", "couch", "armchair" },
                ["Table"] = new() { "table", "dining table", "coffee table" },
                ["Chair"] = new() { "chair", "stool", "office chair" },
                ["Wardrobe"] = new() { "wardrobe", "closet", "armoire" }
            };

            var esSynonyms = new Dictionary<string, List<string>>
            {
                ["Toilet"] = new() { "Inodoro con tanque alto-3D", "Inodoro suspendido-3D", "Combinación de WC", "Asiento de WC_2 con cisterna - Basado en muro" },
                ["Sink"] = new() { "Lavabo de baño", "Lavabo con encimera-Múltiple-3D", "s719_u765_under_mount_sink_370_370_43430800", "Kitchen_Sinks_Roca_871741D01-Praga-Stainless-steel-single-bowl kitchen sink" },
                ["Shower"] = new() { "Ducha", "Plato de ducha-3D", "Cubeta de ducha - Desagüe axial", "Cubeta de ducha - Desagüe en ángulo", "Cubeta de ducha - Esquina de compartimento" },
                ["Bathtub"] = new() { "Bañera-Rectangular con grifo-3D", "Bañera-De pie-3D", "Bañera-2D" },
                ["Bed"] = new() { "Cama - Diván", "Cama con respaldo de madera", "Cama-Cuadro", "Cama-Hospital", "Cama-Shaker" },
                ["Refrigerator"] = new() { "Refrigerador", "Kitchen_Appliances_Electrolux-Brasil_Refrigerator-French-Door-Connected-540L_Refrigerator French Door Connected 540L" },
                ["Dishwasher"] = new() { "Lavavajillas", "Kitchen_Built-In - Dishwasher_HAFELE_Florence", "Kitchen_Freestanding-Dishwasher_HAFELE_Hygiene" },
                ["Oven"] = new() { "Kitchen_Built-In-Microwave_HAFELE_ETNA", "Kitchen_Built-In-Microwave_HAFELE_ALBANO", "Kitchen_Appliances_Electrolux-Brasil_Electrolux-Efficient-23L-Stainless-Steel-Microwave-with-Assisted-Defrost-ME23S-ME23S" },
                ["Stove"] = new() { "Cocina encimera - 4 fuegos 2", "Cocinita - Media", "Cocinita - Pequeña", "Kitchen_Gas-Hob_HAFELE_Haco", "Kitchen_Induction-Hob_HAFELE_Lina", "Kitchen_Appliances_Electrolux_Electrolux-Gas-Hob-60-White" },
                ["BaseCabinet"] = new() { "Armario base-2 cajones", "Armario base-2 cubos", "Kitchen_Kitchen-Cabinets_Invita_Base-cabinet-60-cm-Alba-KU10-060", "Kitchen_Kitchen-Cabinets_Invita_Base-cabinet-100-cm-Athena-KU10-100" },
                ["WallCabinet"] = new() { "Armario superior - 1 puerta", "Armario superior - 2 puertas con cristal", "Armario superior - Con cristal", "Kitchen_Cabinets_Marbodal_Wall-cabinet-5210080-Aspekt", "Kitchen_Kitchen-Cabinets_Invita_Wall-cabinet-80-cm-Athena-KU1-080" },
                ["Countertop"] = new() { "Encimera con agujero para fregadero", "Encimera - Isla", "Encimera - Forma en L con agujero para fregadero 2", "Encimera con agujero para fregadero cuadrado", "Encimera con agujero para fregadero redondo", "Encimera de armario de baño con agujero para fregadero" },
                ["Sofa"] = new() { "Butaca de cine", "Silla - Diseño (1)", "Silla - Madera (1)", "Silla-Oficina (brazos)" },
                ["Table"] = new() { "Mesa", "Mesa - Cristal", "Mesa-Comedor redonda con sillas" },
                ["Chair"] = new() { "Silla - Diseño (1)", "Silla - Madera (1)", "Silla-Oficina (brazos)", "Taburete (2)" },
                ["Wardrobe"] = new() { "Armario - 4 cajones", "Armario alto-2 Puertas-Empotrado", "Armario alto-Una puerta (2)", "Armario dormitorio" }
            };

            var result = new Dictionary<string, List<string>>();
            foreach (var kvp in baseSynonyms)
            {
                var synonyms = new List<string>(kvp.Value);
                if (languageCode == "es" && esSynonyms.TryGetValue(kvp.Key, out var esList))
                    synonyms.AddRange(esList);
                result[kvp.Key] = synonyms.Distinct().ToList();
            }
            return result;
        }

        private Solid? CreateBox(XYZ min, XYZ max)
        {
            try
            {
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(new XYZ(min.X, min.Y, min.Z), new XYZ(max.X, min.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z)));
                loop.Append(Line.CreateBound(new XYZ(min.X, max.Y, min.Z), new XYZ(min.X, min.Y, min.Z)));
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, max.Z - min.Z);
            }
            catch { return null; }
        }

        private void LogGenerationSummary()
        {
            try
            {
                var allWalls = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .ToList();
                int wallCount = allWalls.Count;

                var allFloors = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Floor))
                    .Cast<Floor>()
                    .ToList();
                int floorCount = allFloors.Count;

                _log?.Invoke($"📊 RESUMEN FINAL: {wallCount} muros, {floorCount} losas/techos creados.");

                if (_levelCache.Values.Count > 0)
                {
                    _log?.Invoke("🔎 Primer espacio por nivel (si existe):");
                    foreach (var kvp in _levelCache.OrderBy(l => l.Value.Elevation))
                    {
                        var levelName = kvp.Key;
                        var floorsOnLevel = allFloors.Where(f => f.LevelId == kvp.Value.Id).ToList();
                        if (floorsOnLevel.Any())
                        {
                            var firstFloor = floorsOnLevel.First();
                            BoundingBoxXYZ bbox = firstFloor.get_BoundingBox(null);
                            if (bbox != null)
                            {
                                double w = (bbox.Max.X - bbox.Min.X) * 304.8;
                                double d = (bbox.Max.Y - bbox.Min.Y) * 304.8;
                                _log?.Invoke($"   ➤ {levelName}: losa bounding box: {w:F0}mm x {d:F0}mm (en ft: {bbox.Max.X - bbox.Min.X:F2} x {bbox.Max.Y - bbox.Min.Y:F2})");
                            }
                        }
                        else
                        {
                            _log?.Invoke($"   ➤ {levelName}: sin losa.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"⚠️ Error en resumen final: {ex.Message}");
            }
        }

        private void ResetDefault3DView()
        {
            try
            {
                var view3D = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.Contains("{3D}"));

                if (view3D != null)
                {
                    XYZ eye = new XYZ(100, 100, 100);
                    XYZ target = new XYZ(0, 0, 0);
                    XYZ up = XYZ.BasisZ;

                    ViewOrientation3D orientation = new ViewOrientation3D(eye, up, target);
                    view3D.SetOrientation(orientation);
                    _log?.Invoke("🔄 Vista 3D orientada correctamente.");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"⚠️ No se pudo reorientar la vista 3D: {ex.Message}");
            }
        }
    }
}