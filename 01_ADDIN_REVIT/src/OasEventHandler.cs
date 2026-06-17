using System;
using Autodesk.Revit.UI;
using ZBIMCopilot.OAS;
using ZBIMCopilot.Execution;

namespace ZBIMCopilot
{
    public class OasEventHandler : IExternalEventHandler
    {
        private OasProject? _pendingProject;
        private readonly object _lock = new object();

        public void SetProject(OasProject project)
        {
            lock (_lock)
            {
                _pendingProject = project;
            }
        }

        public void Execute(UIApplication app)
        {
            OasProject? project;
            lock (_lock)
            {
                project = _pendingProject;
                _pendingProject = null;
            }

            if (project == null) return;

            try
            {
                ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] 🏗️ Ejecutando Text2MBL para: {project.ProjectName}");
                
                Text2MblOrchestrator.Execute(project, app);
                
                ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] ✅ Proyecto completado");
            }
            catch (Exception ex)
            {
                ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] ❌ Error en Execute: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public string GetName()
        {
            return "OAS Event Handler";
        }
    }
}