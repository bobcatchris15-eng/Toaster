# Toaster

**Local, Windows-native expertise memory for AI agents.**

Toaster is a standalone local service that stores reusable, application-specific expertise learned from research and real agent execution. It is intentionally independent of any specific agent framework, model vendor, IDE, or orchestration methodology.

The core idea is simple:

- **Toast Graph** — distilled operational lessons: what has already been learned, what worked, what failed, and under what conditions.
- **Encyclopedia Graph** — a cheap semantic map of retained source material so an agent can decide what is worth opening before loading a large document.
- **Raw sources** — retained locally behind those cheaper representations, with provenance linking knowledge back to evidence.

## Product goals

Toaster is being built for ordinary Windows users. The intended experience is: run a normal installer, let it register the local service, open the tray/config app, and copy the displayed MCP URL/configuration into whichever agent app the user wants to connect.

The default local endpoints are:

```text
REST: http://127.0.0.1:47321/api/v1/
MCP:  http://127.0.0.1:47321/mcp
```

The actual configured endpoint is always shown by the tray app and CLI.

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
