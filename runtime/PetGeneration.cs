using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        private static System.Windows.Controls.Button FindGenerateButton(DependencyObject parent)
        {
            System.Windows.Controls.Button button = parent as System.Windows.Controls.Button;
            if (button != null && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "PhotoSubmit") return button;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                System.Windows.Controls.Button found = FindGenerateButton(VisualTreeHelper.GetChild(parent, i));
                if (found != null) return found;
            }
            return null;
        }

        private static void TestGenerateClick(string specPath, string output)
        {
            Directory.CreateDirectory(output);
            Dictionary<string, object> spec = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(specPath)) as Dictionary<string, object>;
            creationPhotos.Clear(); foreach (string photo in (object[])spec["photos"]) creationPhotos.Add(photo);
            creationDraftPrompt = ReadString(spec, "prompt");
            System.Windows.Application app = new System.Windows.Application(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            activeTheme = CreatePublicDemoTheme(); BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            window.Show(); ShowPetManager(0); petManager.UpdateLayout();
            System.Windows.Controls.Button button = FindGenerateButton(petManager);
            if (button == null || !button.IsEnabled) throw new InvalidOperationException("Generate button unavailable.");
            bool handoff = !File.Exists(Path.Combine(experienceRoot, "generator.json"));
            System.Windows.IDataObject previousClipboard = handoff ? System.Windows.Clipboard.GetDataObject() : null;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            bool copied = handoff && latestCreationRequest != null && System.Windows.Clipboard.GetText() == CreationHandoff(latestCreationRequest);
            if (handoff) {
                if (previousClipboard != null) System.Windows.Clipboard.SetDataObject(previousClipboard, true);
                else System.Windows.Clipboard.Clear();
            }
            if (latestCreationRequest == null || (handoff ? !copied : button.IsEnabled)) throw new InvalidOperationException("Generate click did not start a request or copy its handoff.");
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer(); timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += delegate {
                timer.Stop(); petManager.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)petManager.ActualWidth, (int)petManager.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(petManager); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(Path.Combine(output, "generate-click.png"))) encoder.Save(stream);
                File.WriteAllText(Path.Combine(output, "result.json"), new JavaScriptSerializer().Serialize(new {
                    id = latestCreationRequest, buttonTriggered = true, handoffCopied = copied,
                    generatorConfigured = !handoff, generation = GenerationStatus(latestCreationRequest) }));
                ExitApplication();
            };
            timer.Start(); app.Run();
        }

        private static void TestGenerationReview(string id, string output)
        {
            Directory.CreateDirectory(output);
            System.Windows.Application app = new System.Windows.Application(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            activeTheme = CreatePublicDemoTheme(); BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            window.Show(); latestCreationRequest = id;
            string previousTheme = File.Exists(activeThemeFilePath) ? FileSha256Hex(activeThemeFilePath) : null;
            string previousRole = File.Exists(activeRoleFilePath) ? FileSha256Hex(activeRoleFilePath) : null;
            string candidate = Path.Combine(CreationFolder(id), "candidate.lotheme");
            ThemePackage reviewTheme = ParseThemePackage(candidate);
            ThemeAction reviewAction = reviewTheme.Actions.Find(delegate(ThemeAction action) { return action.Slot == "click"; }) ?? reviewTheme.IdleAction;
            themeReviewRenderPath = Path.Combine(output, "candidate-review.png"); themeReviewRenderSlot = reviewAction.Slot;
            themeReviewRenderFrame = Math.Min(24, reviewAction.Frames.Count - 1);
            ImportCreationCandidate(candidate);
            ShowPetManager(0); petManager.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)petManager.ActualWidth, (int)petManager.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(petManager); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(Path.Combine(output, "manager.png"))) encoder.Save(stream);
            bool preserved = previousTheme == (File.Exists(activeThemeFilePath) ? FileSha256Hex(activeThemeFilePath) : null) &&
                previousRole == (File.Exists(activeRoleFilePath) ? FileSha256Hex(activeRoleFilePath) : null);
            File.WriteAllText(Path.Combine(output, "result.json"), new JavaScriptSerializer().Serialize(new {
                preserved = preserved, reviewRendered = File.Exists(Path.Combine(output, "candidate-review.png")), photosReloaded = creationPhotos.Count,
                candidateReady = LatestCreationCandidate() != null }));
            ExitApplication(); if (!preserved || !File.Exists(Path.Combine(output, "candidate-review.png"))) throw new InvalidOperationException("Generation review failed.");
        }

        private static Dictionary<string, object> ReadGenerator(string path)
        {
            Dictionary<string, object> config = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(path)) as Dictionary<string, object>;
            if (config == null || Convert.ToInt32(config["version"], CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("生成器配置版本无效。");
            string executable = ReadString(config, "executable"), work = ReadString(config, "workingDirectory");
            string name = ReadString(config, "name"), handling = ReadString(config, "dataHandling");
            object[] arguments = config["arguments"] as object[];
            if (String.IsNullOrWhiteSpace(name) || name.Length > 80 || String.IsNullOrWhiteSpace(handling) || handling.Length > 200 ||
                !Path.IsPathRooted(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable) ||
                !Path.IsPathRooted(work) || !Directory.Exists(work) || arguments == null || arguments.Length < 2 || arguments.Length > 32)
                throw new InvalidDataException("请让 Agent 配置可用的生成器程序、名称和数据处理说明。");
            bool request = false, output = false;
            foreach (object item in arguments) {
                string argument = item as string;
                if (argument == null || argument.Length > 4096 || argument.IndexOf('\0') >= 0) throw new InvalidDataException("生成器参数无效。");
                request |= argument == "{request}"; output |= argument == "{output}";
            }
            int timeout = Convert.ToInt32(config["timeoutSeconds"], CultureInfo.InvariantCulture);
            int references = Convert.ToInt32(config["maxReferences"], CultureInfo.InvariantCulture);
            if (!request || !output || timeout < 10 || timeout > 7200 || references < 1 || references > 4)
                throw new InvalidDataException("生成器需接收请求和输出路径，支持 1–4 张图片，并设置 10–7200 秒超时。");
            return config;
        }

        private static string CreationFolder(string id)
        {
            if (id == null || !Regex.IsMatch(id, @"\A[a-f0-9]{32}\z")) throw new InvalidDataException("创作编号无效。");
            string folder = Path.Combine(experienceRoot, "creation-requests", id);
            if (!File.Exists(Path.Combine(folder, "request.json"))) throw new InvalidDataException("未找到创作请求。");
            return folder;
        }

        private static object GenerationStatus(string id)
        {
            string folder = CreationFolder(id), path = Path.Combine(folder, "generation.json");
            if (File.Exists(Path.Combine(folder, "candidate.lotheme"))) return new { status = "ready", message = "动作已生成，可以预览使用。", running = false };
            if (!File.Exists(path)) return new { status = "pending", message = "等待生成。", running = false };
            Dictionary<string, object> state = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(path)) as Dictionary<string, object>;
            string status = ReadString(state, "status");
            bool running = status == "running";
            if (running) {
                try {
                    using (Process process = Process.GetProcessById(Convert.ToInt32(state["pid"], CultureInfo.InvariantCulture)))
                        running = !process.HasExited && process.StartTime.ToUniversalTime().Ticks == Convert.ToInt64(state["startTicks"], CultureInfo.InvariantCulture) &&
                            String.Equals(process.MainModule.FileName, Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase);
                } catch (Exception) { running = false; }
                if (!running) { state["status"] = "interrupted"; state["message"] = "上次生成已中断，可以重试。"; }
            }
            state["running"] = running;
            if (running) {
                try {
                    string attempt = ReadString(state, "attempt");
                    if (!Regex.IsMatch(attempt, @"\A[a-f0-9]{32}\z")) return state;
                    string progressPath = Path.Combine(folder, "attempts", attempt, "progress.json");
                    if (new FileInfo(progressPath).Length <= 8192) {
                        Dictionary<string, object> progress = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(progressPath)) as Dictionary<string, object>;
                        string message = ReadString(progress, "message");
                        if (message.Length > 0 && message.Length <= 200) state["message"] = message;
                    }
                } catch (Exception) { }
            }
            return state;
        }

        private static void StartGeneration(string id)
        {
            string folder = CreationFolder(id); ReadGenerator(Path.Combine(experienceRoot, "generator.json"));
            using (FileStream locked = new FileStream(Path.Combine(folder, "generation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                if (File.Exists(Path.Combine(folder, "candidate.lotheme"))) throw new InvalidOperationException("已有生成结果，请先预览。");
                string cancel = Path.Combine(folder, "cancel-generation");
                if (File.Exists(cancel)) File.Delete(cancel);
            }
            Process.Start(new ProcessStartInfo { FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "--ai-generate " + id, UseShellExecute = false, CreateNoWindow = true });
        }

        private static void SaveGenerationState(string folder, string attempt, string status, string message)
        {
            using (Process self = Process.GetCurrentProcess())
                WriteCodexEventAtomically(Path.Combine(folder, "generation.json"), new JavaScriptSerializer().Serialize(new {
                    version = 1, attempt = attempt, status = status, message = message, pid = self.Id,
                    startTicks = self.StartTime.ToUniversalTime().Ticks, updatedAtMs = CurrentUnixMilliseconds() }));
        }

        // Windows argument quoting for a direct executable invocation. No shell or Prompt interpolation.
        private static string QuoteGeneratorArgument(string value)
        {
            StringBuilder quoted = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value) {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { quoted.Append('\\', slashes * 2 + 1); quoted.Append(character); }
                else { quoted.Append('\\', slashes); quoted.Append(character); }
                slashes = 0;
            }
            quoted.Append('\\', slashes * 2); quoted.Append('"'); return quoted.ToString();
        }

        private static void RunGeneration(string id)
        {
            string folder = CreationFolder(id), attempt = Guid.NewGuid().ToString("N");
            using (FileStream locked = new FileStream(Path.Combine(folder, "generation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                if (File.Exists(Path.Combine(folder, "candidate.lotheme"))) throw new InvalidDataException("已有生成结果，请预览或创建新的请求。");
                string cancel = Path.Combine(folder, "cancel-generation");
                SaveGenerationState(folder, attempt, "running", "正在准备生成…");
                try {
                    if (File.Exists(cancel)) throw new OperationCanceledException();
                    Dictionary<string, object> config = ReadGenerator(Path.Combine(experienceRoot, "generator.json"));
                    Dictionary<string, object> request = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.Combine(folder, "request.json"))) as Dictionary<string, object>;
                    object[] references = request["references"] as object[];
                    if (references == null || references.Length > Convert.ToInt32(config["maxReferences"], CultureInfo.InvariantCulture))
                        throw new InvalidDataException("当前生成器不支持这么多张参考图，请减少图片或让 Agent 更换生成器。");
                    string work = Path.Combine(folder, "attempts", attempt); Directory.CreateDirectory(work);
                    string candidate = Path.Combine(work, "candidate.lotheme"), progress = Path.Combine(work, "progress.json");
                    List<string> arguments = new List<string>();
                    foreach (string template in (object[])config["arguments"]) {
                        string value = template == "{request}" ? Path.Combine(folder, "request.json")
                            : template == "{output}" ? candidate : template == "{progress}" ? progress : template;
                        arguments.Add(QuoteGeneratorArgument(value));
                    }
                    ProcessStartInfo start = new ProcessStartInfo {
                        FileName = (string)config["executable"], Arguments = String.Join(" ", arguments.ToArray()),
                        WorkingDirectory = (string)config["workingDirectory"], UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true };
                    using (GeneratorJob job = new GeneratorJob())
                    using (Process provider = new Process())
                    using (StreamWriter log = new StreamWriter(Path.Combine(work, "provider.log"), false, new UTF8Encoding(false))) {
                        object logLock = new object(); int logLength = 0;
                        DataReceivedEventHandler capture = delegate(object sender, DataReceivedEventArgs line) {
                            if (line.Data == null) return;
                            lock (logLock) { if (logLength < 1024 * 1024) {
                                try { log.WriteLine(line.Data); logLength += line.Data.Length; } catch (Exception) { }
                            } }
                        };
                        provider.StartInfo = start; provider.OutputDataReceived += capture; provider.ErrorDataReceived += capture;
                        provider.Start();
                        try { job.Attach(provider); }
                        catch { if (!provider.HasExited) provider.Kill(); throw; }
                        provider.BeginOutputReadLine(); provider.BeginErrorReadLine();
                        Stopwatch clock = Stopwatch.StartNew();
                        while (!provider.WaitForExit(200)) {
                            if (File.Exists(cancel)) throw new OperationCanceledException();
                            if (clock.Elapsed.TotalSeconds > Convert.ToInt32(config["timeoutSeconds"], CultureInfo.InvariantCulture))
                                throw new TimeoutException("生成超时，可以重试或让 Agent 调整生成器。");
                        }
                        provider.WaitForExit();
                        if (File.Exists(cancel)) throw new OperationCanceledException();
                        if (provider.ExitCode != 0) {
                            string error = null;
                            try {
                                Dictionary<string, object> detail = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(progress)) as Dictionary<string, object>;
                                error = ReadString(detail, "error");
                            } catch (Exception) { }
                            if (String.IsNullOrEmpty(error) || error.Length > 300) error = "生成器未完成（退出码 " + provider.ExitCode + "），可以重试。Agent 可检查本次生成记录。";
                            throw new InvalidOperationException(error);
                        }
                    }
                    EnsureThemeNotRejected(candidate); ThemePackage theme = ParseThemePackage(candidate);
                    if (theme.IdleAction == null) throw new InvalidDataException("生成结果缺少休息动作。");
                    if (File.Exists(cancel)) throw new OperationCanceledException();
                    File.Move(candidate, Path.Combine(folder, "candidate.lotheme"));
                    SaveGenerationState(folder, attempt, "ready", "动作已生成，可以预览使用。");
                }
                catch (OperationCanceledException) { SaveGenerationState(folder, attempt, "cancelled", "已取消生成，可以重试。"); }
                catch (Exception error) { SaveGenerationState(folder, attempt, "failed", error.Message); }
            }
        }

        // Closing this handle terminates only this generator process and its descendants.
        private sealed class GeneratorJob : IDisposable
        {
            private IntPtr handle;
            [StructLayout(LayoutKind.Sequential)] private struct Basic {
                public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max;
                public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling;
            }
            [StructLayout(LayoutKind.Sequential)] private struct Extended {
                public Basic Limits; public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
                public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref Extended value, uint length);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr value);
            public GeneratorJob() {
                handle = CreateJobObject(IntPtr.Zero, null);
                Extended value = new Extended(); value.Limits.Flags = 0x2000;
                if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref value, (uint)Marshal.SizeOf(value))) {
                    Dispose(); throw new InvalidOperationException("无法建立可取消的生成进程。");
                }
            }
            public void Attach(Process process) {
                if (!AssignProcessToJobObject(handle, process.Handle)) throw new InvalidOperationException("无法管理生成进程，请重试。");
            }
            public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        }
    }
}
