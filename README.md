# Toaster

**Local, Windows-native expertise memory for AI agents.**

Toaster is a standalone local service that stores reusable, application-specific expertise learned from research, manuals, and real agent execution. It is intentionally independent of any specific agent framework, model vendor, IDE, or orchestration methodology.

The core idea is simple:

- **Toast Graph** — distilled operational lessons: what has already been learned, what worked, what failed, and under what conditions.
- **Encyclopedia Graph** — a cheap semantic map of retained manuals/research so an agent can decide what is worth opening before loading a large document.
- **Raw sources** — retained locally behind those cheaper representations, with provenance linking knowledge back to evidence.
- **Provider-neutral toasting queue** — semantic work that can be performed by whatever model/provider the user is already using rather than by a model hard-wired into Toaster.

## Product goals

Toaster is being built for ordinary Windows users. The intended experience is: run a normal installer, let it register the local service, open the tray/config app, and copy the displayed MCP URL/configuration into whichever agent app the user wants to connect.

The default local endpoints are:

```text
REST: http://127.0.0.1:47321/api/v1/
MCP:  http://127.0.0.1:47321/mcp
```

The actual configured endpoint is always shown by the tray app and CLI.

## Feed Toaster manuals

The tray app accepts PDF manuals plus text/Markdown/HTML/JSON/source files. PDF text is extracted in reading order and indexed page-by-page; large pages are split into bounded sections. Original files are retained locally under Toaster's source-object store so the indexed Encyclopedia representation remains backed by the original evidence.

From the CLI:

```powershell
toaster ingest .\manual.pdf
```

To also queue the manual for distillation by a connected model/provider:

```powershell
toaster ingest .\manual.pdf --toast
```

Image-only/scanned PDFs currently need OCR before import. Native OCR is a later ingestion adapter rather than a requirement of the core store.

## Toasting with whichever model you already use

Toaster deliberately does **not** require its own generative model or API account.

Instead it exposes semantic work as **toasting jobs** over REST and MCP. A connected OpenAI, Anthropic, Gemini, local-model, IDE, research, or other compatible agent can:

1. pull a pending toasting job;
2. inspect the supplied manual section or live implementation observation;
3. extract zero or more reusable operational lessons;
4. submit structured toast candidates;
5. let Toaster persist them with provenance back to the evidence.

This works in two directions:

- **Manual toasting** — imported source sections can be queued for a provider to distill into techniques, constraints, warnings, compatibility facts, and other reusable lessons.
- **Live toasting** — an agent can record an implementation attempt/result as it works. Toaster returns/queues a live distillation job so the same model (or a later one) can generalize the lesson while the evidence is fresh.

The integration screen includes suggested agent instructions telling callers to use these tools. Processing is opt-in; queueing a job does not itself consume provider tokens.

## Progressive retrieval

Agents should normally move through Toaster from cheapest to most expensive context:

```text
Toast search
  -> source/manual preview
    -> section/page retrieval
      -> original source when necessary
```

The goal is to avoid repeatedly loading entire manuals or repeating research simply to rediscover a lesson already learned.

## Developer build

Requires the .NET 8 SDK.

```powershell
./scripts/dev.ps1
```

or:

```powershell
dotnet restore Toaster.sln
dotnet build Toaster.sln -c Release
```

## Windows packaging

`scripts/package.ps1` publishes the Windows executables and, when Inno Setup is installed, builds `installer/Toaster.iss` into a normal Windows setup executable. The installer registers the service, creates Start Menu shortcuts, launches the tray app after setup, and is automatically represented in Windows Installed Apps by Inno Setup.

## Independence

Toaster stores reusable expertise rather than project state. No API, schema, or subsystem depends on a particular agent framework or orchestration methodology.

## License

MIT.
