# ThtaAi — AI Content Generation for Umbraco

ThtaAi adds AI-powered content generation directly into the Umbraco 17 backoffice, using a local LLM (e.g. [Ollama](https://ollama.com)) or another external LLM API, so your content and prompts never leave your own infrastructure.

It provides AI-assisted property editors, a rich text editor toolbar extension, and a full **AI Page Generation** tool that can scaffold entire content pages — document type, blocks, and all — from a single natural-language prompt.

## Features

- **AI Textstring / AI Textarea** — property editors with a "Generate" button that opens a chat-style modal for iterative content drafting.
- **AI Rich Text Editor extension** — a Tiptap toolbar button that generates and inserts content at the current cursor position, without leaving the editor.
- **AI Image** — generates several image variations from a text prompt, sourced from Pixabay, so editors can pick the closest match.
- **AI Page Generation** — analyses your site's document types and block structures, then generates a complete unpublished page from a prompt, ready for editorial review.

## Prerequisites

- Umbraco CMS 17
- [Ollama](https://ollama.com) running and reachable from your Umbraco site, with a chat-capable model pulled (we've had the best results with `qwen2.5:7b`)
- Optional: a [Pixabay API key](https://pixabay.com/api/docs/) if you want the AI Image editor to return results

## Installation

```bash
dotnet add package ThtaAi
```

Then add configuration to your project's `appsettings.json` (see below) and restart the site. The property editors and the **AI Generation** backoffice section become available immediately — no further setup required.

## Configuration

Add an `AiGeneration` section to `appsettings.json`:

```json
{
  "AiGeneration": {
    "BaseUrl": "http://localhost:11434",
    "ApiKey": "",
    "Model": "qwen2.5:7b",
    "TimeoutSeconds": 300,
    "PixabayKey": "YOUR_PIXABAY_API_KEY"
  }
}
```

| Option | Description |
|---|---|
| `BaseUrl` | Endpoint of your LLM instance (or any OpenAI-compatible chat completions endpoint). |
| `ApiKey` | Auth key for the LLM endpoint, if required. Leave empty for a local LLM instance with no auth. |
| `Model` | The model to use for generation. Must already be pulled on the LLM instance. |
| `TimeoutSeconds` | Maximum time allowed per request before it's aborted. |
| `PixabayKey` | Auth key for AI Image's stock photo search. Leave empty to disable the AI Image editor. |

> If `BaseUrl` is left unset, generation requests will fail with a clear configuration error rather than a silent timeout.

## How It Works

1. Assign an AI property editor (Textstring, Textarea, Rich Text extension, or Image) to a Data Type.
2. In the backoffice, click **Generate** on that field.
3. A chat-style modal opens — describe what you want in plain language and refine it iteratively.
4. Click **Insert** to place the result into the field, then save and publish as normal.

Each time the modal opens, a fresh conversation begins server-side, so context from a previous editing session never bleeds into a new one.

## AI Page Generation

For generating whole pages rather than individual fields, use the **AI Generation** section in the backoffice:

1. The extension inspects your site's document types, Block List / Block Grid configurations, and available block types to build a schema of what content structures exist.
2. You describe the page you want and pick a parent page for it to be created under.
3. The model generates a full page — including nested blocks where applicable — conforming to your existing content architecture.
4. The page is created **unpublished**, so an editor can review, adjust, and publish it through Umbraco's normal workflow.

Because generation is schema-aware, output aligns with your site's actual document types and property editors rather than requiring manual restructuring afterwards.

## Prompt Handling

You don't need to write carefully engineered prompts. Plain natural-language input is automatically restructured before being sent to the model, with system-level guidance applied for tone, formatting, and CMS-appropriate content length, and prior conversation turns included for continuity.

## Contributing

Build instructions and architecture notes for contributors live in [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

MIT