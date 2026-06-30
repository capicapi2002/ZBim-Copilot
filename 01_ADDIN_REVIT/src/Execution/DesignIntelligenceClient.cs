#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZBimCopilot.Knowledge;

namespace ZBIMCopilot.Execution
{
    public class DesignIntelligenceClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(120);

        public DesignIntelligenceClient(string baseUrl = "http://127.0.0.1:5000")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient { Timeout = _defaultTimeout };
        }

        // ============================================================
        // HEALTH CHECK
        // ============================================================

        public bool IsAvailable()
        {
            return IsAvailableAsync().GetAwaiter().GetResult();
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/health").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        // GENERACIÓN OAS (con soporte de cancelación)
        // ============================================================

        public string GenerateOAS(FullProjectConfig config)
        {
            return GenerateOASAsync(config, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<string> GenerateOASAsync(
            FullProjectConfig config,
            CancellationToken cancellationToken = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/generate_oas");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception($"Error generando OAS: {response.StatusCode} - {error}");
                }

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"No se pudo generar OAS: {ex.Message}", ex);
            }
        }

        // ============================================================
        // NORMATIVAS
        // ============================================================

        public void SaveNormative(string name, string base64Data)
        {
            SaveNormativeAsync(name, base64Data).GetAwaiter().GetResult();
        }

        public async Task SaveNormativeAsync(
            string name,
            string base64Data,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(base64Data))
                throw new ArgumentException("Name y base64Data son obligatorios");

            var payload = new { name, content_base64 = base64Data };
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync($"{_baseUrl}/save_normative", content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception($"Error guardando normativa: {error}");
            }
        }

        public List<string> ListNormatives()
        {
            return ListNormativesAsync().GetAwaiter().GetResult();
        }

        public async Task<List<string>> ListNormativesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync($"{_baseUrl}/list_normatives", cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("normatives", out var normsEl) &&
                        normsEl.ValueKind == JsonValueKind.Array)
                    {
                        var result = new List<string>();
                        foreach (var norm in normsEl.EnumerateArray())
                        {
                            if (norm.TryGetProperty("name", out var nameEl))
                            {
                                string? name = nameEl.GetString();
                                if (!string.IsNullOrEmpty(name))
                                    result.Add(name);
                            }
                        }
                        return result;
                    }
                }
            }
            catch { }
            return new List<string>();
        }

        // ============================================================
        // TOKENS
        // ============================================================

        public int GetTokenBalance()
        {
            return GetTokenBalanceAsync().GetAwaiter().GetResult();
        }

        public async Task<int> GetTokenBalanceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync($"{_baseUrl}/token_balance", cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("balance", out var balanceEl))
                    {
                        return balanceEl.GetInt32();
                    }
                }
            }
            catch { }
            return 0;
        }

        public string InitRecharge(decimal amount)
        {
            return InitRechargeAsync(amount).GetAwaiter().GetResult();
        }

        public async Task<string> InitRechargeAsync(
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            if (amount <= 0) throw new ArgumentException("Monto debe ser mayor que 0");

            var payload = new { amount };
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync($"{_baseUrl}/init_recharge", content, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("payment_url", out var urlEl))
                {
                    return urlEl.GetString() ?? "";
                }
            }

            throw new Exception("No se pudo iniciar la recarga");
        }

        // ============================================================
        // PREGUNTAS DE CLARIFICACIÓN
        // ============================================================

        public List<string> AskQuestions(FullProjectConfig config)
        {
            return AskQuestionsAsync(config).GetAwaiter().GetResult();
        }

        public async Task<List<string>> AskQuestionsAsync(
            FullProjectConfig config,
            CancellationToken cancellationToken = default)
        {
            if (config == null) return new List<string>();

            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient
                    .PostAsync($"{_baseUrl}/generate_questions", content, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("questions", out var questionsEl) &&
                        questionsEl.ValueKind == JsonValueKind.Array)
                    {
                        var result = new List<string>();
                        foreach (var q in questionsEl.EnumerateArray())
                        {
                            string? qs = q.GetString();
                            if (!string.IsNullOrEmpty(qs))
                                result.Add(qs);
                        }
                        return result;
                    }
                }
            }
            catch { }

            return new List<string>();
        }

        // ============================================================
        // ANÁLISIS DE IMÁGENES
        // ============================================================

        public List<string> AnalyzeImages(List<byte[]> images)
        {
            return AnalyzeImagesAsync(images).GetAwaiter().GetResult();
        }

        public async Task<List<string>> AnalyzeImagesAsync(
            List<byte[]> images,
            CancellationToken cancellationToken = default)
        {
            var descriptions = new List<string>();
            if (images == null || images.Count == 0) return descriptions;

            foreach (var imageData in images)
            {
                if (imageData == null || imageData.Length == 0) continue;

                try
                {
                    string base64 = Convert.ToBase64String(imageData);
                    var payload = new
                    {
                        image_base64 = base64,
                        prompt = "Describe esta imagen arquitectónica: estilo, materiales, colores, composición."
                    };
                    string json = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var response = await _httpClient
                        .PostAsync($"{_baseUrl}/analyze_image", content, cancellationToken)
                        .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("description", out var descEl))
                        {
                            string? desc = descEl.GetString();
                            if (!string.IsNullOrEmpty(desc))
                                descriptions.Add(desc);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    descriptions.Add($"(error: {ex.Message})");
                }
            }

            return descriptions;
        }

        // ============================================================
        // ANÁLISIS DE TEXTO
        // ============================================================

        public JsonElement AnalyzeText(string text)
        {
            return AnalyzeTextAsync(text).GetAwaiter().GetResult();
        }

        public async Task<JsonElement> AnalyzeTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new { text, use_spacy = true };
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient
                    .PostAsync($"{_baseUrl}/analyze_text", content, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    return doc.RootElement.Clone();
                }
            }
            catch { }

            return default;
        }

        // ============================================================
        // FASE E: ENRIQUECIMIENTO DE CONTEXTO
        // ============================================================

        public async Task<string> EnrichContextAsync(string location)
        {
            var payload = new { location };
            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/enrich_context", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> EnrichContextAsync(double latitude, double longitude, int radius = 200)
        {
            var payload = new { latitude, longitude, radius };
            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/enrich_context", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// [NUEVO] Envía coordenadas, radio y opcionalmente un KML con el polígono de la parcela.
        /// </summary>
        public async Task<string> EnrichContextAsync(double latitude, double longitude, int radius, string? kml)
        {
            var payload = new
            {
                latitude,
                longitude,
                radius,
                kml = kml ?? string.Empty
            };
            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/enrich_context", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        // ============================================================
        // DISPOSABLE
        // ============================================================

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}