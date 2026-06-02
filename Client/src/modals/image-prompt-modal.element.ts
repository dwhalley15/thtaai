import { html } from "lit";
import { customElement, property, state } from "lit/decorators.js";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { css } from '@umbraco-cms/backoffice/external/lit';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

import type {
    UmbModalContext,
    UmbModalExtensionElement
} from "@umbraco-cms/backoffice/modal";

import type {
    ImageGenerateResponse,
    ImagePromptModalData,
    ImagePromptModalValue
} from "./image-prompt-modal.token";

@customElement("thta-ai-image-prompt-modal")
export class ImagePromptModalElement
    extends UmbLitElement
    implements UmbModalExtensionElement<
        ImagePromptModalData,
        ImagePromptModalValue
    > {

    @property({ attribute: false })
    modalContext?: UmbModalContext<
        ImagePromptModalData,
        ImagePromptModalValue
    >;

    @state()
    private _prompt = "";

    @state()
    private _selectedPreviewUrl?: string;

    @state()
    private _altText?: string;

    @state()
    private _results: ImageGenerateResponse[] = [];

    @state()
    private _selectedIndex?: number;

    @state()
    private _loading = false;


    override connectedCallback() {
        super.connectedCallback();

        this._prompt = this.modalContext?.data?.prompt ?? "";

        this._results = [];
        this._selectedIndex = undefined;
        this._selectedPreviewUrl = undefined;
        this._altText = undefined;

    }


    private _close() {
        this.modalContext?.reject();
    }

    private async _searchImage() {

        this._loading = true;

        try {

            const auth = await this.getContext(UMB_AUTH_CONTEXT);
            const token = await auth?.getLatestToken();

            const res = await fetch("/umbraco/thtaai/api/v1/generateImage", {
                method: "POST",
                credentials: "include",
                headers: {
                    "Content-Type": "application/json",
                    "Authorization": `Bearer ${token}`
                },
                body: JSON.stringify({
                    prompt: this._prompt
                })
            });

            if (!res.ok) {
                console.error("Image API failed:", res.status);
                return;
            }

            this._results = await res.json();
            this._selectedIndex = undefined;

        } finally {
            this._loading = false;
        }
    }

    private _select(index: number) {

        this._selectedIndex = index;

        const selected = this._results[index];

        this._selectedPreviewUrl = selected.previewUrl;
        this._altText = selected.altText;
    }

    private async _confirm() {
        this._loading = true;

        const selected = this._results[this._selectedIndex!];

        const auth = await this.getContext(UMB_AUTH_CONTEXT);
        const token = await auth?.getLatestToken();

        const res = await fetch("/umbraco/thtaai/api/v1/uploadImage", {
            method: "POST",
            credentials: "include",
            headers: {
                "Content-Type": "application/json",
                "Authorization": `Bearer ${token}`
            },
            body: JSON.stringify({
                imageUrl: selected.mediaUrl,
                altText: selected.altText
            })
        });

        const uploaded = await res.json();

        const value = {
            mediaKey: uploaded.mediaKey,
            url: selected.mediaUrl,
            altText: selected.altText
        };

        this.modalContext?.updateValue(value);
        this.modalContext?.submit();

        this._loading = false;
    }

    render() {
        return html`
      <uui-dialog-layout headline="Generate Image">

        <div style="padding: 12px;">
          <uui-textarea
            label="Image prompt"
            .value=${this._prompt}
            placeholder="Describe the image you want..."
            @input=${(e: InputEvent) => {
                const target = e.target as HTMLTextAreaElement;
                this._prompt = target.value;
            }}
            ></uui-textarea>

        ${this._loading
                ? html`
                    <div style="padding:24px; text-align:center;">
                        <uui-loader></uui-loader>
                        <p>Generating images...</p>
                    </div>
                `
                : null
            }

          ${this._results.length
                ? html`
                    <div style="display:grid; grid-template-columns:repeat(3, 1fr); gap:8px; margin-top:12px;">
                        ${this._results.map((img, index) => html`
                            <div
                                style="
                                    border:2px solid ${this._selectedIndex === index ? 'blue' : '#ddd'};
                                    padding:4px;
                                    cursor:pointer;
                                "
                                @click=${() => this._select(index)}
                            >
                                <img src=${img.previewUrl} style="width:100%; height:120px; object-fit:cover;" />
                                <small>${img.altText}</small>
                            </div>
                        `)}
                    </div>
                `
                : null
            }

          ${this._selectedPreviewUrl
                ? html`
                <div style="margin-top:12px;">
                  <img src=${this._selectedPreviewUrl} style="max-width:100%;" />
                  <p>${this._altText}</p>
                </div>
              `
                : null
            }
        </div>

        <uui-button label="Cancel" slot="actions" @click=${this._close}>
          Cancel
        </uui-button>

        ${!this._results.length && !this._loading
                ? html`
                <uui-button
                    slot="actions"
                    label="Generate images"
                    look="primary"
                    ?disabled=${!this._prompt.trim()}
                    @click=${this._searchImage}
                >
                    Generate Images
                </uui-button>
            `
                : null
            }

        <uui-button
          label="Insert"
          slot="actions"
          look="primary"
          color="positive"
          ?disabled=${this._selectedIndex === undefined || this._loading}
          @click=${this._confirm}
        >
          Insert
        </uui-button>

      </uui-dialog-layout>
    `;
    }

    static styles = [
        css`
            :host {
              display: block;
              max-width: 90vw;
              width: 800px;
            }
    
            uui-dialog-layout::part(main) {
              display: flex;
              flex-direction: column;
              gap: 12px;
              max-height: 80vh;
              overflow: hidden;
            }
    
    
            uui-textarea {
              width: 100%;
              max-width: 100%;
              box-sizing: border-box;
            }
          `
    ];
}

export default ImagePromptModalElement;