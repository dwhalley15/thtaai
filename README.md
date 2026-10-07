# HuGen — AI Content Generation for Umbraco

HuGen adds AI-powered content generation directly into the Umbraco 17 backoffice.

It can use a locally hosted LLM, such as [Ollama](https://ollama.com), or another compatible LLM API, giving you control over where your content and prompts are processed.

HuGen provides AI-assisted property editors, a rich text editor toolbar extension, image search, and a full **AI Page Generation** tool capable of scaffolding complete content pages — including document types, properties and blocks — from a natural-language prompt.

## Features

- **AI Textstring / AI Textarea** — property editors with a **Generate** button that opens a chat-style modal for iterative content drafting.
- **AI Rich Text Editor extension** — a Tiptap toolbar button that generates and inserts content at the current cursor position without leaving the editor.
- **AI Image** — searches for several image options based on a text prompt using Pixabay, allowing editors to select the most appropriate result.
- **AI Page Generation** — analyses your site's document types and block structures, then generates a complete unpublished page from a prompt, ready for editorial review.

## Prerequisites

- Umbraco CMS 17
- Umbraco Delivery API enabled
- Access to a compatible LLM endpoint
- A suitable chat-capable model
- Optional: a [Pixabay API key](https://pixabay.com/api/docs/) if you want to use the AI Image editor

HuGen can be used with a locally hosted LLM. For example, you can run [Ollama](https://ollama.com) on your own infrastructure and configure HuGen to use it.

We've had good results with models such as `qwen2.5:7b`, although you can configure HuGen to use another suitable model.

## Installation

Install HuGen from NuGet:

```bash
dotnet add package HuGen
```

Then add the HuGen configuration to your project's `appsettings.json` and restart the site.

The HuGen property editors and **AI Generation** backoffice functionality will then become available.

## Configuration

HuGen is configured through your Umbraco project's `appsettings.json`.

Add a `HuGen` section:

```json
{
  "HuGen": {
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
| `BaseUrl` | Endpoint of your LLM instance. |
| `ApiKey` | Authentication key for the LLM endpoint, if required. Leave empty for a local LLM instance that does not require authentication. |
| `Model` | The model to use for content generation. The model must be available to the configured LLM service. |
| `TimeoutSeconds` | Maximum amount of time HuGen will wait for a generation request to complete. |
| `PixabayKey` | Pixabay API key used by the AI Image editor. Leave empty if you do not want to use Pixabay image search. |

For example, when running Ollama locally:

```json
{
  "HuGen": {
    "BaseUrl": "http://localhost:11434",
    "ApiKey": "",
    "Model": "qwen2.5:7b",
    "TimeoutSeconds": 300,
    "PixabayKey": ""
  }
}
```

If `BaseUrl` is not configured, generation requests will fail with a configuration error rather than silently timing out.

### Upgrading from ThtaAi

HuGen is the new name for **ThtaAi**.

New installations should use the `HuGen` configuration section shown above.

For backwards compatibility, existing installations can continue using the previous `AiGeneration` section if you are running a HuGen version that retains legacy configuration support:

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

Existing installations can therefore migrate their configuration from `AiGeneration` to `HuGen` when convenient.

## How It Works

1. Assign an AI property editor — Textstring, Textarea, Rich Text extension, or Image — to a Data Type.
2. In the Umbraco backoffice, click **Generate** on that field.
3. A chat-style modal opens where you can describe the content you want in plain language and refine it iteratively.
4. Click **Insert** to place the generated result into the field.
5. Review, edit, save and publish the content through Umbraco as normal.

Each time the generation modal opens, a fresh conversation begins server-side, preventing context from a previous editing session from carrying into a new one.

## AI Page Generation

For generating whole pages rather than individual fields, use the **AI Generation** functionality in the Umbraco backoffice.

1. HuGen inspects your site's document types, Block List and Block Grid configurations, and available block types to understand the content structures available.
2. Describe the page you want to create and select the parent page it should be created beneath.
3. The configured model generates content for the page, including nested blocks where applicable, using your existing content architecture.
4. HuGen creates the page as **unpublished**.
5. An editor can then review, adjust and publish the generated page through Umbraco's normal workflow.

Because page generation is schema-aware, HuGen generates content against your site's actual document types and property editors rather than requiring the generated output to be manually restructured afterwards.

## Prompt Handling

You don't need to write carefully engineered prompts to use HuGen.

Plain natural-language instructions are automatically prepared before being sent to the configured model, with additional guidance applied for content structure, formatting and CMS-appropriate output.

For iterative field generation, previous turns within the current generation session can also be included to provide conversational context.

## Local AI

One of HuGen's main goals is to make AI-assisted content generation possible without requiring a specific hosted AI provider.

A local LLM can be used where appropriate, allowing the model to run on infrastructure you control.

For example, with Ollama running locally:

```bash
ollama pull qwen2.5:7b
```

Your HuGen configuration can then point to the local Ollama instance:

```json
{
  "HuGen": {
    "BaseUrl": "http://localhost:11434",
    "ApiKey": "",
    "Model": "qwen2.5:7b",
    "TimeoutSeconds": 300,
    "PixabayKey": ""
  }
}
```

You are not limited to this particular model. The appropriate model will depend on your infrastructure, performance requirements and desired output quality.

## AI Image

The **AI Image** property editor uses Pixabay to search for suitable images based on the editor's prompt.

To enable image search, obtain a [Pixabay API key](https://pixabay.com/api/docs/) and configure:

```json
{
  "HuGen": {
    "PixabayKey": "YOUR_PIXABAY_API_KEY"
  }
}
```

If you don't require image search, the Pixabay key can be left empty.

## Editorial Review

AI-generated content should be treated as a draft.

HuGen deliberately creates generated pages as **unpublished**, allowing editors to review and modify generated content before it becomes publicly visible.

Generated text inserted into individual fields can similarly be edited using Umbraco's normal editing experience before the content is saved or published.

## Issues and Feedback

Found a bug or have an idea for HuGen?

Please open an issue on the HuGen GitHub issue tracker.

Pull requests are also welcome through the HuGen GitHub repository.

## License

HuGen is released under the MIT License.

See [LICENSE](./LICENSE) for details.

## Author

**David Whalley**  
Developer at the human tech agency

[GitHub](https://github.com/dwhalley15)

---

HuGen is an independent open-source package and is not affiliated with or endorsed by Umbraco, Ollama or Pixabay.