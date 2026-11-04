using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        private sealed class CodexSessionDisplay
        {
            public string Title, Context;
        }

        private static Dictionary<string, string> codexTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, string> codexTitlesByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        private static DateTime codexTitlesCheckedAt = DateTime.MinValue;
        private static string codexTitlesIndex;
        private static long codexTitlesLength = -1, codexTitlesWriteTicks = -1;

        private static string CleanCodexTitle(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            value = System.Text.RegularExpressions.Regex.Replace(value, @"[\p{Cc}\p{Cf}]+", " ").Trim();
            int[] elements = StringInfo.ParseCombiningCharacters(value);
            return elements.Length > 120 ? value.Substring(0, elements[120]) + "…" : value;
        }

        private static void RefreshCodexTitles()
        {
            string index = Path.Combine(Path.GetDirectoryName(CodexSessionsRoot()), "session_index.jsonl");
            if (index == codexTitlesIndex && DateTime.UtcNow - codexTitlesCheckedAt < TimeSpan.FromSeconds(3)) return;
            codexTitlesCheckedAt = DateTime.UtcNow;
            try {
                FileInfo info = new FileInfo(index);
                if (!info.Exists) {
                    codexTitles.Clear(); codexTitlesByHash.Clear(); codexTitlesIndex = index;
                    codexTitlesLength = codexTitlesWriteTicks = -1; return;
                }
                if (index == codexTitlesIndex && info.Length == codexTitlesLength && info.LastWriteTimeUtc.Ticks == codexTitlesWriteTicks) return;
                Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.Ordinal);
                JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 8 };
                // Local compatibility metadata only. Never scan user prompts or
                // task bodies to invent a title. Bound work even for large indexes.
                using (FileStream file = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    const long limit = 8L * 1024 * 1024;
                    bool tail = file.Length > limit;
                    if (tail) file.Seek(-limit, SeekOrigin.End);
                    using (StreamReader reader = new StreamReader(file, Encoding.UTF8, true)) {
                        if (tail) reader.ReadLine();
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            if (line.Length < 2 || line.Length > 32768) continue;
                            try {
                                Dictionary<string, object> entry = json.DeserializeObject(line) as Dictionary<string, object>;
                                string id = ReadString(entry, "id"); Guid parsed;
                                if (!Guid.TryParse(id, out parsed) || !String.Equals(id, parsed.ToString("D"), StringComparison.OrdinalIgnoreCase)) continue;
                                if (titles.Count >= 20000 && !titles.ContainsKey(id.ToLowerInvariant())) continue;
                                titles[id.ToLowerInvariant()] = CleanCodexTitle(ReadString(entry, "thread_name"));
                            } catch (ArgumentException) { } catch (InvalidOperationException) { }
                        }
                    }
                }
                Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in titles) if (pair.Value != null) {
                    hashes[DesktopSessionHash(pair.Key)] = pair.Value;
                    hashes[Sha256Text(pair.Key)] = pair.Value;
                }
                codexTitles = titles; codexTitlesByHash = hashes; codexTitlesIndex = index;
                codexTitlesLength = info.Length; codexTitlesWriteTicks = info.LastWriteTimeUtc.Ticks;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static string CodexTitleForHash(string session)
        {
            RefreshCodexTitles(); string title;
            return session != null && codexTitlesByHash.TryGetValue(session, out title) ? title : null;
        }

        private static CodexSessionDisplay DescribeCodexSession(string id, string cwd, DateTime modified)
        {
            RefreshCodexTitles(); string title;
            if (!codexTitles.TryGetValue(id, out title) || title == null) title = "未命名会话";
            string project = Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (String.IsNullOrEmpty(project)) project = cwd;
            return new CodexSessionDisplay { Title = title, Context = project + " · " + modified.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) };
        }

        private static void TestCodexSessionTitles(string output)
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME"), data = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR");
            if (String.IsNullOrEmpty(home) || String.IsNullOrEmpty(data) || !Path.GetFullPath(home).StartsWith(Path.GetFullPath(data).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Title QA requires an isolated Codex home inside test data.");
            if (Directory.Exists(output) || Directory.Exists(home)) throw new InvalidDataException("Refusing to overwrite title fixtures.");
            Directory.CreateDirectory(output); Directory.CreateDirectory(CodexSessionsRoot());
            string one = "11111111-1111-4111-8111-111111111111", two = "22222222-2222-4222-8222-222222222222", three = "33333333-3333-4333-8333-333333333333";
            JavaScriptSerializer json = new JavaScriptSerializer();
            string index = Path.Combine(home, "session_index.jsonl");
            File.WriteAllLines(index, new [] {
                json.Serialize(new { id = one, thread_name = "旧名称" }), "invalid metadata",
                json.Serialize(new { id = one, thread_name = "测试宠物 🐱" }),
                json.Serialize(new { id = two, thread_name = "测试宠物 🐱" }), "{\"id\":" }, Encoding.UTF8);
            string[] ids = {one, two, three};
            foreach (string id in ids) {
                string path = Path.Combine(CodexSessionsRoot(), "rollout-test-" + id + ".jsonl");
                string cwd = Path.Combine(data, id == one ? "project-a" : "project-b");
                File.WriteAllText(path, json.Serialize(new { type = "session_meta", payload = new { id = id, cwd = cwd, originator = "Codex Desktop" } }) +
                    "\nPRIVATE-TASK-BODY-MUST-NOT-BE-A-TITLE\n", new UTF8Encoding(false));
            }
            System.Windows.Application app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            activeTheme = CreatePublicDemoTheme(); BuildTray();
            BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            PopulateCodexSessionMenu();
            System.Windows.Forms.ToolStripMenuItem desktop = (System.Windows.Forms.ToolStripMenuItem)codexSessionItem.DropDownItems[2];
            bool titles = desktop.DropDownItems.Count == 3, distinguish = false, fallback = false;
            string context = null;
            foreach (System.Windows.Forms.ToolStripMenuItem menu in desktop.DropDownItems) {
                CodexSessionDisplay display = menu.Tag as CodexSessionDisplay;
                titles &= display != null && !menu.Text.Contains("PRIVATE-") && !menu.Text.Contains("11111111") && !menu.Text.Contains("22222222");
                if (display == null) continue;
                if (display.Title == "未命名会话") fallback = true;
                else {
                    titles &= display.Title == "测试宠物 🐱";
                    if (context == null) context = display.Context;
                    else distinguish = context != display.Context;
                    if (display.Context.StartsWith("project-b", StringComparison.Ordinal)) menu.PerformClick();
                }
            }
            SaveAiConnection("codex", selectedCodexSession);
            bool binding = connectedAiSession == DesktopSessionHash(two) && selectedDesktopRolloutPath.EndsWith(two + ".jsonl", StringComparison.Ordinal);
            bool status = FriendlyConnectionStatus().Contains("测试宠物 🐱") && !FriendlyConnectionStatus().Contains(connectedAiSession);
            byte[] evidence = Encoding.UTF8.GetBytes("{}");
            WriteDesktopLifecycleEvent("DesktopToolActivity", CodexEvidenceJson("desktop-rollout", evidence, 0, evidence.Length, 0, one)); PopulateCodexSessionMenu();
            bool noDuplicate = codexSessionItem.DropDownItems.Count == 4;
            ShowPetManager(1);
            string configBefore = FileSha256Hex(aiConfigurationPath);
            System.Windows.Controls.Button pinFirst = null;
            Action<System.Windows.DependencyObject> expand = null;
            expand = delegate(System.Windows.DependencyObject item) {
                System.Windows.Controls.Expander list = item as System.Windows.Controls.Expander;
                if (list != null && list.Header as string == "固定一个 Codex 会话") list.IsExpanded = true;
                var button = item as System.Windows.Controls.Button;
                if (button != null && button.Content is System.Windows.Controls.StackPanel) {
                    var label = (System.Windows.Controls.StackPanel)button.Content;
                    if (label.Children.Count == 2 && label.Children[1] is System.Windows.Controls.TextBlock &&
                        ((System.Windows.Controls.TextBlock)label.Children[1]).Text.StartsWith("project-a", StringComparison.Ordinal))
                        pinFirst = button;
                }
                foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(item)) if (child is System.Windows.DependencyObject) expand((System.Windows.DependencyObject)child);
            }; expand(petManagerTabs);
            CaptureAppearanceContent(petManager, Path.Combine(output, "session-titles.png"));
            bool browsePreservesBinding = configBefore == FileSha256Hex(aiConfigurationPath);
            if (pinFirst != null) pinFirst.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            bool pinFromList = pinFirst != null && connectedAiSession == DesktopSessionHash(one) && codexFollowMode == "manual";
            File.AppendAllText(index, "\n" + json.Serialize(new { id = one, thread_name = "新的会话标题" }) + "\n", Encoding.UTF8);
            codexTitlesCheckedAt = DateTime.MinValue;
            bool renamed = FriendlyConnectionStatus().Contains("新的会话标题");
            File.Delete(index); codexTitlesCheckedAt = DateTime.MinValue;
            bool missingIndex = FriendlyConnectionStatus().Contains("未命名会话") && !FriendlyConnectionStatus().Contains(connectedAiSession);
            bool passed = titles && distinguish && fallback && binding && status && renamed && missingIndex && noDuplicate && browsePreservesBinding && pinFromList;
            File.WriteAllText(Path.Combine(output, "result.json"), json.Serialize(new { passed = passed, titles = titles, duplicateNamesDistinguished = distinguish,
                unnamedFallback = fallback, exactBinding = binding, connectedTitle = status, renameRefresh = renamed, missingIndex = missingIndex, duplicateDesktopEventsHidden = noDuplicate,
                browsePreservesBinding = browsePreservesBinding, pinFromList = pinFromList }));
            ExitApplication();
            if (!passed) throw new InvalidOperationException("Codex title checks failed.");
        }
    }
}
