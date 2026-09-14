# Windows installation design

The Windows package is intentionally designed for people who should not need to understand services, ports, JSON-RPC, or MCP internals.

## Installation

`Toaster-Setup-<version>.exe` uses a normal modern Inno Setup wizard. It requires elevation because it installs a Windows service. The installer:

1. copies self-contained .NET executables into Program Files;
2. registers `Toaster.Service.exe` as an automatic Windows service;
3. starts that service;
4. registers the tray/configuration application to launch at sign-in;
5. adds Start Menu entries;
6. creates the normal uninstaller entry used by Windows Installed Apps;
7. launches the Toaster configuration window after setup.

The user does not need a separate .NET runtime installation because release packaging uses self-contained publishing.

## Connecting an AI application

The Integrations tab shows the actual MCP URL and a copy button. The expected default is:

```text
http://127.0.0.1:47321/mcp
```

Where an external application supports automatic registration in a stable way, future Toaster versions may offer one-click registration. The baseline behavior remains explicit copy/paste because client configuration locations and formats vary and frequently change.

## Runtime data

The Windows service stores shared runtime data under `%ProgramData%\Toaster`. The HTTP listener is loopback-only by default, so other machines cannot connect without a future explicit remote-access feature.
