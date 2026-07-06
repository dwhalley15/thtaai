# Contributing to ThtaAi

Thanks for your interest in contributing! This doc covers how to get the project running locally, the general architecture, and how to submit changes.

## Requirements

- .NET SDK matching `net10.0`
- Node.js LTS 22.17.0+ — use a version manager to keep this isolated:
  - [nvm-windows](https://github.com/coreybutler/nvm-windows) (Windows)
  - [nvm](https://github.com/nvm-sh/nvm) (macOS/Linux)
  - [Volta](https://docs.volta.sh/guide/getting-started)
- [Ollama](https://ollama.com) running locally with a chat-capable model pulled (`qwen2.5:7b` recommended) for testing the generation pipeline end-to-end
- A local Umbraco 17 site to run the extension against (see **Local Development Setup** below)

Recommended editor: **VS Code** — it has solid TypeScript tooling and will prompt you to install the recommended extension for Lit web component completions.

## Repository Structure

- **`/Client`** — TypeScript/Lit frontend for the backoffice extension (property editors, AI Generation section, Tiptap toolbar button)
- **root `.cs` files / `/Composers`, `/Services`** — C# ASP.NET backend: Umbraco composer registration, LLM pipeline services, content mapping
- **`/wwwroot/App_Plugins/thtaai`** — build output target for the compiled client bundle; don't edit this directly, it's generated

## Local Development Setup

1. Clone the repo.
2. From the `/Client` folder:
   ```bash
   npm install
   npm run build
   ```
   This compiles the TypeScript/Lit frontend and copies the output to `wwwroot/App_Plugins/thtaai/thta-ai.js`.
3. Add this project as a **project reference** (not a package reference) to an Umbraco 17 website project you have locally — this lets you run and debug against a real backoffice instance.
4. Set up your local Umbraco site's `appsettings.json` with an `AiGeneration` section pointing at your local Ollama instance (see the README's Configuration section).
5. From `/Client`, run:
   ```bash
   npm run watch
   ```
   This watches `.ts` files and rebuilds on change. With the Umbraco site running via the project reference, the backoffice will hot-reload once a build completes.

## Architecture Notes

The content generation pipeline runs as a multi-pass process:

1. **Planning pass** — the LLM produces a high-level plan for the page/content given the discovered schema.
2. **Expansion pass** — the plan is expanded into more detailed content direction per area.
3. **Content-fill pass** — actual field-level content is generated.
4. **`ContentMappingService`** — maps the LLM's output onto Umbraco's content structure (document types, Block List/Grid areas).
5. The mapped content is written via the **Umbraco Management API**.

A few things worth knowing before touching this code:

- Each pass is a separate LLM conversation — conversation history must not bleed between planning and content-fill. If you're debugging odd cross-contamination in generated content, check this first.
- In BlockGrid layout JSON, `area.Alias` is used for LLM/schema lookups, while `area.Key` is required for Management API calls. These are easy to mix up — if content silently fails to place into the right area, check which one is being used.
- `System.Text.Json`'s default case sensitivity means if the LLM emits camelCase where the mapping code expects PascalCase, deserialization fails silently (null, not an exception). If a generation pass looks like it "did nothing," check for this before assuming a prompt problem.
- HTTP calls to the LLM endpoint go through a **named** `HttpClient` (`ThtaAi.Ollama`), not the default client — don't register against `string.Empty` in the composer, as that affects every other HttpClient consumer in the host application.
- Configuration is bound via `IOptions<AiGenerationOptions>` from the consuming site's `appsettings.json`. Don't hardcode endpoints, models, or keys anywhere in the pipeline.

## Testing Your Changes

Before opening a PR:

1. Confirm `npm run build` succeeds cleanly from `/Client`.
2. Test against a local Umbraco 17 site via the project reference — exercise the property editor you changed, and if you touched the pipeline, run a full **AI Page Generation** end to end.
3. If you want to test the packaged output specifically (not just the project reference), pack and install locally:
   ```bash
   dotnet pack -c Release -o ./local-nuget-feed
   dotnet add package ThtaAi --source ./local-nuget-feed
   ```
   This catches packaging issues (missing assets, wrong output paths) that the project-reference workflow won't surface.

## Submitting Changes

1. Fork the repo and create a branch off `main`.
2. Keep PRs focused — one fix or feature per PR makes review much faster.
3. Describe what changed and why in the PR description; if it's a bug fix, a brief repro of the original issue helps.
4. Please don't bump the package version in your PR — that's handled at release time.

## Reporting Issues

Open a GitHub issue with:
- Umbraco version and ThtaAi version
- Your `AiGeneration` config (with keys redacted)
- Steps to reproduce, and what you expected vs. what happened

If it's related to LLM output quality rather than a bug (e.g. odd generated content), including the model name and prompt is more useful than a stack trace.