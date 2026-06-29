using thta_ai.Models;

public class ContentMappingService : IContentMappingService
{
    // ─── LLM Response Mapping ─────────────────────────────────────────────────

    /// <summary>
    /// Maps an LLM page response to a DocumentCreateModel using the provided schema.
    /// </summary>
    public DocumentCreateModel MapLlmResponse(LlmPageResponse llmResponse, PageSchema schema)
    {
        var values = new List<PropertyValueModel>();

        // Simple page-level fields
        foreach (var (alias, value) in llmResponse.Fields)
        {
            values.Add(new PropertyValueModel { Alias = alias, Value = value });
        }

        // Block name → (owning BlockProperty, BlockDefinition) — safe against duplicate names
        var blockLookup = schema.BlockProperties
            .SelectMany(bp => bp.AllBlocks.Select(b => (bp, b)))
            .DistinctBy(x => x.b.Name)
            .ToDictionary(x => x.b.Name, x => (BlockProperty: x.bp, BlockDef: x.b));

        // Full block definitions by name — used to resolve nested/area block content
        var allBlockDefs = schema.BlockProperties
            .SelectMany(bp => bp.AllBlocks)
            .DistinctBy(b => b.Name)
            .ToDictionary(b => b.Name, b => b);

        // Group LLM blocks by their owning property alias
        var blocksByAlias = new Dictionary<string, List<LlmBlock>>();
        foreach (var llmBlock in llmResponse.Blocks)
        {
            if (!blockLookup.TryGetValue(llmBlock.Name, out var match)) continue;
            var alias = match.BlockProperty.Alias;
            if (!blocksByAlias.TryGetValue(alias, out var list))
            {
                list = [];
                blocksByAlias[alias] = list;
            }
            list.Add(llmBlock);
        }

        // Build each block property value
        foreach (var (alias, blocks) in blocksByAlias)
        {
            var schemaProp = schema.BlockProperties.First(bp => bp.Alias == alias);
            var value = schemaProp.EditorAlias == "Umbraco.BlockGrid"
                ? (object)BuildBlockGridValue(blocks, blockLookup, allBlockDefs)
                : (object)BuildBlockListValue(blocks, allBlockDefs);

            values.Add(new PropertyValueModel
            {
                Alias = alias,
                Culture = null,
                Segment = null,
                Value = value
            });
        }

        return new DocumentCreateModel
        {
            Values = values,
            Variants =
            [
                new()
                {
                    Culture = null,
                    Segment = null,
                    Name = llmResponse.Fields.GetValueOrDefault("title")?.ToString() ?? "New Page"
                }
            ]
        };
    }

    // ─── Block Grid ───────────────────────────────────────────────────────────

    private BlockGridValue BuildBlockGridValue(
        List<LlmBlock> blocks,
        Dictionary<string, (BlockPropertySchema BlockProperty, BlockDefinition BlockDef)> blockLookup,
        Dictionary<string, BlockDefinition> allBlockDefs)
    {
        var layout = new List<object>();
        var contentData = new List<Dictionary<string, object?>>();

        foreach (var llmBlock in blocks)
        {
            if (!blockLookup.TryGetValue(llmBlock.Name, out var match)) continue;

            var blockDef = match.BlockDef;
            var udi = NewUdi();

            if (blockDef.Areas.Any())
            {
                // Area container — distribute inner blocks into their respective areas
                var areaLayouts = new List<object>();

                foreach (var area in blockDef.Areas)
                {
                    var innerLlmBlocks = llmBlock.Areas?.GetValueOrDefault(area.Alias) ?? [];
                    var areaItems = new List<object>();

                    foreach (var innerLlmBlock in innerLlmBlocks)
                    {
                        if (!allBlockDefs.TryGetValue(innerLlmBlock.Name, out var innerDef)) continue;

                        var innerUdi = NewUdi();
                        areaItems.Add(new { contentUdi = innerUdi, areas = Array.Empty<object>() });
                        contentData.Add(BuildContentEntry(innerUdi, innerDef, innerLlmBlock, allBlockDefs));
                    }

                    areaLayouts.Add(new { key = area.Alias, items = areaItems });
                }

                layout.Add(new { contentUdi = udi, areas = areaLayouts });
            }
            else
            {
                layout.Add(new { contentUdi = udi, areas = Array.Empty<object>() });
            }

            contentData.Add(BuildContentEntry(udi, blockDef, llmBlock, allBlockDefs));
        }

        return new BlockGridValue
        {
            Layout = new() { ["Umbraco.BlockGrid"] = layout },
            ContentData = contentData,
            SettingsData = []
        };
    }

    // ─── Block List ───────────────────────────────────────────────────────────

    private BlockListValue BuildBlockListValue(
        IEnumerable<LlmBlock> blocks,
        Dictionary<string, BlockDefinition> allBlockDefs)
    {
        var layout = new List<BlockGridLayoutItem>();
        var contentData = new List<Dictionary<string, object?>>();

        foreach (var llmBlock in blocks)
        {
            if (!allBlockDefs.TryGetValue(llmBlock.Name, out var blockDef)) continue;

            var udi = NewUdi();
            layout.Add(new BlockGridLayoutItem { ContentUdi = udi, Areas = [] });
            contentData.Add(BuildContentEntry(udi, blockDef, llmBlock, allBlockDefs));
        }

        return new BlockListValue
        {
            Layout = new() { ["Umbraco.BlockList"] = layout },
            ContentData = contentData,
            SettingsData = []
        };
    }

    // ─── Content Entry ────────────────────────────────────────────────────────

    private Dictionary<string, object?> BuildContentEntry(
        string udi,
        BlockDefinition blockDef,
        LlmBlock llmBlock,
        Dictionary<string, BlockDefinition> allBlockDefs)
    {
        var entry = new Dictionary<string, object?>
        {
            ["contentTypeKey"] = blockDef.Id,
            ["udi"] = udi,
        };

        var fieldLookup = blockDef.Fields.ToDictionary(f => f.Alias, f => f.EditorAlias);

        foreach (var (key, val) in llmBlock.Fields)
        {
            var editorAlias = fieldLookup.GetValueOrDefault(key, "");
            entry[key] = TransformFieldValue(val, editorAlias);
        }

        if (llmBlock.NestedBlocks is not null)
        {
            foreach (var (nestedAlias, nestedBlocks) in llmBlock.NestedBlocks)
            {
                entry[nestedAlias] = BuildBlockListValue(nestedBlocks, allBlockDefs);
            }
        }

        return entry;
    }

    // ─── Field Transform ──────────────────────────────────────────────────────

    private static object? TransformFieldValue(object? value, string editorAlias)
    {
        if (value is null) return null;

        var str = value.ToString() ?? "";

        return editorAlias switch
        {
            // Multi URL Picker — wrap plain string in Umbraco's expected link format
            "Umbraco.MultiUrlPicker" => string.IsNullOrWhiteSpace(str) ? null : new[]
            {
                new
                {
                    name = (string?)null,
                    type = "url",
                    url = str,
                    unique = (string?)null,
                    target = "_self"
                }
            },

            // Flexible dropdown — value must be an array
            "Umbraco.DropDown.Flexible" => string.IsNullOrWhiteSpace(str) ? null : new[] { str },

            // Media picker — LLM can't know media IDs, leave empty
            "Umbraco.MediaPicker3" => null,

            // Checkbox
            "Umbraco.TrueFalse" => value is bool b ? b : str.Equals("true", StringComparison.OrdinalIgnoreCase),

            // Pass through as-is
            _ => value,
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string NewUdi() =>
        $"umb://element/{Guid.NewGuid():N}";
}