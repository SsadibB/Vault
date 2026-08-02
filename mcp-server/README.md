# Unity MCP Server for Vault

This package provides a **Model Context Protocol (MCP)** server specifically built for the Unity project **Vault**.

It allows AI assistants (Claude Desktop, Cursor, Gemini Antigravity, VS Code, Roo Code, etc.) to inspect, analyze, debug, and control the Unity project both statically and dynamically.

---

## Capabilities & Tools

### Static Analysis (Works offline, without Unity Editor open)
- `unity_list_scripts`: List all C# scripts in `Assets/` with class names and meta file checks.
- `unity_inspect_script`: Inspect a C# script for classes, serialized fields, methods, and base classes.
- `unity_list_scenes`: List all scenes in `Assets/` and scenes configured in Build Settings.
- `unity_inspect_scene`: Inspect YAML scene structure (GameObjects, components, missing script references).
- `unity_search_assets`: Query assets by name pattern or extension (`.prefab`, `.asset`, `.mat`, `.png`, etc.).
- `unity_get_project_settings`: Expose Unity Editor version, installed Packages (`manifest.json`), custom tags and layers.
- `unity_read_logs`: Read Unity Editor import logs (`Logs/AssetImportWorker*.log`) and `%LOCALAPPDATA%/Unity/Editor/Editor.log`.

### Live Unity Editor RPC (When Unity Editor is running)
- `unity_editor_command`: Send live commands to Unity Editor via the HTTP bridge:
  - `health`: Check if Unity Editor is running, play mode state, compile status.
  - `selection`: Get active selected GameObjects / Assets in Unity Editor.
  - `refresh`: Trigger `AssetDatabase.Refresh()`.
  - `playmode`: Toggle or set Play mode state (`{"play": true}`).
  - `console`: Fetch recent Unity Editor console logs & errors.
  - `hierarchy`: Fetch complete GameObject hierarchy of the active scene.
  - `menu`: Execute Unity Editor menu items (`{"menuItem": "File/Save"}`).

---

## Setup & Configuration

### 1. Enable Unity Editor Bridge
1. Open the **Vault** project in Unity Editor.
2. The script `Assets/Editor/UnityMcpBridge.cs` automatically starts a local listener on `http://localhost:8080/mcp/`.

### 2. Configure MCP Client
Add the following snippet to your client config (e.g. `%APPDATA%\Claude\claude_desktop_config.json` or Antigravity/Cursor MCP config):

```json
{
  "mcpServers": {
    "vault-unity": {
      "command": "python",
      "args": [
        "c:/Users/mashr/Documents/GitHub/Vault/mcp-server/server.py"
      ]
    }
  }
}
```

### 3. Testing Standalone via Command Line
Run the server script directly:
```bash
python mcp-server/server.py
```
*(The server implements standard JSON-RPC 2.0 stdio protocol and will respond to MCP `initialize` and `tools/list` messages).*
