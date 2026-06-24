using thta_ai.Models;

public class ContentMappingService : IContentMappingService
{
    public DocumentCreateModel MapLlmResponse(LlmPageResponse llmResponse, PageSchema schema)
    {
        var values = new List<PropertyValueModel>();

        // Simple fields
        foreach (var (alias, value) in llmResponse.Fields)
        {
            values.Add(new PropertyValueModel { Alias = alias, Value = value });
        }

        // Build a lookup: block name → schema block definition (top-level)
        var blockLookup = schema.BlockProperties
            .SelectMany(bp => bp.AllBlocks.Select(b => (bp, b)))
            .ToDictionary(x => x.b.Name, x => (BlockProperty: x.bp, BlockDef: x.b));

        // Build a flat lookup for nested block definitions
        // Full definitions available: direct blocks + area containers themselves
        var allBlockDefinitions = schema.BlockProperties
            .SelectMany(bp => bp.AllBlocks)
            .DistinctBy(b => b.Name)
            .ToDictionary(b => b.Name, b => b);

        // Names of blocks allowed inside areas or as nested block list items
        var nestedBlockLookup = schema.BlockProperties
            .SelectMany(bp => bp.DirectBlocks)
            .SelectMany(b => b.NestedBlocks)
            .SelectMany(nb => nb.AllowedBlocks.Select(ab => ab.Name))
            .Concat(schema.BlockProperties
                .SelectMany(bp => bp.AreaContainers)
                .SelectMany(ac => ac.Areas)
                .SelectMany(a => a.AllowedBlocks.Select(ab => ab.Name)))
            .Distinct()
            // Resolve name → full BlockDefinition from directBlocks (where full defs live)
            .Select(name => schema.BlockProperties
                .SelectMany(bp => bp.DirectBlocks)
                .FirstOrDefault(b => b.Name == name))
            .Where(b => b is not null)
            .DistinctBy(b => b!.Name)
            .ToDictionary(b => b!.Name, b => b!);

        // Group LLM blocks by their owning property alias
        var blocksByAlias = new Dictionary<string, List<LlmBlock>>();
        foreach (var llmBlock in llmResponse.Blocks)
        {
            if (!blockLookup.TryGetValue(llmBlock.Block, out var match)) continue;
            var alias = match.BlockProperty.Alias;
            if (!blocksByAlias.ContainsKey(alias))
                blocksByAlias[alias] = new();
            blocksByAlias[alias].Add(llmBlock);
        }

        // Build each block property value
        foreach (var (alias, blocks) in blocksByAlias)
        {
            var schemaProp = schema.BlockProperties.First(bp => bp.Alias == alias);
            var value = schemaProp.EditorAlias == "Umbraco.BlockGrid"
                ? (object)BuildBlockGridValue(blocks, blockLookup, nestedBlockLookup)
                : (object)BuildBlockListValue(blocks, nestedBlockLookup);

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
            Variants = [new() { Culture = null, Segment = null, Name = llmResponse.Fields.GetValueOrDefault("title")?.ToString() ?? "New Page" }]
        };
    }

    // ─── Block Grid ───────────────────────────────────────────────────────────

    private BlockGridValue BuildBlockGridValue(
    List<LlmBlock> blocks,
    Dictionary<string, (BlockPropertySchema BlockProperty, BlockDefinition BlockDef)> blockLookup,
    Dictionary<string, BlockDefinition> nestedBlockLookup)
    {
        var layout = new List<object>();
        var contentData = new List<Dictionary<string, object?>>();

        foreach (var llmBlock in blocks)
        {
            if (!blockLookup.TryGetValue(llmBlock.Block, out var match)) continue;

            var blockDef = match.BlockDef;
            var id = Guid.NewGuid().ToString("N");
            var udi = $"umb://element/{id}";

            if (blockDef.Areas.Any())
            {
                // This is an area container — place inner blocks into its areas
                var areaLayouts = new List<object>();

                foreach (var area in blockDef.Areas)
                {
                    // Find inner blocks the LLM assigned to this area
                    // Convention: LLM puts them in nestedBlocks keyed by area alias
                    var innerLlmBlocks = llmBlock.Areas
                        ?.GetValueOrDefault(area.Alias)
                        ?? llmBlock.Areas?.GetValueOrDefault(area.Key)
                        ?? [];

                    var areaItems = new List<object>();

                    foreach (var innerLlmBlock in innerLlmBlocks)
                    {
                        // Inner blocks resolve from nestedBlockLookup OR blockLookup
                        BlockDefinition? innerDef = null;
                        if (nestedBlockLookup.TryGetValue(innerLlmBlock.Block, out var nd))
                            innerDef = nd;
                        else if (blockLookup.TryGetValue(innerLlmBlock.Block, out var bm))
                            innerDef = bm.BlockDef;

                        if (innerDef is null) continue;

                        var innerId = Guid.NewGuid().ToString("N");
                        var innerUdi = $"umb://element/{innerId}";

                        areaItems.Add(new { contentUdi = innerUdi, areas = Array.Empty<object>() });
                        contentData.Add(BuildContentEntry(innerUdi, innerDef, innerLlmBlock, nestedBlockLookup));
                    }

                    areaLayouts.Add(new { key = area.Key, items = areaItems });
                }

                layout.Add(new { contentUdi = udi, areas = areaLayouts });
            }
            else
            {
                // Regular block — no areas
                layout.Add(new { contentUdi = udi, areas = Array.Empty<object>() });
            }

            contentData.Add(BuildContentEntry(udi, blockDef, llmBlock, nestedBlockLookup));
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
    Dictionary<string, BlockDefinition> nestedBlockLookup)
    {
        var layout = new List<BlockGridLayoutItem>();
        var contentData = new List<Dictionary<string, object?>>();

        foreach (var llmBlock in blocks)
        {
            if (!nestedBlockLookup.TryGetValue(llmBlock.Block, out var blockDef)) continue;

            var id = Guid.NewGuid().ToString("N");
            var udi = $"umb://element/{id}";

            layout.Add(new BlockGridLayoutItem { ContentUdi = udi, Areas = [] });
            contentData.Add(BuildContentEntry(udi, blockDef, llmBlock, nestedBlockLookup));
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
    Dictionary<string, BlockDefinition> nestedBlockLookup)
    {
        var entry = new Dictionary<string, object?>
        {
            ["contentTypeKey"] = blockDef.Id,
            ["udi"] = udi,
        };

        // Build a field lookup so we can check editor alias per field
        var fieldLookup = blockDef.Fields.ToDictionary(f => f.Alias, f => f.EditorAlias);

        foreach (var (key, val) in llmBlock.Fields)
        {
            var editorAlias = fieldLookup.GetValueOrDefault(key, "");
            entry[key] = TransformFieldValue(val, editorAlias);
        }

        if (llmBlock.NestedBlocks != null)
        {
            foreach (var (nestedAlias, nestedBlocks) in llmBlock.NestedBlocks)
            {
                entry[nestedAlias] = BuildBlockListValue(nestedBlocks, nestedBlockLookup);
            }
        }

        return entry;
    }

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

            // Everything else — pass through as-is
            _ => value,
        };
    }
}