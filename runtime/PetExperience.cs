using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace AIPet.Desktop
{
    // Product controls and the small, tool-independent Agent interface.
    internal static partial class DesktopPet
    {
        private static readonly string[] AiStates = { "started", "running", "finished", "paused", "needs-user" };
        private static readonly string[] AiStateLabels = { "任务开始", "任务进行中", "任务结束", "任务暂停", "需要回答或确认" };
        private static readonly string[] DefaultAiSlots = { "recent-activity", "recent-activity", "stop-unknown", "interrupt", "needs-user" };
        private static readonly Dictionary<string, string> aiBindings = new Dictionary<string, string>();
        private static readonly HashSet<string> seenAiEvents = new HashSet<string>();
        private static readonly Queue<string> seenAiEventOrder = new Queue<string>();
        private static string experienceRoot, aiConfigurationPath, aiEventsPath;
        private static readonly List<string> creationPhotos = new List<string>();
        private static string latestCreationRequest;
        private static string creationDraftPrompt;
        private static string connectedAiTool, connectedAiSession, lastAiState, lastAiOutcome;
        private static long aiConnectedAt;
        private static string aiConfigurationText;
        private static Window petManager;
        private static TabControl petManagerTabs;
        private static Forms.ToolStripMenuItem actionsMenu;
        private static readonly List<Forms.ToolStripItem> advancedPetItems = new List<Forms.ToolStripItem>();

        private static void InitializeSimpleExperience(string root)
        {
            experienceRoot = root;
            InitializeCodexFollow(root);
            aiConfigurationPath = Path.Combine(root, "ai-connection.json");
            aiEventsPath = Path.Combine(root, "ai-events");
            for (int i = 0; i < AiStates.Length; i++) aiBindings[AiStates[i]] = DefaultAiSlots[i];
            try { ReloadAiConnection(); }
            catch (Exception) { connectedAiTool = connectedAiSession = null; }
        }

        private static bool ValidAiId(string value)
        {
            return value != null && Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\z");
        }

        private static void SaveAiConnection(string tool, string session, bool keepFollowMode = false)
        {
            if (tool != null && (!ValidAiId(tool) || !ValidAiId(session)))
                throw new InvalidDataException("工具和会话标识只允许字母、数字、点、下划线与连字符（最多96字符）。");
            if (!keepFollowMode) SetCodexFollowMode(tool == null ? "off" : "manual");
            else if (!File.Exists(codexFollowPath)) SetCodexFollowMode(codexFollowMode);
            Dictionary<string, object> config = new Dictionary<string, object>();
            config["version"] = 1;
            config["tool"] = tool;
            config["session"] = tool == null ? null : session;
            config["connectedAtMs"] = CurrentUnixMilliseconds();
            config["bindings"] = new Dictionary<string, string>(aiBindings);
            Directory.CreateDirectory(experienceRoot);
            if (tool != "codex") ClearDesktopSelection();
            WriteCodexEventAtomically(aiConfigurationPath, new JavaScriptSerializer().Serialize(config));
            ReloadAiConnection();
        }

        private static void ReloadAiConnection()
        {
            if (!File.Exists(aiConfigurationPath))
            {
                bool wasConnected = connectedAiTool != null;
                connectedAiTool = connectedAiSession = aiConfigurationText = null;
                if (wasConnected) ResetConnectedPlayback();
                return;
            }
            string text = ReadBoundedJson(aiConfigurationPath);
            if (text == aiConfigurationText) return;
            Dictionary<string, object> config = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            if (config == null || Convert.ToInt32(config["version"], CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("连接配置版本不支持。");
            string tool = config["tool"] as string, session = config["session"] as string;
            if (tool != null && (!ValidAiId(tool) || !ValidAiId(session))) throw new InvalidDataException("连接标识无效。");
            Dictionary<string, object> bindings = config["bindings"] as Dictionary<string, object>;
            if (bindings == null || bindings.Count != AiStates.Length) throw new InvalidDataException("需要五项状态映射。");
            foreach (string state in AiStates)
                if (!bindings.ContainsKey(state) || Array.IndexOf(SupportedThemeSlots, bindings[state] as string) < 0)
                    throw new InvalidDataException("动作映射无效。");
            long connected = Convert.ToInt64(config["connectedAtMs"], CultureInfo.InvariantCulture);
            bool changed = tool != connectedAiTool || session != connectedAiSession || connected != aiConnectedAt;
            foreach (string state in AiStates) aiBindings[state] = (string)bindings[state];
            connectedAiTool = tool; connectedAiSession = session; aiConnectedAt = connected; aiConfigurationText = text;
            if (changed) {
                seenAiEvents.Clear(); seenAiEventOrder.Clear(); lastAiState = lastAiOutcome = null;
                selectedCodexSession = null; selectedDesktopRolloutPath = null;
                if (tool == "codex") RestoreDesktopSelection();
                if (selectedCodexSession != session) { selectedCodexSession = null; selectedDesktopRolloutPath = null; }
                if (window != null && activeTheme != null) { activeThemeOneShot = false; SetThemeAction("idle", false); }
            }
        }

        private static object AiCapabilities()
        {
            List<object> actions = new List<object>();
            if (activeTheme != null) foreach (ThemeAction action in activeTheme.Actions)
                actions.Add(new { slot = action.Slot, label = SimpleActionLabel(action.Slot) });
            object generator = null;
            try {
                string generatorPath = Path.Combine(experienceRoot, "generator.json");
                if (File.Exists(generatorPath)) {
                    Dictionary<string, object> configured = ReadGenerator(generatorPath);
                    generator = new { name = configured["name"], maxReferences = configured["maxReferences"], dataHandling = configured["dataHandling"] };
                }
            } catch (Exception) { }
            return new { version = 1, states = AiStates, tool = connectedAiTool, session = connectedAiSession,
                videoExtractionProtocolVersion = 1, generationProtocolVersion = 1, generator = generator, defaultCreationMode = generator == null ? "agent-handoff" : "configured-generator",
                codexDesktop = new { displayedInputRequests = true, permissionPrompts = false, proseQuestions = false, agentReportedStates = true },
                codexFollow = AutomaticCodexStatus(),
                bindings = aiBindings, actions = actions, lastState = lastAiState, lastOutcome = lastAiOutcome,
                activeSlot = activeThemeAction == null ? null : activeThemeAction.Slot,
                dataDirectory = experienceRoot, runtimeRunning = window != null && !exiting, checkedAtMs = CurrentUnixMilliseconds(),
                pid = System.Diagnostics.Process.GetCurrentProcess().Id };
        }

        private static bool HandleAgentCommand(string[] args)
        {
            if (args.Length == 0) return false;
            if (args[0] == "--test-simple-experience" && args.Length == 2)
            {
                try { TestSimpleExperience(Path.GetFullPath(args[1])); }
                catch (Exception error) {
                    Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "error.txt"), error.ToString());
                    Environment.ExitCode = 2;
                    if (trayIcon != null) ExitApplication();
                }
                return true;
            }
            if (!args[0].StartsWith("--ai-", StringComparison.Ordinal)) return false;
            try
            {
                if (HandleVideoRequestCommand(args)) { }
                else if (args[0] == "--ai-codex-hook" && args.Length == 1) RecordSimpleCodexHook();
                else if (args[0] == "--ai-test-appearance" && args.Length == 2) TestModernAppearance(Path.GetFullPath(args[1]));
                else if (args[0] == "--ai-test-session-titles" && args.Length == 2) TestCodexSessionTitles(Path.GetFullPath(args[1]));
                else if (args[0] == "--ai-test-codex-auto" && args.Length == 2) TestAutomaticCodex(Path.GetFullPath(args[1]));
                else if (args[0] == "--ai-codex-auto" && args.Length == 1) SetCodexFollowMode("auto");
                else if (args[0] == "--ai-test-live-desktop" && args.Length == 4)
                    ObserveLiveDesktopForTest(args[1], Int32.Parse(args[2], CultureInfo.InvariantCulture), Path.GetFullPath(args[3]));
                else if (args[0] == "--ai-test-generation-review" && args.Length == 3) TestGenerationReview(args[1], Path.GetFullPath(args[2]));
                else if (args[0] == "--ai-test-generate-click" && args.Length == 3) TestGenerateClick(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                else if (args[0] == "--ai-codex-connect" && args.Length == 2) ConnectSimpleCodex(args[1]);
                else if (args[0] == "--ai-codex-event" && args.Length == 3) {
                    if (!ValidAiId(args[1])) throw new InvalidDataException("Invalid Codex session.");
                    RecordAiStateEvent("codex", DesktopSessionHash(args[1]), args[2]);
                }
                else if (args[0] == "--ai-create-request" && args.Length == 3) {
                    Dictionary<string, object> spec = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.GetFullPath(args[1]))) as Dictionary<string, object>;
                    List<string> photos = new List<string>();
                    foreach (object photo in (object[])spec["photos"]) photos.Add((string)photo);
                    string id = SaveCreationRequest(photos, ReadString(spec, "prompt"));
                    File.WriteAllText(Path.GetFullPath(args[2]), new JavaScriptSerializer().Serialize(new { id = id }), new UTF8Encoding(false));
                }
                else if (args[0] == "--ai-generator-config" && args.Length == 2) {
                    Dictionary<string, object> generator = ReadGenerator(Path.GetFullPath(args[1]));
                    Directory.CreateDirectory(experienceRoot);
                    WriteCodexEventAtomically(Path.Combine(experienceRoot, "generator.json"), new JavaScriptSerializer().Serialize(generator));
                }
                else if (args[0] == "--ai-start-generation" && args.Length == 2) StartGeneration(args[1]);
                else if (args[0] == "--ai-generate" && args.Length == 2) RunGeneration(args[1]);
                else if (args[0] == "--ai-cancel-generation" && args.Length == 2)
                    File.WriteAllText(Path.Combine(CreationFolder(args[1]), "cancel-generation"), "cancel");
                else if (args[0] == "--ai-generation-status" && args.Length == 3)
                    File.WriteAllText(Path.GetFullPath(args[2]), new JavaScriptSerializer().Serialize(GenerationStatus(args[1])), new UTF8Encoding(false));
                else if (args[0] == "--ai-connect" && args.Length == 3) SaveAiConnection(args[1], args[2]);
                else if (args[0] == "--ai-disconnect" && args.Length == 1) SaveAiConnection(null, null);
                else if (args[0] == "--ai-bind" && args.Length == 3)
                {
                    if (Array.IndexOf(AiStates, args[1]) < 0 || Array.IndexOf(SupportedThemeSlots, args[2]) < 0)
                        throw new InvalidDataException("未知状态或动作。");
                    aiBindings[args[1]] = args[2]; SaveAiConnection(connectedAiTool, connectedAiSession, true);
                }
                else if (args[0] == "--ai-event" && args.Length == 4)
                {
                    RecordAiStateEvent(args[1], args[2], args[3]);
                }
                else if (args[0] == "--ai-status" && args.Length == 2)
                {
                    if (File.Exists(activeThemeFilePath))
                    {
                        EnsureThemeNotRejected(activeThemeFilePath);
                        activeTheme = ParseThemePackage(activeThemeFilePath);
                    }
                    object report = AiCapabilities();
                    string livePath = Path.Combine(experienceRoot, "ai-status.json");
                    try {
                        Dictionary<string, object> live = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(livePath)) as Dictionary<string, object>;
                        long checkedAt = Convert.ToInt64(live["checkedAtMs"], CultureInfo.InvariantCulture);
                        System.Diagnostics.Process owner = System.Diagnostics.Process.GetProcessById(Convert.ToInt32(live["pid"], CultureInfo.InvariantCulture));
                        if (CurrentUnixMilliseconds() - checkedAt < 3000 && checkedAt <= CurrentUnixMilliseconds() &&
                            String.Equals(owner.MainModule.FileName, System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase) &&
                            owner.Id != System.Diagnostics.Process.GetCurrentProcess().Id && ReadString(live, "tool") == (connectedAiTool ?? String.Empty) &&
                            ReadString(live, "session") == (connectedAiSession ?? String.Empty)) report = live;
                    } catch (Exception) { }
                    File.WriteAllText(Path.GetFullPath(args[1]), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(false));
                }
                else if (args[0] == "--ai-requests" && args.Length == 2)
                {
                    List<object> requests = new List<object>(); string root = Path.Combine(experienceRoot, "creation-requests");
                    if (Directory.Exists(root)) foreach (string folder in Directory.GetDirectories(root))
                    {
                        if (!Regex.IsMatch(Path.GetFileName(folder), @"\A[a-f0-9]{32}\z")) continue;
                        string request = Path.Combine(folder, "request.json");
                        if (File.Exists(request)) requests.Add(new { id = Path.GetFileName(folder), request = request,
                            instructions = Path.Combine(folder, "agent-instructions.txt"),
                            status = File.Exists(Path.Combine(folder, "candidate.lotheme")) ? "ready" : "pending",
                            generation = GenerationStatus(Path.GetFileName(folder)) });
                    }
                    File.WriteAllText(Path.GetFullPath(args[1]), new JavaScriptSerializer().Serialize(requests), new UTF8Encoding(false));
                }
                else if (args[0] == "--ai-result" && args.Length == 3)
                {
                    if (!Regex.IsMatch(args[1], @"\A[a-f0-9]{32}\z")) throw new InvalidDataException("创作编号无效。");
                    string folder = Path.Combine(experienceRoot, "creation-requests", args[1]);
                    if (!File.Exists(Path.Combine(folder, "request.json"))) throw new InvalidDataException("未找到创作请求。");
                    string source = Path.GetFullPath(args[2]); EnsureThemeNotRejected(source); ParseThemePackage(source);
                    string destination = Path.Combine(folder, "candidate.lotheme");
                    if (File.Exists(destination)) throw new InvalidDataException("本请求已有候选，请新建请求或保留旧版本后再提交。");
                    File.Copy(source, destination);
                }
                else throw new InvalidDataException("不支持的 Agent 命令或参数。");
                Environment.ExitCode = 0;
            }
            catch (Exception error) {
                try {
                    Directory.CreateDirectory(experienceRoot);
                    WriteCodexEventAtomically(Path.Combine(experienceRoot, "agent-error.json"), new JavaScriptSerializer().Serialize(new {
                        command = args[0], message = error.Message, atMs = CurrentUnixMilliseconds() }));
                } catch (Exception) { }
                Environment.ExitCode = 2;
            }
            return true;
        }

        private static void RecordAiStateEvent(string tool, string session, string state)
        {
            if (!ValidAiId(tool) || !ValidAiId(session) || Array.IndexOf(AiStates, state) < 0)
                throw new InvalidDataException("事件需要工具、会话和五个合法状态之一。");
            Directory.CreateDirectory(aiEventsPath);
            string id = Guid.NewGuid().ToString("N");
            object record = new { version = 1, eventId = id, observedAtMs = CurrentUnixMilliseconds(),
                tool = tool, session = session, state = state };
            WriteCodexEventAtomically(Path.Combine(aiEventsPath, CurrentUnixMilliseconds().ToString("D13") + "." + id + ".json"),
                new JavaScriptSerializer().Serialize(record));
            PruneAiEvents();
        }

        private static void PruneAiEvents()
        {
            string[] files = Directory.GetFiles(aiEventsPath, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
                if (i < files.Length - 256 || File.GetLastWriteTimeUtc(files[i]) < DateTime.UtcNow.AddMinutes(-10))
                    TryDeleteCodexEvent(files[i]);
        }

        private static bool ApplyAiEvent(Dictionary<string, object> record)
        {
            if (connectedAiTool == null || record == null || !HasExactKeys(record,
                new string[] { "version", "eventId", "observedAtMs", "tool", "session", "state" })) return false;
            if (!(record["version"] is int) || (int)record["version"] != 1 ||
                ReadString(record, "tool") != connectedAiTool || ReadString(record, "session") != connectedAiSession) return false;
            string state = ReadString(record, "state"), id = ReadString(record, "eventId");
            if (Array.IndexOf(AiStates, state) < 0 || id == null || !Regex.IsMatch(id, @"\A[a-f0-9]{32}\z")) return false;
            if (!(record["observedAtMs"] is long) && !(record["observedAtMs"] is int)) return false;
            long timestamp = Convert.ToInt64(record["observedAtMs"], CultureInfo.InvariantCulture);
            long now = CurrentUnixMilliseconds();
            if (timestamp < aiConnectedAt || timestamp > now + 1000 || now - timestamp > 10000 || !seenAiEvents.Add(id)) return false;
            if (codexFollowMode == "auto" && !applyingAutomaticCodex)
                foreach (CodexWatch watch in codexWatches.Values) if (watch.Hash == connectedAiSession && timestamp < watch.SignalAt) return false;
            seenAiEventOrder.Enqueue(id);
            if (seenAiEventOrder.Count > 512) seenAiEvents.Remove(seenAiEventOrder.Dequeue());
            string previousState = lastAiState;
            lastAiState = state;
            ObserveAutomaticAgentState(state, timestamp);
            string slot = aiBindings[state];
            if (window == null || !window.IsVisible) lastAiOutcome = "hidden";
            else if (IsWithinQuietHours(preferences, DateTime.Now.TimeOfDay)) lastAiOutcome = "quiet-hours";
            else if (activeTheme == null || activeTheme.FindAction(slot) == null) lastAiOutcome = "action-unavailable";
            else {
                // Activity events are heartbeats, not new animation requests.
                // Preserve the clock instead of repeatedly showing the first frames.
                bool continuing = (state == "running" && (previousState == "running" || previousState == "started") ||
                    state == "needs-user" && previousState == "needs-user") && activeThemeAction != null &&
                    activeThemeAction.Slot == slot && themeTimer != null && themeTimer.IsEnabled;
                activeThemeOneShot = false;
                lastAiOutcome = continuing || SetThemeAction(slot, slot != "idle" && state != "running") ? "played" : "action-unavailable";
            }
            UpdatePetActivityCue();
            return true;
        }

        private static void PollAiConnection()
        {
            try
            {
                ReloadCodexFollow();
                ReloadAiConnection();
                PollAutomaticCodex(CurrentUnixMilliseconds());
                if (connectedAiTool == null) {
                    UpdatePetActivityCue();
                    UpdateCodexFollowTooltip();
                    Directory.CreateDirectory(experienceRoot);
                    WriteCodexEventAtomically(Path.Combine(experienceRoot, "ai-status.json"), new JavaScriptSerializer().Serialize(AiCapabilities()));
                    return;
                }
                if (codexFollowMode != "auto" && connectedAiTool == "codex" && selectedCodexSession == connectedAiSession) {
                    PollSelectedDesktopRollout(); ConsumeSelectedCodexEvent();
                }
                else if (codexFollowMode == "auto" && connectedAiTool == "codex" && selectedCodexSession == connectedAiSession) ConsumeSelectedCodexEvent();
                if (lastAiState != null && window != null && (!window.IsVisible || IsWithinQuietHours(preferences, DateTime.Now.TimeOfDay))) {
                    lastAiOutcome = window.IsVisible ? "quiet-hours" : "hidden";
                    ResetConnectedPlayback();
                }
                string[] files = Directory.Exists(aiEventsPath) ? Directory.GetFiles(aiEventsPath, "*.json") : new string[0];
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    try { ApplyAiEvent(new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(file)) as Dictionary<string, object>); }
                    catch (Exception) { }
                }
                UpdateCodexFollowTooltip();
                UpdatePetActivityCue();
                Directory.CreateDirectory(experienceRoot);
                WriteCodexEventAtomically(Path.Combine(experienceRoot, "ai-status.json"), new JavaScriptSerializer().Serialize(AiCapabilities()));
            }
            catch (Exception) {
                bool wasConnected = connectedAiTool != null;
                connectedAiTool = connectedAiSession = aiConfigurationText = null; lastAiOutcome = "invalid-config";
                if (wasConnected) ResetConnectedPlayback();
                UpdatePetActivityCue();
            }
        }

        private static void ResetConnectedPlayback()
        {
            if (window != null && activeTheme != null && activeThemeAction != null && activeThemeAction.Slot != "idle") {
                activeThemeOneShot = false; SetThemeAction("idle", false);
            }
        }

        private static string SimpleActionLabel(string slot)
        {
            if (slot == "idle") return "休息";
            if (slot == "ambient") return "自由活动";
            if (slot == "recent-activity") return "专注工作";
            if (slot == "needs-user") return "招呼你";
            if (slot == "stop-unknown") return "收工";
            if (slot == "interrupt") return "停一停";
            if (slot == "hover") return "好奇看看";
            if (slot == "click") return "打个招呼";
            return slot;
        }

        private static void SimplifyTrayMenu(Forms.ToolStripMenuItem exitItem)
        {
            advancedPetItems.Clear();
            foreach (Forms.ToolStripItem item in trayMenu.Items)
                if (!(item is Forms.ToolStripSeparator) && item != visibilityItem && item != exitItem && item != codexSessionItem)
                    advancedPetItems.Add(item);
            trayMenu.Items.Clear();
            trayMenu.Items.Add(visibilityItem);
            actionsMenu = new Forms.ToolStripMenuItem("宠物动作");
            actionsMenu.DropDownOpening += delegate { PopulatePetActions(); };
            trayMenu.Items.Add(actionsMenu);
            Forms.ToolStripMenuItem custom = new Forms.ToolStripMenuItem("自定义宠物和动作…");
            custom.Click += delegate { ShowPetManager(0); }; trayMenu.Items.Add(custom);
            Forms.ToolStripMenuItem connections = new Forms.ToolStripMenuItem("连接 AI 工具…");
            connections.Click += delegate { ShowPetManager(1); }; trayMenu.Items.Add(connections);
            Forms.ToolStripMenuItem settings = new Forms.ToolStripMenuItem("设置…");
            settings.Click += delegate { ShowPetManager(2); }; trayMenu.Items.Add(settings);
            trayMenu.Items.Add(new Forms.ToolStripSeparator()); trayMenu.Items.Add(exitItem);
        }

        private static void PopulatePetActions()
        {
            actionsMenu.DropDownItems.Clear();
            if (activeTheme != null) foreach (ThemeAction action in activeTheme.Actions)
            {
                string slot = action.Slot;
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(SimpleActionLabel(slot));
                item.Click += delegate { ShowWindow(); activeThemeOneShot = false; SetThemeAction(slot, slot != "idle"); };
                actionsMenu.DropDownItems.Add(item);
            }
            if (actionsMenu.DropDownItems.Count == 0)
                actionsMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("先添加一只宠物") { Enabled = false });
        }

        private static TextBlock ManagerText(string text, bool heading)
        {
            TextBlock block = new TextBlock { Text = text, FontSize = heading ? 26 : 14,
                FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap, LineHeight = heading ? 34 : 22, Margin = new Thickness(0, 0, 0, heading ? 12 : 18) };
            block.SetResourceReference(TextBlock.ForegroundProperty, heading ? "PetText" : "PetMuted");
            return block;
        }

        private static Button ManagerButton(string label, Action action)
        {
            Button button = new Button { Content = label, Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 12) };
            button.Click += delegate {
                try { action(); }
                catch (Exception error) { MessageBox.Show(petManager, error.Message, "未完成操作", MessageBoxButton.OK, MessageBoxImage.Information); }
            };
            return button;
        }

        private static StackPanel ManagerPage(string title, string description)
        {
            StackPanel page = new StackPanel { Margin = new Thickness(28) };
            page.Children.Add(ManagerText(title, true)); page.Children.Add(ManagerText(description, false)); return page;
        }

        private static bool resumePhotoDraft;

        private sealed class PhotoCreationPanel : StackPanel
        {
            private readonly StackPanel summary = new StackPanel(), draft = new StackPanel(), details = new StackPanel();
            private readonly WrapPanel thumbnails = new WrapPanel(), commands = new WrapPanel();
            private readonly TextBlock status = ManagerText("", false), count = ManagerText("", false), hint = ManagerText("", false);
            private readonly TextBlock requestTitle = ManagerText("", false);
            private string displayedRequest;
            private readonly TextBox prompt;
            private readonly Button add, submit, preview, copy, retry, cancel, edit;
            private readonly Expander history;
            private bool editing, running, configured;
            private int maxPhotos = 4;

            public PhotoCreationPanel(string savedPrompt, Window owner)
            {
                editing = latestCreationRequest == null || resumePhotoDraft;
                requestTitle.TextWrapping = TextWrapping.NoWrap; requestTitle.TextTrimming = TextTrimming.CharacterEllipsis;
                requestTitle.FontWeight = FontWeights.SemiBold; requestTitle.Margin = new Thickness(0, 0, 0, 3);
                Children.Add(summary); summary.Children.Add(requestTitle); summary.Children.Add(status); summary.Children.Add(commands);
                preview = ManagerButton("预览生成的动作…", delegate { ImportCreationCandidate(LatestCreationCandidate()); });
                copy = ManagerButton("再次复制制作说明", delegate { CopyCreationHandoff(latestCreationRequest); status.Text = "已复制，可以粘贴到你的 Agent。"; });
                retry = ManagerButton("重试生成", delegate { StartGeneration(latestCreationRequest); Refresh(); });
                cancel = ManagerButton("取消生成", delegate {
                    File.WriteAllText(Path.Combine(CreationFolder(latestCreationRequest), "cancel-generation"), "cancel");
                    status.Text = "正在取消生成…";
                });
                edit = ManagerButton("制作另一个动作", delegate { editing = !editing; Refresh(); });
                foreach (Button button in new[] { preview, copy, retry, cancel, edit }) commands.Children.Add(button);
                Children.Add(draft);
                add = ManagerButton("选择参考照片…", PickPhotos); draft.Children.Add(add);
                draft.Children.Add(thumbnails); draft.Children.Add(count);
                StackPanel description = new StackPanel(); draft.Children.Add(description);
                description.Children.Add(ManagerText("希望宠物做什么动作？", false));
                prompt = new TextBox { Text = savedPrompt ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 2000, Margin = new Thickness(0, 0, 0, 8) };
                System.Windows.Automation.AutomationProperties.SetName(prompt, "动作描述");
                description.Children.Add(prompt); description.Children.Add(hint);
                submit = ManagerButton("生成动作", Submit);
                System.Windows.Automation.AutomationProperties.SetAutomationId(submit, "PhotoSubmit");
                description.Children.Add(submit);
                prompt.TextChanged += delegate { UpdateDraft(); };
                history = new Expander { Header = "之前做好的动作", Margin = new Thickness(0, 2, 0, 10) };
                history.Expanded += delegate { LoadHistory(); }; Children.Add(history);
                details.Children.Add(ManagerButton("打开本机眨眼、歪头模板", OpenSimpleCreator));
                Children.Add(new Expander { Header = "本机动作模板", Content = details, Margin = new Thickness(0, 0, 0, 10) });
                RenderPhotos(); Refresh();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += delegate { if (IsVisible) Refresh(); };
                timer.Start();
                owner.Closed += delegate { timer.Stop(); resumePhotoDraft = editing; creationDraftPrompt = prompt.Text; };
            }

            private void PickPhotos()
            {
                using (Forms.OpenFileDialog picker = new Forms.OpenFileDialog()) {
                    picker.Filter = "宠物照片|*.png;*.jpg;*.jpeg"; picker.Multiselect = true;
                    if (picker.ShowDialog() != Forms.DialogResult.OK) return;
                    List<string> additions = new List<string>();
                    HashSet<string> paths = new HashSet<string>(creationPhotos, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in picker.FileNames) if (paths.Add(file)) additions.Add(file);
                    if (creationPhotos.Count + additions.Count > maxPhotos)
                        throw new InvalidDataException("最多可以使用 " + maxPhotos + " 张照片。请减少本次选择，或先移除已有照片。");
                    foreach (string file in additions) ValidateCreationPhoto(file);
                    creationPhotos.AddRange(additions);
                    RenderPhotos(); UpdateDraft();
                }
            }

            private void RenderPhotos()
            {
                thumbnails.Children.Clear();
                foreach (string file in creationPhotos) {
                    string path = file, name = Path.GetFileName(path);
                    StackPanel tile = new StackPanel { Width = 82, Margin = new Thickness(0, 0, 8, 4) };
                    Image image = new Image { Width = 78, Height = 60, Stretch = Stretch.Uniform, ToolTip = name };
                    try {
                        using (FileStream data = File.OpenRead(path)) {
                            BitmapFrame original = BitmapDecoder.Create(data, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                            bool wide = original.PixelWidth >= original.PixelHeight;
                            data.Position = 0;
                            BitmapImage thumb = new BitmapImage(); thumb.BeginInit(); thumb.CacheOption = BitmapCacheOption.OnLoad;
                            if (wide) thumb.DecodePixelWidth = 100; else thumb.DecodePixelHeight = 100;
                            thumb.StreamSource = data; thumb.EndInit(); thumb.Freeze(); image.Source = thumb;
                        }
                    } catch (IOException) { image.ToolTip = "照片无法读取，请重新选择。"; }
                      catch (UnauthorizedAccessException) { image.ToolTip = "照片无法读取，请重新选择。"; }
                      catch (NotSupportedException) { image.ToolTip = "照片格式无法读取，请重新选择。"; }
                      catch (FileFormatException) { image.ToolTip = "照片格式无法读取，请重新选择。"; }
                    tile.Children.Add(image);
                    TextBlock caption = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name, FontSize = 11 };
                    caption.SetResourceReference(TextBlock.ForegroundProperty, "PetMuted"); tile.Children.Add(caption);
                    Button remove = ManagerButton("移除", delegate { creationPhotos.Remove(path); RenderPhotos(); UpdateDraft(); });
                    remove.ToolTip = "移除 " + name;
                    System.Windows.Automation.AutomationProperties.SetName(remove, "移除 " + name);
                    remove.Padding = new Thickness(8, 2, 8, 2); remove.MinHeight = 26; remove.Margin = new Thickness(0, 4, 0, 0);
                    tile.Children.Add(remove); thumbnails.Children.Add(tile);
                }
            }

            private void UpdateDraft()
            {
                bool hasPhotos = creationPhotos.Count > 0;
                ((StackPanel)prompt.Parent).Visibility = hasPhotos ? Visibility.Visible : Visibility.Collapsed;
                count.Text = hasPhotos ? String.Format("已选 {0} / {1} 张照片", creationPhotos.Count, maxPhotos) : "可选择 1–" + maxPhotos + " 张照片。";
                add.Content = hasPhotos ? "继续添加照片…" : "选择参考照片…";
                add.Visibility = creationPhotos.Count >= maxPhotos ? Visibility.Collapsed : Visibility.Visible;
                add.SetResourceReference(FrameworkElement.StyleProperty, hasPhotos ? (object)typeof(Button) : "PetPrimary");
                submit.IsEnabled = !running && hasPhotos && creationPhotos.Count <= maxPhotos && !String.IsNullOrWhiteSpace(prompt.Text);
                submit.SetResourceReference(FrameworkElement.StyleProperty, "PetPrimary");
            }

            private void Submit()
            {
                if (running) return;
                latestCreationRequest = SaveCreationRequest(creationPhotos, prompt.Text);
                editing = false; history.IsExpanded = false;
                try {
                    if (configured) StartGeneration(latestCreationRequest);
                    else CopyCreationHandoff(latestCreationRequest);
                } finally { Refresh(); }
            }

            private void Refresh()
            {
                try {
                    string config = Path.Combine(experienceRoot, "generator.json");
                    configured = File.Exists(config);
                    maxPhotos = 4;
                    if (configured) {
                        Dictionary<string, object> backend = ReadGenerator(config);
                        maxPhotos = Convert.ToInt32(backend["maxReferences"], CultureInfo.InvariantCulture);
                        hint.Text = "使用 " + ReadString(backend, "name") + "。 " + ReadString(backend, "dataHandling");
                    } else hint.Text = "例如「抬起右爪，向我挥两下」。复制制作说明后，粘贴给你的 Agent。";
                    submit.Content = configured ? "生成动作" : "复制给 Agent 的制作说明";
                    if (latestCreationRequest == null) latestCreationRequest = LatestCreationId();
                    bool hasRequest = latestCreationRequest != null;
                    summary.Visibility = hasRequest ? Visibility.Visible : Visibility.Collapsed;
                    if (!hasRequest) history.Visibility = Visibility.Collapsed;
                    if (hasRequest && latestCreationRequest != displayedRequest) {
                        string folder = CreationFolder(latestCreationRequest);
                        var request = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.Combine(folder, "request.json"))) as Dictionary<string, object>;
                        string description = (ReadString(request, "prompt") ?? "").Replace("\r", " ").Replace("\n", " ");
                        requestTitle.Text = "上次制作 · " + description; requestTitle.ToolTip = description;
                        bool hasOtherRequest = false;
                        foreach (string sibling in Directory.EnumerateDirectories(Path.GetDirectoryName(folder))) {
                            if (sibling != folder && File.Exists(Path.Combine(sibling, "request.json"))) { hasOtherRequest = true; break; }
                        }
                        history.Visibility = hasOtherRequest ? Visibility.Visible : Visibility.Collapsed;
                        displayedRequest = latestCreationRequest;
                    }
                    draft.Visibility = editing || !hasRequest ? Visibility.Visible : Visibility.Collapsed;
                    running = false;
                    if (hasRequest) {
                        JavaScriptSerializer json = new JavaScriptSerializer();
                        var state = json.DeserializeObject(json.Serialize(GenerationStatus(latestCreationRequest))) as Dictionary<string, object>;
                        string kind = ReadString(state, "status");
                        bool ready = kind == "ready";
                        running = state.ContainsKey("running") && (bool)state["running"];
                        status.Text = ready ? "动作已做好，可以预览。" : !configured && kind == "pending"
                            ? "等待 Agent 制作。将制作说明粘贴给它，完成后在这里预览。" : ReadString(state, "message");
                        preview.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
                        copy.Visibility = !ready && !running && !configured ? Visibility.Visible : Visibility.Collapsed;
                        cancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
                        retry.Visibility = !ready && !running && configured ? Visibility.Visible : Visibility.Collapsed;
                        retry.Content = kind == "pending" ? "生成这个动作" : "重试生成";
                        edit.Content = editing ? "收起制作草稿" : "制作另一个动作";
                        preview.SetResourceReference(FrameworkElement.StyleProperty, editing ? (object)typeof(Button) : "PetPrimary");
                    }
                    UpdateDraft();
                } catch (Exception error) { summary.Visibility = Visibility.Visible; status.Text = error.Message; submit.IsEnabled = false; }
            }

            private void LoadHistory()
            {
                StackPanel list = new StackPanel();
                string root = Path.Combine(experienceRoot, "creation-requests");
                if (Directory.Exists(root)) {
                    string[] folders = Directory.GetDirectories(root);
                    Array.Sort(folders, delegate(string a, string b) { return Directory.GetCreationTimeUtc(b).CompareTo(Directory.GetCreationTimeUtc(a)); });
                    foreach (string folder in folders) {
                        string id = Path.GetFileName(folder), candidate = Path.Combine(folder, "candidate.lotheme");
                        if (id == latestCreationRequest || !Regex.IsMatch(id, @"\A[a-f0-9]{32}\z") || !File.Exists(candidate)) continue;
                        try {
                            var request = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.Combine(folder, "request.json"))) as Dictionary<string, object>;
                            string description = (ReadString(request, "prompt") ?? "").Replace("\r", " ").Replace("\n", " ");
                            string when = Directory.GetCreationTime(folder).ToString("MM-dd HH:mm");
                            Button item = ManagerButton(when + "  " + description, delegate { ImportCreationCandidate(candidate); });
                            item.Content = new TextBlock { Text = (string)item.Content, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
                            item.ToolTip = description; list.Children.Add(item);
                        } catch (IOException) { continue; } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                    }
                }
                if (list.Children.Count == 0) list.Children.Add(ManagerText("还没有更早的已完成动作。", false));
                history.Content = list;
            }
        }

        private static void ShowPetManager(int tab)
        {
            if (petManager != null)
            { petManagerTabs.SelectedIndex = tab; petManager.Show(); petManager.Activate(); return; }
            petManager = new Window { Title = "AI Pet · 你的桌面伙伴", Width = 860, Height = 740,
                MinWidth = 720, MinHeight = 540, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            AttachModernAppearance(petManager);
            AttachDisplayRecovery(petManager);
            petManager.Closed += delegate { petManager = null; petManagerTabs = null; };
            petManagerTabs = new TabControl { FontSize = 14 };
            string savedCreationPrompt = creationDraftPrompt ?? "";
            if (latestCreationRequest == null) latestCreationRequest = LatestCreationId();
            if (latestCreationRequest != null) {
                try {
                    string folder = CreationFolder(latestCreationRequest);
                    Dictionary<string, object> saved = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.Combine(folder, "request.json"))) as Dictionary<string, object>;
                    savedCreationPrompt = creationDraftPrompt ?? ReadString(saved, "prompt");
                    if (creationDraftPrompt == null && creationPhotos.Count == 0) foreach (Dictionary<string, object> photo in (object[])saved["references"]) {
                        string name = ReadString(photo, "path");
                        if (Regex.IsMatch(name, @"\Areference-[1-4]\.(png|jpg|jpeg)\z")) creationPhotos.Add(Path.Combine(folder, name));
                    }
                } catch (Exception) { }
            }
            StackPanel customPage = ManagerPage("自定义宠物和动作", "用照片和描述制作动作，或从拍摄的视频中提取真实动作。");
            StackPanel custom = new StackPanel();
            AddVideoSourcePicker(customPage, custom);
            custom.Children.Add(new PhotoCreationPanel(savedCreationPrompt, petManager));
            Expander library = new Expander { Header = "我的动作与主题", Margin = new Thickness(0, 12, 0, 0) };
            library.Expanded += delegate {
                StackPanel content = new StackPanel();
                WrapPanel actionButtons = new WrapPanel();
                if (activeTheme != null) foreach (ThemeAction action in activeTheme.Actions) {
                    string slot = action.Slot;
                    actionButtons.Children.Add(ManagerButton(SimpleActionLabel(slot), delegate { ShowWindow(); activeThemeOneShot = false; SetThemeAction(slot, slot != "idle"); }));
                }
                if (actionButtons.Children.Count > 0) content.Children.Add(actionButtons);
                content.Children.Add(ManagerButton("导入已有主题…", ImportThemePackage));
                library.Content = content;
            };
            customPage.Children.Add(library);
            AddManagerTab("自定义", customPage);

            StackPanel connection = ManagerPage("连接 AI 工具", "自动跟随任务，也可以固定一段会话。");
            CheckBox automaticFollow = new CheckBox { Content = "自动跟随正在运行的 Codex 会话", IsChecked = codexFollowMode == "auto", Margin = new Thickness(0, 0, 0, 16) };
            bool syncingFollow = false;
            automaticFollow.Checked += delegate { if (!syncingFollow) SetCodexFollowMode("auto"); };
            automaticFollow.Unchecked += delegate { if (!syncingFollow) SetCodexFollowMode(connectedAiTool == null ? "off" : "manual"); };
            TextBlock connectionStatus = ManagerText("", false);
            connectionStatus.Margin = new Thickness(0);
            TextBlock currentSession = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,5) };
            StackPanel current = new StackPanel(); current.Children.Add(currentSession); current.Children.Add(connectionStatus);
            Border connectionCard = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 16), Child = current };
            connectionCard.SetResourceReference(Border.BackgroundProperty, "PetSelected");
            connection.Children.Add(connectionCard); connection.Children.Add(automaticFollow);
            Button disconnect = ManagerButton("断开当前连接", delegate {
                SaveAiConnection(null, null); selectedCodexSession = null; selectedDesktopRolloutPath = null; ClearDesktopSelection(); ResetConnectedPlayback();
            });
            TextBlock connectionDetails = ManagerText("", false);
            Expander details = new Expander { Header = "连接详情", Margin = new Thickness(0, 0, 0, 10) };
            StackPanel diagnostics = new StackPanel(); diagnostics.Children.Add(connectionDetails); diagnostics.Children.Add(disconnect);
            details.Content = diagnostics;
            System.Windows.Threading.DispatcherTimer connectionRefresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            Action refreshConnection = delegate {
                syncingFollow = true; automaticFollow.IsChecked = codexFollowMode == "auto"; syncingFollow = false;
                currentSession.Text = CurrentConnectionHeading();
                connectionStatus.Text = ConnectionOverview();
                disconnect.Visibility = connectedAiTool != null ? Visibility.Visible : Visibility.Collapsed;
                if (details.IsExpanded) connectionDetails.Text = FriendlyConnectionStatus();
            };
            details.Expanded += delegate { refreshConnection(); };
            connectionRefresh.Tick += delegate { refreshConnection(); }; refreshConnection(); connectionRefresh.Start();
            petManager.Closed += delegate { connectionRefresh.Stop(); };
            Expander agentSetup = new Expander { Header = "连接其他 AI 工具", Margin = new Thickness(0, 0, 0, 10) };
            StackPanel agentFields = new StackPanel();
            bool customTool = connectedAiTool != null && connectedAiTool != "codex";
            TextBox toolInput = new TextBox { Text = customTool ? connectedAiTool : "", Margin = new Thickness(0, 0, 0, 10) };
            TextBox sessionInput = new TextBox { Text = customTool ? connectedAiSession : "", Margin = new Thickness(0, 0, 0, 12) };
            agentFields.Children.Add(ManagerText("填写你的 Agent 提供的两个标识。", false));
            agentFields.Children.Add(ManagerText("工具标识", false)); agentFields.Children.Add(toolInput);
            agentFields.Children.Add(ManagerText("任务 / 会话标识", false)); agentFields.Children.Add(sessionInput);
            Button connectOther = ManagerButton("连接这个工具", delegate {
                SaveAiConnection(toolInput.Text.Trim(), sessionInput.Text.Trim());
                selectedCodexSession = null; selectedDesktopRolloutPath = null; ClearDesktopSelection();
                refreshConnection();
            });
            Action validateTool = delegate { connectOther.IsEnabled = ValidAiId(toolInput.Text.Trim()) && ValidAiId(sessionInput.Text.Trim()); };
            toolInput.TextChanged += delegate { validateTool(); }; sessionInput.TextChanged += delegate { validateTool(); };
            validateTool(); agentFields.Children.Add(connectOther);
            agentSetup.Content = agentFields;
            Expander codexSetup = new Expander { Header = "固定一个 Codex 会话", Margin = new Thickness(0, 0, 0, 10) };
            codexSetup.Expanded += delegate {
                PopulateCodexSessionMenu();
                StackPanel sessions = new StackPanel();
                AddCodexConnectionButtons(sessions, codexSessionItem.DropDownItems, connectionStatus, delegate {
                    codexSetup.IsExpanded = false; refreshConnection();
                });
                codexSetup.Content = sessions;
            };
            connection.Children.Add(codexSetup);
            Expander mapping = new Expander { Header = "更改任务状态对应的动作", Margin = new Thickness(0, 0, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(mapping, "StateActionBindings");
            mapping.Expanded += delegate { mapping.Content = BuildActionMappings(); };
            connection.Children.Add(mapping); connection.Children.Add(agentSetup); connection.Children.Add(details);
            AddManagerTab("连接 AI 工具", connection);
            StackPanel settings = ManagerPage("设置", "日常只需调整陪伴偏好。备份和恢复放在下方，需要时再打开。");
            settings.Children.Add(ManagerButton("陪伴偏好", ShowPreferences));
            Expander advanced = new Expander { Header = "高级：备份、恢复与显示器", Margin = new Thickness(0, 10, 0, 0) };
            StackPanel utilities = new StackPanel();
            foreach (Forms.ToolStripItem item in advancedPetItems)
            {
                Forms.ToolStripMenuItem menu = item as Forms.ToolStripMenuItem;
                if (menu == null || menu == moveScreenItem || menu.Text == "陪伴偏好…") continue;
                Button button = ManagerButton(menu.Text, delegate { menu.PerformClick(); }); button.IsEnabled = menu.Enabled;
                utilities.Children.Add(button);
            }
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                string name = screen.DeviceName;
                utilities.Children.Add(ManagerButton("移动到显示器 " + name, delegate {
                    MovePetToScreen(FindScreen(name));
                }));
            }
            advanced.Content = utilities; settings.Children.Add(advanced); AddManagerTab("设置", settings);
            DockPanel managerShell = new DockPanel();
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 14, 20, 10) };
            brand.Children.Add(new TextBlock { Text = "AI Pet", FontSize = 20, FontWeight = FontWeights.SemiBold });
            TextBlock tagline = new TextBlock { Text = "你的桌面伙伴", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tagline.SetResourceReference(TextBlock.ForegroundProperty, "PetMuted"); brand.Children.Add(tagline);
            DockPanel.SetDock(brand, Dock.Top); managerShell.Children.Add(brand); managerShell.Children.Add(petManagerTabs);
            petManager.Content = managerShell; petManagerTabs.SelectedIndex = tab; petManager.Show();
        }

        private static StackPanel BuildActionMappings()
        {
            StackPanel mappings = new StackPanel();
            for (int i = 0; i < AiStates.Length; i++)
            {
                string state = AiStates[i];
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                row.Children.Add(new TextBlock { Text = AiStateLabels[i], Width = 160, VerticalAlignment = VerticalAlignment.Center });
                ComboBox choice = new ComboBox { Width = 210, Tag = state };
                if (activeTheme != null) foreach (ThemeAction action in activeTheme.Actions)
                {
                    ComboBoxItem item = new ComboBoxItem { Content = SimpleActionLabel(action.Slot), Tag = action.Slot };
                    choice.Items.Add(item); if (action.Slot == aiBindings[state]) choice.SelectedItem = item;
                }
                if (choice.SelectedItem == null)
                {
                    ComboBoxItem unavailable = new ComboBoxItem { Content = SimpleActionLabel(aiBindings[state]) + "（尚未添加）", Tag = aiBindings[state] };
                    choice.Items.Add(unavailable); choice.SelectedItem = unavailable;
                }
                choice.SelectionChanged += delegate {
                    ComboBoxItem selected = choice.SelectedItem as ComboBoxItem;
                    if (selected != null) { aiBindings[state] = (string)selected.Tag; SaveAiConnection(connectedAiTool, connectedAiSession, true); }
                };
                row.Children.Add(choice); mappings.Children.Add(row);
            }
            return mappings;
        }

        private static string CurrentConnectionHeading()
        {
            if (connectedAiTool == null) return codexFollowMode == "auto" ? "等待 Codex 任务" : "还没有连接";
            if (connectedAiTool != "codex") return connectedAiTool;
            if (codexFollowMode == "auto")
                foreach (CodexWatch watch in codexWatches.Values)
                    if (watch.Hash == connectedAiSession) return AutomaticCodexLabel(watch, CurrentUnixMilliseconds());
            return CodexTitleForHash(connectedAiSession) ?? "未命名会话";
        }

        private static string ConnectionOverview()
        {
            if (connectedAiTool == null) return codexFollowMode == "auto" ? "开始一个本机 Codex 任务后，会在这里显示会话标题。" : "可以开启自动跟随，或选择要连接的会话。";
            string mode = connectedAiTool == "codex" ? (codexFollowMode == "auto" ? "自动跟随 Codex" : "固定的 Codex 会话") : "已连接";
            int state = Array.IndexOf(AiStates, lastAiState);
            string status = state < 0 ? "等待任务" : AiStateLabels[state];
            if (lastAiOutcome == "signal-stale") status = "状态待确认";
            else if (lastAiOutcome == "action-unavailable") status += " · 请选择可用的动作";
            else if (lastAiOutcome == "quiet-hours") status += " · 安静时段";
            else if (lastAiOutcome == "hidden") status += " · 桌宠已隐藏";
            int others = 0;
            if (connectedAiTool == "codex" && codexFollowMode == "auto") {
                long now = CurrentUnixMilliseconds();
                foreach (CodexWatch watch in codexWatches.Values) if (watch.Hash != connectedAiSession && ActiveCodexWatch(watch, now)) others++;
            }
            return mode + " · " + status + (others > 0 ? " · 另有 " + others + " 个活动会话" : "");
        }

        private static string FriendlyConnectionStatus()
        {
            if (connectedAiTool == null) return codexFollowMode == "auto" ? AutomaticCodexSummary() + "\n任务开始后会自动关联。" : "尚未连接\n选择 Codex 会话，或连接自己的 Agent。";
            string title = connectedAiTool == "codex" ? "已连接 Codex" : "已连接 " + connectedAiTool;
            if (connectedAiTool == "codex") title += "\n会话：" + (CodexTitleForHash(connectedAiSession) ?? "未命名会话");
            if (codexFollowMode == "auto") title = AutomaticCodexSummary();
            else if (connectedAiTool == "codex") title = "固定会话 · " + title;
            int state = Array.IndexOf(AiStates, lastAiState);
            string detail = state < 0 ? "等待这个会话的新任务状态…" : AiStateLabels[state];
            if (lastAiOutcome == "hidden") detail += " · 桌宠已隐藏";
            else if (lastAiOutcome == "quiet-hours") detail += " · 安静时段，暂停播放";
            else if (lastAiOutcome == "action-unavailable") detail += " · 请为此状态选择可用动作";
            else if (lastAiOutcome == "signal-stale") detail = "没有新的明确信号；宠物暂时休息。";
            else if (lastAiOutcome == "played" && activeThemeAction != null) detail += " · 动作：" + SimpleActionLabel(activeThemeAction.Slot);
            if (connectedAiTool == "codex" && lastCodexActionAtMs > 0) {
                long seconds = Math.Max(0, (CurrentUnixMilliseconds() - lastCodexActionAtMs) / 1000);
                detail += "\n最近收到状态：" + (seconds < 60 ? seconds + " 秒前" : seconds / 60 + " 分钟前");
            }
            return title + "\n" + detail;
        }

        private static void AddManagerTab(string label, StackPanel content)
        {
            petManagerTabs.Items.Add(new TabItem { Header = label, Padding = new Thickness(18, 8, 18, 8),
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }

        private static void AddCodexConnectionButtons(StackPanel panel, Forms.ToolStripItemCollection items, TextBlock status, Action selectedSession = null)
        {
            foreach (Forms.ToolStripItem item in items)
            {
                Forms.ToolStripMenuItem menu = item as Forms.ToolStripMenuItem;
                if (menu == null || (menu.Tag as string) == "disconnect") continue;
                if (menu.DropDownItems.Count > 0) { AddCodexConnectionButtons(panel, menu.DropDownItems, status, selectedSession); continue; }
                Button button = ManagerButton(menu.Text, delegate {
                    menu.PerformClick();
                    string session = selectedCodexSession;
                    if (session != null) SaveAiConnection("codex", session);
                    if (selectedSession != null) selectedSession(); else status.Text = FriendlyConnectionStatus();
                });
                CodexSessionDisplay display = menu.Tag as CodexSessionDisplay;
                if (display != null) {
                    StackPanel label = new StackPanel();
                    label.Children.Add(new TextBlock { Text = display.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 370 });
                    TextBlock context = new TextBlock { Text = display.Context, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
                    context.SetResourceReference(TextBlock.ForegroundProperty, "PetMuted"); label.Children.Add(context);
                    button.Content = label; button.ToolTip = menu.ToolTipText;
                    button.HorizontalContentAlignment = HorizontalAlignment.Left;
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                button.IsEnabled = menu.Enabled; panel.Children.Add(button);
            }
        }

        private static void OpenSimpleCreator()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AI Pet Creator", "AIPetCreator.exe");
            string adjacent = Path.Combine(root, "AIPetCreator.exe");
            string executable = File.Exists(adjacent) ? adjacent : installed;
            if (File.Exists(executable))
            { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true }); return; }
            string sourceRoot = Path.GetFullPath(Path.Combine(root, "..", ".."));
            if (File.Exists(Path.Combine(root, "tools", "creator_launcher.py"))) sourceRoot = root;
            string script = Path.Combine(sourceRoot, "tools", "creator_launcher.py");
            if (!File.Exists(script)) throw new InvalidOperationException("尚未安装创作空间。请让 Agent 按仓库 Skill 安装 AI Pet Creator。");
            string localPython = Path.Combine(sourceRoot, ".venv-matting", "Scripts", "python.exe");
            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo {
                FileName = File.Exists(localPython) ? localPython : "python", Arguments = "\"" + script + "\"",
                WorkingDirectory = sourceRoot, UseShellExecute = false, CreateNoWindow = true };
            System.Diagnostics.Process.Start(start);
        }

        private static void ValidateCreationPhoto(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 1 || file.Length > 10 * 1024 * 1024) throw new InvalidDataException("每张图片需小于 10 MB。");
            using (FileStream stream = File.OpenRead(path)) {
                BitmapDecoder image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                if (image.Frames.Count != 1 || image.Frames[0].PixelWidth > 8192 || image.Frames[0].PixelHeight > 8192)
                    throw new InvalidDataException("请使用单帧、边长不超过 8192 像素的图片。");
            }
        }

        private static string SaveCreationRequest(IList<string> photos, string prompt)
        {
            if (photos.Count < 1 || photos.Count > 4 || String.IsNullOrWhiteSpace(prompt) || prompt.Length > 2000)
                throw new InvalidDataException("请添加 1–4 张图片，并描述想要的动作。");
            foreach (string photo in photos) ValidateCreationPhoto(photo);
            string id = Guid.NewGuid().ToString("N");
            string folder = Path.Combine(experienceRoot, "creation-requests", id);
            Directory.CreateDirectory(folder);
            List<object> references = new List<object>();
            for (int i = 0; i < photos.Count; i++) {
                string name = "reference-" + (i + 1) + Path.GetExtension(photos[i]).ToLowerInvariant();
                string destination = Path.Combine(folder, name); File.Copy(photos[i], destination);
                references.Add(new { path = name, sha256 = FileSha256Hex(destination) });
            }
            WriteCodexEventAtomically(Path.Combine(folder, "request.json"), new JavaScriptSerializer().Serialize(new {
                version = 1, id = id, prompt = prompt.Trim(), references = references, createdAtMs = CurrentUnixMilliseconds(),
                target = "transparent-desktop-pet", review = "required" }));
            WriteCodexEventAtomically(Path.Combine(folder, "agent-instructions.txt"), CreationHandoff(id));
            return id;
        }

        private static string CreationHandoff(string id)
        {
            string folder = CreationFolder(id);
            return "请使用 $ai-pet Skill 制作这份桌宠动作请求。\r\n"
                + "Skill 来源：https://github.com/tanghuanqiang/ai-pet/tree/main/skills/ai-pet\r\n"
                + "创作编号：" + id + "\r\n"
                + "请求文件：" + Path.Combine(folder, "request.json") + "\r\n"
                + "桌宠程序：" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName + "\r\n"
                + "数据目录：" + experienceRoot + "\r\n"
                + "读取请求中的全部参考图并校验 SHA-256，按其中的 Prompt 使用你已有的图像/动画生成工具制作透明动作。"
                + "保持原 Prompt 的动作意图；动画需在约 190 像素的桌宠尺寸下仍能看清动作。"
                + "非待机动作应在约 0.2 秒内起势，主要动作优先在 0.8–1.6 秒内完成，减少长时间静止与缓慢铺垫。"
                + "让头部、前爪或尾巴的实际动作幅度清楚可辨，不要只做呼吸或整张图片平移缩放；不同任务状态需要可区分的姿态或节奏。"
                + "保持脸部、花纹、身体比例、透明边缘和循环接缝稳定，先按桌面实际尺寸预览；用户明确指定的时长或慢动作要求优先。"
                + "若工具不可用，请明确告诉我缺少什么。将候选主题通过 ai-pet result 提交到上述编号，使用相同桌宠程序和数据目录。"
                + "完成后我会在桌宠中预览并选择使用。";
        }

        private static void CopyCreationHandoff(string id)
        {
            string instructions = CreationHandoff(id);
            WriteCodexEventAtomically(Path.Combine(CreationFolder(id), "agent-instructions.txt"), instructions);
            try { System.Windows.Clipboard.SetText(instructions); }
            catch (Exception) { throw new InvalidOperationException("请求已保存，剪贴板暂时不可用，请稍后点击“复制制作说明”。"); }
        }

        private static string LatestCreationCandidate()
        {
            string id = LatestCreationId();
            if (id == null) return null;
            string result = Path.Combine(experienceRoot, "creation-requests", id, "candidate.lotheme");
            return File.Exists(result) ? result : null;
        }

        private static string LatestCreationId()
        {
            if (latestCreationRequest != null) return File.Exists(Path.Combine(experienceRoot, "creation-requests", latestCreationRequest, "request.json")) ? latestCreationRequest : null;
            string root = Path.Combine(experienceRoot, "creation-requests");
            if (!Directory.Exists(root)) return null;
            string[] folders = Directory.GetDirectories(root);
            Array.Sort(folders, delegate(string a, string b) { return Directory.GetCreationTimeUtc(b).CompareTo(Directory.GetCreationTimeUtc(a)); });
            foreach (string folder in folders) {
                if (latestCreationRequest != null && Path.GetFileName(folder) != latestCreationRequest) continue;
                if (!File.Exists(Path.Combine(folder, "request.json"))) continue;
                return Path.GetFileName(folder);
            }
            return null;
        }

        private static void ImportCreationCandidate(string candidate)
        {
            string folder = Path.GetDirectoryName(candidate);
            Dictionary<string, object> request = new JavaScriptSerializer().DeserializeObject(ReadBoundedJson(Path.Combine(folder, "request.json"))) as Dictionary<string, object>;
            System.Collections.IList references = request["references"] as System.Collections.IList;
            Dictionary<string, object> first = references[0] as Dictionary<string, object>;
            string relative = ReadString(first, "path");
            if (Path.GetFileName(relative) != relative) throw new InvalidDataException("参考图片路径无效。");
            string photo = Path.Combine(folder, relative);
            if (FileSha256Hex(photo) != ReadString(first, "sha256")) throw new InvalidDataException("参考图片已变化，请重新制作。");
            ValidateCreationPhoto(photo);
            BitmapImage source = LoadReferenceBitmap(photo);
            JpegBitmapEncoder image = new JpegBitmapEncoder(); image.QualityLevel = 90; image.Frames.Add(BitmapFrame.Create(source));
            string photoData;
            using (MemoryStream stream = new MemoryStream()) { image.Save(stream); photoData = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray()); }
            string prompt = ReadString(request, "prompt"); if (prompt.Length > 1200) prompt = prompt.Substring(0, 1200);
            object card = new { kind = "little-orbit.character-card", version = 1, status = "draft", character = new {
                name = "我的宠物", mode = "real", photo = photoData, photoName = relative,
                action = "打个招呼", prompt = prompt, traits = new string[0] } };
            string previousJson = File.Exists(pendingRoleFilePath) ? ReadBoundedJson(pendingRoleFilePath) : null;
            RoleCard previous = pendingRole;
            try {
                string json = new JavaScriptSerializer().Serialize(card);
                PersistRoleCardAt(pendingRoleFilePath, json); pendingRole = ParseRoleCard(json);
                ImportThemePackage(candidate);
            }
            finally {
                if (previousJson != null) PersistRoleCardAt(pendingRoleFilePath, previousJson);
                else if (File.Exists(pendingRoleFilePath)) File.Delete(pendingRoleFilePath);
                pendingRole = previous; UpdatePendingRoleMenu();
            }
        }

        private static void TestPetBubble(string themePath, string output)
        {
            string data = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR"), home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrEmpty(data) || String.IsNullOrEmpty(home) || Directory.Exists(output) ||
                !Path.GetFullPath(data).StartsWith(Path.GetFullPath(output).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(home).StartsWith(Path.GetFullPath(data).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bubble QA requires a new isolated data/Codex directory.");
            Directory.CreateDirectory(output); Directory.CreateDirectory(home);
            string one = "11111111-1111-4111-8111-111111111111", two = "22222222-2222-4222-8222-222222222222";
            string title = "猫咪动作与会话气泡 🐱", secondTitle = "另一个正在运行的任务 · 标题保持静止";
            string index = Path.Combine(home, "session_index.jsonl");
            JavaScriptSerializer json = new JavaScriptSerializer();
            File.WriteAllLines(index, new[] { json.Serialize(new {id=one, thread_name=title}), json.Serialize(new {id=two, thread_name=secondTitle}) }, Encoding.UTF8);
            string digest = FileSha256Hex(themePath);
            activeTheme = ParseThemePackage(themePath);
            WpfApplication app = new WpfApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            window.Show(); window.Left = window.Top = -10000;
            SaveAiConnection("codex", DesktopSessionHash(one));
            Dictionary<string, bool> checks = new Dictionary<string, bool>();
            Action<string> send = delegate(string state) {
                if (!ApplyAiEvent(new Dictionary<string, object> { {"version",1}, {"eventId",Guid.NewGuid().ToString("N")},
                    {"observedAtMs",CurrentUnixMilliseconds()}, {"tool","codex"}, {"session",connectedAiSession}, {"state",state} }))
                    throw new InvalidOperationException("Bubble fixture event rejected.");
            };
            Action<string> capture = delegate(string name) {
                window.UpdateLayout(); RenderTargetBitmap bitmap = new RenderTargetBitmap(190, 254, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render((FrameworkElement)window.Content); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
            };
            UpdatePetActivityCue();
            checks["connectedTitleVisible"] = petActivityTitle.Text == title && petActivityLabel == "等待任务";
            send("running"); capture("running-0");
            bool animationAllowed = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew(); int stage = 0;
            System.Windows.Threading.DispatcherTimer observe = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            observe.Tick += delegate {
                try {
                    if (stage == 0 && clock.ElapsedMilliseconds >= 600) {
                        checks["runningTextAnimated"] = petActivityAnimating == animationAllowed && (!animationAllowed || petActivityText.Text.Contains("."));
                        checks["titleStaysStill"] = petActivityTitle.Text == title && !petActivityTitle.HasAnimatedProperties;
                        capture("running-1"); send("needs-user");
                        checks["waitingIsStatic"] = !petActivityTimer.IsEnabled && petActivityLabel == "等你回答" && petActivityText.Effect == null;
                        capture("waiting"); send("paused"); capture("paused"); send("finished"); capture("finished");
                        checks["terminalStatesStatic"] = !petActivityAnimating && petActivityLabel == "任务结束";
                        SaveAiConnection("codex", DesktopSessionHash(two)); send("running");
                        checks["switchUpdatesTitle"] = petActivityTitle.Text == secondTitle;
                        capture("switched");
                        string longTitle = String.Concat(System.Linq.Enumerable.Repeat("非常长的会话标题🐱", 12));
                        File.AppendAllText(index, json.Serialize(new {id=two, thread_name=longTitle}) + "\n", Encoding.UTF8);
                        codexTitlesCheckedAt = DateTime.MinValue; UpdatePetActivityCue(); window.UpdateLayout();
                        checks["renamedTitleUpdates"] = petActivityTitle.Text.StartsWith("非常长的会话标题");
                        checks["longTitleFits"] = petActivityTitle.ActualHeight <= 28.1 && petActivityCue.ActualWidth <= 182.1 && petActivityCue.ActualHeight <= 70;
                        capture("long-title");
                        window.Hide(); checks["hiddenStopsAnimation"] = !petActivityTimer.IsEnabled;
                        window.Show(); UpdatePetActivityCue();
                        checks["shownResumesAnimation"] = petActivityAnimating == animationAllowed;
                        SaveAiConnection("codex", DesktopSessionHash("33333333-3333-4333-8333-333333333333")); UpdatePetActivityCue();
                        checks["missingTitleHasFallback"] = petActivityTitle.Text == "未命名会话";
                        SaveAiConnection(null, null); UpdatePetActivityCue();
                        checks["disconnectClearsBubble"] = petActivityCue.Visibility == Visibility.Collapsed && petActivityTail.Visibility == Visibility.Collapsed && !petActivityTimer.IsEnabled;
                        checks["sourceUnchanged"] = FileSha256Hex(themePath) == digest;
                        bool passed = true; foreach (bool check in checks.Values) passed &= check;
                        File.WriteAllText(Path.Combine(output, "result.json"), json.Serialize(new {passed=passed, checks=checks, animationAllowed=animationAllowed, installed=false}));
                        observe.Stop(); if (!passed) Environment.ExitCode = 2; ExitApplication(); stage++;
                    }
                } catch (Exception error) { observe.Stop(); File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Environment.ExitCode=2; ExitApplication(); }
            };
            observe.Start(); app.Run();
        }

        private static void TestMotionReadability(string themePath, string output)
        {
            string data = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR");
            if (String.IsNullOrEmpty(data) || Directory.Exists(output) ||
                !Path.GetFullPath(data).StartsWith(Path.GetFullPath(output).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Motion QA requires a new isolated output/data directory.");
            Directory.CreateDirectory(output);
            string originalHash = FileSha256Hex(themePath);
            activeTheme = ParseThemePackage(themePath);
            bool referenceTiming = activeTheme.CompactMotion;
            Dictionary<string, object> checks = new Dictionary<string, object>();
            List<object> timing = new List<object>();
            foreach (ThemeAction action in activeTheme.Actions) {
                int first, count; double fps;
                ThemePlaybackTiming(action, out first, out count, out fps);
                if (first < 0 || count < 1 || first + count > action.Frames.Count) throw new InvalidDataException("Playback range invalid.");
                timing.Add(new { slot = action.Slot, sourceSeconds = action.Frames.Count / (double)action.FramesPerSecond,
                    playbackSeconds = count / fps, first = first, frames = count, fps = fps });
            }
            activeTheme.CompactMotion = false;
            bool otherThemesUnchanged = true;
            foreach (ThemeAction action in activeTheme.Actions) {
                int first, count; double fps; ThemePlaybackTiming(action, out first, out count, out fps);
                otherThemesUnchanged &= first == 0 && count == action.Frames.Count && fps == action.FramesPerSecond;
            }
            activeTheme.CompactMotion = referenceTiming;
            checks["otherThemesKeepAuthoredTiming"] = otherThemesUnchanged;
            WpfApplication app = new WpfApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            window.Show(); window.Left = window.Top = -10000;
            SaveAiConnection("motion-qa", "fixture");
            Action<string> send = delegate(string state) {
                if (!ApplyAiEvent(new Dictionary<string, object> { {"version",1}, {"eventId",Guid.NewGuid().ToString("N")},
                    {"observedAtMs",CurrentUnixMilliseconds()}, {"tool","motion-qa"}, {"session","fixture"}, {"state",state} }))
                    throw new InvalidOperationException("QA event rejected.");
            };
            Action<string> capture = delegate(string name) {
                window.UpdateLayout(); FrameworkElement content = (FrameworkElement)window.Content;
                RenderTargetBitmap image = new RenderTargetBitmap(190, 254, 96, 96, PixelFormats.Pbgra32);
                image.Render(content); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
            };
            send("started");
            checks["startKeepsWorking"] = HasPersistentAiAnimation() && petActivityText.Text == "任务开始";
            send("running");
            System.Diagnostics.Stopwatch preservedClock = themeActionClock;
            System.Diagnostics.Stopwatch qaClock = System.Diagnostics.Stopwatch.StartNew();
            int stage = 0;
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(80);
            timer.Tick += delegate {
                try {
                    if (stage == 0 && qaClock.ElapsedMilliseconds >= 450) {
                        send("running");
                        checks["heartbeatsPreserveClock"] = themeActionClock == preservedClock && themeActionClock.ElapsedMilliseconds >= 400;
                        themeImage.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                        themeImage.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                        checks["hoverDoesNotInterruptWork"] = activeThemeAction.Slot == aiBindings["running"] && themeActionClock == preservedClock;
                        checks["workLabel"] = petActivityLabel == "运行中";
                        capture("working"); send("needs-user"); qaClock.Restart(); stage++;
                    }
                    else if (stage == 1 && qaClock.ElapsedMilliseconds >= 2100) {
                        checks["inputWaitPersists"] = activeThemeAction.Slot == aiBindings["needs-user"] && petActivityText.Text == "等你回答";
                        capture("waiting"); send("paused"); qaClock.Restart(); stage++;
                    }
                    else if (stage == 2 && qaClock.ElapsedMilliseconds >= 1500) {
                        checks["pauseLabelPersists"] = petActivityText.Text == "已暂停";
                        checks["pausePoseHeld"] = activeThemeAction.Slot == aiBindings["paused"] && !themeTimer.IsEnabled;
                        capture("paused"); send("finished"); qaClock.Restart(); stage++;
                    }
                    else if (stage == 3 && qaClock.ElapsedMilliseconds >= 2100) {
                        checks["finishIsNotSuccess"] = petActivityText.Text == "任务结束";
                        checks["finishPoseHeld"] = activeThemeAction.Slot == aiBindings["finished"] && !themeTimer.IsEnabled;
                        capture("finished"); lastAiOutcome = "signal-stale"; ResetConnectedPlayback(); UpdatePetActivityCue();
                        checks["staleIsNotWork"] = petActivityText.Text == "状态待确认" && activeThemeAction.Slot == "idle";
                        capture("unconfirmed"); SaveAiConnection(null, null); UpdatePetActivityCue();
                        checks["disconnectClearsCue"] = petActivityCue.Visibility == Visibility.Collapsed;
                        checks["sourceUnchanged"] = FileSha256Hex(themePath) == originalHash;
                        checks["playbackRangesValid"] = true;
                        timer.Stop(); bool passed = true; foreach (object value in checks.Values) if (!(bool)value) passed = false;
                        File.WriteAllText(Path.Combine(output, "result.json"), new JavaScriptSerializer().Serialize(new {
                            passed = passed, checks = checks, timing = timing, compactProfile = referenceTiming, installed = false }));
                        if (!passed) Environment.ExitCode = 2;
                        ExitApplication();
                    }
                } catch (Exception error) {
                    timer.Stop(); File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Environment.ExitCode = 2; ExitApplication();
                }
            };
            timer.Start(); app.Run();
        }

        private static void TestSimpleExperience(string output)
        {
            Directory.CreateDirectory(output);
            WpfApplication app = new WpfApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string referenceTheme = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reviewed-reference-cat.lotheme");
            activeTheme = File.Exists(referenceTheme) ? ParseThemePackage(referenceTheme) : CreatePublicDemoTheme();
            BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            window.Show();
            PopulatePetActions(); ShowPetManager(0);
            Window first = petManager; ShowPetManager(1);
            bool reused = first == petManager && petManagerTabs.SelectedIndex == 1;
            List<string> menu = new List<string>();
            foreach (Forms.ToolStripItem item in trayMenu.Items) if (!(item is Forms.ToolStripSeparator)) menu.Add(item.Text);
            bool simple = menu.Count == 6 && actionsMenu.DropDownItems.Count == activeTheme.Actions.Count;
            trayMenu.Size = trayMenu.GetPreferredSize(System.Drawing.Size.Empty);
            using (System.Drawing.Bitmap menuBitmap = new System.Drawing.Bitmap(trayMenu.Width, trayMenu.Height)) {
                trayMenu.DrawToBitmap(menuBitmap, new System.Drawing.Rectangle(0, 0, trayMenu.Width, trayMenu.Height));
                menuBitmap.Save(Path.Combine(output, "menu.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            for (int i = 0; i < 3; i++)
            {
                petManagerTabs.SelectedIndex = i; petManager.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)petManager.ActualWidth, (int)petManager.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(petManager); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(Path.Combine(output, "manager-" + i + ".png"))) encoder.Save(stream);
            }
            SaveAiConnection("test-agent", "task-1");
            bool states = true; List<object> outcomes = new List<object>();
            foreach (string state in AiStates)
            {
                Dictionary<string, object> record = new Dictionary<string, object> {
                    {"version",1}, {"eventId",Guid.NewGuid().ToString("N")}, {"observedAtMs",CurrentUnixMilliseconds()},
                    {"tool","test-agent"}, {"session","task-1"}, {"state",state} };
                bool accepted = ApplyAiEvent(record), duplicate = !ApplyAiEvent(record);
                record["eventId"] = Guid.NewGuid().ToString("N"); record["session"] = "another-task";
                bool isolated = !ApplyAiEvent(record);
                record["session"] = "task-1"; record["observedAtMs"] = CurrentUnixMilliseconds() - 20000;
                bool stale = !ApplyAiEvent(record);
                bool played = lastAiOutcome == "played" && activeThemeAction.Slot == aiBindings[state];
                states &= accepted && duplicate && isolated && stale && played;
                outcomes.Add(new { state = state, accepted = accepted, played = played, duplicateRejected = duplicate, otherSessionRejected = isolated, staleRejected = stale });
            }
            SaveAiConnection(null, null);
            SetThemeAction("click", true); PollAiConnection(); PollAiConnection();
            bool manualWithoutConnection = activeThemeAction.Slot == "click";
            SaveAiConnection("test-agent", "task-1");
            Dictionary<string, object> runningEvent = new Dictionary<string, object> {
                {"version",1}, {"eventId",Guid.NewGuid().ToString("N")}, {"observedAtMs",CurrentUnixMilliseconds()},
                {"tool","test-agent"}, {"session","task-1"}, {"state","running"} };
            ApplyAiEvent(runningEvent); window.Hide(); PollAiConnection();
            bool hiddenStopsRunning = activeThemeAction.Slot == "idle" && lastAiOutcome == "hidden";
            window.Show(); PollAiConnection();
            bool showDoesNotReplay = activeThemeAction.Slot == "idle";
            runningEvent["eventId"] = Guid.NewGuid().ToString("N"); runningEvent["observedAtMs"] = CurrentUnixMilliseconds();
            ApplyAiEvent(runningEvent); File.Delete(aiConfigurationPath); PollAiConnection();
            bool removedConnectionStopsRunning = activeThemeAction.Slot == "idle" && connectedAiTool == null;
            SaveAiConnection(null, null);
            RenderPreview(Path.Combine(output, "pet.png"));
            string creation = SaveCreationRequest(new string[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg") }, "保留参考形象，抬起爪子打招呼。");
            string candidate = Path.Combine(experienceRoot, "creation-requests", creation, "candidate.lotheme");
            ExportPublicDemoTheme(candidate);
            bool themeExisted = File.Exists(activeThemeFilePath);
            themeReviewRenderPath = Path.Combine(output, "candidate-review.png");
            themeReviewRenderSlot = "idle"; themeReviewRenderFrame = 0;
            ImportCreationCandidate(candidate);
            bool previewOnly = File.Exists(Path.Combine(output, "candidate-review.png")) &&
                File.Exists(activeThemeFilePath) == themeExisted && pendingRole == null && !File.Exists(pendingRoleFilePath);
            File.WriteAllText(Path.Combine(output, "result.json"), new JavaScriptSerializer().Serialize(new {
                menu = menu, simpleMenu = simple, reusedWindow = reused, fiveStateIsolation = states,
                actions = actionsMenu.DropDownItems.Count, outcomes = outcomes, disconnected = connectedAiTool == null,
                manualWithoutConnection = manualWithoutConnection, hiddenStopsRunning = hiddenStopsRunning,
                showDoesNotReplay = showDoesNotReplay, removedConnectionStopsRunning = removedConnectionStopsRunning,
                twoPhotoCreationRequest = creation, creationPreviewRequiresConfirmation = previewOnly }));
            ExitApplication();
            if (!simple || !reused || !states || !previewOnly || !manualWithoutConnection || !hiddenStopsRunning ||
                !showDoesNotReplay || !removedConnectionStopsRunning) throw new InvalidOperationException("Simple experience regression failed.");
        }

        // This original illustration is generated from code; no private photo or model is distributed.
        private static BitmapSource DrawPublicDemoFrame(string slot, int index)
        {
            double phase = Math.Sin(index * Math.PI * 2 / 24.0);
            double blink = slot == "interrupt" ? 0.10 : slot == "stop-unknown" ? 0.4 :
                slot == "idle" && index > 16 && index < 21 ? 0.14 : 1;
            double look = slot == "hover" ? phase * 4 : 0;
            SolidColorBrush fur = new SolidColorBrush(Color.FromRgb(230, 173, 112));
            SolidColorBrush cream = new SolidColorBrush(Color.FromRgb(255, 239, 209));
            SolidColorBrush ink = new SolidColorBrush(Color.FromRgb(75, 61, 54));
            Pen outline = new Pen(ink, 2.5);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawEllipse(fur, outline, new Point(153, 188), 23, 15);
                dc.DrawEllipse(fur, outline, new Point(94, 176), 56, 57);
                dc.DrawEllipse(cream, null, new Point(94, 181), 34, 43);
                dc.DrawEllipse(fur, outline, new Point(63, 222), 23, 10);
                dc.DrawEllipse(fur, outline, new Point(123, 222), 23, 10);
                StreamGeometry leftEar = new StreamGeometry();
                using (StreamGeometryContext g = leftEar.Open()) {
                    g.BeginFigure(new Point(42, 91), true, true); g.LineTo(new Point(42, 40), true, false); g.LineTo(new Point(80, 63), true, false);
                }
                StreamGeometry rightEar = new StreamGeometry();
                using (StreamGeometryContext g = rightEar.Open()) {
                    g.BeginFigure(new Point(112, 63), true, true); g.LineTo(new Point(149, 40), true, false); g.LineTo(new Point(148, 91), true, false);
                }
                dc.DrawGeometry(fur, outline, leftEar); dc.DrawGeometry(fur, outline, rightEar);
                dc.DrawEllipse(fur, outline, new Point(95 + look, 103), 61, 48);
                dc.DrawEllipse(cream, null, new Point(95 + look, 118), 35, 26);
                double eyeHeight = 6 * blink;
                dc.DrawEllipse(ink, null, new Point(71 + look, 101), 4, eyeHeight);
                dc.DrawEllipse(ink, null, new Point(118 + look, 101), 4, eyeHeight);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(194, 114, 107)), null, new Point(95 + look, 115), 5, 3);
                dc.DrawLine(new Pen(ink, 2), new Point(95 + look, 119), new Point(95 + look, 124));
                dc.DrawEllipse(fur, outline, new Point(46, 177), 12, 23);
                double paw = slot == "click" || slot == "needs-user" ? 133 + phase * 12 : 177;
                dc.DrawEllipse(fur, outline, new Point(143, paw), 12, 23);
                if (slot == "recent-activity") {
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(129, 160, 160)), outline,
                        new Rect(49, 181, 93, 32), 5, 5);
                    dc.DrawEllipse(fur, outline, new Point(71 + phase * 3, 183), 10, 7);
                    dc.DrawEllipse(fur, outline, new Point(121 - phase * 3, 183), 10, 7);
                }
                if (slot == "ambient") {
                    dc.DrawEllipse(cream, null, new Point(163, 154 + phase * 8), 6, 6);
                }
            }
            RenderTargetBitmap result = new RenderTargetBitmap(190, 254, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual); result.Freeze(); return result;
        }

        private static ThemePackage CreatePublicDemoTheme()
        {
            ThemePackage theme = new ThemePackage { Name = "小麦 · 内置插画宠物", Actions = new List<ThemeAction>() };
            foreach (string slot in SupportedThemeSlots)
            {
                ThemeAction action = new ThemeAction { Slot = slot, FramesPerSecond = 12,
                    Loop = slot == "idle" || slot == "ambient" || slot == "recent-activity", Frames = new List<BitmapSource>(),
                    CanvasWidth = 190, CanvasHeight = 254 };
                for (int i = 0; i < 24; i++) action.Frames.Add(DrawPublicDemoFrame(slot, i));
                theme.Actions.Add(action);
            }
            return theme;
        }

        private static void ExportPublicDemoPhoto(string path)
        {
            JpegBitmapEncoder encoder = new JpegBitmapEncoder(); encoder.QualityLevel = 90;
            encoder.Frames.Add(BitmapFrame.Create(DrawPublicDemoFrame("idle", 0)));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }

        private static void ExportPublicDemoAssets(string root)
        {
            Directory.CreateDirectory(root);
            string photo = Path.Combine(root, "reference-cat.jpg");
            if (!File.Exists(photo)) ExportPublicDemoPhoto(photo);
            string assets = Path.Combine(root, "assets"); Directory.CreateDirectory(assets);
            foreach (string name in new string[] { "reviewed-reference-cat.png", "pending-model-reference-cat.png" })
            {
                string path = Path.Combine(assets, name);
                if (File.Exists(path)) continue;
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(DrawPublicDemoFrame("idle", 0)));
                using (FileStream stream = File.Create(path)) encoder.Save(stream);
            }
        }

        private static void ExportPublicDemoTheme(string path)
        {
            ThemePackage theme = CreatePublicDemoTheme(); List<object> actions = new List<object>();
            using (FileStream stream = File.Create(path))
            using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (ThemeAction action in theme.Actions) {
                    List<string> names = new List<string>();
                    for (int i = 0; i < action.Frames.Count; i++) {
                        string name = "frames/" + action.Slot + "/" + (i + 1).ToString("D4") + ".png"; names.Add(name);
                        System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry(name);
                        entry.LastWriteTime = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
                        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(action.Frames[i]));
                        using (MemoryStream png = new MemoryStream()) {
                            encoder.Save(png); png.Position = 0;
                            using (Stream output = entry.Open()) png.CopyTo(output);
                        }
                    }
                    actions.Add(new { slot = action.Slot, fps = action.FramesPerSecond, loop = action.Loop, frames = names });
                }
                System.IO.Compression.ZipArchiveEntry manifest = zip.CreateEntry("manifest.json");
                manifest.LastWriteTime = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
                using (StreamWriter writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                    writer.Write(new JavaScriptSerializer().Serialize(new { format = "little-orbit.theme", version = 1, name = theme.Name, review = "required", actions = actions }));
            }
        }
    }
}
