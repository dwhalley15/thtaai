import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { customElement, property, state } from "lit/decorators.js";
import { html } from "lit";
import { keyed } from "lit/directives/keyed.js";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UmbPropertyEditorUiElement } from '@umbraco-cms/backoffice/property-editor';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import type { UmbModalManagerContext } from '@umbraco-cms/backoffice/modal';
import { UMB_MEDIA_PICKER_MODAL } from '@umbraco-cms/backoffice/media';
import { THTA_AI_IMAGE_PROMPT_MODAL } from "../modals/image-prompt-modal.token";

@customElement("thta-ai-image")
export class ThtaAiImageElement extends UmbLitElement implements UmbPropertyEditorUiElement {

    @property({ attribute: false, reflect: false })
    value: any;

    @state()
    private _currentMediaKey: string | undefined;

    @state()
    private _modalContext?: UmbModalManagerContext;

    override willUpdate(changed: Map<string, unknown>) {
        if (changed.has('value')) {
            this._currentMediaKey = this.value?.[0]?.mediaKey;
        }
    }

    constructor() {
        super();
        this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (instance) => {
            this._modalContext = instance;
        });
    }

    private _setMedia(mediaKey: string | undefined) {
        this._currentMediaKey = mediaKey;
        this.value = mediaKey ? [{ mediaKey }] : null;
        this.dispatchEvent(new UmbChangeEvent());
    }

    private async _pickExisting() {
        if (!this._modalContext) return;

        const modal = this._modalContext.open(this, UMB_MEDIA_PICKER_MODAL, {
            data: { multiple: false }
        });

        const result = await modal.onSubmit().catch(() => null);
        if (!result?.selection?.length) return;

        const item = result.selection[0] as string | { unique?: string };
        const mediaKey = typeof item === 'string' ? item : item.unique;
        if (!mediaKey) return;
        this._setMedia(mediaKey);
    }

    private async _generateAiImage() {
        if (!this._modalContext) return;

        const modal = this._modalContext.open(
            this,
            THTA_AI_IMAGE_PROMPT_MODAL,
            {
                data: {
                    prompt: ""
                }
            }
        );

        const result = await modal.onSubmit().catch(() => null);

        if (!result?.mediaKey) return;

        this._setMedia(result.mediaKey);
    }

    private _clear() {
        this._setMedia(undefined);
    }

    render() {
        return html`
            <div style="display:flex; flex-direction:column; gap:12px;">

                ${this._currentMediaKey
                ? html`
                        <div style="border:1px solid #ddd; border-radius:8px; padding:8px; width:220px;">
                            ${keyed(this._currentMediaKey, html`
                                <umb-imaging-thumbnail
                                    .unique=${this._currentMediaKey}
                                ></umb-imaging-thumbnail>
                            `)}
                        </div>
                    `
                : html`
                        <div style="opacity:0.6;">No image selected</div>
                    `
            }

                <div style="display:flex; gap:8px;">
                    <uui-button look="secondary" label="Pick image" @click=${this._pickExisting}>
                        Pick
                    </uui-button>
                    <uui-button look="primary" label="Generate image" @click=${this._generateAiImage}>
                        Generate
                    </uui-button>
                    <uui-button look="danger" label="Remove" @click=${this._clear}>
                        Remove
                    </uui-button>
                </div>

            </div>
        `;
    }
}

export { ThtaAiImageElement as default };