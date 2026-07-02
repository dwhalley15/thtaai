namespace thta_ai.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

// ─── LLM Planning (Pass 1) ────────────────────────────────────────────────────
// The planning schema is sent to the LLM so it can choose a page structure
// without needing to know field-level details.

public record LlmPlanningSchema
{
    public List<LlmPlanningPageType> PageTypes { get; init; } = [];
}

public record LlmPlanningPageType
{
    public string Name { get; init; } = "";
    public List<LlmRegion> Regions { get; set; } = [];
}

public class LlmRegion
{
    public string Name { get; set; } = "";
    public List<LlmPlanningBlockDef> DirectBlocks { get; set; } = [];
    public List<LlmAreaContainer> AreaContainers { get; set; } = [];

    public List<LlmPlanningBlockDef> AvailableBlockDefs { get; set; } = new();
}

public record LlmPlanningBlockDef
{
    public string Name { get; init; } = "";
    public List<LlmNestedSlot> NestedSlots { get; init; } = [];

    public List<LlmPlanningArea>? AreaContainerAreas { get; init; } = null;
}

public record LlmNestedSlot
{
    public string Alias { get; init; } = "";    
    public List<string> AllowedBlocks { get; init; } = []; 
}

public class LlmAreaContainer
{
    public string BlockName { get; set; } = "";
    public List<LlmPlanningArea> Areas { get; set; } = [];
}

public record LlmPlanningArea
{
    public string Alias { get; init; } = "";
    public int ColumnSpan { get; init; }
    public List<string> AllowedBlocks { get; init; } = [];

    public int MaxItems { get; init; } = 1;  
}

public record LlmPlanningBlock
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> AllowedAreas { get; init; } = [];
}

// The plan the LLM returns from pass 1 — structure only, no content.
public class LlmPagePlan
{
    public string PageType { get; set; }
    public List<LlmRegionPlan> Regions { get; set; }
}

public class LlmRegionPlan
{
    public string Name { get; set; }
    public List<LlmPlannedBlock> Blocks { get; set; }
    
}

public record LlmPlannedBlock
{
    public string Id { get; init; } = "";
    public string Alias { get; init; } = "";
    public Dictionary<string, List<LlmPlannedBlock>> Areas { get; init; } = [];
    public Dictionary<string, List<LlmPlannedBlock>> NestedBlocks { get; init; } = [];
}

// ─── LLM Content (Pass 2) ─────────────────────────────────────────────────────
// The page shell (with empty fields) is sent to the LLM for content filling.
// The LLM returns the same structure with fields populated.

public record LlmPageResponse
{
    public string PageType { get; init; } = "";
    public Dictionary<string, object?> Fields { get; init; } = [];
    public List<LlmBlock> Blocks { get; init; } = [];

    [JsonIgnore] // guidance only — never sent to/expected back from the LLM
    public Dictionary<string, LlmFieldMeta> FieldTypes { get; set; } = new();
}

public class LlmFieldMeta
{
    public string Type { get; set; } = "";
    public List<string>? Options { get; set; } // populated for dropdowns only
}

public record LlmBlock
{
    public string Id { get; init; } = "";
    public string Alias { get; init; } = "";
    public string Name { get; init; } = "";
    public Dictionary<string, object?> Fields { get; init; } = [];
    public Dictionary<string, List<LlmBlock>> NestedBlocks { get; init; } = [];
    public Dictionary<string, List<LlmBlock>> Areas { get; init; } = [];

    public string Region { get; set; } = "";

    [JsonIgnore]
    public Dictionary<string, LlmFieldMeta> FieldTypes { get; set; } = new();
}

// ─── API Request / Response Models ───────────────────────────────────────────

public record CleanSchemaRequest
{
    public List<PageSchema> Schemas { get; init; } = [];
}

public record CleanSchemaResponse
{
    public LlmPlanningSchema Schema { get; init; } = new();
}

public record MapContentRequest
{
    public LlmPageResponse LlmResponse { get; init; } = new();
    public JsonElement Schema { get; init; } = new();
}

public record GeneratePageRequest
{
    public string Prompt { get; init; } = "";
    public Guid ConversationId { get; init; }
    public bool IsNewConversation { get; init; }
    public JsonElement Schema { get; init; }
}

public record GeneratePageResponse
{
    public Guid ConversationId { get; init; }
    public System.Text.Json.JsonElement RawOutput { get; init; }
}

// ─── LLM Validation ────────────────────────────────────────────────

public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
}