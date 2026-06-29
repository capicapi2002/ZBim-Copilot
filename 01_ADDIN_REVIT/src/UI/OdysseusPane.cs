#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ZBIMCopilot.Execution;
using WpfGrid = System.Windows.Controls.Grid;

namespace ZBIMCopilot
{
    public class OdysseusPane : IDockablePaneProvider
    {
        public static readonly Guid PaneGuid = new Guid("D4C8B5A2-3E1F-4A9B-8C2D-5E6F7A8B9C0D");

        private WebView2? _webView;
        private WpfGrid? _host;
        private bool _isWebViewReady;
        private bool _isPageLoaded;
        private readonly List<string> _messageBuffer = new();
        private readonly object _bufferLock = new();

        public static Action<string>? SendToUI;
        public static event Action<string>? OnUICommand;

        // ExternalEvent estático de topografía (se crea una sola vez en el hilo principal)
        private static ExternalEvent? _topographyExternalEvent;
        internal static List<XYZ>? _pendingTopographyPoints;

        public OdysseusPane()
        {
            ZBIMApp.OnServerStatus += OnServerStatusReceived;

            if (_topographyExternalEvent == null)
            {
                var handler = new TopographyHandler();
                _topographyExternalEvent = ExternalEvent.Create(handler);
                _pendingTopographyPoints = new List<XYZ>();
            }
        }

        ~OdysseusPane()
        {
            try { ZBIMApp.OnServerStatus -= OnServerStatusReceived; } catch { }
        }

        public static void EnqueueTopographyPoints(List<XYZ> points)
        {
            if (_pendingTopographyPoints != null && _topographyExternalEvent != null)
            {
                lock (_pendingTopographyPoints)
                {
                    _pendingTopographyPoints.Clear();
                    _pendingTopographyPoints.AddRange(points);
                }
                _topographyExternalEvent.Raise();
            }
        }

        public static void Register(UIControlledApplication application)
        {
            var paneId = new DockablePaneId(PaneGuid);
            application.RegisterDockablePane(paneId, "Odysseus Solver", new OdysseusPane());
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = CreateUI();
        }

