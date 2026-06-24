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
    @state() private _rawOutput = "";
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

    // ─── Parent Pages ─────────────────────────────────────────────────────────

    private async _loadParentPages() {
        this._parentLoading = true;
        try {
            const authContext = await this.getContext(UMB_AUTH_CONTEXT);
            const token = await authContext?.getLatestToken();

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
        this._loading = true;

        if (!this._prompt.trim()) {
            this._errorMessage = "Prompt cannot be empty.";
            this._loading = false;
            return;
        }

        if (this._isNewConversation) {
            this._conversationId = crypto.randomUUID();
        }

        try {
            const authContext = await this.getContext(UMB_AUTH_CONTEXT);
            const token = await authContext?.getLatestToken();

            const schema = this._getCleanSchema();
            if (!schema.length) {
                this._errorMessage = "No page schema found. Please generate the schema first.";
                this._loading = false;
                return;
            }

            const response = await fetch("/umbraco/thtaai/api/v1/generatePage", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    prompt: this._prompt,
                    conversationId: this._conversationId,
                    isNewConversation: this._isNewConversation,
                    schema,
                })
            });

            if (!response.ok) throw new Error("Generation request failed");

            const { rawOutput } = await response.json();
            this._rawOutput = JSON.stringify(rawOutput, null, 2);
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
            const authContext = await this.getContext(UMB_AUTH_CONTEXT);
            const token = await authContext?.getLatestToken();

            const llmResponse = JSON.parse(this._rawOutput);

            // Find matching schema entry for this page type
            const rawSchema = this._getRawSchema();
            const matchingEntry = rawSchema.find((s: any) => s.docType.alias === llmResponse.pageType);

            if (!matchingEntry) {
                this._errorMessage = `No schema found for page type: ${llmResponse.pageType}`;
                return;
            }

            const documentTypeId = matchingEntry.docType.id;
            const templateId = matchingEntry.docType.allowedTemplates?.[0]?.id ?? null;

            if (!this._selectedParentId) {
                this._errorMessage = "Please select a parent page.";
                return;
            }

            // Step 1: map LLM response to Umbraco document model
            const cleanSchema = this._getCleanSchema().find(
                (s: any) => s.pageType === llmResponse.pageType
            );

            console.log("LLM Response:", llmResponse);

            console.log("Clean Schema for mapping:", cleanSchema);

            const mapResponse = await fetch("/umbraco/thtaai/api/v1/mapContent", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    llmResponse,
                    schema: cleanSchema,
                })
            });

            if (!mapResponse.ok) throw new Error("Content mapping failed");

            const mapped = await mapResponse.json();

            console.log("Mapped content ready for creation:", mapped);

            // Step 2: fill in document type, parent and template then create
            const createPayload = {
                ...mapped,
                documentType: { id: documentTypeId },
                parent: { id: this._selectedParentId },
                ...(templateId ? { template: { id: templateId } } : {}),
            };

            const createResponse = await fetch("/umbraco/management/api/v1/document", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(createPayload),
            });

            if (!createResponse.ok) {
                const text = await createResponse.text();
                throw new Error(`Document creation failed: ${text}`);
            }

            const newId = createResponse.headers.get("Umb-Generated-Resource");
            this._errorMessage = null;
            alert(`Page created successfully! ID: ${newId}`);

        } catch (error: any) {
            this._errorMessage = error.message || "An error occurred while creating content.";
            console.error(error);
        } finally {
            this._loading = false;
        }
    }

    // ─── Schema Helpers ───────────────────────────────────────────────────────

    /** Raw schema as stored in localStorage — includes docType metadata. */
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

    /** Cleaned schema sent to the API — strips docType metadata, keeps field/block structure. */
    private _getCleanSchema(): any[] {
        try {
            return this._getRawSchema().map((s: any) => ({
                pageType: s.docType.alias,
                allowedChildren: s.allowedChildren?.map((c: any) => c.alias) ?? [],
                fields: this._extractSimpleFields(s.properties),
                blockProperties: this._extractBlockProperties(s.properties),
            }));
        } catch {
            return [];
        }
    }

    private _extractBlockProperties(properties: any[]): any[] {
        return (properties ?? [])
            .filter((p: any) => p.blocks?.length)
            .map((p: any) => ({
                alias: p.alias,
                editorAlias: p.type,
                // Separate area containers from direct blocks
                areaContainers: p.blocks
                    .filter((b: any) => b.areas?.length > 0)
                    .map((b: any) => this._extractAreaContainer(b)),
                directBlocks: p.blocks
                    .filter((b: any) => !b.areas?.length)
                    .map((b: any) => this._extractBlockDefinition(b)),
            }));
    }

    private _extractAreaContainer(b: any): any {
        return {
            id: b.id,
            name: b.name,
            isAreaContainer: true,
            areas: b.areas.map((a: any) => ({
                key: a.key,
                alias: a.alias ?? a.key,
                allowedBlocks: a.allowedBlocks?.map((ab: any) => this._extractBlockDefinition(ab)) ?? [],
            })),
        };
    }

    private _extractBlockDefinition(b: any): any {
        return {
            id: b.id,
            name: b.name,
            areas: b.areas ?? [],  // ← add this
            fields: (b.properties ?? [])
                .filter((p: any) => !p.blocks?.length)
                .map((p: any) => ({ alias: p.alias, editorAlias: p.type })),
            nestedBlocks: (b.properties ?? [])
                .filter((p: any) => p.blocks?.length)
                .map((p: any) => ({
                    field: p.alias,
                    allowedBlocks: p.blocks.map((nb: any) => this._extractBlockDefinition(nb)),
                })),
        };
    }

    private _extractSimpleFields(properties: any[]): string[] {
        return (properties ?? [])
            .filter((p: any) => !p.blocks?.length)
            .map((p: any) => p.alias);
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
        return html`<pre class="preview-text">${this._rawOutput}</pre>`;
    }

    override render() {
        return html`
            <uui-box headline="AI Page Generator">
                <div class="layout">

                    <uui-form-layout-item label="Parent Page">
                        ${this._parentLoading
                ? html`<uui-loader></uui-loader>`
                : html`
                                            <h4>Select the Parent Page</h4>
                                            <uui-select label="Parent Page" placeholder="Select a parent page"
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

                    <uui-form-layout-item label="Raw Output Preview">
                        <div class="preview">
                            ${this._previewTemplate}
                        </div>
                    </uui-form-layout-item>

                    ${this._errorMessage ? html`
                        <uui-tag look="danger">${this._errorMessage}</uui-tag>
                    ` : null}

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
            white-space: pre-wrap;
            overflow: auto;
            font-family: monospace;
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
    `];
}

export default AIGeneratorView;