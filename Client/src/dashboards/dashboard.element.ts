import {
  LitElement,
  css,
  html,
  customElement,
  state,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";

@customElement("example-dashboard")
export class ExampleDashboardElement extends UmbElementMixin(LitElement) {


  @state()
  private _prompt: string = "";

  @state()
  private _result: string = "";

  @state()
  private _loading: boolean = false;


  constructor() {
    super();
  }


  render() {
    return html`
    <uui-box headline="AI Assistant">

      <div style="display: flex; flex-direction: column; gap: 12px;">

        <uui-textarea
          placeholder="Enter prompt..."
          .value=${this._prompt}
          @input=${(e: Event) => {
        this._prompt = (e.target as HTMLTextAreaElement).value;
      }}
        ></uui-textarea>

        <uui-button
          look="primary"
          color="default"
          ?disabled=${this._loading}
        >
          ${this._loading ? "Generating..." : "Generate"}
        </uui-button>

        <uui-box headline="Result">
          <pre style="white-space: pre-wrap;">${this._result}</pre>
        </uui-box>

      </div>

    </uui-box>
  `;
  }

  static styles = [
    css`
      :host {
        display: grid;
        gap: var(--uui-size-layout-1);
        padding: var(--uui-size-layout-1);
        grid-template-columns: 1fr 1fr 1fr;
      }

      uui-box {
        margin-bottom: var(--uui-size-layout-1);
      }

      h2 {
        margin-top: 0;
      }

      .wide {
        grid-column: span 3;
      }
    `,
  ];
}

export default ExampleDashboardElement;

declare global {
  interface HTMLElementTagNameMap {
    "example-dashboard": ExampleDashboardElement;
  }
}
