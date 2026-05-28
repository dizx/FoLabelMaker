using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoLabelMaker.Core.Configuration;

namespace FoLabelMaker.Core.Ai;

public sealed partial class OpenAiTextAiService : ITextAiService
{
    private const int MaxTranslationBatchSize = 50;

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    public OpenAiTextAiService(HttpClient httpClient, OpenAiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return requests.Select(request => new TranslationResult
            {
                LabelId = request.LabelId,
                TargetLanguage = request.TargetLanguage,
                Text = request.Text,
                IsValid = false,
                ValidationError = $"No OpenAI API key was configured. Set OpenAi.ApiKey or environment variable '{_options.ApiKeyEnvironmentVariable}'.",
            }).ToList();
        }

        var cache = await LoadCacheAsync(cancellationToken);
        var results = new List<TranslationResult>();
        var uncachedRequests = new List<TranslationRequest>();
        var cachedTranslations = new List<(TranslationRequest Request, string Text)>();
        foreach (var request in requests)
        {
            var cacheKey = BuildTranslationCacheKey(request);
            if (cache.TryGetValue(cacheKey, out var cachedText))
            {
                cachedTranslations.Add((request, cachedText));
                continue;
            }

            uncachedRequests.Add(request);
        }

        if (cachedTranslations.Count > 0)
        {
            var cachedResults = await ValidateTranslationsAsync(apiKey, cachedTranslations, true, cache, cancellationToken);
            results.AddRange(cachedResults);
            uncachedRequests.AddRange(cachedTranslations
                .Where(cached => cachedResults.All(result => !string.Equals(result.LabelId, cached.Request.LabelId, StringComparison.OrdinalIgnoreCase) || !string.Equals(result.TargetLanguage, cached.Request.TargetLanguage, StringComparison.OrdinalIgnoreCase)))
                .Select(cached => cached.Request));
        }

        var cachedCount = results.Count(result => result.IsFromCache);
        if (cachedCount > 0)
        {
            Console.WriteLine($"Loaded {cachedCount} translations from cache.");
        }

        if (uncachedRequests.Count == 0)
        {
            return results;
        }

        Console.WriteLine($"Requesting {uncachedRequests.Count} translations from OpenAI using model '{_options.Model}' in batches of {MaxTranslationBatchSize}...");

        foreach (var batch in uncachedRequests.Chunk(MaxTranslationBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Requesting translation batch {results.Count(result => !result.IsFromCache) + 1}-{results.Count(result => !result.IsFromCache) + batch.Length} of {uncachedRequests.Count}...");

            var payload = new
            {
                model = _options.Model,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = "Translate business software labels into the requested targetLanguage. Do not leave text in the source language unless it is a product name, acronym, placeholder, code, or proper noun. Preserve placeholders like %1 and {0} exactly. Return JSON with a translations array containing labelId, targetLanguage, text." },
                    new { role = "user", content = JsonSerializer.Serialize(batch, _jsonSerializerOptions) },
                },
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            Console.WriteLine($"OpenAI response status: {(int)response.StatusCode} {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Skipping translation batch because OpenAI returned {(int)response.StatusCode} {response.StatusCode}. Previously validated batches will still be saved.");
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var completion = JsonDocument.Parse(body);
            var content = completion.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var translatedDocument = JsonDocument.Parse(content);
            var batchTranslations = new List<(TranslationRequest Request, string Text)>();
            foreach (var item in translatedDocument.RootElement.GetProperty("translations").EnumerateArray())
            {
                var labelId = item.GetProperty("labelId").GetString() ?? string.Empty;
                var targetLanguage = item.GetProperty("targetLanguage").GetString() ?? string.Empty;
                var text = item.GetProperty("text").GetString() ?? string.Empty;
                var sourceRequest = batch.First(request => request.LabelId == labelId && request.TargetLanguage == targetLanguage);
                batchTranslations.Add((sourceRequest, text));
            }

            var validBatchResults = await ValidateTranslationsAsync(apiKey, batchTranslations, false, cache, cancellationToken);
            results.AddRange(validBatchResults);

            foreach (var validResult in validBatchResults.Where(result => result.IsValid))
            {
                var sourceRequest = batch.First(request => request.LabelId == validResult.LabelId && request.TargetLanguage == validResult.TargetLanguage);
                cache[BuildTranslationCacheKey(sourceRequest)] = validResult.Text;
            }

            await SaveCacheAsync(cache, cancellationToken);
        }

