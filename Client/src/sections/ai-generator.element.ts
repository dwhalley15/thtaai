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

            console.log("Raw output:", this._rawOutput);

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