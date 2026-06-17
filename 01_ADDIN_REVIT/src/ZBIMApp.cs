using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using ZBIMCopilot.OAS;

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

        public Result OnStartup(UIControlledApplication app)
        {
            _oasHandler = new OasEventHandler();
            _oasExternalEvent = ExternalEvent.Create(_oasHandler);

            string tabName = "ZBIM-Copilot";
            app.CreateRibbonTab(tabName);

            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Motor BIM");

            PushButtonData buttonData = new PushButtonData(
                "EjecutarSolver",
                "Ejecutar\nSolver",
                typeof(ZBIMApp).Assembly.Location,
                typeof(ZBIMCopilot.SolverCommand).FullName);

            PushButton? pushButton = panel.AddItem(buttonData) as PushButton;
            
            if (pushButton != null)
            {
                pushButton.ToolTip = "Inicia el servidor HTTP para recibir instrucciones del Agente IA.";
            }

            OdysseusPane.Register(app);

            try
            {
                StartHttpServer();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error al iniciar servidor HTTP: {ex.Message}\nAsegúrese de ejecutar Revit como Administrador.", "ZBIM-Copilot");
            }

            return Result.Succeeded;
        }

        private void StartHttpServer()
        {
            if (_httpListener != null) return;

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8080/");
            
            try
            {
                _httpListener.Start();
                NotifyStatus("✅ Servidor HTTP activo en http://localhost:8080/");
            }
            catch (Exception ex)
            {
                NotifyStatus($"❌ Error al iniciar HttpListener: {ex.Message}");
                throw;
            }

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerAsync(_cts.Token), _cts.Token);
        }

        private async Task RunServerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => ProcessRequestAsync(context), cancellationToken);
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    NotifyStatus($"❌ Error en RunServerAsync: {ex.Message}");
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                NotifyStatus($"🔔 Petición HTTP recibida: {request.HttpMethod} {request.Url?.PathAndQuery}");

                if (request.HttpMethod != "POST" || request.Url?.AbsolutePath != "/oas/")
                {
                    await SendResponseAsync(response, 404, "Ruta no encontrada").ConfigureAwait(false);
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                NotifyStatus($" Body recibido ({body.Length} caracteres)");

                if (string.IsNullOrWhiteSpace(body))
                {
                    await SendResponseAsync(response, 400, "Body vacío").ConfigureAwait(false);
                    return;
                }

                OasProject? project;
                try
                {
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = false
                    };
                    project = JsonSerializer.Deserialize<OasProject>(body, options);
                }
                catch (JsonException ex)
                {
                    NotifyStatus($"❌ Error al deserializar JSON: {ex.Message}");
                    await SendResponseAsync(response, 400, $"JSON inválido: {ex.Message}").ConfigureAwait(false);
                    return;
                }

                if (project == null)
                {
                    await SendResponseAsync(response, 400, "JSON deserializado es nulo").ConfigureAwait(false);
                    return;
                }

                NotifyStatus($"✅ Proyecto deserializado: {project.ProjectName}");

                if (_oasHandler != null && _oasExternalEvent != null)
                {
                    _oasHandler.SetProject(project);
                    var eventResult = _oasExternalEvent.Raise();
                    NotifyStatus($"📤 ExternalEvent encolado (Resultado: {eventResult})");
                    
                    await SendResponseAsync(response, 200, "Proyecto encolado").ConfigureAwait(false);
                }
                else
                {
                    NotifyStatus("❌ OasEventHandler o ExternalEvent no inicializados");
                    await SendResponseAsync(response, 500, "Handler no disponible").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                NotifyStatus($"❌ Error en ProcessRequestAsync: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await SendResponseAsync(context.Response, 500, $"Error interno: {ex.Message}").ConfigureAwait(false);
                }
                catch { }
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
                
                NotifyStatus($"📤 Respuesta enviada: {statusCode} - {message}");
            }
            catch (Exception ex)
            {
                NotifyStatus($"❌ Error al enviar respuesta: {ex.Message}");
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
                NotifyStatus(" Servidor HTTP detenido");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en OnShutdown: {ex.Message}");
            }

            return Result.Succeeded;
        }
    }
}