        Console.WriteLine($"Received {results.Count(result => !result.IsFromCache)} new translation results.");
        return results;
    }

    public async Task<IReadOnlyList<TextImprovementResult>> ImproveAsync(IReadOnlyList<TextImprovementRequest> requests, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return requests.Select(request => new TextImprovementResult
        {
            SourceFilePath = request.SourceFilePath,
            OriginalText = request.Text,
            SuggestedText = request.Text,
            Confidence = 0.0,
            Reason = "AI improvement is not invoked for identical text.",
        }).ToList();
    }

    private static bool ValidatePlaceholders(string sourceText, string translatedText, out string? error)
    {
        var sourcePlaceholders = PlaceholderRegex().Matches(sourceText).Select(match => match.Value).ToArray();
        var translatedPlaceholders = PlaceholderRegex().Matches(translatedText).Select(match => match.Value).ToArray();
        if (!sourcePlaceholders.SequenceEqual(translatedPlaceholders))
        {
            error = "Placeholder validation failed.";
            return false;
        }

        if (sourceText.Count(character => character == '\n') != translatedText.Count(character => character == '\n'))
        {
            error = "Line break validation failed.";
            return false;
        }

        error = null;
        return true;
    }

    private async Task<IReadOnlyList<TranslationResult>> ValidateTranslationsAsync(
        string apiKey,
        IReadOnlyList<(TranslationRequest Request, string Text)> translations,
        bool isFromCache,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var results = new List<TranslationResult>();
        var aiValidationRequests = new List<LanguageValidationRequest>();
        foreach (var (request, text) in translations)
        {
            if (!ValidatePlaceholders(request.Text, text, out var placeholderError))
            {
                Console.WriteLine($"Rejected translation for {request.LabelId}: {placeholderError}");
                continue;
            }

            var validationCacheKey = BuildLanguageValidationCacheKey(request.TargetLanguage, text);
            if (cache.TryGetValue(validationCacheKey, out var cachedValidationJson))
            {
                var cachedValidation = JsonSerializer.Deserialize<LanguageValidationResult>(cachedValidationJson, _jsonSerializerOptions);
                if (cachedValidation?.IsTargetLanguage == true)
                {
                    results.Add(new TranslationResult
                    {
                        LabelId = request.LabelId,
                        TargetLanguage = request.TargetLanguage,
                        Text = text,
                        IsFromCache = isFromCache,
                        IsValid = true,
                    });
                }
                else
                {
                    Console.WriteLine($"Rejected translation for {request.LabelId}: {cachedValidation?.Reason ?? "AI language validation failed."}");
                }

                continue;
            }

            aiValidationRequests.Add(new LanguageValidationRequest
            {
                Id = aiValidationRequests.Count + 1,
                LabelId = request.LabelId,
                TargetLanguage = request.TargetLanguage,
                Text = text,
            });
        }

        foreach (var batch in aiValidationRequests.Chunk(MaxTranslationBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Validating language for {batch.Length} translations with OpenAI...");
            var payload = new
            {
                model = _options.Model,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = "Validate whether each business software label is written in its targetLanguage. Return JSON with a validations array containing id, labelId, targetLanguage, isTargetLanguage, detectedLanguage, reason. Treat product names, acronyms, placeholders, codes, and proper nouns as language-neutral. Reject labels that contain untranslated source-language words or phrases." },
                    new { role = "user", content = JsonSerializer.Serialize(batch, _jsonSerializerOptions) },
                },
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            Console.WriteLine($"OpenAI validation response status: {(int)response.StatusCode} {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Skipping validation batch because OpenAI returned {(int)response.StatusCode} {response.StatusCode}. These translations were not accepted or cached.");
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var completion = JsonDocument.Parse(body);
            var content = completion.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var validationDocument = JsonDocument.Parse(content);
            foreach (var item in validationDocument.RootElement.GetProperty("validations").EnumerateArray())
            {
                var validation = new LanguageValidationResult
                {
                    Id = item.GetProperty("id").GetInt32(),
                    LabelId = item.GetProperty("labelId").GetString() ?? string.Empty,
                    TargetLanguage = item.GetProperty("targetLanguage").GetString() ?? string.Empty,
                    IsTargetLanguage = item.GetProperty("isTargetLanguage").GetBoolean(),
                    DetectedLanguage = item.TryGetProperty("detectedLanguage", out var detectedLanguage) ? detectedLanguage.GetString() : null,
                    Reason = item.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                };

                var validationRequest = batch.First(request => request.Id == validation.Id);
                cache[BuildLanguageValidationCacheKey(validationRequest.TargetLanguage, validationRequest.Text)] = JsonSerializer.Serialize(validation, _jsonSerializerOptions);
                if (!validation.IsTargetLanguage)
                {
                    Console.WriteLine($"Rejected translation for {validationRequest.LabelId}: {validation.Reason ?? $"Detected {validation.DetectedLanguage}."}");
                    continue;
                }

                results.Add(new TranslationResult
                {
                    LabelId = validationRequest.LabelId,
                    TargetLanguage = validationRequest.TargetLanguage,
                    Text = validationRequest.Text,
                    IsFromCache = isFromCache,
                    IsValid = true,
                });
            }

            await SaveCacheAsync(cache, cancellationToken);
        }

        return results;
    }

    private async Task<Dictionary<string, string>> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.CacheFilePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_options.CacheFilePath, cancellationToken);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    private async Task SaveCacheAsync(Dictionary<string, string> cache, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(cache, _jsonSerializerOptions);
        await File.WriteAllTextAsync(_options.CacheFilePath, json, cancellationToken);
    }

    private static string BuildTranslationCacheKey(TranslationRequest request) => $"{request.SourceLanguage}|{request.TargetLanguage}|{request.Text}";

    private static string BuildLanguageValidationCacheKey(string targetLanguage, string text) => $"language-validation|{targetLanguage}|{text}";

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
    }

    [GeneratedRegex("%\\d+|\\{\\d+\\}")]
    private static partial Regex PlaceholderRegex();

    private sealed class LanguageValidationRequest
    {
        public required int Id { get; init; }
        public required string LabelId { get; init; }
        public required string TargetLanguage { get; init; }
        public required string Text { get; init; }
    }

    private sealed class LanguageValidationResult
    {
        public required int Id { get; init; }
        public required string LabelId { get; init; }
        public required string TargetLanguage { get; init; }
        public required bool IsTargetLanguage { get; init; }
        public string? DetectedLanguage { get; init; }
        public string? Reason { get; init; }
    }
}
