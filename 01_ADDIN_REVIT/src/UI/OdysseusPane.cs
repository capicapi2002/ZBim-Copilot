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

        public OdysseusPane()
        {
            ZBIMApp.OnServerStatus += OnServerStatusReceived;
        }

        ~OdysseusPane()
        {
            try { ZBIMApp.OnServerStatus -= OnServerStatusReceived; } catch { }
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
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error al inicializar WebView2: {e.InitializationException?.Message}");
                        ShowFallbackLabel("Error al inicializar WebView2 Runtime.");
                    }
                };

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
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
            return INDEX_HTML;
        }

        // ============================================================
        // HTML COMPLETO INCORPORADO COMO CONSTANTE (versión de Z con todos los cambios)
        // ============================================================
        private const string INDEX_HTML = @"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>ZBIM-Copilot Odysseus</title>
    <style>
        :root {
            --bg-main: #E8E8E8;
            --bg-panel: #F0F0F0;
            --bg-input: #FFFFFF;
            --bg-btn-gray: #D0D0D0;
            --border-color: #B0B0B0;
            --accent: #007ACC;
            --accent-hover: #005A9E;
            --text-main: #000000;
            --text-muted: #333333;
            --danger: #D32F2F;
            --success: #2E7D32;
            --warning: #F57C00;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: var(--bg-main); color: var(--text-main); height: 100vh; width: 100vw; overflow: hidden; display: flex; flex-direction: column; }
        .main-container { display: flex; flex: 1; overflow: hidden; }
        #left-panel { width: 35%; min-width: 300px; background-color: var(--bg-panel); border-right: 1px solid var(--border-color); display: flex; flex-direction: column; }
        #right-panel { width: 65%; background-color: var(--bg-main); display: flex; flex-direction: column; }
        .panel-header { padding: 10px 15px; background-color: var(--bg-main); font-size: 0.85em; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid var(--border-color); color: var(--text-muted); display: flex; justify-content: space-between; align-items: center; }
        .panel-content { flex: 1; overflow-y: auto; padding: 15px; }
        .token-widget { display: flex; justify-content: space-between; align-items: center; padding: 8px 15px; background-color: var(--bg-input); border-bottom: 1px solid var(--border-color); font-size: 0.85em; gap: 15px; }
        .token-widget .token-info { flex: 1; display: flex; flex-direction: column; gap: 4px; }
        .token-widget .token-stats { display: flex; justify-content: space-between; }
        .token-progress-container { width: 100%; height: 6px; background-color: var(--bg-btn-gray); border-radius: 3px; overflow: hidden; }
        .token-progress-bar { height: 100%; background-color: var(--success); transition: width 0.3s ease, background-color 0.3s ease; }
        .form-group { margin-bottom: 15px; }
        .form-group label { display: block; margin-bottom: 5px; font-size: 0.8em; color: var(--text-muted); }
        .required-asterisk { color: var(--danger); margin-left: 3px; }
        .form-control { width: 100%; padding: 8px; background-color: var(--bg-input); border: 1px solid var(--border-color); color: var(--text-main); border-radius: 2px; font-size: 0.9em; transition: border-color 0.2s; }
        .form-control:focus { outline: none; border-color: var(--accent); }
        .form-control.invalid { border-color: var(--danger); }
        textarea.form-control { resize: vertical; min-height: 60px; font-family: inherit; }
        select.form-control { cursor: pointer; }
        .form-row { display: flex; gap: 10px; }
        .form-row .form-group { flex: 1; }
        .btn { padding: 8px 12px; border: 1px solid var(--border-color); background-color: var(--bg-btn-gray); color: var(--text-main); border-radius: 2px; cursor: pointer; font-size: 0.85em; transition: all 0.2s; text-align: center; }
        .btn:hover { background-color: #C0C0C0; border-color: #999999; }
        .btn:disabled { opacity: 0.5; cursor: not-allowed; }
        .btn-gray-solid { width: 100%; padding: 10px; background-color: #BEBEBE; color: #000000; border: 1px solid #999999; border-radius: 2px; cursor: pointer; font-size: 0.9em; font-weight: 500; transition: all 0.2s; }
        .btn-gray-solid:hover { background-color: #ACACAC; }
        .btn-primary { width: 100%; padding: 12px; background-color: var(--accent); color: white; border: none; border-radius: 2px; font-weight: 600; cursor: pointer; text-transform: uppercase; font-size: 0.95em; margin-top: 15px; }
        .btn-primary:hover { background-color: var(--accent-hover); }
        .btn-primary:disabled { background-color: #A0A0A0; cursor: not-allowed; }
        .btn-quick-amount { flex: 1; padding: 8px; margin: 0; background-color: var(--bg-panel); border: 1px solid var(--border-color); border-radius: 2px; cursor: pointer; font-size: 0.9em; font-weight: 600; transition: all 0.2s; }
        .btn-quick-amount:hover { background-color: var(--accent); color: white; border-color: var(--accent); }
        .btn-danger { background-color: transparent; border: 1px solid var(--danger); color: var(--danger); font-weight: 600; }
        .btn-danger:hover { background-color: var(--danger); color: white; }
        .generation-container { display: none; flex-direction: column; align-items: center; gap: 10px; margin-top: 15px; padding: 15px; background-color: var(--bg-input); border: 1px solid var(--border-color); border-radius: 4px; }
        .generation-container.active { display: flex; }
        .generation-row { display: flex; align-items: center; gap: 10px; justify-content: center; }
        .spinner { width: 20px; height: 20px; border: 3px solid var(--bg-btn-gray); border-top-color: var(--accent); border-radius: 50%; animation: spin 1s linear infinite; }
        @keyframes spin { to { transform: rotate(360deg); } }
        .tooltip-trigger { border-bottom: 1px dotted var(--text-muted); cursor: help; }
        #tooltip-container { position: fixed; z-index: 10000; background-color: var(--bg-input); border: 1px solid var(--accent); border-radius: 4px; padding: 8px 12px; box-shadow: 0 4px 8px rgba(0,0,0,0.2); max-width: 250px; font-size: 0.85em; color: var(--text-main); display: none; }
        #tooltip-text { margin-right: 8px; }
        #tooltip-manual-icon { cursor: pointer; font-size: 1.1em; border-left: 1px solid var(--border-color); padding-left: 8px; }
        #logs-container { border-top: 1px solid var(--border-color); height: 150px; min-height: 100px; display: flex; flex-direction: column; flex-shrink: 0; }
        #logs-output { flex: 1; overflow-y: auto; padding: 10px; font-family: 'Consolas', 'Courier New', monospace; font-size: 0.8em; color: var(--text-muted); background-color: var(--bg-panel); }
        .log-entry { margin-bottom: 4px; border-bottom: 1px dotted #D0D0D0; padding-bottom: 2px; word-break: break-word; }
        .modal-overlay { display: none; position: fixed; top: 0; left: 0; right: 0; bottom: 0; background-color: rgba(0, 0, 0, 0.5); z-index: 1000; justify-content: center; align-items: center; }
        .modal-overlay.active { display: flex; }
        .modal-content { background-color: var(--bg-panel); border: 1px solid var(--border-color); border-radius: 4px; width: 80%; height: 85%; display: flex; flex-direction: column; box-shadow: 0 10px 30px rgba(0,0,0,0.2); }
        .modal-content-small { width: 450px; height: auto; }
        .modal-header { padding: 15px; border-bottom: 1px solid var(--border-color); display: flex; justify-content: space-between; align-items: center; }
        .modal-body { flex: 1; padding: 15px; overflow-y: auto; display: flex; flex-direction: column; }
        .modal-guide { background-color: var(--bg-input); border-left: 3px solid var(--accent); padding: 12px; margin-bottom: 15px; font-size: 0.85em; color: var(--text-muted); white-space: pre-line; line-height: 1.4; border-radius: 2px; max-height: 25%; overflow-y: auto; }
        .modal-body textarea { flex: 1; width: 100%; background-color: var(--bg-input); color: var(--text-main); border: 1px solid var(--border-color); padding: 15px; font-size: 15px; font-family: 'Segoe UI', sans-serif; resize: none; line-height: 1.5; }
        .modal-footer { padding: 15px; border-top: 1px solid var(--border-color); display: flex; justify-content: flex-end; gap: 10px; }
        #manual-content h2 { margin-bottom: 15px; color: var(--accent); }
        #manual-content h3 { margin-top: 20px; margin-bottom: 10px; border-bottom: 1px solid var(--border-color); padding-bottom: 5px; }
        #manual-content p { margin-bottom: 10px; line-height: 1.6; font-size: 0.9em; }
        #clarification-body { position: relative; }
        #clarification-spinner-container { display: none; position: absolute; top: 0; left: 0; right: 0; bottom: 0; background-color: rgba(240, 240, 240, 0.85); z-index: 10; justify-content: center; align-items: center; flex-direction: column; gap: 15px; }
        #clarification-spinner-container.active { display: flex; }
        .drop-zone { border: 2px dashed var(--border-color); border-radius: 4px; padding: 40px; text-align: center; color: var(--text-muted); margin: 15px; cursor: pointer; transition: all 0.2s; }
        .drop-zone.dragover { border-color: var(--accent); background-color: rgba(0, 122, 204, 0.05); color: var(--accent); }
        .image-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 10px; padding: 0 15px 15px 15px; }
        .image-item { position: relative; width: 100%; padding-bottom: 100%; border-radius: 4px; overflow: hidden; border: 1px solid var(--border-color); background-color: #CCC; }
        .image-item img { position: absolute; top: 0; left: 0; width: 100%; height: 100%; object-fit: cover; }
        .image-item .remove-btn { position: absolute; top: 5px; right: 5px; background: rgba(0,0,0,0.7); color: var(--danger); border: none; border-radius: 50%; width: 20px; height: 20px; cursor: pointer; font-size: 0.8em; display: flex; align-items: center; justify-content: center; }
        .info-list { padding: 15px; }
        .info-item { background-color: var(--bg-panel); border-left: 3px solid var(--accent); padding: 8px 12px; margin-bottom: 8px; font-size: 0.85em; display: flex; justify-content: space-between; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
        .checkbox-group { display: flex; flex-wrap: wrap; gap: 10px; }
        .checkbox-item { display: flex; align-items: center; gap: 5px; background: var(--bg-input); padding: 5px 10px; border: 1px solid var(--border-color); border-radius: 2px; cursor: pointer; font-size: 0.85em; }
        .checkbox-item input { cursor: pointer; }
        ::-webkit-scrollbar { width: 8px; }
        ::-webkit-scrollbar-track { background: var(--bg-main); }
        ::-webkit-scrollbar-thumb { background: #B0B0B0; border-radius: 4px; }
        ::-webkit-scrollbar-thumb:hover { background: #999999; }
    </style>
</head>
<body>
    <div class=""main-container"">
        <div id=""left-panel"">
            <div class=""panel-header""><span data-i18n=""hdr_config"">Configuración del Proyecto</span></div>
            <div class=""token-widget"">
                <div class=""token-info"">
                    <div class=""token-stats""><span class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_recharge"">🔹 <span data-i18n=""token_balance"">Saldo de tokens</span>: <strong id=""token-balance"">0</strong></span><span id=""token-usage-percent"">0%</span></div>
                    <div class=""token-progress-container""><div class=""token-progress-bar"" id=""token-progress-bar"" style=""width: 0%;""></div></div>
                </div>
                <button class=""btn"" onclick=""openRechargeModal()"" data-i18n=""recharge"">Recargar</button>
            </div>
            <div class=""panel-content"" id=""form-container"">
                <div class=""form-group""><label for=""project_name"" data-i18n=""project_name"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_project_name"">Nombre del Proyecto <span class=""required-asterisk"">*</span></label><input type=""text"" id=""project_name"" class=""form-control"" required></div>
                <div class=""form-group""><label data-i18n=""program_reqs"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_program_reqs"">Programa de Necesidades</label><button class=""btn-gray-solid"" onclick=""openProgramModal()"" data-i18n=""draft_program"">Redactar programa de necesidades</button><label for=""program_doc_file"" class=""btn"" style=""margin-top: 8px; width: 100%; display:block;"" data-i18n=""upload_program"">Subir Programa (PDF/DOCX)</label><input type=""file"" id=""program_doc_file"" accept="".pdf,.docx"" style=""display:none"" onchange=""handleProgramDocUpload(this.files)""><div id=""program_doc_name"" style=""font-size: 0.8em; color: var(--success); margin-top: 5px;""></div><div id=""program_summary"" style=""font-size: 0.8em; color: var(--success); margin-top: 5px; font-style: italic;""></div></div>
                <div class=""form-group""><label for=""location"" data-i18n=""location"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_location"">Ciudad / País (referencia general) <span class=""required-asterisk"">*</span></label><input type=""text"" id=""location"" class=""form-control"" placeholder=""Madrid, España"" required></div>
                <div class=""form-row"">
                    <div class=""form-group""><label for=""latitude"" data-i18n=""latitude"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_latitude"">Latitud <span class=""required-asterisk"">*</span></label><input type=""number"" id=""latitude"" class=""form-control"" step=""any"" min=""-90"" max=""90"" placeholder=""Ej. 36.7226"" oninput=""updateEnvironmentButtons()""></div>
                    <div class=""form-group""><label for=""longitude"" data-i18n=""longitude"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_longitude"">Longitud <span class=""required-asterisk"">*</span></label><input type=""number"" id=""longitude"" class=""form-control"" step=""any"" min=""-180"" max=""180"" placeholder=""Ej. -4.4234"" oninput=""updateEnvironmentButtons()""></div>
                </div>
                <div class=""form-group""><label for=""radius"" data-i18n=""radius"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_radius"">Radio del lote (metros)</label><input type=""number"" id=""radius"" class=""form-control"" value=""200"" min=""10"" max=""5000"" step=""10""></div>
                <div class=""form-group""><label for=""project_type"" data-i18n=""project_type"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_project_type"">Tipo de Proyecto</label><select id=""project_type"" class=""form-control"" onchange=""toggleOtherType()""><option value=""Vivienda unifamiliar"">Vivienda unifamiliar</option><option value=""Viviendas apareadas"">Viviendas apareadas</option><option value=""Vivienda multifamiliar en altura"">Vivienda multifamiliar en altura</option><option value=""Oficinas"">Oficinas</option><option value=""Comercial"">Comercial</option><option value=""Educacional"">Educacional</option><option value=""Industrial"">Industrial</option><option value=""Salud / Hospital"">Salud / Hospital</option><option value=""Usos mixtos"">Usos mixtos</option><option value=""Otros"">Otros</option></select></div>
                <div class=""form-group"" id=""other_type_container"" style=""display: none;""><label for=""other_type"">Especificar Otro</label><input type=""text"" id=""other_type"" class=""form-control""></div>
                <div class=""form-group""><label for=""site_description"" data-i18n=""site_dims"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_site_description"">Dimensiones del Terreno</label><textarea id=""site_description"" class=""form-control"" placeholder=""Ej. Terreno rectangular 20x30m, esquina""></textarea><label for=""site_plan_file"" class=""btn tooltip-trigger"" data-tooltip-i18n=""tooltip_upload_site"" style=""margin-top: 8px; width: 100%; display:block;"" data-i18n=""upload_site"">Subir Plano del Terreno (PDF/DWG)</label><input type=""file"" id=""site_plan_file"" accept="".pdf,.dwg"" style=""display:none"" onchange=""handleSitePlanUpload(this.files)""><div id=""site_plan_name"" style=""font-size: 0.8em; color: var(--success); margin-top: 5px;""></div></div>
                <div class=""form-row""><div class=""form-group""><label for=""retreat_front"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_retreat_front"">Retiro frontal (m)</label><input type=""number"" id=""retreat_front"" class=""form-control"" min=""0"" step=""0.1"" value=""0""></div><div class=""form-group""><label for=""retreat_side"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_retreat_side"">Retiro lateral (m)</label><input type=""number"" id=""retreat_side"" class=""form-control"" min=""0"" step=""0.1"" value=""0""></div></div>
                <div class=""form-row""><div class=""form-group""><label for=""retreat_back"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_retreat_back"">Retiro posterior (m)</label><input type=""number"" id=""retreat_back"" class=""form-control"" min=""0"" step=""0.1"" value=""0""></div><div class=""form-group""><label for=""implantation_area"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_implantation_area"">Área de implantación (m²)</label><input type=""number"" id=""implantation_area"" class=""form-control"" min=""0"" step=""0.1"" value=""0""></div></div>
                <div class=""form-row""><div class=""form-group""><label for=""floors_above"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_floors_above"">Plantas sobre rasante</label><input type=""number"" id=""floors_above"" class=""form-control"" min=""0"" step=""1"" value=""1""></div><div class=""form-group""><label for=""floors_below"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_floors_below"">Plantas bajo rasante</label><input type=""number"" id=""floors_below"" class=""form-control"" min=""0"" step=""1"" value=""0""></div></div>
                <div class=""form-group""><label data-i18n=""applicable_code"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_applicable_code"">Normativa Aplicable</label><div style=""display:flex; gap: 5px; align-items: center;""><select id=""normative_select"" class=""form-control"" style=""flex: 1;""><option value="""">- Sin normativa -</option></select><button class=""btn"" onclick=""removeNormative()"" title=""Eliminar"" style=""padding: 8px 10px;"">X</button></div><button class=""btn"" style=""margin-top: 8px; width: 100%;"" onclick=""document.getElementById('normative_file_input').click()"" data-i18n=""upload_new_code"">Cargar nueva normativa</button><input type=""file"" id=""normative_file_input"" accept="".pdf"" style=""display:none"" onchange=""handleNormativeUpload(this.files)""></div>
                <div class=""form-group""><label data-i18n=""style_refs"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_style_refs"">Referencias de Estilo</label><div class=""checkbox-group"" id=""materials_group""><label class=""checkbox-item""><input type=""checkbox"" value=""Hormigón visto""> Hormigón visto</label><label class=""checkbox-item""><input type=""checkbox"" value=""Vidrio""> Vidrio</label><label class=""checkbox-item""><input type=""checkbox"" value=""Acero""> Acero</label><label class=""checkbox-item""><input type=""checkbox"" value=""Madera""> Madera</label><label class=""checkbox-item""><input type=""checkbox"" value=""Piedra""> Piedra</label></div></div>
                <div class=""form-group""><label for=""style_language"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_style_language"">Lenguaje arquitectónico</label><select id=""style_language"" class=""form-control""><option value=""Moderno"">Moderno</option><option value=""Minimalista"">Minimalista</option><option value=""Clásico"">Clásico</option><option value=""High-Tech"">High-Tech</option><option value=""Orgánico"">Orgánico</option><option value=""Brutalista"">Brutalista</option></select></div>
                <div class=""form-group""><label for=""max_height"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_max_height"">Altura máxima deseada (m)</label><input type=""number"" id=""max_height"" class=""form-control"" min=""0"" step=""0.1""></div>
                <div class=""form-group""><label for=""climate_conditions"" class=""tooltip-trigger"" data-tooltip-i18n=""tooltip_climate_conditions"">Condiciones climáticas relevantes</label><textarea id=""climate_conditions"" class=""form-control"" placeholder=""Ej. Clima frío con nevadas invernales, alta humedad""></textarea></div>
                <button id=""generate-btn"" class=""btn-primary tooltip-trigger"" data-tooltip-i18n=""tooltip_generate_project"" onclick=""submitProject()"" disabled data-i18n=""generate_project"">Generar Proyecto</button>
                <div id=""generation-container"" class=""generation-container""><div class=""generation-row""><div class=""spinner""></div><span id=""generation-status-text"" data-i18n=""generating_oas"">Generando OAS con IA...</span></div><button class=""btn btn-danger"" style=""width: 100%;"" onclick=""cancelGeneration()"" data-i18n=""cancel_generation"">Cancelar</button></div>
            </div>
            <div id=""logs-container""><div class=""panel-header"" style=""font-size: 0.75em; padding: 8px 15px;"">System Logs</div><div id=""logs-output""></div></div>
        </div>
        <div id=""right-panel"">
            <div class=""panel-header"" data-i18n=""ref_gallery"">Galería de Referencias</div>
            <div class=""drop-zone tooltip-trigger"" data-tooltip-i18n=""tooltip_ref_gallery"" id=""drop-zone"" ondragover=""handleDragOver(event)"" ondrop=""handleDrop(event)"" ondragleave=""handleDragLeave(event)"" onclick=""document.getElementById('image_upload').click()""><p>Arrastra y suelta imágenes aquí o haz clic para añadir</p><input type=""file"" id=""image_upload"" multiple accept=""image/*"" style=""display:none"" onchange=""handleFiles(this.files)""></div>
            <div class=""image-grid"" id=""image-grid""></div>
            <div class=""panel-header"" style=""margin-top: 20px;"">Normativas Cargadas</div>
            <div class=""info-list"" id=""codes-info-list""><div class=""info-item"">No hay normativas cargadas</div></div>
            <div class=""panel-header"" style=""margin-top: 20px;"">Entorno</div>
            <div style=""padding: 15px; display: flex; flex-direction: column; gap: 10px;"">
                <button class=""btn"" onclick=""openGoogleEarth()"" data-i18n=""google_earth_btn"">🌐 Obtener coordenadas desde Google Earth</button>
                <button id=""btn-topography"" class=""btn tooltip-trigger"" data-tooltip-i18n=""tooltip_generate_topography"" onclick=""generateTopography()"" disabled>🗺️ Generar Topografía</button>
                <button id=""btn-climate"" class=""btn tooltip-trigger"" data-tooltip-i18n=""tooltip_climate_analysis"" onclick=""climateAnalysis()"" disabled>🌤️ Análisis Climático</button>
            </div>
        </div>
    </div>
    <div id=""tooltip-container""><span id=""tooltip-text""></span><span id=""tooltip-manual-icon"" onclick=""openManualFromTooltip()"">📖</span></div>
    <div class=""modal-overlay"" id=""program-modal"">
        <div class=""modal-content"">
            <div class=""modal-header""><h3 data-i18n=""program_modal_title"">Redactar Programa de Necesidades</h3><button class=""btn-remove"" onclick=""cancelProgram()"" style=""font-size: 1.5em; background:none; border:none; color:var(--text-muted); cursor:pointer;"">x</button></div>
            <div class=""modal-body"">
                <div class=""modal-guide"" data-i18n=""program_guide"">Preguntas clave a considerar en tu descripción:
• Metros cuadrados totales de construcción.
• ¿Cuántos pisos tendrá el proyecto?
• Áreas funcionales por piso y sus dimensiones deseadas.
• Orientación preferida para el acceso principal.
• Vistas que deseas rescatar para el living u otros espacios.</div>
                <textarea id=""program_textarea"" placeholder=""Escribe aquí tu visión completa del proyecto...""></textarea>
            </div>
            <div class=""modal-footer""><button class=""btn"" onclick=""cancelProgram()"" data-i18n=""cancel"">Cancelar</button><button class=""btn-primary"" style=""width: auto; margin: 0; padding: 10px 30px;"" onclick=""saveProgram()"" data-i18n=""done"">Listo</button></div>
        </div>
    </div>
    <div class=""modal-overlay"" id=""tokens-modal"">
        <div class=""modal-content modal-content-small"">
            <div class=""modal-header""><h3 data-i18n=""insufficient_tokens_title"">Tokens Insuficientes</h3></div>
            <div class=""modal-body"" style=""justify-content: center; align-items: center; text-align: center;""><p data-i18n=""insufficient_tokens_msg"">No tienes suficientes tokens para generar el proyecto. Por favor, recarga para continuar.</p></div>
            <div class=""modal-footer""><button class=""btn"" onclick=""closeTokensModal()"" data-i18n=""cancel"">Cancelar</button><button class=""btn-primary"" style=""width: auto; margin: 0; padding: 10px 20px;"" onclick=""closeTokensModal(); openRechargeModal();"" data-i18n=""recharge_now"">Recargar ahora</button></div>
        </div>
    </div>
    <div class=""modal-overlay"" id=""recharge-modal"">
        <div class=""modal-content modal-content-small"">
            <div class=""modal-header""><h3 data-i18n=""recharge_tokens_title"">Recargar Tokens</h3><button class=""btn-remove"" onclick=""closeRechargeModal()"" style=""font-size: 1.5em; background:none; border:none; color:var(--text-muted); cursor:pointer;"">x</button></div>
            <div class=""modal-body"" style=""padding: 20px;"">
                <div class=""form-group""><label data-i18n=""amount_usd"">Monto (USD/EUR)</label><input type=""number"" id=""recharge-amount"" class=""form-control"" min=""1"" step=""1"" value=""10""></div>
                <div style=""display: flex; gap: 5px; margin-bottom: 15px;""><button class=""btn-quick-amount"" onclick=""setQuickAmount(10)"">10</button><button class=""btn-quick-amount"" onclick=""setQuickAmount(20)"">20</button><button class=""btn-quick-amount"" onclick=""setQuickAmount(50)"">50</button><button class=""btn-quick-amount"" onclick=""setQuickAmount(100)"">100</button></div>
                <div id=""payment-url-container"" style=""display:none; margin-top: 15px; border: 1px dashed var(--border-color); padding: 15px; text-align: center; background-color: var(--bg-input);""><p style=""font-size: 0.85em; margin-bottom: 10px;"" data-i18n=""payment_link"">Enlace de pago generado:</p><a id=""payment-link"" href=""#"" target=""_blank"" class=""btn-primary"" style=""text-decoration: none; display: inline-block; width: auto; padding: 8px 15px; margin: 0;"" data-i18n=""go_to_pay"">Ir a Pagar</a></div>
            </div>
            <div class=""modal-footer""><button class=""btn"" onclick=""closeRechargeModal()"" data-i18n=""cancel"">Cancelar</button><button class=""btn-primary"" style=""width: auto; margin: 0; padding: 10px 20px;"" onclick=""confirmRecharge()"" data-i18n=""pay"">Pagar</button></div>
        </div>
    </div>
    <div class=""modal-overlay"" id=""clarification-modal"">
        <div class=""modal-content"">
            <div class=""modal-header""><div><h3 data-i18n=""clarification_title"">Aclaraciones Necesarias</h3><p id=""clarification-round-counter"" style=""font-size: 0.85em; color: var(--text-muted); margin-top: 5px; font-weight: bold;""></p></div><button class=""btn-remove"" onclick=""closeClarificationModal()"" style=""font-size: 1.5em; background:none; border:none; color:var(--text-muted); cursor:pointer;"">x</button></div>
            <div class=""modal-body"" id=""clarification-body"" style=""padding: 20px; overflow-y: auto;"">
                <div id=""clarification-spinner-container""><div class=""generation-row"" style=""background: var(--bg-input); padding: 20px; border-radius: 8px; border: 1px solid var(--border-color);""><div class=""spinner""></div><span data-i18n=""clarifying"">Aclarando información…</span></div></div>
                <div id=""clarification-questions-inner""></div>
            </div>
            <div class=""modal-footer""><button class=""btn"" onclick=""closeClarificationModal()"" data-i18n=""cancel"">Cancelar</button><button class=""btn-primary"" id=""submit-clar-btn"" style=""width: auto; margin: 0; padding: 10px 20px;"" onclick=""submitClarifications()"" data-i18n=""submit_answers"">Enviar respuestas</button></div>
        </div>
    </div>
    <div class=""modal-overlay"" id=""manual-modal"">
        <div class=""modal-content"">
            <div class=""modal-header""><h3 data-i18n=""manual_title"">Manual de ZBIM-Copilot</h3><button class=""btn-remove"" onclick=""closeManualModal()"" style=""font-size: 1.5em; background:none; border:none; color:var(--text-muted); cursor:pointer;"">x</button></div>
            <div class=""modal-body"" id=""manual-content"" style=""padding: 20px; overflow-y: auto;"">
                <h2 data-i18n=""manual_h2_title"">Manual de ZBIM-Copilot</h2>
                <div id=""manual-chapter-1""><h3 data-i18n=""manual_h3_1"">1. Introducción</h3><p data-i18n=""manual_p_1"">ZBIM‑Copilot genera proyectos arquitectónicos completos a partir de una descripción textual...</p></div>
                <div id=""manual-chapter-2""><h3 data-i18n=""manual_h3_2"">2. Nombre y Ubicación</h3><p data-i18n=""manual_p_2"">Introduzca el nombre del proyecto y la ciudad/país donde se ubicará...</p></div>
                <div id=""manual-chapter-3""><h3 data-i18n=""manual_h3_3"">3. Programa de Necesidades</h3><p data-i18n=""manual_p_3"">Describa los espacios que necesita el edificio...</p></div>
                <div id=""manual-chapter-4""><h3 data-i18n=""manual_h3_4"">4. Terreno y Retiros</h3><p data-i18n=""manual_p_4"">Configure las dimensiones del solar y los retiros normativos...</p></div>
                <div id=""manual-chapter-5""><h3 data-i18n=""manual_h3_5"">5. Plantas y Altura</h3><p data-i18n=""manual_p_5"">Indique el número de plantas y la altura máxima...</p></div>
                <div id=""manual-chapter-6""><h3 data-i18n=""manual_h3_6"">6. Normativa</h3><p data-i18n=""manual_p_6"">Seleccione la normativa aplicable o cargue un archivo PDF...</p></div>
                <div id=""manual-chapter-7""><h3 data-i18n=""manual_h3_7"">7. Estilo y Materiales</h3><p data-i18n=""manual_p_7"">Elija el lenguaje arquitectónico y los materiales predominantes...</p></div>
                <div id=""manual-chapter-8""><h3 data-i18n=""manual_h3_8"">8. Tokens y Generación</h3><p data-i18n=""manual_p_8"">Recargue tokens para análisis de imágenes y pulse Generar Proyecto...</p></div>
                <div id=""manual-chapter-9""><h3 data-i18n=""manual_h3_9"">9. Solución de Problemas</h3><p data-i18n=""manual_p_9"">Si el proyecto no se genera, verifique su conexión a Ollama...</p></div>
            </div>
        </div>
    </div>
    <div class=""modal-overlay"" id=""climate-modal"">
        <div class=""modal-content modal-content-small"">
            <div class=""modal-header""><h3 data-i18n=""climate_title"">Análisis Climático</h3><button class=""btn-remove"" onclick=""closeClimateModal()"" style=""font-size: 1.5em; background:none; border:none; color:var(--text-muted); cursor:pointer;"">x</button></div>
            <div class=""modal-body"" id=""climate-body"" style=""padding: 20px; line-height: 1.6; font-size: 0.9em;""></div>
            <div class=""modal-footer""><button class=""btn"" onclick=""closeClimateModal()"" data-i18n=""close"">Cerrar</button></div>
        </div>
    </div>
    <script>
        const i18n = {
            es: {
                project_name: ""Nombre del Proyecto"",
                location: ""Ciudad / País (referencia general)"",
                latitude: ""Latitud"",
                longitude: ""Longitud"",
                radius: ""Radio del lote (metros)"",
                google_earth_btn: ""🌐 Obtener coordenadas desde Google Earth"",
                project_type: ""Tipo de Proyecto"",
                site_dims: ""Dimensiones del Terreno"",
                upload_site: ""Subir Plano del Terreno (PDF/DWG)"",
                program_reqs: ""Programa de Necesidades"",
                draft_program: ""Redactar programa de necesidades"",
                upload_program: ""Subir Programa (PDF/DOCX)"",
                applicable_code: ""Normativa Aplicable"",
                upload_new_code: ""Cargar nueva normativa"",
                style_refs: ""Referencias de Estilo"",
                generate_project: ""Generar Proyecto"",
                ref_gallery: ""Galería de Referencias"",
                program_modal_title: ""Redactar Programa de Necesidades"",
                cancel: ""Cancelar"",
                done: ""Listo"",
                hdr_config: ""Configuración del Proyecto"",
                err_required: ""Error: Faltan campos obligatorios."",
                log_packaging: ""Empaquetando configuración del proyecto..."",
                log_sent: ""Configuración enviada al backend correctamente."",
                log_err_send: ""Error crítico enviando mensaje a C#: "",
                log_processing: ""Procesando..."",
                log_code_added: ""Normativa añadida: "",
                log_code_removed: ""Normativa eliminada: "",
                log_code_exists: ""Error: Ya existe una normativa con ese nombre."",
                ask_code_name: ""Introduce un nombre obligatorio para esta normativa:"",
                ask_confirm_delete: ""¿Seguro que deseas eliminar esta normativa?"",
                program_loaded: ""Programa cargado (clic para editar)"",
                program_guide: ""Preguntas clave a considerar en tu descripción:\n• Metros cuadrados totales de construcción.\n• ¿Cuántos pisos tendrá el proyecto?\n• Áreas funcionales por piso y sus dimensiones deseadas.\n• Orientación preferida para el acceso principal.\n• Vistas que deseas rescatar para el living u otros espacios."",
                tokens: ""Tokens"",
                recharge: ""Recargar"",
                insufficient_tokens_title: ""Tokens Insuficientes"",
                insufficient_tokens_msg: ""No tienes suficientes tokens para generar el proyecto. Por favor, recarga para continuar."",
                recharge_now: ""Recargar ahora"",
                token_balance: ""Saldo de tokens"",
                recharge_tokens_title: ""Recargar Tokens"",
                amount_usd: ""Monto (USD/EUR)"",
                pay: ""Pagar"",
                payment_link: ""Enlace de pago generado:"",
                go_to_pay: ""Ir a Pagar"",
                clarification_title: ""Aclaraciones Necesarias"",
                clarification_round: ""Ronda {0}/{1}"",
                clarifying: ""Aclarando información…"",
                submit_answers: ""Enviar respuestas"",
                generating_oas: ""Generando OAS con IA..."",
                cancel_generation: ""Cancelar generación"",
                tooltip_generate_topography: ""Obtiene la topografía del terreno desde OpenTopography."",
                tooltip_climate_analysis: ""Obtiene datos climáticos de Open-Meteo (temperatura, viento, sol, humedad)."",
                climate_title: ""Análisis Climático"",
                close: ""Cerrar"",
                tooltip_project_name: ""Nombre del proyecto arquitectónico. Ej: Casa unifamiliar."",
                tooltip_location: ""Ciudad y país donde se ubicará el proyecto (referencia general)."",
                tooltip_latitude: ""Latitud exacta del lote (obtenida de Google Earth)."",
                tooltip_longitude: ""Longitud exacta del lote (obtenida de Google Earth)."",
                tooltip_radius: ""Radio de análisis del terreno en metros."",
                tooltip_project_type: ""Tipo de proyecto: vivienda, oficinas, comercial, etc."",
                tooltip_site_description: ""Describe las dimensiones y forma del terreno."",
                tooltip_upload_site: ""Carga un plano del terreno (PDF o DWG)."",
                tooltip_retreat_front: ""Distancia mínima al frente del terreno (m)."",
                tooltip_retreat_side: ""Distancia mínima a los laterales del terreno (m)."",
                tooltip_retreat_back: ""Distancia mínima al fondo del terreno (m)."",
                tooltip_implantation_area: ""Superficie máxima ocupada por el edificio (m²)."",
                tooltip_floors_above: ""Número de plantas sobre el nivel de la calle."",
                tooltip_floors_below: ""Número de plantas bajo el nivel de la calle (sótanos)."",
                tooltip_program_reqs: ""Describe los espacios que necesitas (dormitorios, baños, etc.)."",
                tooltip_applicable_code: ""Selecciona o carga la normativa aplicable al proyecto."",
                tooltip_style_refs: ""Elige los materiales predominantes del edificio."",
                tooltip_style_language: ""Lenguaje arquitectónico: moderno, clásico, minimalista..."",
                tooltip_max_height: ""Altura máxima permitida o deseada en metros."",
                tooltip_climate_conditions: ""Condiciones climáticas relevantes: frío, calor, humedad, viento."",
                tooltip_generate_project: ""Genera el proyecto arquitectónico completo con IA."",
                tooltip_recharge: ""Recarga tokens para usar la IA de análisis de imágenes."",
                tooltip_ref_gallery: ""Arrastra imágenes de referencia para inspirar el diseño."",
                manual_title: ""Manual de ZBIM-Copilot"",
                manual_h2_title: ""Manual de ZBIM-Copilot"",
                manual_h3_1: ""1. Introducción"",
                manual_p_1: ""ZBIM‑Copilot genera proyectos arquitectónicos completos a partir de una descripción textual..."",
                manual_h3_2: ""2. Nombre y Ubicación"",
                manual_p_2: ""Introduzca el nombre del proyecto y la ciudad/país donde se ubicará..."",
                manual_h3_3: ""3. Programa de Necesidades"",
                manual_p_3: ""Describa los espacios que necesita el edificio..."",
                manual_h3_4: ""4. Terreno y Retiros"",
                manual_p_4: ""Configure las dimensiones del solar y los retiros normativos..."",
                manual_h3_5: ""5. Plantas y Altura"",
                manual_p_5: ""Indique el número de plantas y la altura máxima..."",
                manual_h3_6: ""6. Normativa"",
                manual_p_6: ""Seleccione la normativa aplicable o cargue un archivo PDF..."",
                manual_h3_7: ""7. Estilo y Materiales"",
                manual_p_7: ""Elija el lenguaje arquitectónico y los materiales predominantes..."",
                manual_h3_8: ""8. Tokens y Generación"",
                manual_p_8: ""Recargue tokens para análisis de imágenes y pulse Generar Proyecto..."",
                manual_h3_9: ""9. Solución de Problemas"",
                manual_p_9: ""Si el proyecto no se genera, verifique su conexión a Ollama...""
            },
            en: {
                project_name: ""Project Name"",
                location: ""City / Country (general reference)"",
                latitude: ""Latitude"",
                longitude: ""Longitude"",
                radius: ""Plot radius (meters)"",
                google_earth_btn: ""🌐 Get coordinates from Google Earth"",
                project_type: ""Project Type"",
                site_dims: ""Site Dimensions"",
                upload_site: ""Upload Site Plan (PDF/DWG)"",
                program_reqs: ""Program of Requirements"",
                draft_program: ""Draft program of requirements"",
                upload_program: ""Upload Program (PDF/DOCX)"",
                applicable_code: ""Applicable Code"",
                upload_new_code: ""Upload new code"",
                style_refs: ""Style References"",
                generate_project: ""Generate Project"",
                ref_gallery: ""References Gallery"",
                program_modal_title: ""Draft Program of Requirements"",
                cancel: ""Cancel"",
                done: ""Done"",
                hdr_config: ""Project Configuration"",
                err_required: ""Error: Missing required fields."",
                log_packaging: ""Packaging project configuration..."",
                log_sent: ""Configuration sent to backend successfully."",
                log_err_send: ""Critical error sending message to C#: "",
                log_processing: ""Processing..."",
                log_code_added: ""Code added: "",
                log_code_removed: ""Code removed: "",
                log_code_exists: ""Error: A code with that name already exists."",
                ask_code_name: ""Enter a mandatory name for this code:"",
                ask_confirm_delete: ""Are you sure you want to delete this code?"",
                program_loaded: ""Program loaded (click to edit)"",
                program_guide: ""Key questions to consider in your description:\n• Total square meters of construction.\n• How many floors will the project have?\n• Functional areas per floor and their desired dimensions.\n• Preferred orientation for the main access.\n• Views you want to rescue for the living room or other spaces."",
                tokens: ""Tokens"",
                recharge: ""Recharge"",
                insufficient_tokens_title: ""Insufficient Tokens"",
                insufficient_tokens_msg: ""You do not have enough tokens to generate the project. Please recharge to continue."",
                recharge_now: ""Recharge now"",
                token_balance: ""Token balance"",
                recharge_tokens_title: ""Recharge Tokens"",
                amount_usd: ""Amount (USD/EUR)"",
                pay: ""Pay"",
                payment_link: ""Generated payment link:"",
                go_to_pay: ""Go to Pay"",
                clarification_title: ""Clarifications Needed"",
                clarification_round: ""Round {0}/{1}"",
                clarifying: ""Clarifying information…"",
                submit_answers: ""Submit answers"",
                generating_oas: ""Generating OAS with AI..."",
                cancel_generation: ""Cancel generation"",
                tooltip_generate_topography: ""Gets site topography from OpenTopography."",
                tooltip_climate_analysis: ""Gets climate data from Open-Meteo (temperature, wind, sun, humidity)."",
                climate_title: ""Climate Analysis"",
                close: ""Close"",
                tooltip_project_name: ""Name of the architectural project. Ex: Single family house."",
                tooltip_location: ""City and country where the project will be located (general reference)."",
                tooltip_latitude: ""Exact latitude of the plot (from Google Earth)."",
                tooltip_longitude: ""Exact longitude of the plot (from Google Earth)."",
                tooltip_radius: ""Plot analysis radius in meters."",
                tooltip_project_type: ""Project type: residential, offices, commercial, etc."",
                tooltip_site_description: ""Describe the dimensions and shape of the site."",
                tooltip_upload_site: ""Upload a site plan (PDF or DWG)."",
                tooltip_retreat_front: ""Minimum distance to the front of the site (m)."",
                tooltip_retreat_side: ""Minimum distance to the sides of the site (m)."",
                tooltip_retreat_back: ""Minimum distance to the back of the site (m)."",
                tooltip_implantation_area: ""Maximum area occupied by the building (m²)."",
                tooltip_floors_above: ""Number of floors above ground level."",
                tooltip_floors_below: ""Number of floors below ground level (basements)."",
                tooltip_program_reqs: ""Describe the spaces you need (bedrooms, bathrooms, etc.)."",
                tooltip_applicable_code: ""Select or upload the applicable building code."",
                tooltip_style_refs: ""Choose the predominant materials of the building."",
                tooltip_style_language: ""Architectural style: modern, classic, minimalist..."",
                tooltip_max_height: ""Maximum allowed or desired height in meters."",
                tooltip_climate_conditions: ""Relevant climatic conditions: cold, hot, humid, windy."",
                tooltip_generate_project: ""Generate the complete architectural project with AI."",
                tooltip_recharge: ""Recharge tokens to use the image analysis AI."",
                tooltip_ref_gallery: ""Drag reference images to inspire the design."",
                manual_title: ""ZBIM-Copilot Manual"",
                manual_h2_title: ""ZBIM-Copilot Manual"",
                manual_h3_1: ""1. Introduction"",
                manual_p_1: ""ZBIM-Copilot generates complete architectural projects from a textual description..."",
                manual_h3_2: ""2. Name and Location"",
                manual_p_2: ""Enter the project name and the city/country where it will be located..."",
                manual_h3_3: ""3. Program of Requirements"",
                manual_p_3: ""Describe the spaces needed by the building..."",
                manual_h3_4: ""4. Site and Setbacks"",
                manual_p_4: ""Configure the site dimensions and regulatory setbacks..."",
                manual_h3_5: ""5. Floors and Height"",
                manual_p_5: ""Indicate the number of floors and maximum height..."",
                manual_h3_6: ""6. Building Code"",
                manual_p_6: ""Select the applicable code or upload a PDF file..."",
                manual_h3_7: ""7. Style and Materials"",
                manual_p_7: ""Choose the architectural style and predominant materials..."",
                manual_h3_8: ""8. Tokens and Generation"",
                manual_p_8: ""Recharge tokens for image analysis and press Generate Project..."",
                manual_h3_9: ""9. Troubleshooting"",
                manual_p_9: ""If the project is not generated, verify your connection to Ollama...""
            }
        };
        let lang = navigator.language.startsWith('es') ? 'es' : 'en';
        function t(key) { return i18n[lang][key] || key; }
        function applyTranslations() {
            document.querySelectorAll('[data-i18n]').forEach(el => {
                const key = el.getAttribute('data-i18n');
                if (el.innerHTML.includes('*')) {
                    el.innerHTML = t(key) + ' <span class=""required-asterisk"">*</span>';
                } else if (el.tagName === ""INPUT"" || el.tagName === ""TEXTAREA"") {
                    el.placeholder = t(key);
                } else {
                    el.innerText = t(key);
                }
            });
            document.documentElement.lang = lang;
        }
        let programText = """"; let codesList = []; let imagesList = []; let sitePlanBase64 = null; let programDocBase64 = null; let tokenBalance = 0; let generationInProgress = false; const MAX_TOKENS = 10000;
        function addLog(message) {
            const logs = document.getElementById('logs-output');
            const entry = document.createElement('div');
            entry.className = 'log-entry';
            const timestamp = new Date().toLocaleTimeString();
            entry.innerText = `[${timestamp}] ${message}`;
            logs.appendChild(entry);
            logs.scrollTop = logs.scrollHeight;
        }
        function toggleOtherType() {
            const select = document.getElementById('project_type');
            const container = document.getElementById('other_type_container');
            if (select.value === 'Otros') { container.style.display = 'block'; } else { container.style.display = 'none'; }
        }
        const tooltipContainer = document.getElementById('tooltip-container');
        const tooltipText = document.getElementById('tooltip-text');
        const tooltipIcon = document.getElementById('tooltip-manual-icon');
        let tooltipShowTimeout;
        let tooltipHideTimeout;
        let currentManualChapter = null;
        const tooltipManualMap = {
            ""tooltip_project_name"": ""manual-chapter-2"",
            ""tooltip_location"": ""manual-chapter-2"",
            ""tooltip_latitude"": ""manual-chapter-2"",
            ""tooltip_longitude"": ""manual-chapter-2"",
            ""tooltip_radius"": ""manual-chapter-4"",
            ""tooltip_project_type"": ""manual-chapter-2"",
            ""tooltip_site_description"": ""manual-chapter-4"",
            ""tooltip_upload_site"": ""manual-chapter-4"",
            ""tooltip_retreat_front"": ""manual-chapter-4"",
            ""tooltip_retreat_side"": ""manual-chapter-4"",
            ""tooltip_retreat_back"": ""manual-chapter-4"",
            ""tooltip_implantation_area"": ""manual-chapter-4"",
            ""tooltip_floors_above"": ""manual-chapter-5"",
            ""tooltip_floors_below"": ""manual-chapter-5"",
            ""tooltip_program_reqs"": ""manual-chapter-3"",
            ""tooltip_applicable_code"": ""manual-chapter-6"",
            ""tooltip_style_refs"": ""manual-chapter-7"",
            ""tooltip_style_language"": ""manual-chapter-7"",
            ""tooltip_max_height"": ""manual-chapter-5"",
            ""tooltip_climate_conditions"": ""manual-chapter-7"",
            ""tooltip_generate_project"": ""manual-chapter-8"",
            ""tooltip_recharge"": ""manual-chapter-8"",
            ""tooltip_ref_gallery"": ""manual-chapter-1"",
            ""tooltip_generate_topography"": ""manual-chapter-4"",
            ""tooltip_climate_analysis"": ""manual-chapter-7""
        };
        function initTooltips() {
            document.querySelectorAll('[data-tooltip-i18n]').forEach(el => {
                el.addEventListener('mouseenter', () => showTooltip(el));
                el.addEventListener('mouseleave', () => hideTooltip());
            });
            tooltipContainer.addEventListener('mouseenter', () => { clearTimeout(tooltipHideTimeout); });
            tooltipContainer.addEventListener('mouseleave', () => { hideTooltip(); });
        }
        function showTooltip(element) {
            clearTimeout(tooltipHideTimeout);
            clearTimeout(tooltipShowTimeout);
            tooltipShowTimeout = setTimeout(() => {
                const key = element.getAttribute('data-tooltip-i18n');
                tooltipText.innerText = t(key);
                currentManualChapter = tooltipManualMap[key];
                if (currentManualChapter) { tooltipIcon.style.display = 'inline'; } else { tooltipIcon.style.display = 'none'; }
                const rect = element.getBoundingClientRect();
                tooltipContainer.style.top = `${rect.bottom + 10}px`;
                tooltipContainer.style.left = `${rect.left}px`;
                tooltipContainer.style.display = 'block';
            }, 500);
        }
        function hideTooltip() {
            clearTimeout(tooltipShowTimeout);
            tooltipHideTimeout = setTimeout(() => { tooltipContainer.style.display = 'none'; }, 200);
        }
        function openManualFromTooltip() {
            if (currentManualChapter) { openManualModal(currentManualChapter); tooltipContainer.style.display = 'none'; }
        }
        function openManualModal(chapterId) {
            document.getElementById('manual-modal').classList.add('active');
            if (chapterId) { setTimeout(() => { const chapter = document.getElementById(chapterId); if (chapter) chapter.scrollIntoView({ behavior: 'smooth', block: 'start' }); }, 100); }
        }
        function closeManualModal() { document.getElementById('manual-modal').classList.remove('active'); }
        function openProgramModal() {
            document.getElementById('program-modal').classList.add('active');
            document.getElementById('program_textarea').value = programText;
            document.getElementById('program_textarea').focus();
        }
        function saveProgram() {
            programText = document.getElementById('program_textarea').value;
            document.getElementById('program-modal').classList.remove('active');
            const summaryDiv = document.getElementById('program_summary');
            if (programText.trim().length > 0) { summaryDiv.innerText = t('program_loaded'); } else { summaryDiv.innerText = """"; }
        }
        function cancelProgram() { document.getElementById('program-modal').classList.remove('active'); }
        function closeTokensModal() { document.getElementById('tokens-modal').classList.remove('active'); }
        function openRechargeModal() { document.getElementById('recharge-modal').classList.add('active'); document.getElementById('payment-url-container').style.display = 'none'; }
        function closeRechargeModal() { document.getElementById('recharge-modal').classList.remove('active'); }
        function setQuickAmount(amount) { document.getElementById('recharge-amount').value = amount; }
        function confirmRecharge() {
            const amount = parseInt(document.getElementById('recharge-amount').value);
            if (!amount || amount <= 0) return;
            addLog(""Solicitando pago de "" + amount + "" para tokens..."");
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""recharge_tokens"", amount: amount }));
        }
        function showClarificationModal(questions, round, maxRounds) {
            maxRounds = maxRounds || 3;
            const body = document.getElementById('clarification-questions-inner');
            body.innerHTML = '';
            questions.forEach((q, index) => {
                const div = document.createElement('div');
                div.className = 'form-group';
                div.innerHTML = `<label for=""clar-${index}"">${q}</label><textarea id=""clar-${index}"" class=""form-control"" rows=""3"" placeholder=""Tu respuesta...""></textarea>`;
                body.appendChild(div);
            });
            const counter = document.getElementById('clarification-round-counter');
            counter.innerText = t('clarification_round').replace('{0}', round).replace('{1}', maxRounds);
            if (!document.getElementById('clarification-modal').classList.contains('active')) {
                document.getElementById('clarification-modal').classList.add('active');
            }
        }
        function closeClarificationModal() {
            document.getElementById('clarification-modal').classList.remove('active');
            document.getElementById('clarification-spinner-container').classList.remove('active');
            const btn = document.getElementById('submit-clar-btn');
            btn.disabled = false;
            btn.innerText = t('submit_answers');
        }
        function submitClarifications() {
            const answers = [];
            document.querySelectorAll('#clarification-questions-inner textarea').forEach(ta => answers.push(ta.value));
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""submit_clarifications"", answers: answers }));
            addLog(""Respuestas de aclaración enviadas."");
            const btn = document.getElementById('submit-clar-btn');
            btn.disabled = true;
            btn.innerText = t('clarifying');
            document.getElementById('clarification-spinner-container').classList.add('active');
        }
        function handleSitePlanUpload(files) {
            if (!files || files.length === 0) return;
            const file = files[0];
            const reader = new FileReader();
            reader.onload = function(e) { sitePlanBase64 = e.target.result; document.getElementById('site_plan_name').innerText = file.name; addLog(""Plano del terreno cargado: "" + file.name); };
            reader.readAsDataURL(file);
            document.getElementById('site_plan_file').value = '';
        }
        function handleProgramDocUpload(files) {
            if (!files || files.length === 0) return;
            const file = files[0];
            const reader = new FileReader();
            reader.onload = function(e) { programDocBase64 = e.target.result; document.getElementById('program_doc_name').innerText = file.name; addLog(""Documento de programa cargado: "" + file.name); };
            reader.readAsDataURL(file);
            document.getElementById('program_doc_file').value = '';
        }
        function handleNormativeUpload(files) {
            if (!files || files.length === 0) return;
            const file = files[0];
            let name = prompt(t('ask_code_name'), file.name.replace('.pdf', ''));
            if (name && name.trim() !== """") {
                name = name.trim();
                if (codesList.some(c => c.name === name)) { addLog(t('log_code_exists')); return; }
                const reader = new FileReader();
                reader.onload = function(e) { codesList.push({ name: name, data: e.target.result }); updateNormativeUI(); addLog(t('log_code_added') + name); };
                reader.readAsDataURL(file);
            }
            document.getElementById('normative_file_input').value = '';
        }
        function removeNormative() {
            const select = document.getElementById('normative_select');
            if (select.value !== """") {
                if (confirm(t('ask_confirm_delete'))) {
                    const index = parseInt(select.value);
                    const removed = codesList.splice(index, 1)[0];
                    updateNormativeUI();
                    addLog(t('log_code_removed') + (removed ? removed.name : ''));
                }
            }
        }
        function updateNormativeUI() {
            const select = document.getElementById('normative_select');
            select.innerHTML = `<option value="""">- ${lang === 'es' ? 'Sin normativa' : 'No code'} -</option>`;
            codesList.forEach((code, index) => { const opt = document.createElement('option'); opt.value = index; opt.innerText = code.name; select.appendChild(opt); });
            const listDiv = document.getElementById('codes-info-list');
            if (codesList.length === 0) { listDiv.innerHTML = `<div class=""info-item"">${lang === 'es' ? 'No hay normativas cargadas' : 'No codes loaded'}</div>`; }
            else { listDiv.innerHTML = ''; codesList.forEach(code => listDiv.innerHTML += `<div class=""info-item"">${code.name}</div>`); }
        }
        function handleDragOver(e) { e.preventDefault(); e.stopPropagation(); document.getElementById('drop-zone').classList.add('dragover'); }
        function handleDragLeave(e) { e.preventDefault(); e.stopPropagation(); document.getElementById('drop-zone').classList.remove('dragover'); }
        function handleDrop(e) { e.preventDefault(); e.stopPropagation(); document.getElementById('drop-zone').classList.remove('dragover'); handleFiles(e.dataTransfer.files); }
        function handleFiles(files) {
            if (!files || files.length === 0) return;
            Array.from(files).forEach(file => {
                if (file.type.startsWith('image/')) {
                    const reader = new FileReader();
                    reader.onload = function(e) { imagesList.push({ name: file.name, data: e.target.result }); renderImages(); addLog(""Imagen añadida: "" + file.name); };
                    reader.readAsDataURL(file);
                }
            });
            document.getElementById('image_upload').value = '';
        }
        function renderImages() {
            const grid = document.getElementById('image-grid');
            grid.innerHTML = '';
            imagesList.forEach((img, index) => {
                const div = document.createElement('div');
                div.className = 'image-item';
                div.innerHTML = `<img src=""${img.data}"" alt=""${img.name}""><button class=""remove-btn"" onclick=""removeImage(${index})"">X</button>`;
                grid.appendChild(div);
            });
        }
        function removeImage(index) { const removed = imagesList.splice(index, 1)[0]; renderImages(); if (removed) addLog(""Imagen eliminada: "" + removed.name); }
        function updateTokenBalance(newBalance) {
            tokenBalance = newBalance;
            document.getElementById('token-balance').innerText = tokenBalance;
            const generateBtn = document.getElementById('generate-btn');
            const tokensModal = document.getElementById('tokens-modal');
            const rechargeModal = document.getElementById('recharge-modal');
            if (tokenBalance > 0) {
                if (!generationInProgress) generateBtn.disabled = false;
                if (tokensModal.classList.contains('active')) tokensModal.classList.remove('active');
                if (rechargeModal.classList.contains('active')) rechargeModal.classList.remove('active');
            } else { generateBtn.disabled = true; }
            const usage = MAX_TOKENS > 0 ? 100 - Math.min(100, (tokenBalance / MAX_TOKENS) * 100) : 100;
            const progressBar = document.getElementById('token-progress-bar');
            progressBar.style.width = `${100 - usage}%`;
            if (usage > 80) progressBar.style.backgroundColor = 'var(--danger)';
            else if (usage > 50) progressBar.style.backgroundColor = 'var(--warning)';
            else progressBar.style.backgroundColor = 'var(--success)';
            document.getElementById('token-usage-percent').innerText = `${Math.round(100 - usage)}% disp.`;
        }
        function setGenerationUI(isGenerating) {
            generationInProgress = isGenerating;
            const generateBtn = document.getElementById('generate-btn');
            const genContainer = document.getElementById('generation-container');
            if (isGenerating) { generateBtn.disabled = true; generateBtn.innerText = t('generating_oas'); genContainer.classList.add('active'); }
            else { generateBtn.disabled = tokenBalance <= 0; generateBtn.innerText = t('generate_project'); genContainer.classList.remove('active'); }
        }
        function cancelGeneration() { window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""cancel_generation"" })); addLog(""Solicitando cancelación de la generación...""); }
        function updateEnvironmentButtons() {
            const lat = parseFloat(document.getElementById('latitude').value);
            const lon = parseFloat(document.getElementById('longitude').value);
            const btnTopo = document.getElementById('btn-topography');
            const btnClim = document.getElementById('btn-climate');
            const isValidLat = !isNaN(lat) && lat >= -90 && lat <= 90;
            const isValidLon = !isNaN(lon) && lon >= -180 && lon <= 180;
            const isValid = isValidLat && isValidLon;
            btnTopo.disabled = !isValid;
            btnClim.disabled = !isValid;
        }
        function openGoogleEarth() {
            const locationVal = document.getElementById('location').value.trim() || ""Earth"";
            const url = `https://earth.google.com/web/search/${encodeURIComponent(locationVal)}`;
            addLog(""Solicitando abrir Google Earth para: "" + locationVal);
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""open_url_in_browser"", url: url }));
        }
        function generateTopography() {
            const lat = parseFloat(document.getElementById('latitude').value);
            const lon = parseFloat(document.getElementById('longitude').value);
            const rad = parseInt(document.getElementById('radius').value) || 200;
            if (isNaN(lat) || isNaN(lon)) { addLog(""Error: Coordenadas inválidas para topografía.""); return; }
            addLog(""Solicitando generación de topografía..."");
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""generate_topography"", latitude: lat, longitude: lon, radius: rad }));
        }
        function climateAnalysis() {
            const lat = parseFloat(document.getElementById('latitude').value);
            const lon = parseFloat(document.getElementById('longitude').value);
            if (isNaN(lat) || isNaN(lon)) { addLog(""Error: Coordenadas inválidas para clima.""); return; }
            addLog(""Solicitando análisis climático..."");
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""climate_analysis"", latitude: lat, longitude: lon }));
        }
        function showClimateModal(data) {
            const body = document.getElementById('climate-body');
            let obj = data;
            if (typeof data === 'string') { try { obj = JSON.parse(data); } catch(e) { obj = {}; } }
            const climate = obj.climate || {};
            body.innerHTML = `<p><strong>🌡️ Temperatura media:</strong> ${climate.temperature_avg ?? 'N/D'} °C</p><p><strong>💨 Viento medio:</strong> ${climate.wind_speed_avg ?? 'N/D'} km/h (${climate.wind_direction_dominant ?? 'N/D'})</p><p><strong>☀️ Radiación solar media:</strong> ${climate.solar_radiation_avg ?? 'N/D'} W/m²</p><p><strong>💧 Humedad media:</strong> ${climate.humidity_avg ?? 'N/D'} %</p>`;
            document.getElementById('climate-modal').classList.add('active');
        }
        function closeClimateModal() { document.getElementById('climate-modal').classList.remove('active'); }
        function validateField(id) {
            const field = document.getElementById(id);
            if (!field.value || field.value.trim() === '') { field.classList.add('invalid'); return false; }
            else { field.classList.remove('invalid'); return true; }
        }
        function submitProject() {
            document.querySelectorAll('.invalid').forEach(el => el.classList.remove('invalid'));
            let isValid = true;
            if (!validateField('project_name')) isValid = false;
            if (!validateField('location')) isValid = false;
            if (!isValid) { addLog(t('err_required')); return; }
            if (tokenBalance <= 0) { document.getElementById('tokens-modal').classList.add('active'); return; }
            addLog(t('log_packaging'));
            const materials = [];
            document.querySelectorAll('#materials_group input[type=""checkbox""]:checked').forEach(cb => materials.push(cb.value));
            let pType = document.getElementById('project_type').value;
            if (pType === 'Otros') pType = document.getElementById('other_type').value || 'Otros';
            const configObject = {
                project_name: document.getElementById('project_name').value,
                location: document.getElementById('location').value,
                project_type: pType,
                site_description: document.getElementById('site_description').value,
                site_plan_base64: sitePlanBase64,
                retreats: {
                    front: parseFloat(document.getElementById('retreat_front').value) || 0,
                    side: parseFloat(document.getElementById('retreat_side').value) || 0,
                    back: parseFloat(document.getElementById('retreat_back').value) || 0
                },
                implantation_area: parseFloat(document.getElementById('implantation_area').value) || 0,
                floors_above: parseInt(document.getElementById('floors_above').value) || 0,
                floors_below: parseInt(document.getElementById('floors_below').value) || 0,
                program_text: programText,
                program_doc_base64: programDocBase64,
                applicable_normative: codesList,
                style_references: {
                    materials: materials,
                    style: document.getElementById('style_language').value,
                    max_height: parseFloat(document.getElementById('max_height').value) || 0,
                    climate: document.getElementById('climate_conditions').value
                },
                images_references: imagesList
            };
            try {
                setGenerationUI(true);
                window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""submit_full_project"", data: configObject }));
                addLog(t('log_sent'));
            } catch (e) {
                addLog(t('log_err_send') + e.message);
                setGenerationUI(false);
            }
        }
        window.onload = function() {
            applyTranslations();
            updateNormativeUI();
            updateTokenBalance(0);
            initTooltips();
            updateEnvironmentButtons();
            addLog(""Odysseus UI inicializada. Idioma: "" + lang.toUpperCase());
            window.chrome.webview.postMessage('UI_READY');
            window.chrome.webview.postMessage(JSON.stringify({ type: ""command"", command: ""get_balance"" }));
            window.chrome.webview.addEventListener('message', event => {
                try {
                    const msg = JSON.parse(event.data);
                    if (msg.type === ""update_balance"") { updateTokenBalance(msg.balance); }
                    else if (msg.type === ""error"" && msg.code === ""INSUFFICIENT_TOKENS"") { updateTokenBalance(0); document.getElementById('tokens-modal').classList.add('active'); addLog(""Error: Tokens insuficientes.""); }
                    else if (msg.type === ""payment_url"") { document.getElementById('payment-url-container').style.display = 'block'; document.getElementById('payment-link').href = msg.url; addLog(""URL de pago recibida.""); }
                    else if (msg.type === ""clarification_needed"") { showClarificationModal(msg.questions, msg.round || 1, msg.max_rounds || 3); }
                    else if (msg.type === ""generation_status"") {
                        if (msg.status === ""started"") { setGenerationUI(true); const clarModal = document.getElementById('clarification-modal'); if (clarModal.classList.contains('active')) closeClarificationModal(); }
                        else if (msg.status === ""completed"" || msg.status === ""cancelled"") {
                            setGenerationUI(false);
                            if (msg.status === ""completed"") addLog(""Generación completada."");
                            if (msg.status === ""cancelled"") addLog(""Generación cancelada."");
                            const clarModal = document.getElementById('clarification-modal'); if (clarModal.classList.contains('active')) closeClarificationModal();
                        } else if (msg.status === ""clarifying"") { document.getElementById('submit-clar-btn').disabled = true; document.getElementById('submit-clar-btn').innerText = t('clarifying'); document.getElementById('clarification-spinner-container').classList.add('active'); }
                    }
                    else if (msg.type === ""climate_data"") { showClimateModal(msg.data); }
                } catch (e) { if (typeof event.data === 'string') addLog(event.data); }
            });
        };
    </script>
</body>
</html>";

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
                        var handler = new TopographyHandler(points);
                        var externalEvent = ExternalEvent.Create(handler);
                        externalEvent.Raise();
                        SendMessage($"✅ Topografía creada con {points.Count} puntos.");
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
        private readonly List<XYZ> _points;

        public TopographyHandler(List<XYZ> points)
        {
            _points = points;
        }

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            using (Transaction t = new Transaction(doc, "Crear topografía desde OpenTopography"))
            {
                t.Start();
                TopographySurface.Create(doc, _points);
                t.Commit();
            }
        }

        public string GetName() => "TopographyHandler";
    }
}