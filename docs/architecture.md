# Toaster architecture

Toaster is a local expertise service with three levels of context cost:

1. **Toast** — compact reusable operational lessons.
2. **Encyclopedia index** — source/section metadata and searchable text that helps an agent decide what to open.
3. **Raw source content** — retained evidence retrieved only when needed.

## Core invariant

Toaster stores reusable expertise, not project state. Callers are treated as interchangeable clients.

## Runtime

The service is the single writer/owner of the SQLite database. REST, MCP, CLI, and the tray UI are adapters around that service.

The default service binds to loopback only (`127.0.0.1`). Remote access is deliberately not part of the initial product.

## Storage

SQLite is canonical storage. FTS5 provides the initial lexical retrieval layer. The schema includes graph edges, provenance, observations, source sections, and provider-neutral toasting jobs. Local semantic embeddings are an intended next retrieval layer, but canonical knowledge is never stored only as an embedding.

Raw imported files are kept under the local Toaster source-object store, keyed by SHA-256. Duplicate exact files are detected by hash rather than creating duplicate Encyclopedia entries.

## Manual ingestion

PDF manuals are parsed locally using PdfPig's reading-order text extraction. The initial representation preserves page identity (`Page N`) and splits unusually large pages into bounded parts. This makes source retrieval cheap and provides useful human/audit provenance even before richer outline/bookmark extraction exists.

Text/Markdown sources are split on headings when available and otherwise into bounded sections.

Scanned/image-only PDF OCR is deliberately an adapter to add later; the core ingestion contract only requires a sequence of source sections.

## Provider-neutral toasting

Generative inference is not built into the storage engine.

When semantic distillation is useful, Toaster creates a `ToastingJob`. A job contains:

- a kind (`source-section` or `live-observation` initially);
- stable evidence identifiers;
- the evidence payload/context;
- an explicit distillation instruction;
- lifecycle/status metadata.

Any connected model/provider can consume these jobs over MCP or REST and submit zero or more structured toast candidates. Toaster validates/stores the candidates and records provenance back to the manual section or implementation observation.

This creates one shared knowledge-metabolism path for both research and experience:

```text
Manual/source -> Encyclopedia section -> toasting job -> external model -> Toast + provenance

Live attempt/result -> observation -> toasting job -> external model -> Toast + provenance
```

The external model can be the model currently doing the work. No OpenAI, Anthropic, Gemini, local-model, or other provider is privileged in the architecture.

Queueing a job does not invoke a provider or spend tokens by itself. A caller must choose to process it.

## MCP

`POST /mcp` exposes the initial Streamable HTTP JSON-RPC surface. Current tools cover:

- querying toast and source previews;
- retrieving toast + provenance;
- searching manuals/sources;
- listing and retrieving source sections;
- recording live observations;
- queueing an ingested source for distillation;
- pulling pending toasting jobs;
- submitting provider-generated toast candidates.

The public endpoint remains stable even as transport compliance and capabilities are expanded.

## Windows install model

A packaged installation contains three executables:

- `Toaster.Service.exe` — Windows service and HTTP host.
- `toaster.exe` — CLI.
- `Toaster.exe` — human-facing tray/configuration application.

The installer owns service registration and uninstall cleanup. Inno Setup supplies the standard wizard and Windows Installed Apps registration.

The tray app is intentionally sufficient for a normal user to:

- see whether Toaster is healthy;
- see/copy the MCP endpoint and suggested agent instructions;
- import manuals/research;
- choose whether imported material is queued for provider-side toasting.
