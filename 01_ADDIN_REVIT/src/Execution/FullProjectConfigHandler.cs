#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.UI;
using ZBimCopilot.Knowledge;

namespace ZBIMCopilot.Execution
{
    /// <summary>
    /// Handler de configuración completa del proyecto.
    /// Recibe el JSON desde la UI, deserializa a FullProjectConfig,
    /// invoca NeufertEngine, genera OAS con IA y construye el modelo BIM.
    /// [FASE H] Envía feedback anónimo tras generación exitosa.
    /// </summary>
    public class FullProjectConfigHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<string> _configQueue = new();

        /// <summary>
        /// Token de cancelación público para abortar la generación en curso.
        /// </summary>
        public static CancellationTokenSource? CurrentCts { get; private set; }

        /// <summary>
        /// Encola una configuración JSON para procesamiento.
        /// </summary>
        public void Enqueue(string json)
        {
            if (!string.IsNullOrWhiteSpace(json))
                _configQueue.Enqueue(json);
        }

        /// <summary>
        /// Ejecuta el handler principal de Revit.
        /// </summary>
        public void Execute(UIApplication app)
        {
            if (_configQueue.IsEmpty) return;
            if (!_configQueue.TryDequeue(out string? json)) return;

            // Cancelar cualquier generación anterior si aún está activa
            CancelCurrentGeneration();

            // Lanzar el procesamiento completo en segundo plano para no bloquear el hilo principal
            Task.Run(() => ProcessConfigAsync(app, json));
        }

        /// <summary>
        /// Cancela la generación actual si está activa.
        /// </summary>
        public static void CancelCurrentGeneration()
        {
            if (CurrentCts != null)
            {
                CurrentCts.Cancel();
                CurrentCts.Dispose();
                CurrentCts = null;
            }
        }

