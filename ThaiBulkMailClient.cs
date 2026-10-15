using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThaiBulkEmailPoc;

/// <summary>Request body of POST /email/v1/send_template.</summary>
public sealed class SendTemplateRequest
{
    [JsonPropertyName("template_uuid")] public string TemplateUuid { get; init; } = "";
    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("mail_from")] public ApiAddress MailFrom { get; init; } = new();
    [JsonPropertyName("mail_to")] public ApiAddress MailTo { get; init; } = new();

    /// <summary>Merge tag values, e.g. { "FIRST_NAME": "John" } fills {{FIRST_NAME}} in the template.</summary>
    [JsonPropertyName("payload")] public Dictionary<string, string> Payload { get; init; } = new();
}

public sealed class ApiAddress
{
    [JsonPropertyName("email")] public string Email { get; init; } = "";

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}

/// <summary>Successful response of send_template.</summary>
public sealed class SendTemplateResponse
{
    [JsonPropertyName("message_id")] public string? MessageId { get; init; }
    [JsonPropertyName("credit_type")] public string? CreditType { get; init; }
    [JsonPropertyName("credit_used")] public decimal? CreditUsed { get; init; }
    [JsonPropertyName("credit_remain")] public decimal? CreditRemain { get; init; }
}

/// <summary>Raw outcome of an API call: HTTP status plus response body.</summary>
public sealed record ApiResult(int StatusCode, bool IsSuccess, string Body);

public sealed class ThaiBulkMailClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep Thai characters readable in logs instead of \uXXXX escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly ThaiBulkMailOptions _options;

    public ThaiBulkMailClient(ThaiBulkMailOptions options)
    {
        _options = options;
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };

        // The API uses Basic Auth: username = API Key, password = API Secret.
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:{options.ApiSecret}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<ApiResult> SendTemplateAsync(SendTemplateRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendAsync(new HttpRequestMessage(HttpMethod.Post, RelativePath(_options.SendTemplatePath)) { Content = content }, ct);
    }

    public Task<ApiResult> GetCreditAsync(CancellationToken ct = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, RelativePath(_options.CreditPath)), ct);

    private async Task<ApiResult> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        using (var response = await _http.SendAsync(request, ct))
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return new ApiResult((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }
    }

    private static string RelativePath(string path) => path.TrimStart('/');

    public void Dispose() => _http.Dispose();
}
