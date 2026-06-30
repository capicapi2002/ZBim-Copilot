#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using Autodesk.Revit.UI;
using ZBimCopilot.Knowledge;

namespace ZBIMCopilot.Execution
{
    public class ProjectConfigHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<string> _configQueue = new ConcurrentQueue<string>();

        public void Enqueue(string json)
        {
            if (!string.IsNullOrWhiteSpace(json))
                _configQueue.Enqueue(json);
        }

        public void Execute(UIApplication app)
        {
            if (_configQueue.IsEmpty) return;
            if (!_configQueue.TryDequeue(out string? json)) return;

            try
            {
                ZBIMApp.OnServerStatus?.Invoke("⚙️ Procesando ProjectConfig desde UI...");
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<ProjectConfig>(json, options)
                    ?? throw new InvalidOperationException("El JSON deserializado es nulo o inválido.");

                ZBIMApp.OnServerStatus?.Invoke($"📐 Configuración recibida: {config.ProjectName} ({config.TotalFloors} plantas)");

                var topology = ProjectConfigEngine.GenerateTopology(config);
                ZBIMApp.OnServerStatus?.Invoke($"🗺️ Topología generada: {topology.Levels.Count} niveles");

                var builder = new HybridProjectBuilder();
                var layout = builder.BuildProjectLayout(topology);
                ZBIMApp.OnServerStatus?.Invoke($"🏗️ Layout híbrido construido: {layout.Levels.Count} niveles");

                var orchestrator = new Text2MblOrchestrator(app);
                orchestrator.BuildFromLayout(layout);

                ZBIMApp.OnServerStatus?.Invoke("✅ Proyecto generado exitosamente desde UI (Fase B completa).");
            }
            catch (Exception ex)
            {
                ZBIMApp.OnServerStatus?.Invoke($"❌ Error crítico en ProjectConfigHandler: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error en ProjectConfigHandler: {ex.StackTrace}");
            }
        }

        public string GetName() => "ProjectConfigHandler";
    }
}