        /// <summary>
        /// Procesa la configuración de forma asíncrona.
        /// </summary>
        private async Task ProcessConfigAsync(UIApplication app, string json)
        {
            // Crear un nuevo token de cancelación para esta generación
            CancelCurrentGeneration();
            CurrentCts = new CancellationTokenSource();
            var cancellationToken = CurrentCts.Token;

            var log = new StringBuilder();
            void Log(string msg)
            {
                log.AppendLine(msg);
                Application.Current.Dispatcher.Invoke(() =>
                    ZBIMApp.OnServerStatus?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}"));
            }

            try
            {
                Log("📋 Procesando FullProjectConfig...");

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<FullProjectConfig>(json, options);
                if (config == null)
                    throw new InvalidOperationException("JSON inválido o nulo.");

                Log($"🏛️ Proyecto: {config.ProjectName}");
                Log($"📍 Ubicación: {config.Location}");
                Log($"🏗️ Tipo: {config.EffectiveProjectType}");
                Log($"📐 Plantas: {config.FloorsAbove}↑ / {config.FloorsBelow}↓");

                // Neufert (síncrono, pero rápido)
                Log("📚 Consultando Neufert...");
                var requirements = new List<SpaceRequirement>();
                if (!string.IsNullOrWhiteSpace(config.ProgramText))
                {
                    var parsed = NeufertEngine.ParseProgramText(config.ProgramText);
                    requirements.AddRange(parsed);
                    Log($"🔍 Espacios extraídos del texto: {parsed.Count}");
                }

                var neufertMatches = NeufertEngine.QuerySpaces(config.EffectiveProjectType, requirements);
                Log($"✅ Neufert sugiere {neufertMatches.Count} espacios normativos:");
                foreach (var m in neufertMatches)
                    Log($"   • {m.SpaceName} ({m.AreaMin:F1}-{m.AreaMax:F1} m²)");

                // Generar OAS en segundo plano, con timeout de 30 segundos
                string oasJson = "";
                Log("🤖 Generando OAS con IA (máx. 30 segundos)...");
                try
                {
                    var client = new DesignIntelligenceClient();
                    // Combinar el token de cancelación del usuario con un timeout de 30s
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(30));
                    oasJson = await client.GenerateOASAsync(config, linkedCts.Token).ConfigureAwait(false);
                    Log("✅ OAS generado exitosamente");
                    Log($"📄 OAS (primeros 200 caracteres): {oasJson[..Math.Min(oasJson.Length, 200)]}...");
                }
                catch (OperationCanceledException)
                {
                    Log("⚠️ La generación del OAS fue cancelada o superó el tiempo límite (30s). Continuando sin IA.");
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Error en generación OAS: {ex.Message}. Continuando sin IA.");
                }
                finally
                {
                    if (CurrentCts == null || CurrentCts.Token == cancellationToken)
                    {
                        CurrentCts?.Dispose();
                        CurrentCts = null;
                    }
                }

                // Guardar normativas si las hay
                if (config.ApplicableNormative.Count > 0)
                {
                    Log($"💾 Guardando {config.ApplicableNormative.Count} normativa(s)...");
                    var client = new DesignIntelligenceClient();
                    foreach (var norm in config.ApplicableNormative)
                    {
                        try { client.SaveNormative(norm.Name, norm.DataBase64); }
                        catch (Exception ex) { Log($"⚠️ Error guardando normativa '{norm.Name}': {ex.Message}"); }
                    }
                }

                // Construir el modelo BIM en el hilo principal de Revit
                Log("🗺️ Generando topología y construyendo modelo...");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var topology = ProjectConfigEngine.GenerateTopologyFromFullConfig(config);
                        Log($"✅ Topología: {topology.Levels.Count} niveles");

                        var builder = new HybridProjectBuilder();
                        var layout = builder.BuildProjectLayout(topology);

                        var orchestrator = new Text2MblOrchestrator(app);
                        orchestrator.BuildFromLayout(layout);

                        Log("✅ Proyecto completo generado exitosamente.");
                        ProjectConfigEngine.StoreLastConfig(config);

                        // ============================================================
                        // FASE H: Enviar feedback anónimo para mejora continua (fire-and-forget)
                        // ============================================================
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var feedbackPayload = new
                                {
                                    installation_id = "6dd75239-10c5-4407-a8d0-a8610141bf18", // TODO: reemplazar por el real
                                    config = new
                                    {
                                        project_type = config.EffectiveProjectType,
                                        location = config.Location,
                                        floors_above = config.FloorsAbove,
                                        implantation_area = config.ImplantationArea
                                    },
                                    original_oas = string.IsNullOrEmpty(oasJson) 
                                        ? new object() 
                                        : JsonSerializer.Deserialize<object>(oasJson),
                                    modified_oas = string.IsNullOrEmpty(oasJson) 
                                        ? new object() 
                                        : JsonSerializer.Deserialize<object>(oasJson)
                                };
                                var jsonFeedback = JsonSerializer.Serialize(feedbackPayload);
                                var content = new StringContent(jsonFeedback, Encoding.UTF8, "application/json");
                                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                                await httpClient.PostAsync("http://127.0.0.1:5000/feedback", content);
                            }
                            catch { /* silencioso - no bloquear ni mostrar errores al usuario */ }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Error construyendo proyecto: {ex.Message}");
                    }
                }).Task.ConfigureAwait(false);

                // Enviar preguntas a la UI si las hay
                try
                {
                    var client = new DesignIntelligenceClient();
                    var questions = client.AskQuestions(config);
                    if (questions.Count > 0)
                    {
                        Log($"❓ El agente sugiere {questions.Count} pregunta(s) de clarificación:");
                        foreach (var q in questions) Log($"   ? {q}");
                        SendQuestionsToUI(questions);
                    }
                }
                catch (Exception ex) { Log($"⚠️ No se pudieron generar preguntas: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Log($"❌ Error crítico: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Envía preguntas de clarificación a la UI vía OdysseusPane.
        /// </summary>
        private void SendQuestionsToUI(List<string> questions)
        {
            try
            {
                var payload = new { type = "clarification_needed", questions = questions };
                string json = JsonSerializer.Serialize(payload);
                OdysseusPane.SendToUI?.Invoke(json);
            }
            catch { }
        }

        /// <summary>
        /// Nombre del handler para registro en Revit.
        /// </summary>
        public string GetName() => "FullProjectConfigHandler";
    }
}