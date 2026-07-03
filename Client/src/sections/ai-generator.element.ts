import {
    html,
    customElement,
    css,
    state,
} from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";

@customElement("ai-generator-view")
export class AIGeneratorView extends UmbLitElement {

    @state() private _prompt = "";
    @state() private _schema: any[] = [];
    @state() private _mappedContent: any = null;
    @state() private _rawOutput = "";
    @state() private _parsedOutput: any = null;
    @state() private _showRawJson = false;
    @state() private _loading = false;
    @state() private _errorMessage: string | null = null;
    @state() private _conversationId?: string;
    @state() private _isNewConversation = true;
    @state() private _parentPages: { id: string; name: string }[] = [];
    @state() private _selectedParentId: string = "";
    @state() private _parentLoading = false;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    override async connectedCallback() {
        super.connectedCallback();
        await this._loadParentPages();
    }

    // ─── Auth ─────────────────────────────────────────────────────────────────

    private async _getToken(): Promise<string> {
        const authContext = await this.getContext(UMB_AUTH_CONTEXT);
        const token = await authContext?.getLatestToken();
        if (!token) throw new Error("Could not retrieve auth token.");
        return token;
    }

    // ─── Parent Pages ─────────────────────────────────────────────────────────

    private async _loadParentPages() {
        this._parentLoading = true;
        try {
            const token = await this._getToken();

            const res = await fetch("/umbraco/delivery/api/v2/content", {
                headers: { Authorization: `Bearer ${token}` },
            });

            if (!res.ok) throw new Error("Failed to fetch content tree");

            const data = await res.json();
            this._parentPages = (data.items ?? []).map((item: any) => ({
                id: item.id,
                name: item.name,
            }));

            if (this._parentPages.length > 0) {
                this._selectedParentId = this._parentPages[0].id;
            }
        } catch {
            // Non-fatal — parent picker will just be empty
        } finally {
            this._parentLoading = false;
        }
    }

    // ─── Generation ───────────────────────────────────────────────────────────

