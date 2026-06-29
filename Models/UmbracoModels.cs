namespace thta_ai.Models;

// ─── Umbraco Management API Models ───────────────────────────────────────────
// These models represent the payload shape expected by Umbraco's
// Management API (/umbraco/management/api/v1/document).

public record DocumentCreateModel
{
    public List<PropertyValueModel> Values { get; init; } = [];
    public List<VariantModel> Variants { get; init; } = [];
    public ParentModel? Parent { get; init; }
    public DocumentTypeModel? DocumentType { get; init; }
    public TemplateModel? Template { get; init; }
}

public record PropertyValueModel
{
    public string Alias { get; init; } = "";
    public string? Culture { get; init; }
    public string? Segment { get; init; }
    public object? Value { get; init; }
}

public record VariantModel
{
    public string? Culture { get; init; }
    public string? Segment { get; init; }
    public string Name { get; init; } = "";
}

public record ParentModel
{
    public string Id { get; init; } = "";
}

public record DocumentTypeModel
{
    public string Id { get; init; } = "";
}

public record TemplateModel
{
    public string Id { get; init; } = "";
}

// ─── Block Editor Value Models ────────────────────────────────────────────────
// Serialised into property values for BlockGrid and BlockList editors.

public record BlockGridValue
{
    public Dictionary<string, object> Layout { get; init; } = [];
    public List<Dictionary<string, object?>> ContentData { get; init; } = [];
    public List<object> SettingsData { get; init; } = [];
}

public record BlockGridLayoutItem
{
    public string ContentUdi { get; init; } = "";
    public List<object> Areas { get; init; } = [];
}

public record BlockListValue
{
    public Dictionary<string, object> Layout { get; init; } = [];
    public List<Dictionary<string, object?>> ContentData { get; init; } = [];
    public List<object> SettingsData { get; init; } = [];
}