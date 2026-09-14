# Toaster

**Local, Windows-native expertise memory for AI agents.**

Toaster is a standalone local service that stores reusable, application-specific expertise learned from research and real agent execution. It is intentionally independent of any specific agent framework, model vendor, IDE, or orchestration methodology.

The core idea is simple:

- **Toast Graph** — distilled operational lessons: what has already been learned, what worked, what failed, and under what conditions.
- **Encyclopedia Graph** — a cheap semantic map of retained source material so an agent can decide what is worth opening before loading a large document.
- **Raw sources** — retained locally behind those cheaper representations, with provenance linking knowledge back to evidence.

## Product goals

Toaster is being built for ordinary Windows users, not only developers. The intended experience is:

1. Run a normal Windows installer.
2. Toaster installs and starts its local background service.
3. A tray/configuration app shows service health and the exact local MCP endpoint.
4. The **Integrations** page gives copyable configuration snippets for compatible agent apps.
5. Agents can immediately query, add observations, submit research, and retrieve supporting source material.

No Docker, WSL, cloud database, or paid API is required for the base product.

## Current architecture

The first implementation uses modern .NET and is organized around:

- `Toaster.Core` — domain contracts and models.
- `Toaster.Storage` — SQLite persistence.
- `Toaster.Service` — Windows-capable background service and HTTP host.
- `Toaster.Cli` — script/PowerShell-friendly command line client.
- `installer/` — Windows installer packaging.
- `docs/` — architecture, API, and integration documentation.

The local service exposes:

- REST health/status and knowledge APIs under `/api/v1/`.
- A **Streamable HTTP MCP** endpoint at `/mcp`.
- Loopback-only binding by default.

The default endpoint is intended to look like:

```text
http://127.0.0.1:47321/mcp
```

The port is configurable. The UI/CLI should always display the actual configured endpoint rather than requiring users to guess it.

## MVP

The first usable milestone is:

> Any compatible external agent can ask Toaster a technical question, recover a previously stored reusable lesson, inspect its provenance, and continue working without loading the original research corpus.

The MVP therefore prioritizes:

- Windows-native service operation.
- SQLite-backed Toast and Encyclopedia data.
- provenance-preserving knowledge storage.
- local HTTP API.
- Streamable HTTP MCP.
- CLI administration.
- installer/uninstaller integration with Windows Installed Apps.
- a human-friendly integration/configuration surface.

GitHub Actions and hosted CI are deliberately not part of the critical path at this stage.

## Building

Requirements for developers:

- Windows 10/11 recommended.
- .NET 8 SDK.

```powershell
./scripts/dev.ps1
```

Or directly:

```powershell
dotnet restore Toaster.sln
dotnet build Toaster.sln -c Release
```

## Project independence

Toaster must remain useful outside any one agent workflow. No public API, schema, or internal architectural assumption should depend on a particular calling agent, project-management system, or context-management methodology.

## License

MIT. See `LICENSE`.
