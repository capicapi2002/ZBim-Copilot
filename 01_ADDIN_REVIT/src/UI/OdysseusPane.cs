using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

namespace ZBIMCopilot
{
    public class OdysseusPane : IDockablePaneProvider
    {
        public static readonly Guid PaneGuid = new Guid("D4C8B5A2-3E1F-4A9B-8C2D-5E6F7A8B9C0D");
        private WebView2? _webView;
        private bool _isWebViewReady = false;
        private readonly List<string> _messageBuffer = new List<string>();
        private readonly object _bufferLock = new object();

        public OdysseusPane()
        {
            ZBIMApp.OnServerStatus += OnServerStatusReceived;
        }

        private void OnServerStatusReceived(string message)
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                SendMessageToWebView(message);
            }
            else
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(() => SendMessageToWebView(message)),
                    DispatcherPriority.Normal);
            }
        }

        private void SendMessageToWebView(string message)
        {
            if (_isWebViewReady && _webView?.CoreWebView2 != null)
            {
                try
                {
                    _webView.CoreWebView2.PostWebMessageAsString(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error al enviar mensaje a WebView: {ex.Message}");
                }
            }
            else
            {
                lock (_bufferLock)
                {
                    _messageBuffer.Add(message);
                    if (_messageBuffer.Count > 100)
                    {
                        _messageBuffer.RemoveAt(0);
                    }
                }
            }
        }

        public static void Register(UIControlledApplication application)
        {
            DockablePaneId paneId = new DockablePaneId(PaneGuid);
            application.RegisterDockablePane(paneId, "Odysseus Solver", new OdysseusPane());
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _webView = new WebView2();
            
            string dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "ZBIMCopilot", 
                "WebView2Cache");
            Directory.CreateDirectory(dataFolder);

            _webView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (e.IsSuccess)
                {
                    _isWebViewReady = true;

                    _webView.CoreWebView2.WebMessageReceived += async (sender, args) =>
                    {
                        string jsonOas = args.TryGetWebMessageAsString();
                        if (!string.IsNullOrEmpty(jsonOas))
                        {
                            try
                            {
                                using (var client = new HttpClient())
                                {
                                    var content = new StringContent(jsonOas, Encoding.UTF8, "application/json");
                                    var response = await client.PostAsync("http://localhost:8080/oas/", content).ConfigureAwait(false);
                                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    System.Diagnostics.Debug.WriteLine($"WebView POST response: {response.StatusCode} - {responseBody}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error en WebView POST: {ex.Message}");
                            }
                        }
                    };

                    // CORREGIDO: Usar Virtual Host Mapping para cargar archivos locales
                    string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string? dllDir = Path.GetDirectoryName(dllPath);
                    string webRootPath = Path.Combine(dllDir ?? "", "UI", "OdysseusWeb");
                    
                    if (Directory.Exists(webRootPath))
                    {
                        // Mapear el host virtual "https://zbim.local" a la carpeta local
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "zbim.local",
                            webRootPath,
                            CoreWebView2HostResourceAccessKind.Allow);
                        
                        // Navegar al archivo index.html a través del host virtual
                        _webView.CoreWebView2.Navigate("https://zbim.local/index.html");
                    }
                    else
                    {
                        _webView.CoreWebView2.NavigateToString($"<html><body><h1>Error: Carpeta no encontrada en:<br>{webRootPath}</h1></body></html>");
                    }

                    // Enviar mensajes en búfer
                    lock (_bufferLock)
                    {
                        foreach (var msg in _messageBuffer)
                        {
                            SendMessageToWebView(msg);
                        }
                        _messageBuffer.Clear();
                    }
                }
            };

            var env = CoreWebView2Environment.CreateAsync(null, dataFolder).Result;
            _webView.EnsureCoreWebView2Async(env);

            data.FrameworkElement = _webView;
        }
    }
}