        private FrameworkElement CreateUI()
        {
            _host = new WpfGrid();
            _webView = new WebView2();
            _host.Children.Add(_webView);
            _host.Loaded += async (s, e) => await InitializeAsync();
            return _host;
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (_webView == null) return;

                var dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZBIMCopilot", "WebView2Cache");
                Directory.CreateDirectory(dataFolder);

                _webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _isWebViewReady = true;

                        SendToUI = (json) =>
                        {
                            if (_isWebViewReady && _isPageLoaded && _webView?.CoreWebView2 != null)
                            {
                                try
                                {
                                    _webView.CoreWebView2.PostWebMessageAsString(json);
                                }
                                catch { }
                            }
                        };

                        _webView.CoreWebView2.WebMessageReceived += (sender, args) =>
                        {
                            var rawMessage = args.TryGetWebMessageAsString();
                            if (string.IsNullOrEmpty(rawMessage)) return;

                            if (rawMessage == "UI_READY")
                            {
                                _isPageLoaded = true;
                                SendMessage("✅ Odysseus UI conectada al motor ZBIM-Copilot.");
                                lock (_bufferLock)
                                {
                                    _messageBuffer.ForEach(msg => SendMessage(msg));
                                    _messageBuffer.Clear();
                                }
                                return;
                            }

                            try
                            {
                                using var doc = JsonDocument.Parse(rawMessage);
                                if (doc.RootElement.TryGetProperty("type", out var typeEl) &&
                                    typeEl.GetString() == "command" &&
                                    doc.RootElement.TryGetProperty("command", out var cmdEl))
                                {
                                    var command = cmdEl.GetString() ?? "";

                                    if (command == "build_mixed_tower")
                                    {
                                        HandleSimpleCommand(command);
                                        return;
                                    }

                                    if (command == "submit_project_config" &&
                                        doc.RootElement.TryGetProperty("data", out var dataEl))
                                    {
                                        HandleProjectConfigCommand(dataEl.GetRawText());
                                        return;
                                    }

                                    if (command == "submit_full_project" &&
                                        doc.RootElement.TryGetProperty("data", out var fullDataEl))
                                    {
                                        HandleFullProjectCommand(fullDataEl.GetRawText());
                                        return;
                                    }

                                    if (command == "get_balance")
                                    {
                                        _ = HandleGetBalanceCommandAsync();
                                        return;
                                    }

                                    if (command == "recharge_tokens" &&
                                        doc.RootElement.TryGetProperty("amount", out var amountEl))
                                    {
                                        _ = HandleRechargeCommandAsync(amountEl.GetDecimal());
                                        return;
                                    }

                                    if (command == "open_url_in_browser" &&
                                        doc.RootElement.TryGetProperty("url", out var urlEl))
                                    {
                                        _ = OpenUrlInBrowser(urlEl.GetString() ?? "");
                                        return;
                                    }

                                    if (command == "generate_topography")
                                    {
                                        double lat = GetDoubleProp(doc.RootElement, "latitude") ?? 0;
                                        double lon = GetDoubleProp(doc.RootElement, "longitude") ?? 0;
                                        int radius = GetIntProp(doc.RootElement, "radius") ?? 200;
                                        _ = HandleGenerateTopographyCommandAsync(lat, lon, radius);
                                        return;
                                    }

                                    if (command == "climate_analysis")
                                    {
                                        double lat = GetDoubleProp(doc.RootElement, "latitude") ?? 0;
                                        double lon = GetDoubleProp(doc.RootElement, "longitude") ?? 0;
                                        _ = HandleClimateAnalysisCommandAsync(lat, lon);
                                        return;
                                    }
                                }
                            }
                            catch (JsonException) { }

                            _ = ForwardOasToHttp(rawMessage);
                        };

                        string html = LoadEmbeddedHtml();
                        _webView.CoreWebView2.NavigateToString(html);

                        // Forzar recarga después de 500 ms para eliminar caché residual
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            if (_webView?.CoreWebView2 != null)
                            {
                                System.Diagnostics.Debug.WriteLine("🔄 [ZBIM] Forzando recarga del WebView2...");
                                _webView.CoreWebView2.Reload();
                            }
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error al inicializar WebView2: {e.InitializationException?.Message}");
                        ShowFallbackLabel("Error al inicializar WebView2 Runtime.");
                    }
                };

                // Opciones para deshabilitar completamente la caché de Chromium
                var options = new CoreWebView2EnvironmentOptions();
                options.AdditionalBrowserArguments =
                    "--disable-cache " +
                    "--disable-application-cache " +
                    "--disable-offline-load-stale-cache " +
                    "--disk-cache-size=0 " +
                    "--media-cache-size=0";

