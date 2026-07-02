import { html, customElement, css, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

/**
 * Shared context passed through the schema-building pipeline.
 * Avoids threading token/maps as individual parameters across every method.
 */
interface Context {
    token: string;
    allTypes: Map<string, any>;      // All document + element types, keyed by ID
    allDataTypes: Map<string, any>;  // All fetched data types, keyed by ID
}

/**
 * Maps raw Umbraco editor aliases to a friendly label + icon for non-technical
 * display. Unknown aliases fall back to a stripped/prettified version of the
 * alias itself, so nothing ever renders blank.
 */
const FRIENDLY_LABELS: Record<string, { label: string; icon: string }> = {
    "Umbraco.RichText": { label: "Rich Text", icon: "icon-browser-window" },
    "Umbraco.TextBox": { label: "Text", icon: "icon-text-align-left" },
    "Umbraco.TextArea": { label: "Long Text", icon: "icon-text-align-left" },
    "Umbraco.MediaPicker3": { label: "Image / Media", icon: "icon-picture" },
    "Umbraco.MultiUrlPicker": { label: "Link(s)", icon: "icon-link" },
    "Umbraco.TrueFalse": { label: "Yes / No Toggle", icon: "icon-checkbox-dotted" },
    "Umbraco.DropDown.Flexible": { label: "Dropdown", icon: "icon-list" },
    "Umbraco.RadioButtonList": { label: "Choice (pick one)", icon: "icon-radio-button" },
    "Umbraco.CheckBoxList": { label: "Choice (pick many)", icon: "icon-checkbox-dotted" },
    "Umbraco.DatePicker": { label: "Date", icon: "icon-calendar" },
    "Umbraco.ContentPicker": { label: "Page Link", icon: "icon-document" },
    "Umbraco.MultiNodeTreePicker": { label: "Page / Content Links", icon: "icon-documents" },
    "Umbraco.MemberPicker": { label: "Member Link", icon: "icon-user" },
    "Umbraco.Tags": { label: "Tags", icon: "icon-tags" },
    "Umbraco.Slider": { label: "Number Slider", icon: "icon-navigation-vertical" },
    "Umbraco.BlockGrid": { label: "Content Blocks (Grid)", icon: "icon-thumbnail-list" },
    "Umbraco.BlockList": { label: "Content Blocks (List)", icon: "icon-list" },
};

function getFriendlyType(editorAlias: string): { label: string; icon: string } {
    if (!editorAlias) return { label: "Field", icon: "icon-science" };
    return (
        FRIENDLY_LABELS[editorAlias] ?? {
            label: editorAlias.replace(/^Umbraco\./, "").replace(/\./g, " "),
            icon: "icon-science",
        }
    );
}

@customElement("template-generator-view")
export class TemplateGeneratorView extends UmbLitElement {

    @state() private _schema: any[] = [];
    @state() private _errorMessage: string | null = null;
    @state() private _loading = false;
    @state() private _lastGenerated: Date | null = null;

    private readonly CACHE_KEY = 'umbraco_page_schema';
    private readonly CACHE_MAX_AGE_MS = 24 * 60 * 60 * 1000; // 24 hours

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    override async connectedCallback() {
        super.connectedCallback();
        this._loadFromCache();
    }

    // ─── Cache ────────────────────────────────────────────────────────────────

    /**
     * Attempts to restore a previously generated schema from localStorage.
     * Discards the cache if it exceeds CACHE_MAX_AGE_MS.
     */
    private _loadFromCache() {
        const raw = localStorage.getItem(this.CACHE_KEY);
        if (!raw) return;

        try {
            const { schema, generatedAt } = JSON.parse(raw);
            if (Date.now() - generatedAt > this.CACHE_MAX_AGE_MS) {
                localStorage.removeItem(this.CACHE_KEY);
                return;
            }
            this._schema = schema;
            this._lastGenerated = new Date(generatedAt);
        } catch {
            localStorage.removeItem(this.CACHE_KEY);
        }
    }

    /**
     * Persists the current schema and a generation timestamp to localStorage.
     */
    private _saveToCache() {
        localStorage.setItem(this.CACHE_KEY, JSON.stringify({
            schema: this._schema,
            generatedAt: Date.now(),
        }));
    }

    /**
     * Clears the cached schema from localStorage and resets component state.
     */
    private _clearCache() {
        localStorage.removeItem(this.CACHE_KEY);
        this._schema = [];
        this._lastGenerated = null;
    }

    // ─── Data Fetching ────────────────────────────────────────────────────────

    /**
     * Fetches a single data type by ID from the Umbraco management API.
     * Returns null if the request fails.
     */
    private async _fetchDataType(id: string, token: string): Promise<any> {
        const res = await fetch(`/umbraco/management/api/v1/data-type/${id}`, {
            headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok) return null;
        return await res.json();
    }

    /**
     * Fetches full details for each document/element type, splits them into
     * documents and elements, then pre-fetches all referenced data types so
     * the schema builder has everything it needs without further API calls.
     */
    private async _classify(items: any[], token: string) {
        const details = await Promise.all(
            items
                .filter(i => !i.isFolder)
                .map(async (i) => {
                    const res = await fetch(
                        `/umbraco/management/api/v1/document-type/${i.id}`,
                        { headers: { Authorization: `Bearer ${token}` } }
                    );
                    if (!res.ok) return null;
                    return await res.json();
                })
        );

        const valid = details.filter(Boolean);
        const elements: any[] = [];
        const documents: any[] = [];

        for (const item of valid) {
            if (item.isElement) {
                elements.push(item);
            } else {
                documents.push(item);
            }
        }

        // Must be built before filtering/resolving children, as we need
        // to look up any document type by ID including non-page types
        const allTypes = new Map<string, any>(valid.map((x: any) => [x.id, x]));

        // Filter to renderable pages only — document types with no assigned
        // template are settings, XML endpoints, or structural placeholders
        const pages = documents.filter(d => d.allowedTemplates?.length > 0);

        const dataTypeIds = new Set<string>();
        for (const item of valid) {
            for (const prop of item.properties ?? []) {
                if (prop.dataType?.id) dataTypeIds.add(prop.dataType.id);
            }
        }

        const allDataTypes = new Map<string, any>();
        await Promise.all(
            [...dataTypeIds].map(async (id) => {
                const dt = await this._fetchDataType(id, token);
                if (dt) allDataTypes.set(id, dt);
            })
        );

        const ctx: Context = { token, allTypes, allDataTypes };

        // Build schema for pages only, resolving allowed children per document
        const schema = await Promise.all(
            pages.map((doc) => this.buildSchema(doc, ctx))
        );

        return { schema, elements, documents };
    }

    /**
     * Entry point for the generate button. Fetches all document types from the
     * Umbraco tree search endpoint, builds the schema, then saves it to cache.
     */
    private async _generate() {
        this._errorMessage = null;
        this._loading = true;

        try {
            const authContext = await this.getContext(UMB_AUTH_CONTEXT);
            const token = await authContext?.getLatestToken();

            if (!token) throw new Error("Missing authentication token.");

            const response = await fetch("/umbraco/management/api/v1/tree/document-type/search", {
                headers: { Authorization: `Bearer ${token}` },
            });

            if (!response.ok || !response.body) {
                throw new Error(`Error fetching document types: ${response.statusText}`);
            }

            const data = await response.json();
            const result = await this._classify(data.items ?? data, token);
            this._schema = result.schema;
            this._lastGenerated = new Date();
            this._saveToCache();
        } catch (error) {
            this._errorMessage = "An error occurred while generating the template.";
        } finally {
            this._loading = false;
        }
    }

    // ─── Schema Building ──────────────────────────────────────────────────────

    /**
     * Builds the top-level schema entry for a single document type.
     * Resolves compositions, merges all inherited properties, then builds
     * the full property tree for each one.
     */
    private async buildSchema(docType: any, ctx: Context) {
        const compositions = this.collectCompositions(docType, ctx.allTypes);
        const properties = this.getAllProperties(docType, compositions);

        // Resolve allowed child page types from their IDs, filtering out any
        // that don't exist in allTypes (e.g. element types or unresolved refs)
        const allowedChildren = (docType.allowedDocumentTypes ?? [])
            .map((c: any) => ctx.allTypes.get(c.documentType.id))
            .filter(Boolean)
            .map((t: any) => ({ id: t.id, name: t.name, alias: t.alias }));

        return {
            docType,
            allowedChildren,
            compositions,
            properties: await Promise.all(
                properties.map((p) => this.buildPropertyTree(p, ctx))
            ),
        };
    }

    /**
     * Merges a document/element type's own properties with those inherited
     * from its compositions into a single flat array.
     */
    private getAllProperties(docType: any, compositions: any[]) {
        const props = [...(docType.properties ?? [])];
        for (const comp of compositions) {
            props.push(...(comp.properties ?? []));
        }
        return props;
    }

    /**
     * Recursively walks a document/element type's composition tree,
     * returning a deduplicated flat list of all composed types.
     */
    private collectCompositions(docType: any, allTypes: Map<string, any>) {
        const seen = new Set<string>();
        const result: any[] = [];

        function walk(dt: any) {
            for (const c of dt.compositions ?? []) {
                const id = c.documentType.id;
                if (seen.has(id)) continue;
                seen.add(id);
                const resolved = allTypes.get(id);
                if (resolved) {
                    result.push(resolved);
                    walk(resolved);
                }
            }
        }

        walk(docType);
        return result;
    }

    /**
     * Resolves a single property to its editor alias and, for BlockGrid/BlockList
     * properties, recursively builds the tree of allowed block elements.
     */
    private async buildPropertyTree(property: any, ctx: Context): Promise<any> {
        const dataType = ctx.allDataTypes.get(property.dataType?.id);

        if (!dataType) {
            return { alias: property.alias, type: "Unknown" };
        }

        const result = { alias: property.alias, type: dataType.editorAlias };

        if (
            dataType.editorAlias === "Umbraco.BlockGrid" ||
            dataType.editorAlias === "Umbraco.BlockList"
        ) {
            return { ...result, blocks: await this.getAllowedBlocks(dataType, ctx) };
        }

        if (dataType.editorAlias === "Umbraco.DropDown.Flexible") {
            const itemsConfig = dataType.values?.find((x: any) => x.alias === "items");
            const options = itemsConfig?.value ?? [];
            return { ...result, options };
        }

        return result;
    }

    /**
     * Reads the "blocks" configuration value from a BlockGrid/BlockList data type
     * and returns the element tree for each allowed block content type.
     */
    private async getAllowedBlocks(dataType: any, ctx: Context): Promise<any[]> {
        const blocksConfig = dataType.values?.find((x: any) => x.alias === "blocks");
        if (!blocksConfig) return [];

        const results = [];
        for (const block of blocksConfig.value ?? []) {
            const element = ctx.allTypes.get(block.contentElementTypeKey);
            if (!element) continue;

            const elementTree = await this.buildElementTree(element, ctx);

            // Capture area definitions from the block grid config entry
            const areas = (block.areas ?? []).map((a: any) => ({
                key: a.key,
                alias: a.alias ?? a.key,
                columnSpan: a.columnSpan ?? 1,
                rowSpan: a.rowSpan ?? 1,
                allowedBlocks: (a.allowedElementTypeKeys ?? [])
                    .map((key: string) => ctx.allTypes.get(key))
                    .filter(Boolean)
                    .map((el: any) => ({ id: el.id, name: el.name }))
            }));

            results.push({ ...elementTree, areas });
        }
        return results;
    }

    /**
     * Builds the schema tree for a single block element type, including its
     * composed properties. Properties are themselves passed through buildPropertyTree,
     * so nested BlockGrid/BlockList properties recurse automatically.
     */
    private async buildElementTree(element: any, ctx: Context): Promise<any> {
        const compositions = this.collectCompositions(element, ctx.allTypes);
        const properties = this.getAllProperties(element, compositions);

        return {
            id: element.id,
            name: element.name,
            properties: await Promise.all(
                properties.map((p) => this.buildPropertyTree(p, ctx))
            ),
        };
    }

    // ─── Rendering ────────────────────────────────────────────────────────────

    /**
     * Renders a single field row. Fields with nested blocks (BlockGrid/BlockList)
     * become a collapsible <details> node so the tree can go arbitrarily deep
     * without turning into a wall of text; plain fields render as a flat row.
     */
    private _renderPropertyNode(p: any): any {
        const friendly = getFriendlyType(p.type);
        const hasBlocks = p.blocks?.length > 0;

        if (!hasBlocks) {
            return html`
                <div class="field-row">
                    <uui-icon name="${friendly.icon}" class="field-icon"></uui-icon>
                    <span class="field-name">${p.alias}</span>
                    <uui-tag look="outline" class="field-type" title="${p.type}">${friendly.label}</uui-tag>
                    ${p.options?.length ? html`
                        <div class="field-options">
                            ${p.options.map((o: string) => html`
                                <uui-tag look="default" class="option-tag">${o}</uui-tag>
                            `)}
                        </div>
                    ` : ''}
                </div>
            `;
        }

        return html`
            <details class="field-tree">
                <summary>
                    <uui-icon name="${friendly.icon}" class="field-icon"></uui-icon>
                    <span class="field-name">${p.alias}</span>
                    <uui-tag look="outline" class="field-type" title="${p.type}">${friendly.label}</uui-tag>
                    <span class="muted count-badge">
                        ${p.blocks.length} block type${p.blocks.length === 1 ? '' : 's'}
                    </span>
                </summary>
                <div class="field-children">
                    ${p.blocks.map((b: any) => this._renderBlockNode(b))}
                </div>
            </details>
        `;
    }

    /**
     * Renders a single allowed block element as a collapsible node containing
     * its own fields, and any BlockGrid areas defined on it.
     */
    private _renderBlockNode(b: any): any {
        const fieldCount = b.properties?.length ?? 0;
        return html`
            <details class="block-node">
                <summary>
                    <uui-icon name="icon-box" class="field-icon"></uui-icon>
                    <strong>${b.name}</strong>
                    <span class="muted count-badge">
                        ${fieldCount} field${fieldCount === 1 ? '' : 's'}
                    </span>
                </summary>
                <div class="block-children">
                    ${fieldCount
                ? b.properties.map((p: any) => this._renderPropertyNode(p))
                : html`<em class="muted">No fields on this block</em>`}
                    ${b.areas?.length ? b.areas.map((a: any) => this._renderAreaNode(a)) : ''}
                </div>
            </details>
        `;
    }

    /**
     * Renders a single BlockGrid area as a collapsible node listing which
     * block types are allowed to be dropped into it.
     */
    private _renderAreaNode(a: any): any {
        const count = a.allowedBlocks?.length ?? 0;
        return html`
            <details class="area-node">
                <summary>
                    <uui-icon name="icon-navigation-vertical" class="field-icon"></uui-icon>
                    <span>Area: <strong>${a.alias}</strong></span>
                    <span class="muted count-badge">
                        allows ${count} block type${count === 1 ? '' : 's'}
                    </span>
                </summary>
                <div class="area-children">
                    ${count
                ? a.allowedBlocks.map((ab: any) => html`<uui-tag look="secondary" class="option-tag">${ab.name}</uui-tag>`)
                : html`<em class="muted">No block types specified</em>`}
                </div>
            </details>
        `;
    }

    /**
     * Renders one page type as a collapsible card: a summary line with
     * at-a-glance counts, expanding to compositions, allowed children,
     * and the full field tree.
     */
    private _renderPageCard(s: any): any {
        const propCount = s.properties?.length ?? 0;

        return html`
            <details class="page-card">
                <summary class="page-card-summary">
                    <uui-icon name="icon-document" class="page-icon"></uui-icon>
                    <div class="page-title">
                        <strong>${s.docType.name}</strong>
                        <span class="muted">${s.docType.alias}</span>
                    </div>
                    <div class="page-summary-chips">
                        <uui-tag look="primary" class="summary-chip">
                            ${propCount} field${propCount === 1 ? '' : 's'}
                        </uui-tag>
                        ${s.compositions.length ? html`
                            <uui-tag look="secondary" class="summary-chip">
                                ${s.compositions.length} composition${s.compositions.length === 1 ? '' : 's'}
                            </uui-tag>
                        ` : ''}
                        ${s.allowedChildren?.length ? html`
                            <uui-tag look="secondary" class="summary-chip">
                                ${s.allowedChildren.length} allowed child page${s.allowedChildren.length === 1 ? '' : 's'}
                            </uui-tag>
                        ` : ''}
                    </div>
                </summary>
 
                <div class="page-card-body">
                    ${s.compositions.length ? html`
                        <div class="page-section">
                            <div class="section-label">Built from</div>
                            <div class="chip-row">
                                ${s.compositions.map((c: any) => html`
                                    <uui-tag look="secondary" class="option-tag">${c.name}</uui-tag>
                                `)}
                            </div>
                        </div>
                    ` : ''}
 
                    ${s.allowedChildren?.length ? html`
                        <div class="page-section">
                            <div class="section-label">Can contain these page types</div>
                            <div class="chip-row">
                                ${s.allowedChildren.map((c: any) => html`
                                    <uui-tag look="secondary" class="option-tag">${c.name}</uui-tag>
                                `)}
                            </div>
                        </div>
                    ` : ''}
 
                    <div class="page-section">
                        <div class="section-label">Fields</div>
                        <div class="field-list">
                            ${propCount
                ? s.properties.map((p: any) => this._renderPropertyNode(p))
                : html`<em class="muted">No fields</em>`}
                        </div>
                    </div>
                </div>
            </details>
        `;
    }

    override render() {
        return html`
            <uui-box headline="Page Schema Generator">
 
                <div class="toolbar">
                    ${this._loading
                ? html`<uui-loader></uui-loader>`
                : html`
                            <uui-button @click=${this._generate} look="primary" label="Generate Schema">
                                ${this._schema.length > 0 ? 'Regenerate' : 'Generate'}
                            </uui-button>
                        `}
 
                    ${this._schema.length > 0 && !this._loading ? html`
                        <uui-button @click=${this._clearCache} look="secondary" label="Clear Cache">
                            Clear Cache
                        </uui-button>
                    ` : ''}
 
                    ${this._lastGenerated ? html`
                        <span class="muted">
                            Last generated: ${this._lastGenerated.toLocaleString()}
                        </span>
                    ` : ''}
                </div>
 
                ${this._schema.length > 0 ? html`
                    <div class="page-list">
                        ${this._schema.map(s => this._renderPageCard(s))}
                    </div>
                ` : ''}
 
                ${this._errorMessage ? html`
                    <uui-tag look="danger">${this._errorMessage}</uui-tag>
                ` : ''}
 
            </uui-box>
        `;
    }

    static override styles = [
        css`
            :host {
                display: block;
                padding: 20px;
            }
 
            .toolbar {
                display: flex;
                align-items: center;
                gap: 12px;
                margin-bottom: 20px;
            }
 
            .muted {
                color: var(--uui-color-text-alt);
                font-size: 0.85em;
            }
 
            /* ── Page cards ─────────────────────────────────────────────── */
 
            .page-list {
                display: flex;
                flex-direction: column;
                gap: 10px;
            }
 
            .page-card {
                border: 1px solid var(--uui-color-border);
                border-radius: var(--uui-border-radius, 6px);
                background: var(--uui-color-surface);
            }
 
            .page-card-summary {
                display: flex;
                align-items: center;
                gap: 12px;
                padding: 14px 16px;
                cursor: pointer;
                list-style: none;
            }
 
            .page-card-summary::-webkit-details-marker {
                display: none;
            }
 
            .page-card-summary::before {
                content: "▸";
                display: inline-block;
                transition: transform 0.15s ease;
                color: var(--uui-color-text-alt);
                flex: 0 0 auto;
            }
 
            .page-card[open] > .page-card-summary::before {
                transform: rotate(90deg);
            }
 
            .page-icon {
                flex: 0 0 auto;
                font-size: 1.3em;
                color: var(--uui-color-interactive);
            }
 
            .page-title {
                display: flex;
                flex-direction: column;
                gap: 1px;
                margin-right: auto;
            }
 
            .page-title .muted {
                font-size: 0.75em;
            }
 
            .page-summary-chips {
                display: flex;
                gap: 6px;
                flex-wrap: wrap;
                justify-content: flex-end;
            }
 
            .summary-chip {
                font-size: 0.72em;
            }
 
            .page-card-body {
                padding: 4px 16px 16px 44px;
                border-top: 1px solid var(--uui-color-divider);
            }
 
            .page-section {
                margin-top: 14px;
            }
 
            .section-label {
                font-size: 0.75em;
                text-transform: uppercase;
                letter-spacing: 0.04em;
                color: var(--uui-color-text-alt);
                margin-bottom: 6px;
            }
 
            .chip-row {
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
            }
 
            /* ── Field tree ──────────────────────────────────────────────── */
 
            .field-list {
                display: flex;
                flex-direction: column;
                gap: 4px;
            }
 
            .field-row {
                display: flex;
                align-items: center;
                flex-wrap: wrap;
                gap: 8px;
                padding: 6px 8px;
                border-radius: 4px;
            }
 
            .field-row:hover,
            .field-tree > summary:hover,
            .block-node > summary:hover,
            .area-node > summary:hover {
                background: var(--uui-color-surface-alt, rgba(0, 0, 0, 0.03));
            }
 
            .field-icon {
                color: var(--uui-color-text-alt);
                flex: 0 0 auto;
            }
 
            .field-name {
                font-weight: 600;
                font-size: 0.9em;
            }
 
            .field-type {
                font-size: 0.72em;
            }
 
            .field-options {
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
                margin-left: 4px;
            }
 
            .option-tag {
                margin: 0;
                font-size: 0.72em;
            }
 
            .count-badge {
                margin-left: auto;
                font-size: 0.75em;
            }
 
            details.field-tree,
            details.block-node,
            details.area-node {
                border-left: 2px solid var(--uui-color-divider);
                padding-left: 4px;
            }
 
            details.field-tree > summary,
            details.block-node > summary,
            details.area-node > summary {
                display: flex;
                align-items: center;
                gap: 8px;
                padding: 6px 8px;
                cursor: pointer;
                list-style: none;
            }
 
            details.field-tree > summary::-webkit-details-marker,
            details.block-node > summary::-webkit-details-marker,
            details.area-node > summary::-webkit-details-marker {
                display: none;
            }
 
            details.field-tree > summary::before,
            details.block-node > summary::before,
            details.area-node > summary::before {
                content: "▸";
                display: inline-block;
                transition: transform 0.15s ease;
                color: var(--uui-color-text-alt);
                flex: 0 0 auto;
            }
 
            details[open] > summary::before {
                transform: rotate(90deg);
            }
 
            .field-children,
            .block-children,
            .area-children {
                margin-left: 20px;
                padding-left: 8px;
                border-left: 1px dashed var(--uui-color-divider);
                display: flex;
                flex-direction: column;
                gap: 4px;
                padding-bottom: 4px;
            }
 
            .area-children {
                flex-direction: row;
                flex-wrap: wrap;
            }
        `,
    ];
}

export default TemplateGeneratorView;