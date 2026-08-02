import os
import sys
import json
import re
import glob
import urllib.request
import urllib.error
from pathlib import Path

# Base path for Vault Unity project (defaults to repository root containing Assets/ and ProjectSettings/)
PROJECT_ROOT = Path(__file__).resolve().parent.parent

def get_unity_scripts():
    scripts = []
    assets_dir = PROJECT_ROOT / "Assets"
    if not assets_dir.exists():
        return scripts

    for file_path in assets_dir.glob("**/*.cs"):
        rel_path = file_path.relative_to(PROJECT_ROOT).as_posix()
        classes = []
        try:
            with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
                content = f.read()
                # Find class/struct/interface definitions
                matches = re.findall(r"(?:public|protected|private|internal)?\s*(?:partial\s+)?(class|struct|interface|enum)\s+([A-Za-z0-9_]+)", content)
                classes = [m[1] for m in matches]
        except Exception:
            pass

        has_meta = (file_path.parent / (file_path.name + ".meta")).exists()
        scripts.append({
            "path": rel_path,
            "filename": file_path.name,
            "classes": classes,
            "hasMeta": has_meta,
            "sizeBytes": file_path.stat().st_size
        })
    return scripts

def inspect_script(rel_path):
    file_path = PROJECT_ROOT / rel_path
    if not file_path.exists():
        return {"error": f"Script not found at {rel_path}"}

    try:
        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()

        classes = []
        class_blocks = re.finditer(r"(?:public|protected|private|internal)?\s*(?:partial\s+)?(class|struct|interface)\s+([A-Za-z0-9_]+)(?:\s*:\s*([A-Za-z0-9_,\s]+))?", content)
        for cb in class_blocks:
            c_kind, c_name, c_base = cb.groups()
            classes.append({
                "kind": c_kind,
                "name": c_name,
                "baseClass": c_base.strip() if c_base else None
            })

        serialized_fields = re.findall(r"(?:\[SerializeField\]\s*|public\s+)([A-Za-z0-9_<>\[\]]+)\s+([A-Za-z0-9_]+)\s*;", content)
        methods = re.findall(r"(?:public|protected|private|internal)\s+(?:virtual|override|static|async)?\s*([A-Za-z0-9_<>\[\]]+)\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)", content)

        return {
            "path": rel_path,
            "classes": classes,
            "serializedFields": [{"type": f[0], "name": f[1]} for f in serialized_fields[:30]],
            "methods": [{"returnType": m[0], "name": m[1], "args": m[2]} for m in methods[:30]],
            "lines": len(content.splitlines()),
            "content": content
        }
    except Exception as e:
        return {"error": str(e)}

def list_scenes():
    assets_dir = PROJECT_ROOT / "Assets"
    scenes = []
    if assets_dir.exists():
        for file_path in assets_dir.glob("**/*.unity"):
            rel_path = file_path.relative_to(PROJECT_ROOT).as_posix()
            scenes.append({
                "path": rel_path,
                "filename": file_path.name,
                "sizeBytes": file_path.stat().st_size
            })

    build_scenes = []
    build_settings_path = PROJECT_ROOT / "ProjectSettings" / "EditorBuildSettings.asset"
    if build_settings_path.exists():
        try:
            with open(build_settings_path, "r", encoding="utf-8", errors="ignore") as f:
                bs_content = f.read()
                matches = re.findall(r"path:\s*([^\s\r\n]+)", bs_content)
                build_scenes = matches
        except Exception:
            pass

    return {
        "allScenes": scenes,
        "buildScenes": build_scenes
    }

def inspect_scene(rel_path):
    file_path = PROJECT_ROOT / rel_path
    if not file_path.exists():
        return {"error": f"Scene file not found at {rel_path}"}

    try:
        game_objects = []
        components = []
        missing_scripts = 0
        current_object = None

        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            for line in f:
                line_str = line.strip()
                if line_str.startswith("GameObject:"):
                    current_object = {"type": "GameObject", "name": "Unknown", "components": []}
                    game_objects.append(current_object)
                elif current_object and line_str.startswith("m_Name:"):
                    current_object["name"] = line_str.split("m_Name:")[1].strip().strip('"')
                elif line_str.startswith("MonoBehaviour:"):
                    components.append("MonoBehaviour")
                elif "m_Script: {fileID: 0}" in line_str:
                    missing_scripts += 1

        return {
            "path": rel_path,
            "gameObjectCount": len(game_objects),
            "gameObjects": game_objects[:50],
            "totalComponents": len(components),
            "missingScriptReferences": missing_scripts
        }
    except Exception as e:
        return {"error": str(e)}

def search_assets(query="", asset_type=""):
    assets_dir = PROJECT_ROOT / "Assets"
    results = []
    if not assets_dir.exists():
        return results

    query_lower = query.lower()
    ext_filter = asset_type.lower() if asset_type else None

    for file_path in assets_dir.glob("**/*"):
        if file_path.is_file() and not file_path.name.endswith(".meta"):
            rel_path = file_path.relative_to(PROJECT_ROOT).as_posix()
            ext = file_path.suffix.lower()

            if ext_filter and ext != ext_filter and not ext.endswith(ext_filter):
                continue

            if query_lower and query_lower not in rel_path.lower():
                continue

            results.append({
                "path": rel_path,
                "filename": file_path.name,
                "extension": ext,
                "sizeBytes": file_path.stat().st_size
            })
            if len(results) >= 100:
                break

    return results

