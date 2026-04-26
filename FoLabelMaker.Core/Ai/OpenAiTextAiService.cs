using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoLabelMaker.Core.Configuration;

namespace FoLabelMaker.Core.Ai;

public sealed partial class OpenAiTextAiService : ITextAiService
{
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
        foreach (var request in requests)
        {
            var cacheKey = BuildTranslationCacheKey(request);
            if (cache.TryGetValue(cacheKey, out var cachedText))
            {
                results.Add(new TranslationResult
                {
                    LabelId = request.LabelId,
                    TargetLanguage = request.TargetLanguage,
                    Text = cachedText,
                    IsFromCache = true,
                    IsValid = ValidatePlaceholders(request.Text, cachedText, out var error),
                    ValidationError = error,
                });
                continue;
            }

            uncachedRequests.Add(request);
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

        Console.WriteLine($"Requesting {uncachedRequests.Count} translations from OpenAI using model '{_options.Model}'...");

        var payload = new
        {
            model = _options.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "Translate business software labels. Preserve placeholders like %1 and {0} exactly. Return JSON with a translations array containing labelId, targetLanguage, text." },
                new { role = "user", content = JsonSerializer.Serialize(uncachedRequests, _jsonSerializerOptions) },
            },
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        Console.WriteLine($"OpenAI response status: {(int)response.StatusCode} {response.StatusCode}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var completion = JsonDocument.Parse(body);
        var content = completion.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
        var translatedDocument = JsonDocument.Parse(content);
        foreach (var item in translatedDocument.RootElement.GetProperty("translations").EnumerateArray())
        {
            var labelId = item.GetProperty("labelId").GetString() ?? string.Empty;
            var targetLanguage = item.GetProperty("targetLanguage").GetString() ?? string.Empty;
            var text = item.GetProperty("text").GetString() ?? string.Empty;
            var sourceRequest = uncachedRequests.First(request => request.LabelId == labelId && request.TargetLanguage == targetLanguage);
            var isValid = ValidatePlaceholders(sourceRequest.Text, text, out var validationError);
            results.Add(new TranslationResult
            {
                LabelId = labelId,
                TargetLanguage = targetLanguage,
                Text = text,
                IsValid = isValid,
                ValidationError = validationError,
            });

            if (isValid)
            {
                cache[BuildTranslationCacheKey(sourceRequest)] = text;
            }
        }

        await SaveCacheAsync(cache, cancellationToken);
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
}
