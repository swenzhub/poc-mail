namespace ThaiBulkEmailPoc;

/// <summary>API connection settings (section "ThaiBulkMail" in appsettings.json).</summary>
public sealed class ThaiBulkMailOptions
{
    public string BaseUrl { get; set; } = "https://tbs-email-api-gateway.omb.to";
    public string SendTemplatePath { get; set; } = "/email/v1/send_template";
    public string CreditPath { get; set; } = "/email/v1/credit";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>What to send (section "Email" in appsettings.json).</summary>
public sealed class EmailOptions
{
    public string TemplateUuid { get; set; } = "";
    public string Subject { get; set; } = "";
    public MailAddress From { get; set; } = new();
    public List<Recipient> Recipients { get; set; } = new();
}

public sealed class MailAddress
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Recipient
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// Merge tag values. The key is the tag name without braces and is case-sensitive,
    /// e.g. "FIRST_NAME" fills {{FIRST_NAME}} and "Phone" fills {{Phone}}.
    /// </summary>
    public Dictionary<string, string> MergeTags { get; set; } = new();
}
