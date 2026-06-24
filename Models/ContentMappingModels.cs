// LLM response models

public record MapContentRequest
{
    public LlmPageResponse LlmResponse { get; init; } = new();
    public PageSchema Schema { get; init; } = new();
}

public class DocumentCreateModel
{
    public List<PropertyValueModel> Values { get; init; } = [];
    public List<VariantModel> Variants { get; init; } = [];
    public ParentModel? Parent { get; init; }
    public DocumentTypeModel? DocumentType { get; init; }
    public TemplateModel? Template { get; init; }
}

public class PropertyValueModel
{
    public string Alias { get; init; } = "";
    public string? Culture { get; init; }
    public string? Segment { get; init; }
    public object? Value { get; init; }
}

public class VariantModel
{
    public string? Culture { get; init; }
    public string? Segment { get; init; }
    public string Name { get; init; } = "";
}

public class ParentModel
{
    public string Id { get; init; } = "";
}

public class DocumentTypeModel
{
    public string Id { get; init; } = "";
}

public class TemplateModel
{
    public string Id { get; init; } = "";
}

public class BlockGridValue
{
    public Dictionary<string, object> Layout { get; init; } = [];
    public List<Dictionary<string, object?>> ContentData { get; init; } = [];
    public List<object> SettingsData { get; init; } = [];
}

public class BlockGridLayoutItem
{
    public string ContentUdi { get; init; } = "";
    public List<object> Areas { get; init; } = [];
}
public record LlmPageResponse
{
    public string PageType { get; init; } = "";
    public Dictionary<string, object?> Fields { get; init; } = [];
    public List<LlmBlock> Blocks { get; init; } = [];
}

public record LlmBlock
{
    public string Block { get; init; } = "";
    public Dictionary<string, object?> Fields { get; init; } = [];
    public Dictionary<string, List<LlmBlock>>? NestedBlocks { get; init; }  // block list props
    public Dictionary<string, List<LlmBlock>>? Areas { get; init; }          // area placement
}

// Schema models (what the frontend sends alongside the prompt)
public record PageSchema
{
    public string PageType { get; init; } = "";
    public List<string> Fields { get; init; } = [];
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
public record BlockField
{
    public string Alias { get; init; } = "";
    public string EditorAlias { get; init; } = "";
}

public record AreaDefinition
{
    public string Key { get; init; } = "";
    public string Alias { get; init; } = "";
    public int ColumnSpan { get; init; } = 1;
    public List<BlockReference> AllowedBlocks { get; init; } = [];
}

public record BlockReference
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public record BlockDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<AreaDefinition> Areas { get; init; } = [];  // ← add this
    public List<BlockField> Fields { get; init; } = [];
    public List<NestedBlockProperty> NestedBlocks { get; init; } = [];
}
public record NestedBlockProperty
{
    public string Field { get; init; } = "";
    public List<BlockDefinition> AllowedBlocks { get; init; } = [];
}

public class BlockListValue
{
    public Dictionary<string, object> Layout { get; init; } = [];
    public List<Dictionary<string, object?>> ContentData { get; init; } = [];
    public List<object> SettingsData { get; init; } = [];
}