import { html } from "lit";
import { customElement, property, state } from "lit/decorators.js";
import { css } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { createRef, ref } from "lit/directives/ref.js";
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

import type {
  UmbModalContext,
  UmbModalExtensionElement
} from "@umbraco-cms/backoffice/modal";

import type {
  PromptModalData,
  PromptModalValue
} from "./prompt-modal.token.ts";

type ConversationMessage = {
  role: "user" | "assistant";
  content: string;
};

@customElement("thta-ai-prompt-modal")
export class PromptModalElement
  extends UmbLitElement
  implements UmbModalExtensionElement<PromptModalData, PromptModalValue> {
  @property({ attribute: false })
  modalContext?: UmbModalContext<
    PromptModalData,
    PromptModalValue
  >;

  @state()
  private _messages: ConversationMessage[] = [];

  @state()
  private _conversationId?: string;

  @state()
  private _generated = "";

  @state()
  private _prompt = "";

  @state()
  private _loading = false;

  @state()
  private _errorMessage: string | null = null;

  @state()
  private _isNewConversation = true;

  private _messagesRef = createRef<HTMLDivElement>();

  override connectedCallback() {
    super.connectedCallback();

    this._prompt = this.modalContext?.data?.prompt ?? "";

    this._messages = [];
    this._conversationId = crypto.randomUUID();
    this._generated = "";
    this._errorMessage = null;
    this._isNewConversation = true;
  }

  private async _submit() {
    this._errorMessage = null;
    this._loading = true;

    const currentPrompt = this._prompt;

    // add user message
    this._messages = [
      ...this._messages,
      { role: "user", content: currentPrompt }
    ];

    // add empty assistant message (we will stream into it)
    this._messages = [
      ...this._messages,
      { role: "assistant", content: "" }
    ];

    await this._scrollToBottom();

    let assistantText = "";

    try {

      const authContext = await this.getContext(UMB_AUTH_CONTEXT);
      const token = await authContext?.getLatestToken();

      const response = await fetch("/umbraco/thtaai/api/v1/generateStream", {
        method: "POST",
        credentials: "include",
        headers: {
          "Authorization": `Bearer ${token}`,
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          prompt: currentPrompt,
          conversationId: this._conversationId,
          isNewConversation: this._isNewConversation
        })
      });

      if (!response.ok || !response.body) {
        throw new Error("Streaming request failed");
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();

      let buffer = "";

      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        
        buffer += decoder.decode(value, { stream: true });

        let boundaryIndex;

        while ((boundaryIndex = buffer.indexOf("\n\n")) !== -1) {
          const rawEvent = buffer.slice(0, boundaryIndex);
          buffer = buffer.slice(boundaryIndex + 2);

          const lines = rawEvent.split("\n");


          for (const line of lines) {
            if (!line.startsWith("data:")) continue;

            const data = line.replace("data:", "").trim();
            if (data === "[DONE]") {
              break;
            }

            let parsed;
            try {
              parsed = JSON.parse(data);
            } catch {
              continue;
            }

            const delta = parsed?.delta;

            if (delta) {
              assistantText += delta;

              const last = this._messages[this._messages.length - 1];

              if (last?.role === "assistant") {
                last.content = assistantText;
                this._messages = [...this._messages];

                this.requestUpdate();

                await this.updateComplete;

                await new Promise(resolve => setTimeout(resolve, 30));
              }
            }
          }
        }

        await this._scrollToBottom();
      }

      this._generated = assistantText;

      this._prompt = "";
      this._isNewConversation = false;

      this.modalContext?.updateValue({
        prompt: currentPrompt
      });

    } catch (err: any) {
      this._errorMessage = err?.message ?? "Generation failed.";
    } finally {
      this._loading = false;
    }
  }

  private _close() {
    this.modalContext?.reject();
  }

  private _insert() {
    this.modalContext?.updateValue({
      generated: this._generated
    });

    this.modalContext?.submit();
  }

  private async _scrollToBottom() {
    await this.updateComplete;

    const el = this._messagesRef.value;
    if (!el) return;

    el.scrollTop = el.scrollHeight;
  }

  render() {
    return html`
      <uui-dialog-layout headline="Generate AI Content">

       <div class="messages" ${ref(this._messagesRef)}>
        ${this._messages.map(message => html`
          <div class="message">
            <strong>${message.role === "user" ? "You" : "AI"}:</strong>
            <p>${message.content}</p>
          </div>
        `)}
       </div>

        <uui-textarea
          .placeholder=${`Write a prompt...`}
          .rows=${2}
          label="Prompt"
          .value=${this._prompt}
          @input=${(e: any) => {
        this._prompt = e.target.value;
      }}
        ></uui-textarea>

        ${this._errorMessage
        ? html`
              <uui-toast-notification type="danger">
                ${this._errorMessage}
              </uui-toast-notification>
            `
        : null}

        <uui-button
          label="Cancel"
          slot="actions"
          @click=${this._close}
        >
          Cancel
        </uui-button>

        <uui-button
          label="Generate"
          slot="actions"
          look="primary"
          ?disabled=${this._loading}
          @click=${this._submit}
        >
          ${this._loading ? "Generating..." : "Generate"}
        </uui-button>

        <uui-button
          label="Insert"
          slot="actions"
          look="primary"
          color="positive"
          ?disabled=${this._messages.length === 0 || this._loading}
          @click=${this._insert}
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

        .messages {
          display: flex;
          flex-direction: column;
          gap: 8px;
          max-height: 400px;
          overflow-y: auto;
          padding: 8px;
          border-radius: 6px;
          background: var(--uui-color-background);
          margin-bottom: 8px;
        }

        .message {
          margin-bottom: 12px;
          padding-bottom: 8px;
          border-bottom: 1px solid var(--uui-color-border);
        }

        .message:last-child {
          border-bottom: none;
        }

        uui-textarea {
          width: 100%;
          max-width: 100%;
          box-sizing: border-box;
        }
      `
  ];
}

export default PromptModalElement;