import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { customElement, property } from "lit/decorators.js";
import { html } from "lit";
import { UMB_MODAL_MANAGER_CONTEXT } from "@umbraco-cms/backoffice/modal";
import { THTA_AI_PROMPT_MODAL }
    from "../modals/prompt-modal.token";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import type { UmbPropertyEditorUiElement } from '@umbraco-cms/backoffice/property-editor';

@customElement("thta-ai-textstring")
export class ThtaAiTextstringElement extends UmbLitElement implements UmbPropertyEditorUiElement {

    private _modalManager?: any;

    override connectedCallback() {
        super.connectedCallback();

        this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (instance) => {
            this._modalManager = instance;
        });
    }

    @property({ type: String })
    value = "";


    private async _openModal() {

        const modal = this._modalManager?.open(
            this,
            THTA_AI_PROMPT_MODAL,
            {
                data: { prompt: "" },
            }
        );

        if (!modal) {
            console.warn("Modal manager not available");
            return;
        }

        let result;

        try {
            result = await modal.onSubmit?.();
        } catch (e) {
            console.error("Error awaiting modal result", e);
            return;
        }

        if (!result?.generated) return;

        this.value = result.generated;

        this.dispatchEvent(new UmbChangeEvent());
    }


    private _onInput(e: Event) {
        this.value = (e.target as HTMLInputElement).value ?? "";

        this.dispatchEvent(new UmbChangeEvent());
    }

    render() {
        return html`
        <div style="display:flex; gap:8px; align-items:flex-start;">

            <uui-input
                label="AI Generated Content"
                .value=${this.value ?? ""}
                @input=${this._onInput}
                style="flex:1;"
            ></uui-input>

            <uui-button
                label="Generate"
                look="primary"
                @click=${this._openModal}
            >
                Generate
            </uui-button>

        </div>
        `;
    }
}

export { ThtaAiTextstringElement as default };