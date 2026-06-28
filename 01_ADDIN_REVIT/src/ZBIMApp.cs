using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using ZBIMCopilot.OAS;
using ZBIMCopilot.Execution;
using ZBimCopilot.Commands;
using ZBimCopilot.Knowledge;

namespace ZBIMCopilot
{
    public partial class ZBIMApp : IExternalApplication
    {
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        public static Action<string>? OnServerStatus;

        private static OasEventHandler? _oasHandler;
        private static ExternalEvent? _oasExternalEvent;

        private static BuildMixedTowerHandler? _mixedTowerHandler;
        private static ExternalEvent? _mixedTowerExternalEvent;

        private static ProjectConfigHandler? _projectConfigHandler;
        private static ExternalEvent? _projectConfigExternalEvent;

        // ========== NUEVOS CAMPOS (FASE D) ==========
        private static FullProjectConfigHandler? _fullProjectConfigHandler;
        private static ExternalEvent? _fullProjectConfigExternalEvent;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                _oasHandler = new OasEventHandler();
                _oasExternalEvent = ExternalEvent.Create(_oasHandler);

                _mixedTowerHandler = new BuildMixedTowerHandler();
                _mixedTowerExternalEvent = ExternalEvent.Create(_mixedTowerHandler);

                _projectConfigHandler = new ProjectConfigHandler();
                _projectConfigExternalEvent = ExternalEvent.Create(_projectConfigHandler);

                // ========== NUEVA INICIALIZACIÓN (FASE D) ==========
                _fullProjectConfigHandler = new FullProjectConfigHandler();
                _fullProjectConfigExternalEvent = ExternalEvent.Create(_fullProjectConfigHandler);

                string tabName = "ZBIM-Copilot";
                app.CreateRibbonTab(tabName);
                RibbonPanel panel = app.CreateRibbonPanel(tabName, "Motor BIM");

                PushButtonData solverBtnData = new PushButtonData(
                    "EjecutarSolver", "Ejecutar\nSolver",
                    typeof(ZBIMApp).Assembly.Location,
                    typeof(ZBIMCopilot.SolverCommand).FullName);
                if (panel.AddItem(solverBtnData) is PushButton solverBtn)
                    solverBtn.ToolTip = "Inicia el servidor HTTP para recibir instrucciones del Agente IA.";

                PushButtonData hybridBtnData = new PushButtonData(
                    "BuildMixedTower", "Build Mixed\nTower",
                    typeof(ZBIMApp).Assembly.Location,
                    typeof(BuildMixedTowerCommand).FullName);
                if (panel.AddItem(hybridBtnData) is PushButton hybridBtn)
                    hybridBtn.ToolTip = "Genera una torre mixta usando topología OAS fija.";

                OdysseusPane.Register(app);
                // Ya no se suscribe a OnUICommand porque OdysseusPane delega directamente

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error fatal en OnStartup: {ex.Message}\n{ex.StackTrace}", "ZBIM-Copilot");
                return Result.Failed;
            }
        }

        // Métodos estáticos requeridos por OdysseusPane
        public static void TriggerMixedTowerExternalEvent()
        {
            _mixedTowerExternalEvent?.Raise();
        }

        public static void EnqueueProjectConfig(string json)
        {
            _projectConfigHandler?.Enqueue(json);
        }

        public static void TriggerProjectConfigExternalEvent()
        {
            _projectConfigExternalEvent?.Raise();
        }

        // ========== NUEVOS MÉTODOS ESTÁTICOS (FASE D) ==========
        public static void EnqueueFullProjectConfig(string json)
        {
            _fullProjectConfigHandler?.Enqueue(json);
        }

        public static void TriggerFullProjectConfigExternalEvent()
        {
            _fullProjectConfigExternalEvent?.Raise();
        }

        // Servidor HTTP (sin cambios)
        private void StartHttpServer()
        {
            if (_httpListener != null) return;
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8080/oas/");
            _cts = new CancellationTokenSource();

            _serverTask = Task.Run(() =>
            {
                try
                {
                    _httpListener.Start();
                    NotifyStatus("✅ Servidor HTTP activo en http://localhost:8080/oas/");
                    while (!_cts.IsCancellationRequested)
                    {
                        var contextTask = _httpListener.GetContextAsync();
                        contextTask.Wait(_cts.Token);
                        HttpListenerContext context = contextTask.Result;
                        Task.Run(() => ProcessRequestAsync(context), _cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    NotifyStatus($"❌ Error en servidor HTTP: {ex.Message}");
                }
                finally
                {
                    _httpListener.Stop();
                }
            }, _cts.Token);
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                NotifyStatus($"🔔 Petición HTTP: {request.HttpMethod} {request.Url?.PathAndQuery}");

                if (request.HttpMethod != "POST" || request.Url?.AbsolutePath != "/oas/")
                {
                    await SendResponseAsync(context.Response, 404, "Ruta no encontrada").ConfigureAwait(false);
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    await SendResponseAsync(context.Response, 400, "Body vacío").ConfigureAwait(false);
                    return;
                }

                OasProject? project;
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    project = JsonSerializer.Deserialize<OasProject>(body, options);
                }
                catch (JsonException ex)
                {
                    NotifyStatus($"❌ JSON inválido: {ex.Message}");
                    await SendResponseAsync(context.Response, 400, $"JSON inválido: {ex.Message}").ConfigureAwait(false);
                    return;
                }

                if (project == null)
                {
                    await SendResponseAsync(context.Response, 400, "JSON nulo").ConfigureAwait(false);
                    return;
                }

                NotifyStatus($"✅ Proyecto deserializado: {project.ProjectName}");

                if (_oasHandler != null && _oasExternalEvent != null)
                {
                    _oasHandler.Enqueue(project, context);
                    _oasExternalEvent.Raise();
                }
                else
                {
                    await SendResponseAsync(context.Response, 500, "Handler no disponible").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                NotifyStatus($"❌ Error en ProcessRequestAsync: {ex.Message}");
                try { await SendResponseAsync(context.Response, 500, $"Error: {ex.Message}").ConfigureAwait(false); } catch { }
            }
        }

        private async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string message)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "text/plain; charset=utf-8";
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                response.OutputStream.Close();
                NotifyStatus($"📤 Respuesta {statusCode}: {message}");
            }
            catch (Exception ex)
            {
                NotifyStatus($"❌ Error enviando respuesta: {ex.Message}");
            }
        }

        private void NotifyStatus(string message)
        {
            try
            {
                OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en NotifyStatus: {ex.Message}");
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try
            {
                _cts?.Cancel();
                _httpListener?.Stop();
                _httpListener?.Close();
                NotifyStatus("Servidor HTTP detenido");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en OnShutdown: {ex.Message}");
            }
            return Result.Succeeded;
        }
    }
}