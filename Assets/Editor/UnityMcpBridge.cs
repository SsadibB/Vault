using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Vault.Editor
{
    [InitializeOnLoad]
    public static class UnityMcpBridge
    {
        private const string HostUrl = "http://localhost:8080/mcp/";
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static bool _isRunning;

        private static readonly Queue<Action> MainThreadQueue = new Queue<Action>();
        private static readonly List<ConsoleLogEntry> ConsoleLogs = new List<ConsoleLogEntry>();
        private const int MaxConsoleLogs = 100;

        [Serializable]
        public struct ConsoleLogEntry
        {
            public string logType;
            public string condition;
            public string stackTrace;
            public string timestamp;
        }

        static UnityMcpBridge()
        {
            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += UpdateMainThread;
            StartServer();
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (ConsoleLogs)
            {
                if (ConsoleLogs.Count >= MaxConsoleLogs)
                {
                    ConsoleLogs.RemoveAt(0);
                }
                ConsoleLogs.Add(new ConsoleLogEntry
                {
                    logType = type.ToString(),
                    condition = condition,
                    stackTrace = stackTrace,
                    timestamp = DateTime.Now.ToString("HH:mm:ss")
                });
            }
        }

        private static void UpdateMainThread()
        {
            lock (MainThreadQueue)
            {
                while (MainThreadQueue.Count > 0)
                {
                    try
                    {
                        MainThreadQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[UnityMcpBridge] Error on main thread action: {ex.Message}");
                    }
                }
            }
        }

        private static void StartServer()
        {
            try
            {
                if (_isRunning) return;

                _listener = new HttpListener();
                _listener.Prefixes.Add(HostUrl);
                _listener.Start();
                _isRunning = true;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true
                };
                _listenerThread.Start();

                Debug.Log($"[UnityMcpBridge] MCP Bridge Server running at {HostUrl}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityMcpBridge] Failed to start listener: {ex.Message}");
            }
        }

        private static void StopServer()
        {
            _isRunning = false;
            if (_listener != null && _listener.IsListening)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch { }
            }
            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Abort();
            }
        }

        private static void ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityMcpBridge] Listener loop exception: {ex.Message}");
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 200;
                res.Close();
                return;
            }

            string path = req.Url.AbsolutePath.ToLowerInvariant().TrimEnd('/');
            string responseJson = "";
            int statusCode = 200;

            var doneEvent = new ManualResetEvent(false);

            EnqueueMainThread(() =>
            {
                try
                {
                    switch (path)
                    {
                        case "/mcp/health":
                        case "/mcp":
                            responseJson = GetHealthJson();
                            break;

                        case "/mcp/selection":
                            responseJson = GetSelectionJson();
                            break;

                        case "/mcp/refresh":
                            AssetDatabase.Refresh();
                            responseJson = "{\"status\":\"ok\",\"message\":\"AssetDatabase refreshed\"}";
                            break;

                        case "/mcp/playmode":
                            if (req.HttpMethod == "POST")
                            {
                                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                                {
                                    string body = reader.ReadToEnd();
                                    if (body.Contains("\"play\":true") || body.Contains("\"state\":\"play\""))
                                    {
                                        EditorApplication.isPlaying = true;
                                    }
                                    else if (body.Contains("\"play\":false") || body.Contains("\"state\":\"stop\""))
                                    {
                                        EditorApplication.isPlaying = false;
                                    }
                                    else
                                    {
                                        EditorApplication.isPlaying = !EditorApplication.isPlaying;
                                    }
                                }
                            }
                            responseJson = $"{{\"status\":\"ok\",\"isPlaying\":{EditorApplication.isPlaying.ToString().ToLower()}}}";
                            break;

                        case "/mcp/console":
                            responseJson = GetConsoleLogsJson();
                            break;

                        case "/mcp/hierarchy":
                            responseJson = GetHierarchyJson();
                            break;

                        case "/mcp/setup_villager":
                            string resultMsg = VillagerSetupMenu.SetupAiVillagerScene();
                            responseJson = $"{{\"status\":\"ok\",\"result\":\"{EscapeJson(resultMsg)}\"}}";
                            break;

                        case "/mcp/menu":
                            if (req.HttpMethod == "POST")
                            {
                                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                                {
                                    string body = reader.ReadToEnd();
                                    string menuItem = ExtractJsonValue(body, "menuItem");
                                    if (!string.IsNullOrEmpty(menuItem))
                                    {
                                        bool executed = EditorApplication.ExecuteMenuItem(menuItem);
                                        responseJson = $"{{\"status\":\"ok\",\"executed\":{executed.ToString().ToLower()},\"menuItem\":\"{EscapeJson(menuItem)}\"}}";
                                    }
                                    else
                                    {
                                        statusCode = 400;
                                        responseJson = "{\"error\":\"Missing menuItem parameter\"}";
                                    }
                                }
                            }
                            else
                            {
                                statusCode = 405;
                                responseJson = "{\"error\":\"Method not allowed. Use POST.\"}";
                            }
                            break;

                        default:
                            statusCode = 404;
                            responseJson = "{\"error\":\"Endpoint not found\"}";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    statusCode = 500;
                    responseJson = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                }
                finally
                {
                    doneEvent.Set();
                }
            });

            doneEvent.WaitOne(5000);

            byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
            res.StatusCode = statusCode;
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;

            try
            {
                using (var output = res.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch { }
            finally
            {
                res.Close();
            }
        }

        private static void EnqueueMainThread(Action action)
        {
            lock (MainThreadQueue)
            {
                MainThreadQueue.Enqueue(action);
            }
        }

        private static string GetHealthJson()
        {
            return $"{{\"status\":\"ok\",\"editorVersion\":\"{EscapeJson(Application.unityVersion)}\",\"isPlaying\":{EditorApplication.isPlaying.ToString().ToLower()},\"isCompiling\":{EditorApplication.isCompiling.ToString().ToLower()},\"platform\":\"{Application.platform}\",\"dataPath\":\"{EscapeJson(Application.dataPath)}\"}}";
        }

        private static string GetSelectionJson()
        {
            var selectedObjects = Selection.objects;
            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(selectedObjects.Length).Append(",\"items\":[");
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                var obj = selectedObjects[i];
                if (i > 0) sb.Append(",");
                string assetPath = AssetDatabase.GetAssetPath(obj);
                sb.Append("{");
                sb.Append("\"name\":\"").Append(EscapeJson(obj.name)).Append("\",");
                sb.Append("\"type\":\"").Append(EscapeJson(obj.GetType().Name)).Append("\",");
                sb.Append("\"instanceId\":").Append(obj.GetInstanceID()).Append(",");
                sb.Append("\"assetPath\":\"").Append(EscapeJson(assetPath)).Append("\"");
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string GetConsoleLogsJson()
        {
            lock (ConsoleLogs)
            {
                var sb = new StringBuilder();
                sb.Append("{\"count\":").Append(ConsoleLogs.Count).Append(",\"logs\":[");
                for (int i = 0; i < ConsoleLogs.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var log = ConsoleLogs[i];
                    sb.Append("{");
                    sb.Append("\"type\":\"").Append(EscapeJson(log.logType)).Append("\",");
                    sb.Append("\"condition\":\"").Append(EscapeJson(log.condition)).Append("\",");
                    sb.Append("\"stackTrace\":\"").Append(EscapeJson(log.stackTrace)).Append("\",");
                    sb.Append("\"time\":\"").Append(EscapeJson(log.timestamp)).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        private static string GetHierarchyJson()
        {
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var sb = new StringBuilder();
            sb.Append("{\"activeScene\":\"").Append(EscapeJson(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)).Append("\",");
            sb.Append("\"rootCount\":").Append(rootObjects.Length).Append(",\"gameObjects\":[");
            for (int i = 0; i < rootObjects.Length; i++)
            {
                if (i > 0) sb.Append(",");
                SerializeGameObject(rootObjects[i], sb);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void SerializeGameObject(GameObject go, StringBuilder sb)
        {
            sb.Append("{");
            sb.Append("\"name\":\"").Append(EscapeJson(go.name)).Append("\",");
            sb.Append("\"active\":").Append(go.activeSelf.ToString().ToLower()).Append(",");
            sb.Append("\"tag\":\"").Append(EscapeJson(go.tag)).Append("\",");
            sb.Append("\"layer\":").Append(go.layer).Append(",");
            
            var components = go.GetComponents<Component>();
            sb.Append("\"components\":[");
            for (int c = 0; c < components.Length; c++)
            {
                if (c > 0) sb.Append(",");
                string compName = components[c] != null ? components[c].GetType().Name : "MissingScript";
                sb.Append("\"").Append(EscapeJson(compName)).Append("\"");
            }
            sb.Append("],");

            sb.Append("\"childCount\":").Append(go.transform.childCount).Append(",");
            sb.Append("\"children\":[");
            for (int i = 0; i < go.transform.childCount; i++)
            {
                if (i > 0) sb.Append(",");
                SerializeGameObject(go.transform.GetChild(i).gameObject, sb);
            }
            sb.Append("]}");
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string pattern = $"\"{key}\":\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;
            int start = idx + pattern.Length;
            int end = json.IndexOf("\"", start, StringComparison.Ordinal);
            if (end == -1) return null;
            return json.Substring(start, end - start);
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }
    }
}
