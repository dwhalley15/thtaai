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
            results.push(await this.buildElementTree(element, ctx));
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
     * Recursively renders a property list. For BlockGrid/BlockList properties,
     * renders each allowed block and calls itself on the block's own properties,
     * handling arbitrary nesting depth.
     */
    private _renderProperties(properties: any[]): any {
        if (!properties?.length) {
            return html`<em style="color: var(--uui-color-text-alt);">No properties</em>`;
        }

        return properties.map((p: any) => html`
        <div style="margin-bottom: 8px;">
            <div>
                <strong>${p.alias}</strong>
                <uui-tag look="outline" style="margin-left: 6px; font-size: 0.75em;">
                    ${p.type}
                </uui-tag>
            </div>

            ${p.options?.length ? html`
                <div style="margin-top: 4px; padding-left: 12px; border-left: 2px solid var(--uui-color-border);">
                    ${p.options.map((o: string) => html`
                        <uui-tag look="default" style="margin: 2px; font-size: 0.75em;">${o}</uui-tag>
                    `)}
                </div>
            ` : ''}

            ${p.blocks?.length ? html`
                <div style="margin-top: 4px; padding-left: 12px; border-left: 2px solid var(--uui-color-border);">
                    ${p.blocks.map((b: any) => html`
                        <div style="margin-bottom: 6px;">
                            <uui-tag look="positive">${b.name}</uui-tag>
                            <div style="padding-left: 10px; margin-top: 2px;">
                                ${this._renderProperties(b.properties)}
                            </div>
                        </div>
                    `)}
                </div>
            ` : ''}
        </div>
    `);
    }

    override render() {
        return html`
            <uui-box headline="Page Schema Generator">

                <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 16px;">
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
                        <span style="font-size: 0.85em; color: var(--uui-color-text-alt);">
                            Last generated: ${this._lastGenerated.toLocaleString()}
                        </span>
                    ` : ''}
                </div>

                ${this._schema.length > 0 ? html`
                    <uui-table>
                        <uui-table-head>
                            <uui-table-head-cell>Document Type</uui-table-head-cell>
                            <uui-table-head-cell>Compositions</uui-table-head-cell>
                            <uui-table-head-cell>Allowed Children</uui-table-head-cell>
                            <uui-table-head-cell>Properties</uui-table-head-cell>
                        </uui-table-head>

                        ${this._schema.map(s => html`
                        <uui-table-row>

                            <uui-table-cell>
                                <strong>${s.docType.name}</strong>
                                <div style="font-size: 0.8em; color: var(--uui-color-text-alt);">
                                    ${s.docType.alias}
                                </div>
                            </uui-table-cell>

                            <uui-table-cell>
                                ${s.compositions.length === 0
                                            ? html`<em style="color: var(--uui-color-text-alt);">None</em>`
                                            : s.compositions.map((c: any) => html`
                                        <uui-tag look="secondary" style="margin: 2px;">${c.name}</uui-tag>
                                    `)
                                        }
                            </uui-table-cell>

                            <uui-table-cell>
                                ${s.allowedChildren?.length === 0
                                            ? html`<em style="color: var(--uui-color-text-alt);">None</em>`
                                            : s.allowedChildren?.map((c: any) => html`
                                        <uui-tag look="secondary" style="margin: 2px; font-size: 0.75em;">
                                            ${c.name}
                                        </uui-tag>
                                    `)
                                        }
                            </uui-table-cell>

                            <uui-table-cell>
                                ${this._renderProperties(s.properties)}
                            </uui-table-cell>

                        </uui-table-row>
                    `)}

                    </uui-table>
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
        `,
    ];
}

export default TemplateGeneratorView;