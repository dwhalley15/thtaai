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

    @state()
    private _prompt = "";

    @state()
    private _rawOutput = "";

    @state()
    private _loading = false;

    @state()
    private _errorMessage: string | null = null;

    @state()
    private _conversationId?: string;

    @state()
    private _isNewConversation = true;


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
                    schema: schema
                })
            });

            if (!response.ok || !response.body) {
                throw new Error("Generation request failed");
            }

            const { rawOutput } = await response.json();

            this._rawOutput = JSON.stringify(rawOutput, null, 2);

            this._isNewConversation = false;

        } catch (error: any) {
            this._errorMessage = error.message || "An error occurred during generation.";
            console.error(error);
        } finally {
            this._loading = false;
        }
    }

    private async _createContent() {
        // TODO: create Umbraco content item
        console.log("Creating content from:", this._rawOutput);
    }

    private _getCleanSchema(): any[] {
        try {
            const raw = localStorage.getItem('umbraco_page_schema');
            if (!raw) return [];
            const { schema } = JSON.parse(raw);

            return (schema ?? []).map((s: any) => ({
                pageType: s.docType.alias,
                allowedChildren: s.allowedChildren?.map((c: any) => c.alias) ?? [],
                fields: this._extractSimpleFields(s.properties),
                availableBlocks: this._extractBlocks(s.properties),
            }));
        } catch {
            return [];
        }
    }

    private _extractBlocks(properties: any[]): any[] {
        if (!properties?.length) return [];

        const seen = new Set<string>();
        const blocks: any[] = [];

        for (const p of properties) {
            if (!p.blocks?.length) continue;

            for (const b of p.blocks) {
                if (seen.has(b.name)) continue;
                seen.add(b.name);

                blocks.push({
                    name: b.name,
                    fields: this._extractSimpleFields(b.properties ?? []),
                    // Nested block properties (e.g. buttons inside a header)
                    // are kept as named block lists rather than flattened up
                    nestedBlocks: this._extractNestedBlockProps(b.properties ?? []),
                });
            }
        }

        return blocks;
    }

    // Returns only non-block property aliases
    private _extractSimpleFields(properties: any[]): string[] {
        return (properties ?? [])
            .filter((p: any) => !p.blocks?.length)
            .map((p: any) => p.alias);
    }

    // Returns block-type properties as named slots with their allowed block names
    private _extractNestedBlockProps(properties: any[]): any[] {
        return (properties ?? [])
            .filter((p: any) => p.blocks?.length)
            .map((p: any) => ({
                field: p.alias,
                allowedBlocks: p.blocks.map((b: any) => ({
                    name: b.name,
                    fields: this._extractSimpleFields(b.properties ?? []),
                })),
            }));
    }


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

                    <uui-form-layout-item label="Prompt">
                        <uui-textarea
                            .value=${this._prompt}
                            placeholder="Describe the page you want to generate..."
                            label="Prompt"
                            @input=${(e: Event) => {
                const target = e.target as HTMLTextAreaElement;
                this._prompt = target.value;
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

                    ${this._errorMessage
                ? html`
                        <uui-tag type="danger">
                            ${this._errorMessage}
                        </uui-tag>
                        `
                : null}

                    <div class="actions">
                        <uui-button
                            look="primary"
                            label="Create Content"
                            ?disabled=${!this._rawOutput || this._loading}
                            @click=${this._createContent}>
                            Create Content
                        </uui-button>
                    </div>

                </div>

            </uui-box>
        `;
    }

    static override styles = [
        css`
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

            .loading uui-loader {
                display: inline-flex;
                align-items: center;
            }

            uui-textarea {
                width: 100%;
                min-height: 150px;
            }
        `,
    ];
}

export default AIGeneratorView;