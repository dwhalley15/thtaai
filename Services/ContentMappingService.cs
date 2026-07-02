using thta_ai.Models;
using System.Text.Json;

public class ContentMappingService : IContentMappingService
{

    public PageSchema MapRawSchemaToPageSchema(JsonElement rawSchema, string pageType)
    {
        JsonElement? matchedPage = null;
        foreach (var page in rawSchema.EnumerateArray())
        {
            var alias = page.GetProperty("docType").GetProperty("alias").GetString();
            if (alias == pageType)
            {
                matchedPage = page;
                break;
            }
        }

        if (matchedPage is null)
            throw new InvalidOperationException($"Unknown page type: {pageType}");

        var docType = matchedPage.Value.GetProperty("docType");

        var schema = new PageSchema
        {
            PageType = pageType,
            DocumentTypeId = docType.GetProperty("id").GetString() ?? "",
            DefaultTemplateId = docType.TryGetProperty("defaultTemplate", out var template)
                && template.ValueKind != JsonValueKind.Null
                ? template.GetProperty("id").GetString()
                : null
        };


        foreach (var prop in matchedPage.Value.GetProperty("properties").EnumerateArray())
        {
            var editorAlias = prop.GetProperty("type").GetString() ?? "";
            if (editorAlias is not ("Umbraco.BlockGrid" or "Umbraco.BlockList")) continue;

            var propAlias = prop.GetProperty("alias").GetString() ?? "";
            var blockProp = new BlockPropertySchema
            {
                Alias = propAlias,
                EditorAlias = editorAlias
            };

            if (prop.TryGetProperty("blocks", out var blocks))
            {
                var seen = new HashSet<string>();
                foreach (var block in blocks.EnumerateArray())
                    CollectBlockDefs(block, blockProp, seen);
            }
            schema.BlockProperties.Add(blockProp);
        }

        return schema;
    }

    // Recursively walks a block definition and every block reachable through its
    // nested BlockList/BlockGrid properties or area allowedBlocks, so blockLookup/
    // allBlockDefs in MapLlmResponse can resolve ANY block the LLM might reference
    // (top-level containers, plain content blocks, and nested blocks like Button/Accordion).
    // Recursively walks a block definition and every block reachable through its
    // nested BlockList/BlockGrid properties, adding each discovered def into either
    // DirectBlocks (no areas — a leaf/content block) or AreaContainers (has areas —
    // a layout block whose areas hold further blocks).
    private void CollectBlockDefs(JsonElement blockJson, BlockPropertySchema blockProp, HashSet<string> seen)
    {
        var name = blockJson.GetProperty("name").GetString() ?? "";
        if (!seen.Add(name)) return; // already collected, avoid infinite loop on shared nested blocks

        var def = new BlockDefinition
        {
            Id = blockJson.GetProperty("id").GetString() ?? "",
            Name = name,
            Fields = [],
            Areas = []
        };

        if (blockJson.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateArray())
            {
                var fieldAlias = prop.GetProperty("alias").GetString() ?? "";
                var fieldEditorAlias = prop.GetProperty("type").GetString() ?? "";

                def.Fields.Add(new BlockField { Alias = fieldAlias, EditorAlias = fieldEditorAlias });

                if (fieldEditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid"
                    && prop.TryGetProperty("blocks", out var nestedBlocks))
                {
                    foreach (var nested in nestedBlocks.EnumerateArray())
                        CollectBlockDefs(nested, blockProp, seen);
                }
            }
        }

        if (blockJson.TryGetProperty("areas", out var areas))
        {
            foreach (var area in areas.EnumerateArray())
            {
                def.Areas.Add(new AreaDefinition
                {
                    Key = area.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    Alias = area.GetProperty("alias").GetString() ?? "",
                    ColumnSpan = area.TryGetProperty("columnSpan", out var cs) ? cs.GetInt32() : 1,
                });
            }
        }

        if (def.Areas.Count > 0)
            blockProp.AreaContainers.Add(def);
        else
            blockProp.DirectBlocks.Add(def);
    }
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
            if (string.IsNullOrEmpty(llmBlock.Region)) continue; // defensive, shouldn't happen post-ExpandPlanFromRaw

            if (!blocksByAlias.TryGetValue(llmBlock.Region, out var list))
            {
                list = [];
                blocksByAlias[llmBlock.Region] = list;
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

        var headerTitle = llmResponse.Blocks
            .FirstOrDefault(b => b.Region == "headerContent")
            ?.Fields.GetValueOrDefault("title")?.ToString();

        return new DocumentCreateModel
        {
            Values = values,
            DocumentType = new DocumentTypeModel { Id = schema.DocumentTypeId },
            Template = schema.DefaultTemplateId is not null
                        ? new TemplateModel { Id = schema.DefaultTemplateId }
                        : null,
            Variants =
            [
                new()
                {
                    Culture = null,
                    Segment = null,
                    Name = !string.IsNullOrWhiteSpace(headerTitle)
                        ? headerTitle
                        : llmResponse.Fields.GetValueOrDefault("metaTitle")?.ToString() ?? "New Page",

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

                    areaLayouts.Add(new { key = area.Key, items = areaItems });
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

            // Integer
            "Umbraco.Integer" => int.TryParse(str, out var i) ? i : (object?)null,

            // Pass through as-is
            _ => value,
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string NewUdi() =>
        $"umb://element/{Guid.NewGuid():N}";
}