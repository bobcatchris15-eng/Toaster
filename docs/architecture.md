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

SQLite is canonical storage. FTS5 provides the initial lexical retrieval layer. The schema reserves graph edges and provenance from the beginning. Local semantic embeddings are an intended next retrieval layer, but canonical knowledge is never stored only as an embedding.

## MCP

`POST /mcp` implements the MCP JSON-RPC methods needed for a first Streamable HTTP integration: `initialize`, `notifications/initialized`, `tools/list`, and `tools/call`. The public endpoint remains stable even as the implementation is expanded toward the full current MCP transport specification.

## Windows install model

A packaged installation contains three executables:

- `Toaster.Service.exe` — Windows service and HTTP host.
- `toaster.exe` — CLI.
- `Toaster.exe` — human-facing tray/configuration application.

The installer owns service registration and uninstall cleanup. Inno Setup supplies the standard wizard and Windows Installed Apps registration.
