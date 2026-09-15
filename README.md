# Toaster

**Local expertise memory for AI agents. Windows desktop app, or headless on Linux.**

Toaster is a standalone local service that stores reusable, application-specific expertise learned from research, manuals, and real agent execution. It is intentionally independent of any specific agent framework, model vendor, IDE, or orchestration methodology.

The core idea is simple:

- **Toast Graph** — distilled operational lessons: what has already been learned, what worked, what failed, and under what conditions.
- **Encyclopedia Graph** — a cheap semantic map of retained manuals/research so an agent can decide what is worth opening before loading a large document.
- **Raw sources** — retained locally behind those cheaper representations, with provenance linking knowledge back to evidence.
- **Provider-neutral toasting queue** — semantic work that can be performed by whatever model/provider the user is already using rather than by a model hard-wired into Toaster.

## Product goals

Toaster is being built for ordinary Windows users. The intended experience is: run a normal installer, let it register the local service, open the tray/config app, and copy the displayed MCP URL/configuration into whichever agent app the user wants to connect.

On Linux the same service runs headless as a systemd user unit, managed with the `toaster` CLI. There is no Linux GUI; the tray console is Windows-only.

The default local endpoints are:

```text
REST: http://127.0.0.1:47321/api/v1/
MCP:  http://127.0.0.1:47321/mcp
```

The actual configured endpoint is always shown by the tray app and CLI.

## Feed Toaster manuals

The tray app accepts PDF manuals plus text/Markdown/HTML/JSON/source files. PDF text is extracted in reading order and indexed page-by-page; large pages are split into bounded sections. Original files are retained locally under Toaster's source-object store so the indexed Encyclopedia representation remains backed by the original evidence.

Scanned and image-heavy PDF pages automatically fall back to **local OCR** when normal PDF text extraction produces too little useful text. OCR is only attempted on text-poor pages and only against sufficiently large embedded page images, so normal digital manuals pay essentially no OCR cost. OCR-derived sections are labeled `Page N (OCR)` for provenance and debugging.

The Windows package includes the fast English Tesseract language data by default. OCR remains local; no document content is uploaded to an OCR service. Advanced users can point `TOASTER_TESSDATA` at another Tesseract data directory when adding additional language models.

From the CLI:

```powershell
toaster ingest .\manual.pdf
```

To also queue the manual for distillation by a connected model/provider:

```powershell
toaster ingest .\manual.pdf --toast
```

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

`scripts/package.ps1` publishes the Windows executables and, when Inno Setup is installed, builds `installer/Toaster.iss` into a normal Windows setup executable. Packaging also obtains the compact English OCR data and the Microsoft Visual C++ runtime required by the local Tesseract native libraries. The installer registers the service, installs that runtime prerequisite, creates Start Menu shortcuts, launches the tray app after setup, and is automatically represented in Windows Installed Apps by Inno Setup.

## Linux packaging

`scripts/package-linux.ps1` publishes a self-contained `linux-x64` build of the service and CLI and writes `artifacts/linux/toaster-<version>-linux-x64.tar.gz`. It runs on the Windows dev box; no Linux toolchain is needed to produce it.

```sh
tar -xzf toaster-0.3.1-linux-x64.tar.gz
cd toaster-0.3.1-linux-x64
sh install.sh
```

`install.sh` installs to `~/.local/lib/toaster`, links the CLI into `~/.local/bin`, registers a **systemd user unit** and enables lingering so Toaster survives logout. No root required. Where systemd is absent (containers, WSL without `systemd=true`) it installs the files and prints the command to run the service directly. `sh uninstall.sh` removes the unit and program files but keeps your Toast; `sh uninstall.sh --purge` deletes the data too.

Data lives at `$XDG_DATA_HOME/toaster` (usually `~/.local/share/toaster`), or `/var/lib/toaster` when the service runs privileged. Override either platform's default with `Toaster:DataPath` in `appsettings.json`, or the `Toaster__DataPath` environment variable.

Two deliberate differences from the Windows build:

- **No OCR.** The Tesseract package ships Windows-only native libraries, so scanned or image-heavy PDF pages yield no text on Linux. PDFs with a real text layer are unaffected, as are all text, Markdown, HTML, JSON and source files.
- **Invariant globalization.** The build sets `InvariantGlobalization=true` so the tarball has no ICU dependency. Without it .NET terminates at startup on any host lacking ICU — including minimal containers and stock WSL images — with a bare stack trace and no useful message.

## Independence

Toaster stores reusable expertise rather than project state. No API, schema, or subsystem depends on a particular agent framework or orchestration methodology.

## License

MIT.
