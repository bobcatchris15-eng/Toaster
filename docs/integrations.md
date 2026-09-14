# Connecting agent applications to Toaster

Toaster exposes a local Streamable HTTP MCP endpoint. By default:

```text
http://127.0.0.1:47321/mcp
```

The actual address should always be copied from Toaster's **Integrations** screen or obtained with:

```powershell
toaster integrations
```

## Generic setup

For an application that asks for MCP fields, use:

- Name: `Toaster`
- Transport: `Streamable HTTP` / `HTTP`
- URL: the address shown by Toaster

Some applications use JSON configuration. A common shape is:

```json
{
  "mcpServers": {
    "toaster": {
      "url": "http://127.0.0.1:47321/mcp"
    }
  }
}
```

Exact configuration formats vary by application and change over time. Toaster should therefore present app-specific snippets as conveniences, while keeping the generic endpoint visible and copyable rather than pretending it can automatically configure every client.