def get_project_settings():
    manifest_path = PROJECT_ROOT / "Packages" / "manifest.json"
    version_path = PROJECT_ROOT / "ProjectSettings" / "ProjectVersion.txt"
    tags_path = PROJECT_ROOT / "ProjectSettings" / "TagManager.asset"

    version = "Unknown"
    if version_path.exists():
        try:
            with open(version_path, "r", encoding="utf-8") as f:
                version = f.read().strip()
        except Exception:
            pass

    packages = {}
    if manifest_path.exists():
        try:
            with open(manifest_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                packages = data.get("dependencies", {})
        except Exception:
            pass

    tags = []
    layers = []
    if tags_path.exists():
        try:
            with open(tags_path, "r", encoding="utf-8", errors="ignore") as f:
                content = f.read()
                tags = re.findall(r"-\s+([^\r\n]+)", content)
                layers_match = re.findall(r"User Layer \d+:\s*([^\r\n]+)", content)
                layers = [l for l in layers_match if l.strip()]
        except Exception:
            pass

    return {
        "editorVersion": version,
        "packageCount": len(packages),
        "packages": packages,
        "customTags": tags,
        "customLayers": layers
    }

def read_logs():
    logs = []
    logs_dir = PROJECT_ROOT / "Logs"
    if logs_dir.exists():
        for log_file in logs_dir.glob("*.log"):
            try:
                with open(log_file, "r", encoding="utf-8", errors="ignore") as f:
                    lines = f.readlines()
                    last_lines = lines[-50:] if len(lines) > 50 else lines
                    logs.append({
                        "logFile": log_file.name,
                        "path": log_file.relative_to(PROJECT_ROOT).as_posix(),
                        "lines": [l.strip() for l in last_lines if l.strip()]
                    })
            except Exception as e:
                logs.append({"logFile": log_file.name, "error": str(e)})

    # Also check Unity LocalAppData log if available on Windows
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        editor_log = Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
        if editor_log.exists():
            try:
                with open(editor_log, "r", encoding="utf-8", errors="ignore") as f:
                    lines = f.readlines()
                    last_lines = lines[-50:] if len(lines) > 50 else lines
                    logs.append({
                        "logFile": "Editor.log",
                        "path": str(editor_log),
                        "lines": [l.strip() for l in last_lines if l.strip()]
                    })
            except Exception:
                pass

    return logs

def call_unity_editor_bridge(endpoint, method="GET", body=None):
    url = f"http://localhost:8080/mcp/{endpoint.lstrip('/')}"
    headers = {"Content-Type": "application/json"}
    data = json.dumps(body).encode("utf-8") if body else None

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=3) as resp:
            resp_body = resp.read().decode("utf-8")
            return json.loads(resp_body)
    except urllib.error.URLError:
        return {
            "error": "Unity Editor Bridge is offline.",
            "hint": "Ensure Unity Editor is running with Vault project open and Assets/Editor/UnityMcpBridge.cs compiled."
        }
    except Exception as e:
        return {"error": str(e)}


# Standard Tools Catalog
TOOLS_DEFINITIONS = [
    {
        "name": "unity_list_scripts",
        "description": "List all C# scripts in the Unity project Assets folder with class names and metadata.",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    },
    {
        "name": "unity_inspect_script",
        "description": "Inspect a C# script details including class hierarchy, serialized fields, and methods.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative path to C# script (e.g. Assets/Scripts/UIManager.cs)"}
            },
            "required": ["path"]
        }
    },
    {
        "name": "unity_list_scenes",
        "description": "List all scenes in Assets and scenes registered in Unity Build Settings.",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    },
    {
        "name": "unity_inspect_scene",
        "description": "Inspect scene structure (GameObjects, component types, missing script references).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative path to Unity scene (e.g. Assets/AI_Villager/Ai_Villager_Scene.unity)"}
            },
            "required": ["path"]
        }
    },
    {
        "name": "unity_search_assets",
        "description": "Search for assets by name query or extension (.prefab, .asset, .mat, .png, etc.).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "query": {"type": "string", "description": "Name search query"},
                "assetType": {"type": "string", "description": "Extension filter (e.g. .cs, .prefab, .asset, .png)"}
            }
        }
    },
    {
        "name": "unity_get_project_settings",
        "description": "Get Unity project settings including Editor version, Packages manifest, custom tags and layers.",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    },
    {
        "name": "unity_read_logs",
        "description": "Read Unity Editor import logs and Editor.log for compile error detection.",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    },
    {
        "name": "unity_editor_command",
        "description": "Interact with live Unity Editor instance (health, selection, refresh, playmode, console, hierarchy, menu).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "description": "Bridge action: 'health', 'selection', 'refresh', 'playmode', 'console', 'hierarchy', 'menu'"
                },
                "params": {
                    "type": "object",
                    "description": "Action parameters (e.g. {'menuItem': 'File/Save'} for menu, {'play': true} for playmode)"
                }
            },
            "required": ["action"]
        }
    }
]