                System.Diagnostics.Debug.WriteLine("🔧 [ZBIM] Iniciando WebView2 con caché deshabilitada...");

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error en InitializeAsync: {ex.Message}");
                ShowFallbackLabel($"Excepción: {ex.Message}");
            }
        }

        private string LoadEmbeddedHtml()
        {
            // Cargar desde el archivo físico en la carpeta de salida de compilación
            string dllDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string filePath = Path.Combine(dllDirectory, "UI", "OdysseusWeb", "index.html");

            if (File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"📁 [ZBIM] Cargando HTML desde archivo: {filePath}");
                return File.ReadAllText(filePath);
            }

            // Fallback: recurso embebido
            string targetResource = "ZBIMCopilot.UI.OdysseusWeb.index.html";
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream? stream = assembly.GetManifestResourceStream(targetResource))
            {
                if (stream != null)
                {
                    System.Diagnostics.Debug.WriteLine("📦 [ZBIM] Cargando HTML desde recurso embebido");
                    using StreamReader reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }

            return @"<html><body style='background:#1E1E1E;color:#F14C4C;font-family:Consolas;padding:20px;'>
            <h3>⚠️ Error: No se pudo cargar index.html</h3></body></html>";
        }

        private void ShowFallbackLabel(string reason)
        {
            if (_host == null) return;
            _host.Dispatcher.Invoke(() =>
            {
                _host.Children.Clear();
                var label = new Label
                {
                    Content = $"WebView2 no disponible.\n{reason}",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center
                };
                _host.Children.Add(label);
            });
        }

        private void SendMessage(string message)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
                DoSendMessage(message);
            else
                Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(() => DoSendMessage(message)),
                    DispatcherPriority.Normal);
        }

        private async void DoSendMessage(string message)
        {
            try
            {
                if (!_isWebViewReady || !_isPageLoaded || _webView?.CoreWebView2 == null)
                {
                    lock (_bufferLock)
                    {
                        _messageBuffer.Add(message);
                        if (_messageBuffer.Count > 100) _messageBuffer.RemoveAt(0);
                    }
                    return;
                }
                var escaped = JsonSerializer.Serialize(message);
                await _webView.CoreWebView2.ExecuteScriptAsync($"addLog({escaped})");
            }
            catch { }
        }

        private void OnServerStatusReceived(string message) => SendMessage(message);

        private void HandleSimpleCommand(string command)
        {
            if (command == "build_mixed_tower")
            {
                SendMessage($"⚙️ Ejecutando {command}...");
                try { ZBIMApp.TriggerMixedTowerExternalEvent(); }
                catch (Exception ex) { SendMessage($"❌ Error: {ex.Message}"); }
                SendMessage($"✅ Comando {command} procesado.");
            }
        }

        private void HandleProjectConfigCommand(string jsonConfig)
        {
            try
            {
                ZBIMApp.EnqueueProjectConfig(jsonConfig);
                ZBIMApp.TriggerProjectConfigExternalEvent();
                SendMessage("📋 Configuración de proyecto encolada.");
            }
            catch (Exception ex) { SendMessage($"❌ Error: {ex.Message}"); }
        }

        private void HandleFullProjectCommand(string jsonConfig)
        {
            try
            {
                ZBIMApp.EnqueueFullProjectConfig(jsonConfig);
                ZBIMApp.TriggerFullProjectConfigExternalEvent();
                SendMessage("📋 Configuración completa encolada.");
            }
            catch (Exception ex) { SendMessage($"❌ Error: {ex.Message}"); }
        }

        private async Task HandleGetBalanceCommandAsync()
        {
            await Task.Delay(50);
            int balance = 100;
            var payload = new { type = "update_balance", balance = balance };
            string jsonResp = JsonSerializer.Serialize(payload);
            SendToUI?.Invoke(jsonResp);
        }

        private async Task HandleRechargeCommandAsync(decimal amount)
        {
            try
            {
                var client = new DesignIntelligenceClient();
                string paymentUrl = await Task.Run(() => client.InitRecharge(amount));
                var payload = new { type = "payment_url", url = paymentUrl };
                string jsonResp = JsonSerializer.Serialize(payload);
                SendToUI?.Invoke(jsonResp);
            }
            catch (Exception ex)
            {
                SendMessage($"Error iniciando recarga: {ex.Message}");
            }
        }

        private static double? GetDoubleProp(JsonElement el, string name) =>
            el.TryGetProperty(name, out var prop) && prop.TryGetDouble(out double v) ? v : (double?)null;

        private static int? GetIntProp(JsonElement el, string name) =>
            el.TryGetProperty(name, out var prop) && prop.TryGetInt32(out int v) ? v : (int?)null;

        private async Task OpenUrlInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                SendMessage($"🌐 Abriendo {url} en el navegador...");
            }
            catch (Exception ex)
            {
                SendMessage($"❌ Error al abrir navegador: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        private async Task HandleGenerateTopographyCommandAsync(double latitude, double longitude, int radius)
        {
            try
            {
                SendMessage("🗺️ Solicitando datos topográficos...");
                var client = new DesignIntelligenceClient();
                string contextJson = await client.EnrichContextAsync(latitude, longitude, radius);
                SendMessage("✅ Datos topográficos recibidos.");

                using var jsonDoc = JsonDocument.Parse(contextJson);
                if (jsonDoc.RootElement.TryGetProperty("topography", out var topoEl) &&
                    topoEl.TryGetProperty("points", out var pointsEl))
                {
                    var points = new List<XYZ>();
                    foreach (var point in pointsEl.EnumerateArray())
                    {
                        if (point.TryGetProperty("x", out var xEl) &&
                            point.TryGetProperty("y", out var yEl) &&
                            point.TryGetProperty("z", out var zEl))
                        {
                            points.Add(new XYZ(xEl.GetDouble(), yEl.GetDouble(), zEl.GetDouble()));
                        }
                    }

                    if (points.Count >= 3)
                    {
                        EnqueueTopographyPoints(points);
                        SendMessage($"✅ Topografía encolada con {points.Count} puntos.");
                    }
                    else
                    {
                        SendMessage("⚠️ No hay suficientes puntos para crear topografía.");
                    }
                }
                else
                {
                    SendMessage("⚠️ No se recibieron datos de topografía.");
                }
            }
            catch (Exception ex)
            {
                SendMessage($"❌ Error obteniendo topografía: {ex.Message}");
            }
        }

        private async Task HandleClimateAnalysisCommandAsync(double latitude, double longitude)
        {
            try
            {
                SendMessage("🌤️ Solicitando datos climáticos...");
                var client = new DesignIntelligenceClient();
                string contextJson = await client.EnrichContextAsync(latitude, longitude);
                SendMessage($"✅ Datos climáticos recibidos.");

                var payload = new { type = "climate_data", data = contextJson };
                string jsonResp = JsonSerializer.Serialize(payload);
                SendToUI?.Invoke(jsonResp);
                SendMessage("🌤️ Datos climáticos enviados a la UI.");
            }
            catch (Exception ex)
            {
                SendMessage($"❌ Error obteniendo clima: {ex.Message}");
            }
        }

        private async Task ForwardOasToHttp(string jsonOas)
        {
            SendMessage("⏳ Procesando JSON-OAS...");
            try
            {
                using var client = new HttpClient();
                var content = new StringContent(jsonOas, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("http://localhost:8080/oas/", content).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var feedback = response.IsSuccessStatusCode ? $"✅ SUCCESS: {body}" : $"❌ ERROR ({response.StatusCode}): {body}";
                SendMessage(feedback);
            }
            catch (Exception ex)
            {
                SendMessage($"❌ ERROR DE CONEXIÓN: {ex.Message}");
            }
        }
    }

    public class TopographyHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            var points = OdysseusPane._pendingTopographyPoints;
            if (points == null || points.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ [ZBIM] TopographyHandler: No hay puntos para crear topografía.");
                return;
            }

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ [ZBIM] TopographyHandler: No se pudo obtener el documento activo.");
                return;
            }

            // Copiar puntos para liberar el lock rápidamente
            List<XYZ> pointsCopy;
            lock (points)
            {
                pointsCopy = new List<XYZ>(points);
            }

            try
            {
                using (Transaction t = new Transaction(doc, "Crear topografía desde OpenTopography"))
                {
                    t.Start();
                    TopographySurface.Create(doc, pointsCopy);
                    t.Commit();
                }
                System.Diagnostics.Debug.WriteLine($"✅ [ZBIM] Topografía creada correctamente con {pointsCopy.Count} puntos.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ZBIM] Error al crear topografía: {ex.Message}");
            }
        }

        public string GetName() => "TopographyHandler";
    }
}