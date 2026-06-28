using System;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ZBimCopilot.Knowledge;
using ZBIMCopilot.Execution;
using ZBIMCopilot;  // ✅ AÑADIDO: Para acceder a ZBIMApp

namespace ZBimCopilot.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class BuildMixedTowerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExecuteCommand(commandData.Application);
        }

        /// <summary>
        /// Método estático reutilizable que contiene la lógica completa del comando.
        /// Puede ser llamado desde el botón de Revit o desde el evento de la UI.
        /// </summary>
        public static Result ExecuteCommand(UIApplication uiApp)
        {
            try
            {
                // Buscar la raíz del repositorio (donde está ZBim-Copilot.sln)
                string root = FindProjectRoot();
                string jsonPath = Path.Combine(root, "03_DATOS_OAS", "torre_mixta_oas.json");

                if (!File.Exists(jsonPath))
                {
                    ZBIMApp.OnServerStatus?.Invoke($"❌ Archivo de topología no encontrado en {jsonPath}.");
                    return Result.Failed;
                }

                ZBIMApp.OnServerStatus?.Invoke($"📂 Leyendo topología desde: {jsonPath}");

                // Deserializar topología
                string jsonContent = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                TopologyData? topology = JsonSerializer.Deserialize<TopologyData>(jsonContent, options);

                if (topology is null || topology.Levels.Count == 0)
                {
                    ZBIMApp.OnServerStatus?.Invoke("❌ El archivo de topología está vacío o no contiene niveles válidos.");
                    return Result.Failed;
                }

                ZBIMApp.OnServerStatus?.Invoke($"✅ Topología cargada: {topology.Levels.Count} niveles");

                // Generar layout cuadrado con núcleo central
                var builder = new HybridProjectBuilder();
                ProjectLayout layout = builder.BuildProjectLayout(topology);

                ZBIMApp.OnServerStatus?.Invoke($"🏗️ Layout generado: {layout.Levels.Count} niveles con núcleo central");

                // Construir geometría en Revit
                var orchestrator = new Text2MblOrchestrator(uiApp);
                orchestrator.BuildFromLayout(layout);

                ZBIMApp.OnServerStatus?.Invoke("✅ Torre mixta construida exitosamente desde UI");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                ZBIMApp.OnServerStatus?.Invoke($"❌ Error en BuildMixedTowerCommand: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Localiza la raíz del repositorio buscando ZBim-Copilot.sln hacia arriba.
        /// </summary>
        private static string FindProjectRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            while (!File.Exists(Path.Combine(current, "ZBim-Copilot.sln")))
            {
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null)
                    throw new Exception("No se encontró la raíz del proyecto (ZBim-Copilot.sln).");
                current = parent;
            }
            return current;
        }
    }

    /// <summary>
    /// Handler para ejecutar BuildMixedTowerCommand desde un ExternalEvent.
    /// Se usa cuando el comando se dispara desde la UI (OdysseusPane).
    /// </summary>
    public class BuildMixedTowerHandler : IExternalEventHandler
    {
        public void Execute(UIApplication uiApp)
        {
            BuildMixedTowerCommand.ExecuteCommand(uiApp);
        }

        public string GetName()
        {
            return "BuildMixedTowerHandler";
        }
    }
}