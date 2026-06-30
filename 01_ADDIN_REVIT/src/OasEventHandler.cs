using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using ZBIMCopilot.Execution;
using ZBIMCopilot.OAS;

namespace ZBIMCopilot
{
    public class OasEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<(OasProject Project, HttpListenerContext Context)> _queue =
            new ConcurrentQueue<(OasProject, HttpListenerContext)>();

        public void Enqueue(OasProject project, HttpListenerContext context)
        {
            _queue.Enqueue((project, context));
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var item))
            {
                string responseMessage;
                try
                {
                    var orchestrator = new Text2MblOrchestrator();
                    Result result = orchestrator.Execute(item.Project, app);

                    if (result == Result.Succeeded)
                        responseMessage = "SUCCESS: Proyecto creado correctamente.";
                    else
                        responseMessage = "ERROR: Falló la generación del proyecto.";
                }
                catch (Exception ex)
                {
                    responseMessage = $"EXCEPTION: {ex.Message}";
                }

                SendResponse(item.Context, responseMessage);
            }
        }

        public string GetName() => "OasEventHandler";

        private static void SendResponse(HttpListenerContext context, string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}