    private async _generate() {
        this._errorMessage = null;

        if (!this._prompt.trim()) {
            this._errorMessage = "Prompt cannot be empty.";
            return;
        }

        this._loading = true;

        if (this._isNewConversation) {
            this._conversationId = crypto.randomUUID();
        }

        try {
            const token = await this._getToken();

            this._schema = this._getRawSchema();

            console.log("Raw schema:", this._schema);

            const response = await fetch("/umbraco/thtaai/api/v1/generatePage", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({
                    prompt: this._prompt,
                    conversationId: this._conversationId,
                    isNewConversation: this._isNewConversation,
                    schema: this._schema,
                }),
            });

            if (!response.ok) throw new Error("Generation request failed");

            // Only consume the response body once
            const result = await response.json();
            this._rawOutput = JSON.stringify(result.rawOutput, null, 2);

            try {
                this._parsedOutput = typeof result.rawOutput === "string"
                    ? JSON.parse(result.rawOutput)
                    : result.rawOutput;
            } catch {
                this._parsedOutput = null;
            }

            this._isNewConversation = false;

        } catch (error: any) {
            this._errorMessage = error.message || "An error occurred during generation.";
        } finally {
            this._loading = false;
        }
    }

    // ─── Content Creation ─────────────────────────────────────────────────────

    private async _createContent() {
        this._errorMessage = null;
        this._loading = true;

        try {
            const token = await this._getToken();

            const llmResponse = JSON.parse(this._rawOutput);

            const mapResponse = await fetch("/umbraco/thtaai/api/v1/mapContent", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({
                    llmResponse,
                    schema: this._schema,
                }),
            });

            if (!mapResponse.ok) {
                const text = await mapResponse.text();
                throw new Error(`Content mapping failed: ${text}`);
            }

            this._mappedContent = await mapResponse.json();

            console.log("Mapped content:", this._mappedContent);

            const mapped = this._mappedContent as any;

            const createPayload = {
                ...mapped,
                parent: { id: this._selectedParentId },
            };

            const createResponse = await fetch("/umbraco/management/api/v1/document", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json",
                },
                body: JSON.stringify(createPayload),
            });

            if (!createResponse.ok) {
                const text = await createResponse.text();
                throw new Error(`Document creation failed: ${text}`);
            }

            const newId = createResponse.headers.get("Umb-Generated-Resource");
            alert(`Page created successfully! ID: ${newId}`);

        } catch (error: any) {
            this._errorMessage = error.message || "An error occurred while creating content.";
            console.error(error);
        } finally {
            this._loading = false;
        }
    }

    // ─── Schema Helpers ───────────────────────────────────────────────────────

    private _getRawSchema(): any[] {
        try {
            const raw = localStorage.getItem('umbraco_page_schema');
            if (!raw) return [];
            const { schema } = JSON.parse(raw);
            return schema ?? [];
        } catch {
            return [];
        }
    }

    // ─── Display Formatting Helpers (schema-agnostic) ─────────────────────────

    private _humanizeKey(key: string): string {
        if (!key) return key;
        return key
            .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
            .replace(/^./, (c) => c.toUpperCase())
            .trim();
    }

    private _stripHtml(value: string): string {
        return value.replace(/<[^>]*>/g, '').trim();
    }

    private _formatFieldValue(value: any): { text: string; kind: 'bool-true' | 'bool-false' | 'text' } | null {
        if (value === null || value === undefined) return null;
        if (typeof value === 'string') {
            const trimmed = value.trim();
            if (trimmed === '') return null;
            if (trimmed === 'true') return { text: 'Yes', kind: 'bool-true' };
            if (trimmed === 'false') return { text: 'No', kind: 'bool-false' };
            if (trimmed.startsWith('<')) {
                const stripped = this._stripHtml(trimmed);
                return stripped ? { text: stripped, kind: 'text' } : null;
            }
            return { text: trimmed, kind: 'text' };
        }
        if (typeof value === 'boolean') {
            return { text: value ? 'Yes' : 'No', kind: value ? 'bool-true' : 'bool-false' };
        }
        if (Array.isArray(value)) {
            return value.length ? { text: value.join(', '), kind: 'text' } : null;
        }
        return { text: String(value), kind: 'text' };
    }

    private _renderFields(fields: Record<string, any> | undefined) {
        if (!fields) return null;
        const entries = Object.entries(fields)
            .map(([key, val]) => [key, this._formatFieldValue(val)] as const)
            .filter(([, formatted]) => formatted !== null);

        if (!entries.length) return html`<p class="empty-note">No fields set.</p>`;

        return html`
            <dl class="field-list">
                ${entries.map(([key, formatted]) => html`
                    <dt>${this._humanizeKey(key)}</dt>
                    <dd class=${formatted!.kind}>${formatted!.text}</dd>
                `)}
            </dl>
        `;
    }

    private _renderBlockGroup(groupLabel: string, blocksByKey: Record<string, any[]> | undefined): ReturnType<typeof html> | null {
        if (!blocksByKey) return null;
        const nonEmpty = Object.entries(blocksByKey).filter(([, arr]) => Array.isArray(arr) && arr.length);
        if (!nonEmpty.length) return null;

        return html`
            <div class="block-group">
                <span class="block-group-label">${groupLabel}</span>
                ${nonEmpty.map(([key, blocks]) => html`
                    <div class="block-subgroup">
                        ${nonEmpty.length > 1 || key !== groupLabel
                ? html`<span class="block-subgroup-label">${this._humanizeKey(key)}</span>`
                : null}
                        ${blocks.map((b) => this._renderBlock(b))}
                    </div>
                `)}
            </div>
        `;
    }

    private _renderBlock(block: any): ReturnType<typeof html> | null {
        if (!block) return null;
        const title = block.Name || block.Alias || 'Block';
        const hasNested = block.NestedBlocks && Object.values(block.NestedBlocks).some((a: any) => Array.isArray(a) && a.length);
        const hasAreas = block.Areas && Object.values(block.Areas).some((a: any) => Array.isArray(a) && a.length);

        return html`
            <details class="block-card" open>
                <summary>
                    <span class="block-title">${title}</span>
                    ${block.Region ? html`<span class="block-region">${block.Region}</span>` : null}
                </summary>
                <div class="block-body">
                    ${this._renderFields(block.Fields)}
                    ${hasNested ? this._renderBlockGroup('Nested Blocks', block.NestedBlocks) : null}
                    ${hasAreas ? this._renderBlockGroup('Areas', block.Areas) : null}
                </div>
            </details>
        `;
    }

    // ─── Rendering ────────────────────────────────────────────────────────────

    private get _previewTemplate() {
        if (this._loading) {
            return html`
                <div class="loading">
                    <uui-loader></uui-loader>
                    <p>Generating content...</p>
                </div>
            `;
        }
        if (!this._rawOutput) {
            return html`No content generated yet.`;
        }
        if (!this._parsedOutput) {
            // Fallback if parsing failed for some reason
            return html`<pre class="preview-text">${this._rawOutput}</pre>`;
        }

        const page = this._parsedOutput;

        return html`
            <div class="generated-preview">
                <div class="preview-toolbar">
                    ${page.PageType ? html`<uui-tag look="primary">${this._humanizeKey(page.PageType)}</uui-tag>` : null}
                    <uui-button
                        compact
                        look="secondary"
                        label="Toggle raw JSON"
                        @click=${() => { this._showRawJson = !this._showRawJson; }}>
                        ${this._showRawJson ? 'Hide' : 'View'} Raw JSON
                    </uui-button>
                </div>

                ${this._showRawJson
                ? html`<pre class="preview-text">${this._rawOutput}</pre>`
                : html`
                        <section class="page-fields">
                            <h4>Page Fields</h4>
                            ${this._renderFields(page.Fields)}
                        </section>

                        ${Array.isArray(page.Blocks) && page.Blocks.length ? html`
                            <section class="page-blocks">
                                <h4>Blocks</h4>
                                ${page.Blocks.map((b: any) => this._renderBlock(b))}
                            </section>
                        ` : null}
                    `}
            </div>
        `;
    }

    override render() {
        return html`
            <uui-box headline="AI Page Generator">
                <div class="layout">

                    <uui-form-layout-item label="Prompt">
                        <uui-textarea
                            .value=${this._prompt}
                            placeholder="Describe the page you want to generate..."
                            label="Prompt"
                            @input=${(e: Event) => {
                this._prompt = (e.target as HTMLTextAreaElement).value;
            }}>
                        </uui-textarea>
                    </uui-form-layout-item>

                    <div class="actions">
                        <uui-button
                            look="primary"
                            color="positive"
                            label="Generate"
                            ?disabled=${this._loading}
                            @click=${this._generate}>
                            Generate Page
                        </uui-button>
                    </div>

                    <uui-form-layout-item label="Preview">
                        <div class="preview">
                            ${this._previewTemplate}
                        </div>
                    </uui-form-layout-item>

                    ${this._errorMessage ? html`
                        <uui-tag look="danger">${this._errorMessage}</uui-tag>
                    ` : null}

                    <uui-form-layout-item label="Parent Page">
                        ${this._parentLoading
                ? html`<uui-loader></uui-loader>`
                : html`
                                <uui-select
                                    label="Parent Page"
                                    placeholder="Select a parent page"
                                    .options=${this._parentPages.map(p => ({
                    name: p.name,
                    value: p.id,
                    selected: p.id === this._selectedParentId,
                }))}
                                    @change=${(e: Event) => {
                        this._selectedParentId = (e.target as HTMLSelectElement).value;
                    }}>
                                </uui-select>
                            `}
                    </uui-form-layout-item>

                    <div class="actions">
                        <uui-button
                            look="primary"
                            label="Create Content"
                            ?disabled=${!this._rawOutput || this._loading || !this._selectedParentId}
                            @click=${this._createContent}>
                            Create Content
                        </uui-button>
                    </div>

                </div>
            </uui-box>
        `;
    }

    static override styles = [css`
        :host {
            display: block;
            padding: 20px;
        }

        .layout {
            display: flex;
            flex-direction: column;
            gap: 1rem;
        }

        .actions {
            display: flex;
            gap: 0.75rem;
        }

        .preview {
            min-height: 300px;
            padding: 1rem;
            border: 1px solid var(--uui-color-border);
            border-radius: var(--uui-border-radius);
            background: var(--uui-color-surface);
            overflow: auto;
        }

        .preview-text {
            margin: 0;
            white-space: pre-wrap;
            word-break: break-word;
            font-family: monospace;
        }

        .loading {
            display: flex;
            align-items: center;
            gap: 0.75rem;
        }

        uui-textarea {
            width: 100%;
            min-height: 150px;
        }

        /* ── Generated preview ───────────────────────────────────────────── */

        .preview-toolbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 1rem;
        }

        .page-fields h4,
        .page-blocks h4 {
            margin: 0 0 0.5rem 0;
            font-size: 0.85rem;
            text-transform: uppercase;
            letter-spacing: 0.03em;
            color: var(--uui-color-text-alt);
        }

        .page-blocks {
            margin-top: 1.5rem;
        }

        .empty-note {
            color: var(--uui-color-text-alt);
            font-style: italic;
            margin: 0;
        }

        .field-list {
            display: grid;
            grid-template-columns: max-content 1fr;
            gap: 0.35rem 1rem;
            margin: 0;
        }

        .field-list dt {
            font-weight: 600;
            color: var(--uui-color-text-alt);
        }

        .field-list dd {
            margin: 0;
        }

        .field-list dd.bool-true {
            color: var(--uui-color-positive);
            font-weight: 600;
        }

        .field-list dd.bool-false {
            color: var(--uui-color-text-alt);
        }

        .block-card {
            border: 1px solid var(--uui-color-border);
            border-radius: var(--uui-border-radius);
            background: var(--uui-color-surface-alt, var(--uui-color-surface));
            margin-bottom: 0.75rem;
            padding: 0;
        }

        .block-card summary {
            list-style: none;
            cursor: pointer;
            padding: 0.6rem 0.9rem;
            display: flex;
            align-items: center;
            gap: 0.5rem;
            font-weight: 600;
        }

        .block-card summary::-webkit-details-marker {
            display: none;
        }

        .block-card summary::before {
            content: "▸";
            color: var(--uui-color-text-alt);
            transition: transform 0.15s ease;
        }

        .block-card[open] > summary::before {
            transform: rotate(90deg);
        }

        .block-title {
            font-size: 0.95rem;
        }

        .block-region {
            font-size: 0.7rem;
            text-transform: uppercase;
            letter-spacing: 0.03em;
            padding: 0.1rem 0.5rem;
            border-radius: 999px;
            background: var(--uui-color-current, #eee);
            color: var(--uui-color-text-alt);
        }

        .block-body {
            padding: 0 0.9rem 0.9rem 0.9rem;
        }

        .block-group {
            margin-top: 0.75rem;
            padding-left: 0.9rem;
            border-left: 2px solid var(--uui-color-border);
        }

        .block-group-label {
            display: block;
            font-size: 0.75rem;
            text-transform: uppercase;
            letter-spacing: 0.03em;
            color: var(--uui-color-text-alt);
            margin-bottom: 0.4rem;
        }

        .block-subgroup-label {
            display: block;
            font-size: 0.75rem;
            font-weight: 600;
            margin: 0.4rem 0 0.3rem 0;
        }
    `];
}

export default AIGeneratorView;