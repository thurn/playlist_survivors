using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Demo3D.EditorTools
{
    /// <summary>
    /// A long-running batch-mode editor for the inner loop (windows.md, Unity Work by Agents).
    ///
    /// `demo3d editor start` launches Unity with `-executeMethod Demo3D.EditorTools.WarmEditor.Serve`
    /// and no `-quit`. It listens on localhost; a client sends one JSON line
    /// <c>{"token", "id", "cmd", "args"}</c> per connection and gets <c>{"accepted": id}</c>. The
    /// job's result lands in Library/Demo3DWarm/jobs/&lt;id&gt;.json, so a domain reload in the
    /// middle of a job (a refresh that recompiles) doesn't lose it. Commands: refresh, test, exec
    /// (a static method by full name, with args), quit. The listener restarts after each reload
    /// and the editor exits after 30 idle minutes.
    /// </summary>
    [InitializeOnLoad]
    public static class WarmEditor
    {
        const string ActiveKey = "demo3d.warm.active";
        const string PortKey = "demo3d.warm.port";
        const string TokenKey = "demo3d.warm.token";
        const string PendingRefreshKey = "demo3d.warm.pendingRefresh";
        const string CompileErrorsKey = "demo3d.warm.compileErrors";
        const string LastActivityKey = "demo3d.warm.lastActivity";
        const double IdleSeconds = 30 * 60;

        static readonly string Dir = Path.Combine("Library", "Demo3DWarm");
        static readonly ConcurrentQueue<Dictionary<string, string>> Queue = new ConcurrentQueue<Dictionary<string, string>>();
        static TcpListener _listener;
        static Thread _thread;
        static string _token;
        static bool _busy;

        static WarmEditor()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            Start();
        }

        /// <summary>The entry point: run with -executeMethod and without -quit.</summary>
        public static void Serve()
        {
            SessionState.SetBool(ActiveKey, true);
            Touch();
            Start();
        }

        static void Start()
        {
            Directory.CreateDirectory(Path.Combine(Dir, "jobs"));
            _token = SessionState.GetString(TokenKey, "");
            if (_token.Length == 0)
            {
                _token = Guid.NewGuid().ToString("N");
                SessionState.SetString(TokenKey, _token);
            }
            int port = SessionState.GetInt(PortKey, 0);
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            SessionState.SetInt(PortKey, port);
            WriteAtomic(Path.Combine(Dir, "server.json"),
                "{\"port\": " + port + ", \"token\": \"" + _token + "\", \"pid\": " +
                System.Diagnostics.Process.GetCurrentProcess().Id + "}\n");

            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Demo3D warm editor" };
            _thread.Start();
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Debug.Log($"WarmEditor: listening on 127.0.0.1:{port}");
        }

        static void Stop()
        {
            try { _listener?.Stop(); } catch (Exception) { }
            _listener = null;
        }

        static void AcceptLoop()
        {
            var listener = _listener;
            while (listener != null)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { return; } // stopped for a reload or exit
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var reader = new StreamReader(stream, new UTF8Encoding(false));
                        var line = reader.ReadLine();
                        var job = MiniJson.ParseFlat(line ?? "");
                        string reply;
                        if (!job.TryGetValue("token", out var token) || token != _token)
                            reply = "{\"error\": \"bad token\"}";
                        else if (!job.TryGetValue("id", out var id) || !job.ContainsKey("cmd"))
                            reply = "{\"error\": \"missing id or cmd\"}";
                        else
                        {
                            Queue.Enqueue(job);
                            reply = "{\"accepted\": \"" + id + "\"}";
                        }
                        var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("WarmEditor: connection failed: " + e.Message);
                }
            }
        }

        static void Pump()
        {
            var pending = SessionState.GetString(PendingRefreshKey, "");
            if (pending.Length > 0 && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                FinishRefresh(pending);

            if (_busy) return;
            if (Queue.TryDequeue(out var job))
            {
                Touch();
                Run(job);
                return;
            }
            double last = double.Parse(SessionState.GetString(LastActivityKey, "0"), CultureInfo.InvariantCulture);
            if (Now() - last > IdleSeconds && pending.Length == 0)
            {
                Debug.Log("WarmEditor: idle for 30 minutes, exiting");
                EditorApplication.Exit(0);
            }
        }

        static void Run(Dictionary<string, string> job)
        {
            var id = job["id"];
            var cmd = job["cmd"];
            job.TryGetValue("args", out var args);
            try
            {
                switch (cmd)
                {
                    case "refresh":
                        SessionState.SetString(CompileErrorsKey, "");
                        SessionState.SetString(PendingRefreshKey, id);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        // Pump finishes it once compilation and any reload are done.
                        break;
                    case "test":
                        CleanScene();
                        RunTests(id, args);
                        break;
                    case "exec":
                        Exec(id, args);
                        break;
                    case "quit":
                        Result(id, true, "\"quitting\"");
                        EditorApplication.Exit(0);
                        break;
                    default:
                        Result(id, false, "\"unknown command " + MiniJson.Escape(cmd) + "\"");
                        break;
                }
            }
            catch (Exception e)
            {
                Result(id, false, "\"" + MiniJson.Escape(e.ToString()) + "\"");
            }
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            var sb = new StringBuilder(SessionState.GetString(CompileErrorsKey, ""));
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error)
                    sb.Append(m.message).Append('\n');
            SessionState.SetString(CompileErrorsKey, sb.ToString());
        }

        static void OnCompilationFinished(object context)
        {
            // A failed compile doesn't reload the domain, so Pump finishes the refresh as is.
        }

        static void FinishRefresh(string id)
        {
            SessionState.SetString(PendingRefreshKey, "");
            var errors = SessionState.GetString(CompileErrorsKey, "");
            bool ok = errors.Length == 0 && !EditorUtility.scriptCompilationFailed;
            Result(id, ok, "\"" + MiniJson.Escape(ok ? "compiled" : (errors.Length > 0 ? errors : "scripts have compile errors")) + "\"");
        }

        static void Exec(string id, string args)
        {
            var parts = SplitArgs(args ?? "");
            if (parts.Count == 0) throw new ArgumentException("exec needs a method name");
            var full = parts[0];
            int dot = full.LastIndexOf('.');
            var typeName = full.Substring(0, dot);
            var methodName = full.Substring(dot + 1);
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) break;
            }
            var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(full);
            var argv = new List<string> { "Unity.exe" };
            argv.AddRange(parts.GetRange(1, parts.Count - 1));
            TaskArgs.Override = argv.ToArray();
            try
            {
                method.Invoke(null, null);
                CleanScene();
                Result(id, true, "\"" + MiniJson.Escape(full) + " done\"");
            }
            catch (TargetInvocationException e)
            {
                Result(id, false, "\"" + MiniJson.Escape(e.InnerException?.ToString() ?? e.ToString()) + "\"");
            }
            finally
            {
                TaskArgs.Override = null;
            }
        }

        static void RunTests(string id, string args)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = new Filter { testMode = TestMode.EditMode };
            var parts = SplitArgs(args ?? "");
            if (parts.Count > 0) filter.assemblyNames = parts.ToArray();
            _busy = true;
            var callbacks = new TestCallbacks(id, api);
            api.RegisterCallbacks(callbacks);
            api.Execute(new ExecutionSettings(filter));
        }

        sealed class TestCallbacks : ICallbacks
        {
            readonly string _id;
            readonly TestRunnerApi _api;

            public TestCallbacks(string id, TestRunnerApi api)
            {
                _id = id;
                _api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                _busy = false;
                _api.UnregisterCallbacks(this);
                var xml = Path.GetFullPath(Path.Combine(Dir, "jobs", _id + ".xml"));
                TestRunnerApi.SaveResultToFile(result, xml);
                var failures = new StringBuilder();
                CollectFailures(result, failures);
                bool ok = result.FailCount == 0 && result.TestStatus != TestStatus.Failed;
                Result(_id, ok,
                    "{\"passed\": " + result.PassCount + ", \"failed\": " + result.FailCount +
                    ", \"skipped\": " + result.SkipCount + ", \"inconclusive\": " + result.InconclusiveCount +
                    ", \"xml\": \"" + MiniJson.Escape(xml) + "\", \"failures\": \"" + MiniJson.Escape(failures.ToString()) + "\"}");
            }

            static void CollectFailures(ITestResultAdaptor r, StringBuilder sb)
            {
                if (!r.HasChildren && r.TestStatus == TestStatus.Failed)
                    sb.Append(r.Test.FullName).Append(": ").Append(r.Message).Append('\n');
                if (r.HasChildren)
                    foreach (var c in r.Children) CollectFailures(c, sb);
            }
        }

        /// <summary>
        /// Leaves a fresh, unmodified scene: a task's scratch scene would otherwise make the test
        /// runner ask to save it, a dialog that hangs a batch-mode editor (TD4, TD5).
        /// </summary>
        static void CleanScene() =>
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

        static void Result(string id, bool ok, string detailJson)
        {
            Touch();
            WriteAtomic(Path.Combine(Dir, "jobs", id + ".json"),
                "{\"id\": \"" + id + "\", \"ok\": " + (ok ? "true" : "false") + ", \"detail\": " + detailJson + "}\n");
        }

        static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void Touch() => SessionState.SetString(LastActivityKey, Now().ToString(CultureInfo.InvariantCulture));

        static double Now() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        /// <summary>Splits on unescaped tabs: the client joins argv with tabs.</summary>
        static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            foreach (var p in s.Split('\t'))
                if (p.Length > 0) list.Add(p);
            return list;
        }
    }

    /// <summary>Just enough JSON for the warm editor's flat string-valued requests.</summary>
    static class MiniJson
    {
        public static Dictionary<string, string> ParseFlat(string s)
        {
            var d = new Dictionary<string, string>();
            int i = 0;
            Skip(s, ref i);
            if (i >= s.Length || s[i] != '{') return d;
            i++;
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] == '}') break;
                var key = ReadString(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                Skip(s, ref i);
                d[key] = ReadString(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
            return d;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("expected a string at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
