using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ThaiBulkEmailPoc;

// Usage:
//   ThaiBulkEmailPoc.exe             -> send the email to every recipient in appsettings.json
//   ThaiBulkEmailPoc.exe --dry-run   -> print the request JSON without calling the API
//   ThaiBulkEmailPoc.exe --credit    -> check remaining email credit only
//
// Any setting can be overridden with environment variables, e.g.
//   ThaiBulkMail__ApiSecret=xxx   Email__Recipients__0__MergeTags__FIRST_NAME=Somchai

Console.OutputEncoding = Encoding.UTF8;

// Merge tags the email template uses. Each recipient must provide a value for all of them.
string[] requiredMergeTags = { "FIRST_NAME", "LAST_NAME", "UNSUBSCRIBE_URL", "PHONE", "SF_TAG", "OPTION_1" };

var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
var creditOnly = args.Contains("--credit", StringComparer.OrdinalIgnoreCase);

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var apiOptions = config.GetSection("ThaiBulkMail").Get<ThaiBulkMailOptions>() ?? new();
var emailOptions = config.GetSection("Email").Get<EmailOptions>() ?? new();

var errors = Validate(apiOptions, emailOptions, requiredMergeTags);
if (errors.Count > 0)
{
    Console.Error.WriteLine("Configuration error(s):");
    errors.ForEach(e => Console.Error.WriteLine($"  - {e}"));
    return 1;
}

Console.WriteLine($"Endpoint : {apiOptions.BaseUrl}{apiOptions.SendTemplatePath}");
Console.WriteLine($"API Key  : {Mask(apiOptions.ApiKey)}");
Console.WriteLine($"Template : {emailOptions.TemplateUuid}");
Console.WriteLine();

using var client = new ThaiBulkMailClient(apiOptions);

if (creditOnly)
{
    var credit = await client.GetCreditAsync();
    PrintResult("Credit", credit);
    return credit.IsSuccess ? 0 : 1;
}

var failures = 0;
foreach (var recipient in emailOptions.Recipients)
{
    var request = BuildRequest(emailOptions, recipient);

    Console.WriteLine($"--- {recipient.Email} ---");
    Console.WriteLine("Request:");
    Console.WriteLine(JsonSerializer.Serialize(request, ThaiBulkMailClient.JsonOptions));

    if (dryRun)
    {
        Console.WriteLine("(dry run: not sent)");
        Console.WriteLine();
        continue;
    }

    try
    {
        var result = await client.SendTemplateAsync(request);
        PrintResult("Send", result);

        if (result.IsSuccess)
        {
            var sent = JsonSerializer.Deserialize<SendTemplateResponse>(result.Body);
            Console.WriteLine($"Sent. message_id={sent?.MessageId}, credit_used={sent?.CreditUsed}, credit_remain={sent?.CreditRemain}");
        }
        else
        {
            failures++;
        }
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        failures++;
        Console.Error.WriteLine($"Request failed: {ex.Message}");
    }
    Console.WriteLine();
}

return failures == 0 ? 0 : 1;

static SendTemplateRequest BuildRequest(EmailOptions email, Recipient recipient)
{
    var displayName = string.IsNullOrWhiteSpace(recipient.Name)
        ? $"{recipient.MergeTags.GetValueOrDefault("FIRST_NAME")} {recipient.MergeTags.GetValueOrDefault("LAST_NAME")}".Trim()
        : recipient.Name;

    return new SendTemplateRequest
    {
        TemplateUuid = email.TemplateUuid,
        Subject = email.Subject,
        MailFrom = new ApiAddress { Email = email.From.Email, Name = NullIfEmpty(email.From.Name) },
        MailTo = new ApiAddress { Email = recipient.Email, Name = NullIfEmpty(displayName) },
        Payload = new Dictionary<string, string>(recipient.MergeTags),
    };
}

static List<string> Validate(ThaiBulkMailOptions api, EmailOptions email, string[] requiredTags)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(api.BaseUrl)) errors.Add("ThaiBulkMail:BaseUrl is required.");
    if (string.IsNullOrWhiteSpace(api.ApiKey)) errors.Add("ThaiBulkMail:ApiKey is required.");
    if (string.IsNullOrWhiteSpace(api.ApiSecret)) errors.Add("ThaiBulkMail:ApiSecret is required.");
    if (!Guid.TryParse(email.TemplateUuid, out _)) errors.Add("Email:TemplateUuid must be a UUID.");
    if (string.IsNullOrWhiteSpace(email.Subject)) errors.Add("Email:Subject is required.");
    if (string.IsNullOrWhiteSpace(email.From.Email)) errors.Add("Email:From:Email is required.");
    if (email.Recipients.Count == 0) errors.Add("Email:Recipients must contain at least one recipient.");

    for (var i = 0; i < email.Recipients.Count; i++)
    {
        var r = email.Recipients[i];
        if (string.IsNullOrWhiteSpace(r.Email)) errors.Add($"Email:Recipients:{i}:Email is required.");

        // Tag names are case-sensitive in the template ({{Phone}} is not {{PHONE}}), so match exactly.
        foreach (var tag in requiredTags.Where(t => !r.MergeTags.ContainsKey(t)))
            errors.Add($"Email:Recipients:{i}:MergeTags is missing \"{tag}\" (for {{{{{tag}}}}}).");
    }
    return errors;
}

static void PrintResult(string label, ApiResult result)
{
    Console.WriteLine($"{label} response: HTTP {result.StatusCode}");
    Console.WriteLine(PrettyJson(result.Body));
}

static string PrettyJson(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
        return JsonSerializer.Serialize(doc.RootElement, ThaiBulkMailClient.JsonOptions);
    }
    catch (JsonException)
    {
        return body;
    }
}

static string Mask(string value) => value.Length <= 6 ? "***" : value[..4] + new string('*', value.Length - 6) + value[^2..];

static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