def execute_tool(name, arguments):
    if name == "unity_list_scripts":
        return get_unity_scripts()
    elif name == "unity_inspect_script":
        return inspect_script(arguments.get("path", ""))
    elif name == "unity_list_scenes":
        return list_scenes()
    elif name == "unity_inspect_scene":
        return inspect_scene(arguments.get("path", ""))
    elif name == "unity_search_assets":
        return search_assets(arguments.get("query", ""), arguments.get("assetType", ""))
    elif name == "unity_get_project_settings":
        return get_project_settings()
    elif name == "unity_read_logs":
        return read_logs()
    elif name == "unity_editor_command":
        action = arguments.get("action", "health").lower()
        params = arguments.get("params", {})
        if action in ["health", "selection", "refresh", "console", "hierarchy"]:
            method = "POST" if action == "refresh" else "GET"
            return call_unity_editor_bridge(action, method=method)
        elif action == "playmode":
            return call_unity_editor_bridge("playmode", method="POST", body=params)
        elif action == "menu":
            return call_unity_editor_bridge("menu", method="POST", body=params)
        else:
            return {"error": f"Unknown editor command action: {action}"}
    else:
        return {"error": f"Unknown tool: {name}"}


def run_stdio_mcp():
    """Runs a standard JSON-RPC 2.0 stdio server compliant with Model Context Protocol."""
    for line in sys.stdin:
        if not line.strip():
            continue
        try:
            request = json.loads(line)
        except Exception:
            continue

        method = request.get("method")
        msg_id = request.get("id")

        if method == "initialize":
            response = {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {
                        "tools": {}
                    },
                    "serverInfo": {
                        "name": "vault-unity-mcp-server",
                        "version": "1.0.0"
                    }
                }
            }
            sys.stdout.write(json.dumps(response) + "\n")
            sys.stdout.flush()

        elif method == "notifications/initialized":
            pass

        elif method == "tools/list":
            response = {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "tools": TOOLS_DEFINITIONS
                }
            }
            sys.stdout.write(json.dumps(response) + "\n")
            sys.stdout.flush()

        elif method == "tools/call":
            params = request.get("params", {})
            name = params.get("name")
            arguments = params.get("arguments", {})

            result_data = execute_tool(name, arguments)
            response = {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "content": [
                        {
                            "type": "text",
                            "text": json.dumps(result_data, indent=2)
                        }
                    ]
                }
            }
            sys.stdout.write(json.dumps(response) + "\n")
            sys.stdout.flush()

        elif msg_id is not None:
            response = {
                "jsonrpc": "2.0",
                "id": msg_id,
                "error": {
                    "code": -32601,
                    "message": f"Method '{method}' not found"
                }
            }
            sys.stdout.write(json.dumps(response) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    # Try using fastmcp if available, otherwise fallback to stdio loop
    try:
        from mcp.server.fastmcp import FastMCP
        mcp_app = FastMCP("Vault Unity MCP Server")

        @mcp_app.tool()
        def unity_list_scripts():
            """List all C# scripts in the Unity project Assets folder with class names and metadata."""
            return execute_tool("unity_list_scripts", {})

        @mcp_app.tool()
        def unity_inspect_script(path: str):
            """Inspect a C# script details including class hierarchy, serialized fields, and methods."""
            return execute_tool("unity_inspect_script", {"path": path})

        @mcp_app.tool()
        def unity_list_scenes():
            """List all scenes in Assets and scenes registered in Unity Build Settings."""
            return execute_tool("unity_list_scenes", {})

        @mcp_app.tool()
        def unity_inspect_scene(path: str):
            """Inspect scene structure (GameObjects, component types, missing script references)."""
            return execute_tool("unity_inspect_scene", {"path": path})

        @mcp_app.tool()
        def unity_search_assets(query: str = "", assetType: str = ""):
            """Search for assets by name query or extension (.prefab, .asset, .mat, .png, etc.)."""
            return execute_tool("unity_search_assets", {"query": query, "assetType": assetType})

        @mcp_app.tool()
        def unity_get_project_settings():
            """Get Unity project settings including Editor version, Packages manifest, custom tags and layers."""
            return execute_tool("unity_get_project_settings", {})

        @mcp_app.tool()
        def unity_read_logs():
            """Read Unity Editor import logs and Editor.log for compile error detection."""
            return execute_tool("unity_read_logs", {})

        @mcp_app.tool()
        def unity_editor_command(action: str, params: dict = None):
            """Interact with live Unity Editor instance (health, selection, refresh, playmode, console, hierarchy, menu)."""
            return execute_tool("unity_editor_command", {"action": action, "params": params or {}})

        mcp_app.run()
    except ImportError:
        run_stdio_mcp()
