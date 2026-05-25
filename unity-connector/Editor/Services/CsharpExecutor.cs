using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Editor.Services
{
    public static class CsharpExecutor
    {
        private static readonly string[] DefaultUsings =
        {
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Reflection",
            "System.Threading.Tasks",
            "UnityEngine",
            "UnityEngine.SceneManagement",
            "UnityEditor",
            "UnityEditor.SceneManagement",
        };

        public static Dictionary<string, object> Execute(CliParams p)
        {
            var code = p.GetString("code");
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Parameter 'code' is required.");

            var extraUsings = p.GetStringArray("usings").ToList();
            var cscPath = p.GetString("csc");
            var dotnetPath = p.GetString("dotnet");

            var source = BuildSource(code, extraUsings);
            var result = CompileAndExecute(source, cscPath, dotnetPath);
            return new Dictionary<string, object>
            {
                ["result"] = result,
            };
        }

        private static string BuildSource(string code, List<string> extraUsings)
        {
            var sb = new StringBuilder();
            foreach (var u in DefaultUsings)
                sb.AppendLine($"using {u};");
            foreach (var u in extraUsings)
            {
                if (!string.IsNullOrWhiteSpace(u))
                    sb.AppendLine($"using {u};");
            }

            sb.AppendLine();
            sb.AppendLine("public static class __CliDynamic {");
            sb.AppendLine("    public static object Execute() {");
            sb.AppendLine(code);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static object CompileAndExecute(string source, string cscOverride, string dotnetOverride)
        {
            var utf8 = new UTF8Encoding(false);
            var tmpDir = Path.Combine(Path.GetTempPath(), "unity-cli-exec");
            Directory.CreateDirectory(tmpDir);

            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var srcFile = Path.Combine(tmpDir, $"{id}.cs");
            var outFile = Path.Combine(tmpDir, $"{id}.dll");
            var rspFile = Path.Combine(tmpDir, $"{id}.rsp");

            try
            {
                File.WriteAllText(srcFile, source, utf8);

                var rsp = new StringBuilder();
                rsp.AppendLine("-target:library");
                rsp.AppendLine($"-out:\"{outFile}\"");
                rsp.AppendLine("-nologo");
                rsp.AppendLine("-nowarn:0105,1701,1702");
                rsp.AppendLine("-langversion:9.0");
                rsp.AppendLine($"\"{srcFile}\"");

                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                            continue;
                        if (!added.Add(asm.GetName().Name))
                            continue;
                        rsp.AppendLine($"-r:\"{asm.Location}\"");
                    }
                    catch
                    {
                        // ignored
                    }
                }

                File.WriteAllText(rspFile, rsp.ToString(), utf8);

                var rspArg = $"@\"{rspFile}\"";
                var csc = FindCsc(cscOverride);
                string exe;
                string args;

                if (csc != null && csc.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var dotnet = FindDotnet(dotnetOverride);
                    if (dotnet == null)
                    {
                        throw new InvalidOperationException(
                            "Cannot find dotnet runtime under Unity installation. Specify --dotnet.");
                    }

                    exe = dotnet;
                    args = $"exec \"{csc}\" {rspArg}";
                }
                else if (csc != null)
                {
                    exe = csc;
                    args = rspArg;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Cannot find csc compiler under Unity installation. Specify --csc.");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(30000);

                    if (proc.ExitCode != 0)
                    {
                        var output = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        throw new InvalidOperationException($"Compile error:\n{FormatErrors(output)}");
                    }
                }

                var bytes = File.ReadAllBytes(outFile);
                var compiled = Assembly.Load(bytes);
                var method = compiled.GetType("__CliDynamic")?.GetMethod("Execute");
                if (method == null)
                    throw new InvalidOperationException("Internal error: compiled type or method not found.");

                try
                {
                    return Serialize(method.Invoke(null, null), 0);
                }
                catch (TargetInvocationException tie)
                {
                    var inner = tie.InnerException ?? tie;
                    throw new InvalidOperationException(
                        $"Runtime error: {inner.GetType().Name}: {inner.Message}");
                }
            }
            finally
            {
                TryDelete(srcFile);
                TryDelete(outFile);
                TryDelete(rspFile);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        private static string FindCsc(string cscOverride)
        {
            if (!string.IsNullOrEmpty(cscOverride))
                return cscOverride;

            var content = EditorApplication.applicationContentsPath;
            var cscDll = SearchFile(content, "csc.dll");
            if (cscDll != null)
                return cscDll;

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var cscExe = SearchFile(content, "csc.exe");
                if (cscExe != null)
                    return cscExe;
            }

            return null;
        }

        private static string FindDotnet(string dotnetOverride)
        {
            if (!string.IsNullOrEmpty(dotnetOverride))
                return dotnetOverride;

            var name = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
            var found = SearchFile(EditorApplication.applicationContentsPath, name);
            if (found != null)
                return found;

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                var macPaths = new[]
                {
                    "/usr/local/share/dotnet/dotnet",
                    "/opt/homebrew/bin/dotnet",
                    "/usr/local/bin/dotnet",
                };
                foreach (var p in macPaths)
                {
                    if (File.Exists(p))
                        return p;
                }
            }

            return name;
        }

        private static string SearchFile(string dir, string name)
        {
            try
            {
                var files = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    if (Path.GetFileName(f) == name)
                        return f;
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static string FormatErrors(string raw)
        {
            var lines = raw.Split('\n');
            var errors = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                var m = Regex.Match(trimmed, @"\((\d+),\d+\):\s*error\s+\w+:\s*(.+)");
                if (m.Success)
                    errors.Add($"L{m.Groups[1].Value}: {m.Groups[2].Value}");
                else if (trimmed.Contains("error"))
                    errors.Add(trimmed);
            }

            return errors.Count > 0 ? string.Join("\n", errors) : raw;
        }

        private static object Serialize(object obj, int depth)
        {
            if (obj == null)
                return null;
            if (depth > 4)
                return obj.ToString();

            var type = obj.GetType();
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return obj;
            if (type.IsEnum)
                return obj.ToString();
            if (type.Name.StartsWith("FixedString", StringComparison.Ordinal))
                return obj.ToString();

            if (obj is IDictionary dict)
            {
                var r = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dict)
                    r[e.Key.ToString()] = Serialize(e.Value, depth + 1);
                return r;
            }

            if (obj is IEnumerable enumerable && obj is not string)
            {
                var list = new List<object>();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 100)
                    {
                        list.Add("... (truncated at 100)");
                        break;
                    }

                    list.Add(Serialize(item, depth + 1));
                }

                return list;
            }

            if (type.IsValueType || type.IsClass)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (fields.Length > 0)
                {
                    var r = new Dictionary<string, object>();
                    foreach (var f in fields)
                    {
                        try
                        {
                            r[f.Name] = Serialize(f.GetValue(obj), depth + 1);
                        }
                        catch
                        {
                            r[f.Name] = "<error>";
                        }
                    }

                    return r;
                }
            }

            return obj.ToString();
        }
    }
}
