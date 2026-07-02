namespace thta_ai.Models;

// ─── Page / Block Schema ──────────────────────────────────────────────────────
// These models represent the CMS page structure extracted by the TypeScript
// frontend and sent alongside the prompt to describe what blocks/fields exist.

public record PageSchema
{
    public string PageType { get; init; } = "";
    public List<string> Fields { get; init; } = [];
    public string DocumentTypeId { get; set; } = "";      // NEW
    public string? DefaultTemplateId { get; set; }          // NEW
    public List<BlockPropertySchema> BlockProperties { get; init; } = [];
}

public record BlockPropertySchema
{
    public string Alias { get; init; } = "";
    public string EditorAlias { get; init; } = "";
    public List<BlockDefinition> DirectBlocks { get; init; } = [];
    public List<BlockDefinition> AreaContainers { get; init; } = [];
    public IEnumerable<BlockDefinition> AllBlocks => DirectBlocks.Concat(AreaContainers);
}

public record BlockDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<BlockField> Fields { get; init; } = [];
    public List<AreaDefinition> Areas { get; init; } = [];
    public List<NestedBlockProperty> NestedBlocks { get; init; } = [];
}

public record BlockField
{
    public string Alias { get; init; } = "";
    public string EditorAlias { get; init; } = "";
}

public record AreaDefinition
{
    /// <summary>Umbraco's internal area key (GUID-based). Used when building layout items.</summary>
    public string Key { get; init; } = "";
    /// <summary>Human-readable alias used in LLM prompts and block area dictionaries.</summary>
    public string Alias { get; init; } = "";
    public int ColumnSpan { get; init; } = 1;
    public List<BlockReference> AllowedBlocks { get; init; } = [];
}

public record BlockReference
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public record NestedBlockProperty
{
    public string Field { get; init; } = "";
    public List<BlockDefinition> AllowedBlocks { get; init; } = [];
}