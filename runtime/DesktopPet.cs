using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Drawing;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfApplication = System.Windows.Application;
using WpfImage = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        private static Window window;
        private static Forms.NotifyIcon trayIcon;
        private static Forms.ContextMenuStrip trayMenu;
        private static Forms.ToolStripMenuItem visibilityItem;
        private static Forms.ToolStripMenuItem rollbackThemeItem;
        private static Forms.ToolStripMenuItem exportThemeItem;
        private static Forms.ToolStripMenuItem removeThemeItem;
        private static Forms.ToolStripMenuItem reviewSavedThemeItem;
        private static Forms.ToolStripMenuItem rollbackRoleItem;
        private static Forms.ToolStripMenuItem pendingRoleItem;
        private static Forms.ToolStripMenuItem moveScreenItem;
        private static Forms.ToolStripMenuItem codexSessionItem;
        private static string preferredScreenName;
        private static TextBlock greeting;
        private static Border photoFrame;
        private static BitmapImage petBitmap;
        private static WpfImage themeImage;
        private static RotateTransform bundledPreviewRotation;
        private static ScaleTransform bundledPreviewScale;
        private static TranslateTransform bundledPreviewOffset;
        private static Border permissionCue;
        private static DispatcherTimer permissionCueTimer;
        private static string lastBundledPreviewGesture;
        private static DispatcherTimer themeTimer;
        private static DispatcherTimer codexEventTimer;
        private static DispatcherTimer automaticActionTimer;
        private static string codexEventDirectory;
        private static string selectedCodexSession;
        private static string selectedDesktopRolloutPath;
        private static string selectedDesktopHookSession;
        private static string lastCodexActionEventId;
        private static string lastCodexActionSession;
        private static object lastCodexActionEvidence;
        private static string desktopSelectionFilePath;
        private static string desktopWatchStatusFilePath;
        private static long selectedDesktopRolloutOffset;
        private static long lastDesktopObservedAtMs;
        private static long lastDesktopLifecycleEventAtMs;
        private static long lastDesktopRecentActivityAtMs;
        private static long lastDesktopToolActivityAtMs;
        private static long lastDesktopWatchStatusAtMs;
        private static long lastReportedCodexActionAtMs;
        private static string lastDesktopWatchStatus;
        private static long lastCodexActionAtMs;
        private static string lastCodexActionSource;
        private static string lastCodexRequestedSlot;
        private static string lastCodexPlayedSlot;
        private static string lastCodexActionOutcome;
        private static bool skippingDesktopOversizeLine;
        private static string lastCodexEventId;
        private static long selectedCodexAfterMs;
        private static readonly HashSet<string> consumedCodexEventIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> consumedCodexEventOrder = new Queue<string>();
        private static RoleCard activeRole;
        private static RoleCard pendingRole;
        private static ThemePackage activeTheme;
        private static bool bundledMotionPreview;
        private const string ReviewedReferenceThemeSha256 = "63e9cbe074b53c818388229d9c1b5323dee597c20788b27d0f077b89ecc9c5dd";
        private const string ReferencePhotoSha256 = "112378a4bc93c8ae87aa71932f2309ddc41b6f3e3906c1e1e43701803409d209";
        private static ThemeAction activeThemeAction;
        private static int themeFrameIndex;
        private static System.Diagnostics.Stopwatch themeActionClock;
        private static bool activeThemeOneShot;
        private static bool themePointerPressed;
        private static Point themePointerDown;
        private static bool photoPointerPressed;
        private static Point photoPointerDown;
        private static readonly string[] SupportedThemeSlots = new string[] {
            "idle", "ambient", "recent-activity", "needs-user", "stop-unknown", "interrupt", "hover", "click" };
        private static string activeRoleFilePath;
        private static string previousRoleFilePath;
        private static string pendingRoleFilePath;
        private static string activeThemeFilePath;
        private static string previousThemeFilePath;
        private static string themeReviewRenderPath;
        private static string themeReviewRenderSlot;
        private static int themeReviewRenderFrame;
        private static string roleReviewRenderPath;
        private static string preferencesFilePath;
        private static string placementFilePath;
        private static string reviewLedgerFilePath;
        private static string preferencesRenderPath;
        private static LocalPreferences preferences = new LocalPreferences();
        private static SavedPlacement savedPlacement;
        private static bool exiting;
        private static bool persistRuntimePlacement;
        private static bool runtimeSpriteOnly;
        private static bool waitingForTransparentTheme;
        private static readonly Random photoGreetingRandom = new Random();
        private static DateTime lastPhotoInteractionUtc = DateTime.MinValue;
        private static string lastPhotoGreeting = "";
        private static DateTime lastThemeInteractionUtc = DateTime.MinValue;
        private static DateTime lastAutomaticActionUtc = DateTime.UtcNow;
        private static int themeInteractionSequence;

        private sealed class RoleCard
        {
            public string Name;
            public string Mode;
            public string PhotoName;
            public string PhotoData;
            public BitmapImage PhotoBitmap;
            public string[] SourceEvidence;
            public List<ReferencePhoto> ReferencePhotos = new List<ReferencePhoto>();
            public string SubjectCountReview;
            public string OcclusionReview;
        }

        private sealed class ReferencePhoto
        {
            public string PhotoName;
            public string PhotoData;
            public BitmapImage PhotoBitmap;
        }

        private sealed class LocalPreferences
        {
            public bool InteractionEnabled = true;
            public bool AutomaticActionsEnabled;
            public bool QuietEnabled;
            public string QuietStart = "22:00";
            public string QuietEnd = "08:00";
            public int FrequencyMinutes = 15;
        }

        private sealed class SavedPlacement
        {
            public string ScreenName;
            public double Left;
            public double Top;
        }

        private sealed class ThemeAction
        {
            public string Slot;
            public int FramesPerSecond;
            public bool Loop;
            public List<BitmapSource> Frames;
            public int CanvasWidth;
            public int CanvasHeight;
        }

        private sealed class ThemePackage
        {
            public string Name;
            public List<ThemeAction> Actions;
            // Playback-only tuning for the already reviewed reference cat. Other
            // imported themes keep their authored timing and original pixels.
            public bool CompactMotion;

            public ThemeAction FindAction(string slot)
            {
                for (int i = 0; i < Actions.Count; i++)
                    if (Actions[i].Slot == slot) return Actions[i];
                return null;
            }

            public ThemeAction IdleAction
            {
                get { return FindAction("idle"); }
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // Off-screen render/test helpers intentionally use invisible coordinates.
            // Interactive playback and explicit visible reviews recover their position.
            displayRecoveryEnabled = args.Length == 0 || args[0] == "--settings" || args[0] == "--test-display-compatibility" ||
                Array.IndexOf(args, "--visible-review") >= 0;
            if (args.Length == 1 && args[0] == "--ai-codex-hook") {
                try {
                    string hookData = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR");
                    if (String.IsNullOrEmpty(hookData)) hookData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Pet Studio");
                    if (!Path.IsPathRooted(hookData)) return;
                    InitializeSimpleExperience(Path.GetFullPath(hookData)); RecordSimpleCodexHook();
                } catch (Exception) { }
                return;
            }
            if (args.Length == 2 && args[0] == "--export-demo-photo")
            { ExportPublicDemoPhoto(Path.GetFullPath(args[1])); return; }
            if (args.Length == 2 && args[0] == "--export-demo-assets")
            { ExportPublicDemoAssets(Path.GetFullPath(args[1])); return; }
            if (args.Length == 2 && args[0] == "--export-demo-theme")
            { ExportPublicDemoTheme(Path.GetFullPath(args[1])); return; }
            if (args.Length >= 2 && args[0] == "--codex-hook-relay")
            {
                try { RecordCodexHookFromStandardInput(args[1], args.Length >= 3 ? args[2] : null); }
                catch { /* A pet must never interrupt a Codex hook or affect an approval decision. */ }
                return;
            }
            string photoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg");
            if (!File.Exists(photoPath))
            {
                MessageBox.Show("找不到本地参考照片：" + photoPath, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string defaultDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Pet Studio");
            string dataDirectoryOverride = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR");
            if (!String.IsNullOrEmpty(dataDirectoryOverride) && !Path.IsPathRooted(dataDirectoryOverride))
                throw new InvalidDataException("AI_PET_DATA_DIR 必须是绝对路径。");
            string dataRoot = String.IsNullOrEmpty(dataDirectoryOverride) ? defaultDataRoot : Path.GetFullPath(dataDirectoryOverride);
            activeRoleFilePath = Path.Combine(dataRoot, "active-role.little-orbit.json");
            previousRoleFilePath = Path.Combine(dataRoot, "previous-role.little-orbit.json");
            pendingRoleFilePath = Path.Combine(dataRoot, "pending-role.little-orbit.json");
            activeThemeFilePath = Path.Combine(dataRoot, "active-theme.lotheme");
            previousThemeFilePath = Path.Combine(dataRoot, "previous-theme.lotheme");
            preferencesFilePath = Path.Combine(dataRoot, "preferences.json");
            desktopSelectionFilePath = Path.Combine(dataRoot, "codex-desktop-selection.json");
            desktopWatchStatusFilePath = Path.Combine(dataRoot, "codex-desktop-watch-status.json");
            placementFilePath = Path.Combine(dataRoot, "window-placement.json");
            string eventDirectoryOverride = Environment.GetEnvironmentVariable("AI_PET_EVENT_DIR");
            codexEventDirectory = !String.IsNullOrEmpty(eventDirectoryOverride) && Path.IsPathRooted(eventDirectoryOverride)
                ? Path.GetFullPath(eventDirectoryOverride)
                : Path.Combine(String.IsNullOrEmpty(dataDirectoryOverride) ? AppDomain.CurrentDomain.BaseDirectory : dataRoot, "codex-events");
            bool importThemeAtLaunch = args.Length == 2 && args[0] == "--import-theme";
            bool rollbackThemeAtLaunch = args.Length == 1 && args[0] == "--rollback-theme";
            bool settingsAtLaunch = args.Length == 1 && args[0] == "--settings";
            bool normalLaunch = args.Length == 0 || settingsAtLaunch || importThemeAtLaunch || rollbackThemeAtLaunch;
            persistRuntimePlacement = normalLaunch;
            string reviewLedgerOverride = Environment.GetEnvironmentVariable("AI_PET_REVIEW_LEDGER");
            if (!String.IsNullOrEmpty(reviewLedgerOverride) && !Path.IsPathRooted(reviewLedgerOverride))
                throw new InvalidDataException("AI_PET_REVIEW_LEDGER 必须是绝对路径。");
            reviewLedgerFilePath = String.IsNullOrEmpty(reviewLedgerOverride)
                ? Path.Combine(dataRoot, "candidate-review-ledger.json")
                : Path.GetFullPath(reviewLedgerOverride);
            InitializeSimpleExperience(dataRoot);
            if (HandleAgentCommand(args)) return;
            if (normalLaunch && File.Exists(activeRoleFilePath))
            {
                try { activeRole = ParseRoleCard(File.ReadAllText(activeRoleFilePath, Encoding.UTF8)); }
                catch { activeRole = null; }
            }
            if (normalLaunch && File.Exists(pendingRoleFilePath))
            {
                try { pendingRole = ParseRoleCard(ReadBoundedJson(pendingRoleFilePath)); }
                catch { pendingRole = null; }
            }
            if (normalLaunch && File.Exists(activeThemeFilePath))
            {
                try
                {
                    if (ThemeMatchesCurrentRole())
                    {
                        EnsureThemeNotRejected(activeThemeFilePath);
                        activeTheme = ParseThemePackage(activeThemeFilePath);
                    }
                }
                catch { activeTheme = null; }
            }
            if (normalLaunch && File.Exists(preferencesFilePath))
            {
                try { preferences = ParsePreferences(File.ReadAllText(preferencesFilePath, new UTF8Encoding(false, true))); }
                catch { preferences = new LocalPreferences(); }
            }
            if (normalLaunch && File.Exists(placementFilePath))
            {
                try { savedPlacement = ReadPlacementAt(placementFilePath); }
                catch { savedPlacement = null; }
            }
            if (normalLaunch) RestoreDesktopSelection();
            runtimeSpriteOnly = normalLaunch;
            if (args.Length >= 2 && args[0] == "--render-floating-preview")
                TryActivateBundledPreview(photoPath);

            if (args.Length == 4 && args[0] == "--test-reviewed-default")
            {
                string root = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                reviewLedgerFilePath = Path.Combine(root, "candidate-review-ledger.json");
                activeThemeFilePath = Path.Combine(root, "active-theme.lotheme");
                activeRole = null;
                activeTheme = null;
                bool activated = TryActivateReviewedReferenceTheme(photoPath);
                int frames = 0;
                if (activeTheme != null) foreach (ThemeAction action in activeTheme.Actions) frames += action.Frames.Count;
                bool bound = activated && ThemeMatchesCurrentRole() && frames == 433 && !bundledMotionPreview;
                activeTheme = null;
                bool preserved = !TryActivateReviewedReferenceTheme(photoPath) && FileSha256Hex(activeThemeFilePath) == ReviewedReferenceThemeSha256;
                activeThemeFilePath = Path.Combine(root, "invalid-existing.lotheme");
                File.WriteAllText(activeThemeFilePath, "preserve invalid user asset");
                bool invalidPreserved = !TryActivateReviewedReferenceTheme(photoPath) && File.ReadAllText(activeThemeFilePath) == "preserve invalid user asset";
                activeThemeFilePath = Path.Combine(root, "dog-default.lotheme");
                activeRole = ParseRoleCard(ReadBoundedJson(args[2]));
                string dogKey = RolePhotoKey(activeRole);
                bool dogProtected = !TryActivateReviewedReferenceTheme(photoPath) && !File.Exists(activeThemeFilePath) && RolePhotoKey(activeRole) == dogKey;
                activeRole = null;
                string tampered = Path.Combine(root, "tampered.lotheme");
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reviewed-reference-cat.lotheme"), tampered);
                File.AppendAllText(tampered, "changed");
                bool tamperBlocked = !TryActivateReviewedReferenceTheme(photoPath, tampered) && !File.Exists(activeThemeFilePath);
                string rejection = "{\"format\":\"little-orbit.review-veto\",\"version\":1,\"denied\":[{\"sha256\":\"" +
                    ReviewedReferenceThemeSha256 + "\",\"candidateId\":\"" + ReviewedReferenceThemeSha256.Substring(0, 32) +
                    "\",\"reason\":\"motion\",\"decidedAt\":\"" + DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}]}";
                File.WriteAllText(reviewLedgerFilePath, rejection);
                bool vetoBlocked = !TryActivateReviewedReferenceTheme(photoPath) && !File.Exists(activeThemeFilePath) && File.ReadAllText(reviewLedgerFilePath) == rejection;
                Dictionary<string, object> report = new Dictionary<string, object>();
                report["activatedAndBound"] = bound;
                report["frames"] = frames;
                report["existingPreserved"] = preserved && invalidPreserved;
                report["otherPetProtected"] = dogProtected;
                report["tamperBlocked"] = tamperBlocked;
                report["vetoPreservedAndBlocked"] = vetoBlocked;
                report["isolatedRoot"] = root;
                report["visualApproval"] = "not asserted by this structural test";
                File.WriteAllText(Path.GetFullPath(args[3]), new JavaScriptSerializer().Serialize(report));
                if (!bound || !preserved || !invalidPreserved || !dogProtected || !tamperBlocked || !vetoBlocked)
                    throw new InvalidOperationException("Reviewed default activation regression failed.");
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-no-photo-fallback")
            {
                runtimeSpriteOnly = true;
                activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                string originalPhotoKey = RolePhotoKey(activeRole);
                activeTheme = null;
                bundledMotionPreview = false;
                WpfApplication fallbackApp = new WpfApplication();
                fallbackApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                ShowWindow();
                bool trayOnly = waitingForTransparentTheme && !window.IsVisible &&
                    photoFrame == null && greeting == null && window.Content is Grid &&
                    ((Grid)window.Content).Children.Count == 0 && trayIcon.Visible;
                bool clearLabel = visibilityItem.Text.IndexOf("透明主题", StringComparison.Ordinal) >= 0;
                TryActivateBundledPreview(photoPath);
                RebuildRuntimeWindow();
                bool sameCat = SameReferencePhoto(activeRole, photoPath);
                bool correctIdentity = sameCat ? bundledMotionPreview && window.IsVisible && !waitingForTransparentTheme
                    : activeTheme == null && waitingForTransparentTheme && !window.IsVisible;
                bool preserved = RolePhotoKey(activeRole) == originalPhotoKey;
                bool passed = trayOnly && clearLabel && correctIdentity && preserved;
                File.WriteAllText(Path.GetFullPath(args[2]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                    ",\"trayOnlyWithoutTheme\":" + trayOnly.ToString().ToLowerInvariant() +
                    ",\"clearLabel\":" + clearLabel.ToString().ToLowerInvariant() +
                    ",\"correctIdentity\":" + correctIdentity.ToString().ToLowerInvariant() +
                    ",\"rolePreserved\":" + preserved.ToString().ToLowerInvariant() + "}");
                ExitApplication();
                if (!passed) throw new InvalidOperationException("Missing transparent theme must not produce a photo card or substitute pet.");
                return;
            }
            if (args.Length >= 2 && args[0] == "--render-floating-preview")
            {
                if (!bundledMotionPreview) throw new InvalidDataException("Bundled transparent motion preview is unavailable.");
                WpfApplication previewApp = new WpfApplication();
                previewApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                RenderPreview(Path.GetFullPath(args[1]));
                ExitApplication();
                return;
            }

            if (args.Length >= 4 && args[0] == "--render-floating-gesture")
            {
                int captureDelayMs = args[1] == "interrupt" ? 140 : 300;
                if (args.Length >= 5 && (!Int32.TryParse(args[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out captureDelayMs) || captureDelayMs < 50 || captureDelayMs > 5000))
                    throw new InvalidDataException("Floating gesture capture delay must be 50–5000 ms.");
                string previewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundled-cat-motion-preview.lotheme");
                activeTheme = ParseThemePackage(previewPath);
                bundledMotionPreview = true;
                WpfApplication gestureApp = new WpfApplication();
                gestureApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                gestureApp.MainWindow = window;
                window.Show();
                window.Left = -10000;
                window.Top = -10000;
                if (!PlayBundledPreviewGesture(args[1])) throw new InvalidDataException("Unsupported floating preview gesture.");
                DispatcherTimer capture = new DispatcherTimer();
                capture.Interval = TimeSpan.FromMilliseconds(captureDelayMs);
                capture.Tick += delegate
                {
                    capture.Stop();
                    RenderPreview(Path.GetFullPath(args[2]));
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":true,\"gesture\":\"" +
                        EscapeJson(lastBundledPreviewGesture) + "\",\"transparent\":true}");
                    ExitApplication();
                };
                capture.Start();
                gestureApp.Run();
                return;
            }

            if (args.Length >= 3 && args[0] == "--render-preferences")
            {
                try
                {
                    WpfApplication preferencesApp = new WpfApplication();
                    preferencesApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildTray();
                    BuildWindow(photoPath);
                    preferencesRenderPath = Path.GetFullPath(args[1]);
                    ShowPreferences();
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"rendered\":true,\"saved\":false,\"size\":\"470x550\"}");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"rendered\":false,\"saved\":false,\"error\":\"invalid-preferences-view\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-preferences")
            {
                try
                {
                    LocalPreferences parsed = ParsePreferences(File.ReadAllText(args[1], new UTF8Encoding(false, true)));
                    PersistPreferencesAt(args[2], parsed);
                    LocalPreferences reopened = ParsePreferences(File.ReadAllText(args[2], new UTF8Encoding(false, true)));
                    string portablePath = args[2] + ".portable.json";
                    File.WriteAllText(portablePath, PortablePreferencesJson(reopened), new UTF8Encoding(false));
                    LocalPreferences portable = ParsePreferences(File.ReadAllText(portablePath, new UTF8Encoding(false, true)));
                    bool portableRoundTrip = portable.QuietStart == reopened.QuietStart && portable.QuietEnd == reopened.QuietEnd &&
                        portable.FrequencyMinutes == reopened.FrequencyMinutes && portable.InteractionEnabled == reopened.InteractionEnabled &&
                        portable.QuietEnabled == reopened.QuietEnabled &&
                        portable.AutomaticActionsEnabled == reopened.AutomaticActionsEnabled;
                    bool quietAtStart = IsWithinQuietHours(reopened, new TimeSpan(23, 15, 0));
                    bool quietBeforeEnd = IsWithinQuietHours(reopened, new TimeSpan(7, 44, 0));
                    bool quietAtEnd = IsWithinQuietHours(reopened, new TimeSpan(7, 45, 0));
                    bool automaticActionsPersisted = reopened.AutomaticActionsEnabled == parsed.AutomaticActionsEnabled &&
                        portable.AutomaticActionsEnabled == reopened.AutomaticActionsEnabled;
                    bool passed = !reopened.InteractionEnabled && reopened.QuietEnabled && reopened.QuietStart == "23:15" &&
                        reopened.QuietEnd == "07:45" && reopened.FrequencyMinutes == 25 && portableRoundTrip && IsWithinQuietHours(reopened, new TimeSpan(1, 0, 0)) &&
                        automaticActionsPersisted && quietAtStart && quietBeforeEnd && !quietAtEnd;
                    File.WriteAllText(Path.GetFullPath(args[2] + ".results.json"), "{\"persisted\":" + passed.ToString().ToLowerInvariant() +
                        ",\"interactionEnabled\":" + reopened.InteractionEnabled.ToString().ToLowerInvariant() +
                        ",\"automaticActionsEnabled\":" + reopened.AutomaticActionsEnabled.ToString().ToLowerInvariant() +
                        ",\"automaticActionsPersisted\":" + automaticActionsPersisted.ToString().ToLowerInvariant() +
                        ",\"quietHours\":\"" + EscapeJson(reopened.QuietStart) + "-" + EscapeJson(reopened.QuietEnd) +
                        "\",\"quietAtStart\":" + quietAtStart.ToString().ToLowerInvariant() +
                        ",\"quietBeforeEnd\":" + quietBeforeEnd.ToString().ToLowerInvariant() +
                        ",\"quietAtEnd\":" + quietAtEnd.ToString().ToLowerInvariant() +
                        ",\"frequencyMinutes\":" + reopened.FrequencyMinutes +
                        ",\"portableRoundTrip\":" + portableRoundTrip.ToString().ToLowerInvariant() + "}");
                    if (!passed) throw new InvalidDataException("Native preferences did not survive save/reload or overnight quiet-hour evaluation.");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[2] + ".results.json"), "{\"persisted\":false,\"error\":\"invalid-preferences\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 5 && args[0] == "--test-theme-rollback")
            {
                try
                {
                    string testRoot = Path.GetFullPath(args[3]);
                    Directory.CreateDirectory(testRoot);
                    activeThemeFilePath = Path.Combine(testRoot, "active-theme.lotheme");
                    previousThemeFilePath = Path.Combine(testRoot, "previous-theme.lotheme");
                    if (File.Exists(activeThemeFilePath)) File.Delete(activeThemeFilePath);
                    if (File.Exists(previousThemeFilePath)) File.Delete(previousThemeFilePath);
                    File.Copy(args[1], activeThemeFilePath);
                    ThemePackage first = ParseThemePackage(activeThemeFilePath);
                    string replacement = activeThemeFilePath + ".replacement";
                    File.Copy(args[2], replacement, true);
                    PersistThemeFile(replacement);
                    ThemePackage installed = ParseThemePackage(activeThemeFilePath);
                    ThemePackage savedPrevious = ParseThemePackage(previousThemeFilePath);
                    bool previousPreserved = savedPrevious.Name == first.Name;
                    string rollbackStage = activeThemeFilePath + ".rollback.importing";
                    File.Copy(previousThemeFilePath, rollbackStage, true);
                    ThemePackage rollbackCandidate = ParseThemePackage(rollbackStage);
                    PersistThemeFile(rollbackStage);
                    ThemePackage restored = ParseThemePackage(activeThemeFilePath);
                    ThemePackage swappedPrevious = ParseThemePackage(previousThemeFilePath);
                    bool rolledBack = restored.Name == first.Name && swappedPrevious.Name == installed.Name;
                    File.WriteAllText(Path.GetFullPath(args[4]), "{\"installed\":" + (installed.Name != first.Name).ToString().ToLowerInvariant() +
                        ",\"previousPreserved\":" + previousPreserved.ToString().ToLowerInvariant() +
                        ",\"rolledBack\":" + rolledBack.ToString().ToLowerInvariant() +
                        ",\"active\":\"" + EscapeJson(restored.Name) + "\",\"previous\":\"" + EscapeJson(swappedPrevious.Name) + "\"}");
                    if (!previousPreserved || !rolledBack || rollbackCandidate.Name != first.Name)
                        throw new InvalidDataException("The previous theme was not preserved and restored correctly.");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[4]), "{\"installed\":false,\"previousPreserved\":false,\"rolledBack\":false,\"error\":\"theme-rollback-failed\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 4 && args[0] == "--test-theme-export")
            {
                try
                {
                    reviewLedgerFilePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2])),
                        "theme-export-test-empty-ledger-" + Guid.NewGuid().ToString("N") + ".json");
                    string digest = ExportThemePackageFile(args[1], args[2]);
                    string copiedDigest = FileSha256Hex(args[2]);
                    bool passed = digest == copiedDigest && new FileInfo(args[1]).Length == new FileInfo(args[2]).Length;
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                        ",\"sameBytes\":" + passed.ToString().ToLowerInvariant() +
                        ",\"sha256\":\"" + digest + "\"}");
                    if (!passed) throw new InvalidDataException("Exported theme bytes did not match the source.");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"passed\":false,\"sameBytes\":false,\"error\":\"theme-export-failed\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }

            if (args.Length >= 4 && args[0] == "--preview-import")
            {
                try
                {
                    activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                    WpfApplication importApp = new WpfApplication();
                    importApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildWindow(photoPath);
                    RenderPreview(Path.GetFullPath(args[2]));
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"imported\":true,\"name\":\"" + EscapeJson(activeRole.Name)
                        + "\",\"mode\":\"" + EscapeJson(activeRole.Mode) + "\",\"photoDecoded\":true,\"photoPixels\":\""
                        + petBitmap.PixelWidth + "x" + petBitmap.PixelHeight + "\",\"sampleFramingWarning\":"
                        + ((photoFrame.ToolTip as string).IndexOf("示例猫身", StringComparison.Ordinal) >= 0).ToString().ToLowerInvariant()
                        + ",\"fullSilhouetteClaimed\":"
                        + (Array.IndexOf(activeRole.SourceEvidence, "fullSilhouette") >= 0).ToString().ToLowerInvariant()
                        + ",\"referencePhotoCount\":" + activeRole.ReferencePhotos.Count
                        + ",\"subjectCountReview\":\"" + EscapeJson(activeRole.SubjectCountReview) + "\",\"occlusionReview\":\""
                        + EscapeJson(activeRole.OcclusionReview) + "\"}");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"imported\":false,\"error\":\"invalid-role-card\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 4 && args[0] == "--render-reference-role-preview")
            {
                try
                {
                    activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                    bool matched = SameReferencePhoto(activeRole, photoPath);
                    TryActivateBundledPreview(photoPath);
                    if (!matched || !bundledMotionPreview || activeTheme == null)
                        throw new InvalidDataException("角色照片未匹配参考猫动态预览：matched=" + matched +
                            " bundled=" + bundledMotionPreview + " role=" + activeRole.PhotoBitmap.PixelWidth + "x" +
                            activeRole.PhotoBitmap.PixelHeight + " reference=" + LoadReferenceBitmap(photoPath).PixelWidth + "x" +
                            LoadReferenceBitmap(photoPath).PixelHeight);
                    WpfApplication rolePreviewApp = new WpfApplication();
                    rolePreviewApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildTray();
                    BuildWindow(photoPath);
                    RenderPreview(Path.GetFullPath(args[2]));
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"roleMatchesReference\":true,\"transparentPreview\":" +
                        (photoFrame == null && bundledMotionPreview && window.Width == 214).ToString().ToLowerInvariant() +
                        ",\"whitePhotoCard\":false}");
                    ExitApplication();
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"roleMatchesReference\":false,\"transparentPreview\":false,\"error\":\"" + EscapeJson(error.Message) + "\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length == 3 && args[0] == "--test-motion-readability")
            {
                TestMotionReadability(args[1], args[2]);
                return;
            }
            if (args.Length == 3 && args[0] == "--test-pet-bubble")
            {
                TestPetBubble(args[1], args[2]);
                return;
            }
            if (args.Length >= 4 && args[0] == "--preview-theme")
            {
                try
                {
                    activeTheme = ParseThemePackage(args[1]);
                    if (activeTheme.IdleAction == null) throw new InvalidDataException("主题包必须包含 idle 静态待机动作。");
                    WpfApplication themeApp = new WpfApplication();
                    themeApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildWindow(photoPath);
                    RenderPreview(Path.GetFullPath(args[2]));
                    ThemeAction idle = activeTheme.IdleAction;
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"loaded\":true,\"name\":\"" + EscapeJson(activeTheme.Name)
                        + "\",\"slot\":\"idle\",\"frameCount\":" + idle.Frames.Count
                        + ",\"canvas\":\"" + idle.CanvasWidth + "x" + idle.CanvasHeight
                        + "\",\"previewPixels\":\"" + idle.Frames[0].PixelWidth + "x" + idle.Frames[0].PixelHeight
                        + "\",\"installed\":false,\"reviewRequired\":true}");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"loaded\":false,\"installed\":false,\"error\":\"invalid-theme-package\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--check-review-veto")
            {
                bool blocked = false;
                string reason = "";
                try
                {
                    ThemePackage checkedPackage = ParseThemePackage(args[1]);
                    if (checkedPackage.IdleAction == null) throw new InvalidDataException("主题包缺少 idle 动作。");
                    try { EnsureThemeNotRejected(args[1]); }
                    catch (InvalidDataException error) { blocked = true; reason = error.Message; }
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"valid\":true,\"blocked\":" + blocked.ToString().ToLowerInvariant() +
                        ",\"reason\":\"" + EscapeJson(reason) + "\"}");
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"valid\":false,\"blocked\":true,\"reason\":\"" + EscapeJson(error.Message) + "\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 4 && args[0] == "--render-theme-review")
            {
                try
                {
                    activeTheme = ParseThemePackage(args[1]);
                    if (activeTheme.IdleAction == null) throw new InvalidDataException("主题包必须包含 idle 静态待机动作。");
                    themeReviewRenderSlot = args.Length >= 5 ? args[4] : "idle";
                    themeReviewRenderFrame = args.Length >= 6
                        ? Int32.Parse(args[5], CultureInfo.InvariantCulture) : 0;
                    WpfApplication reviewApp = new WpfApplication();
                    reviewApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildWindow(photoPath);
                    themeReviewRenderPath = Path.GetFullPath(args[2]);
                    ReviewThemePackage(activeTheme);
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":true,\"installed\":false,\"name\":\""
                        + EscapeJson(activeTheme.Name) + "\",\"size\":\"650x600\",\"actionCount\":" + activeTheme.Actions.Count
                        + ",\"slot\":\"" + EscapeJson(themeReviewRenderSlot) + "\",\"frame\":"
                        + themeReviewRenderFrame.ToString(CultureInfo.InvariantCulture) + "}");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":false,\"installed\":false,\"error\":\"invalid-theme-review\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 5 && args[0] == "--render-theme-floating")
            {
                if (args.Length >= 7 && args[6] == "--visible-review" &&
                    (File.Exists(args[2]) || File.Exists(args[3]) || File.Exists(args[3] + ".interaction.jsonl")))
                {
                    Environment.ExitCode = 2;
                    return;
                }
                try
                {
                    activeTheme = ParseThemePackage(args[1]);
                    string slot = args[4];
                    if (activeTheme.FindAction(slot) == null)
                        throw new InvalidDataException("The requested floating action is absent.");
                    int delayMs = args.Length >= 6
                        ? Int32.Parse(args[5], CultureInfo.InvariantCulture) : 300;
                    bool visibleReview = args.Length >= 7 && args[6] == "--visible-review";
                    int maximumDelayMs = visibleReview ? 60000 : 5000;
                    if (delayMs < 50 || delayMs > maximumDelayMs)
                        throw new InvalidDataException("Floating capture delay is outside the bounded review duration.");
                    WpfApplication floatingApp = new WpfApplication();
                    floatingApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    if (visibleReview)
                    {
                        floatingReviewTracePath = Path.GetFullPath(args[3]) + ".interaction.jsonl";
                        // Refuse accidental loss of a previous review's evidence.
                        using (FileStream trace = new FileStream(floatingReviewTracePath, FileMode.CreateNew)) { }
                        floatingReviewClock = System.Diagnostics.Stopwatch.StartNew();
                    }
                    BuildTray();
                    BuildWindow(photoPath);
                    if (visibleReview)
                    {
                        trayMenu.Items.Clear();
                        trayMenu.Items.Add("关闭未审核预览", null, delegate { ExitApplication(); });
                        window.Title = "AI Pet · UNAPPROVED review";
                        // Make this bounded, never-installed review discoverable by
                        // accessibility/window pickers for real mouse interaction QA.
                        window.ShowInTaskbar = true;
                    }
                    floatingApp.MainWindow = window;
                    // Position and select the candidate before the first visible
                    // surface. Early desktop captures must not sample the old
                    // saved location (which may contain another pet window).
                    window.SourceInitialized += delegate
                    {
                        window.Left = visibleReview ? SystemParameters.WorkArea.Left + 100 : -10000;
                        window.Top = visibleReview ? SystemParameters.WorkArea.Top + 100 : -10000;
                    };
                    if (!SetThemeAction(slot, slot != "idle"))
                        throw new InvalidDataException("The requested floating action did not start.");
                    // WPF can spend hundreds of milliseconds creating its first
                    // surface. Keep frame zero still until that surface is drawn,
                    // so review playback does not skip the action's onset.
                    if (themeTimer != null) themeTimer.Stop();
                    themeActionClock.Stop();
                    bool floatingFirstFrameDrawn = false;
                    window.ContentRendered += delegate
                    {
                        if (floatingFirstFrameDrawn) return;
                        floatingFirstFrameDrawn = true;
                        themeActionClock.Restart();
                        if (themeTimer != null) themeTimer.Start();
                        TraceFloatingReview("first-visible-frame");
                    };
                    window.Show();
                    DispatcherTimer floatingCapture = new DispatcherTimer();
                    floatingCapture.Interval = TimeSpan.FromMilliseconds(delayMs);
                    floatingCapture.Tick += delegate
                    {
                        floatingCapture.Stop();
                        TraceFloatingReview("review-ending");
                        RenderPreview(Path.GetFullPath(args[2]));
                        string currentSlot = activeThemeAction == null ? "" : activeThemeAction.Slot;
                        File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":true,\"installed\":false,\"slot\":\""
                            + EscapeJson(slot) + "\",\"currentSlot\":\"" + EscapeJson(currentSlot)
                            + "\",\"frame\":" + themeFrameIndex.ToString(CultureInfo.InvariantCulture)
                            + ",\"actionElapsedMs\":" + (themeActionClock == null ? 0 : themeActionClock.ElapsedMilliseconds)
                            + ",\"visibleReview\":" + visibleReview.ToString().ToLowerInvariant()
                            + ",\"oneShotActive\":" + activeThemeOneShot.ToString().ToLowerInvariant()
                            + ",\"interactionTraceFailed\":" + floatingReviewTraceFailed.ToString().ToLowerInvariant()
                            + ",\"transparent\":true}");
                        ExitApplication();
                    };
                    floatingCapture.Start();
                    floatingApp.Run();
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":false,\"installed\":false,\"error\":\"invalid-floating-review\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-theme-playback-clock")
            {
                activeTheme = ParseThemePackage(args[1]);
                ThemeAction clockAction = activeTheme.IdleAction;
                // A long diagnostic sequence makes lost time measurable, even with a two-frame fixture.
                while (clockAction.Frames.Count < 60) clockAction.Frames.Add(clockAction.Frames[0]);
                clockAction.FramesPerSecond = 20;
                clockAction.Loop = true;
                WpfApplication clockApp = new WpfApplication();
                clockApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                window.Show();
                window.Left = -10000;
                window.Top = -10000;
                SetThemeAction("idle", false);
                DispatcherTimer stall = new DispatcherTimer();
                stall.Interval = TimeSpan.FromMilliseconds(100);
                stall.Tick += delegate { stall.Stop(); System.Threading.Thread.Sleep(450); };
                DispatcherTimer observe = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
                observe.Interval = TimeSpan.FromMilliseconds(850);
                observe.Tick += delegate
                {
                    observe.Stop();
                    long elapsed = themeActionClock.ElapsedMilliseconds;
                    int expected = (int)(elapsed * 20 / 1000) % clockAction.Frames.Count;
                    int difference = Math.Abs(themeFrameIndex - expected);
                    bool passed = difference <= 1 || difference == clockAction.Frames.Count - 1;
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"passed\":" + passed.ToString().ToLowerInvariant()
                        + ",\"stallMs\":450,\"elapsedMs\":" + elapsed + ",\"expectedFrame\":" + expected
                        + ",\"actualFrame\":" + themeFrameIndex + ",\"installed\":false}");
                    if (!passed) Environment.ExitCode = 2;
                    ExitApplication();
                };
                stall.Start();
                observe.Start();
                clockApp.Run();
                return;
            }
            if (args.Length >= 3 && args[0] == "--smoke-theme-playback")
            {
                activeTheme = ParseThemePackage(args[1]);
                ThemeAction idle = activeTheme.IdleAction;
                if (idle == null || idle.Frames.Count < 2 || !idle.Loop)
                    throw new InvalidDataException("Playback smoke test requires at least two looping idle frames.");
                WpfApplication playbackApp = new WpfApplication();
                playbackApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                playbackApp.MainWindow = window;
                window.Show();
                window.Left = -10000;
                window.Top = -10000;
                SetThemeAction("idle", false);
                int frameChanges = 0;
                int lastFrame = themeFrameIndex;
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                DispatcherTimer playbackObservation = new DispatcherTimer();
                playbackObservation.Interval = TimeSpan.FromMilliseconds(40);
                playbackObservation.Tick += delegate
                {
                    if (themeFrameIndex != lastFrame)
                    {
                        frameChanges++;
                        lastFrame = themeFrameIndex;
                    }
                    if (frameChanges >= 3 || DateTime.UtcNow >= deadline)
                    {
                        playbackObservation.Stop();
                        if (frameChanges >= 3)
                            File.WriteAllText(Path.GetFullPath(args[2]), "{\"loaded\":true,\"frameCount\":" + idle.Frames.Count
                                + ",\"timerTicks\":" + frameChanges + ",\"currentFrame\":" + themeFrameIndex + ",\"loop\":true}");
                        else Environment.ExitCode = 2;
                        ExitApplication();
                    }
                };
                playbackObservation.Start();
                playbackApp.Run();
                if (frameChanges < 3 || !File.Exists(args[2]))
                    throw new InvalidOperationException("The theme playback timer did not advance three frames.");
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-theme-interaction")
            {
                activeTheme = ParseThemePackage(args[1]);
                activeThemeFilePath = Path.GetFullPath(args[1]);
                if (activeTheme.IdleAction == null || activeTheme.FindAction("click") == null ||
                    activeTheme.FindAction("hover") == null || activeTheme.Actions.Count != SupportedThemeSlots.Length)
                    throw new InvalidDataException("Interaction check requires all eight fixed slots.");
                WpfApplication interactionApp = new WpfApplication();
                interactionApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                bool themeExportMenuEnabled = exportThemeItem != null && exportThemeItem.Enabled;
                BuildWindow(photoPath);
                interactionApp.MainWindow = window;
                window.Left = -10000;
                window.Top = -10000;
                window.Show();
                SetThemeAction("idle", false);
                bool nearBelowThreshold = !PointerDragThresholdExceeded(new Point(20, 20), new Point(21, 21));
                bool farExceedsThreshold = PointerDragThresholdExceeded(new Point(20, 20), new Point(40, 20));
                BeginThemePointer(new Point(20, 20));
                bool clickRecognized = CompleteThemePointer(new Point(21, 21));
                bool clickSelected = activeThemeAction != null && activeThemeAction.Slot == "click";
                int clickSequence = themeInteractionSequence;
                BeginThemePointer(new Point(20, 20));
                bool rapidClickRecognized = CompleteThemePointer(new Point(21, 21));
                bool rapidClickSuppressed = rapidClickRecognized && themeInteractionSequence == clickSequence;
                SetThemeAction("hover", false);
                SetThemeAction("idle", false);
                bool clickNotInterrupted = activeThemeAction != null && activeThemeAction.Slot == "click";
                DispatcherTimer observation = new DispatcherTimer();
                observation.Interval = TimeSpan.FromMilliseconds(800);
                observation.Tick += delegate
                {
                    observation.Stop();
                    bool returnedToIdle = activeThemeAction != null && activeThemeAction.Slot == "idle";
                    BeginThemePointer(new Point(20, 20));
                    bool laterClickRecognized = CompleteThemePointer(new Point(21, 21));
                    bool clickCooldownExpired = laterClickRecognized && themeInteractionSequence > clickSequence;
                    activeThemeOneShot = false;
                    SetThemeAction("idle", false);
                    BeginThemePointer(new Point(20, 20));
                    bool dragWasNotClick = !CompleteThemePointer(new Point(40, 20)) &&
                        activeThemeAction != null && activeThemeAction.Slot == "idle";
                    preferences.InteractionEnabled = false;
                    BeginThemePointer(new Point(20, 20));
                    bool disabledClickDidNotPlay = CompleteThemePointer(new Point(21, 21)) &&
                        activeThemeAction != null && activeThemeAction.Slot == "idle";
                    preferences.InteractionEnabled = true;
                    bool eventSlotsSelectable = true;
                    string[] eventSlots = new string[] { "recent-activity", "needs-user", "stop-unknown", "interrupt" };
                    for (int i = 0; i < eventSlots.Length; i++)
                    {
                        SetThemeAction(eventSlots[i], true);
                        eventSlotsSelectable &= activeThemeAction != null && activeThemeAction.Slot == eventSlots[i];
                    }
                    bool passed = nearBelowThreshold && farExceedsThreshold && clickRecognized && clickSelected &&
                        rapidClickSuppressed && clickNotInterrupted && returnedToIdle && clickCooldownExpired &&
                        dragWasNotClick && disabledClickDidNotPlay &&
                        eventSlotsSelectable && themeExportMenuEnabled;
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                        ",\"slotCount\":" + activeTheme.Actions.Count +
                        ",\"clickRecognized\":" + clickRecognized.ToString().ToLowerInvariant() +
                        ",\"rapidClickSuppressed\":" + rapidClickSuppressed.ToString().ToLowerInvariant() +
                        ",\"clickProtected\":" + clickNotInterrupted.ToString().ToLowerInvariant() +
                        ",\"returnedToIdle\":" + returnedToIdle.ToString().ToLowerInvariant() +
                        ",\"clickCooldownExpired\":" + clickCooldownExpired.ToString().ToLowerInvariant() +
                        ",\"dragRejectedAsClick\":" + dragWasNotClick.ToString().ToLowerInvariant() +
                        ",\"interactionOffBlocksAction\":" + disabledClickDidNotPlay.ToString().ToLowerInvariant() +
                        ",\"themeExportMenuEnabled\":" + themeExportMenuEnabled.ToString().ToLowerInvariant() +
                        ",\"eventSlotsSelectable\":" + eventSlotsSelectable.ToString().ToLowerInvariant() + "}");
                    ExitApplication();
                };
                observation.Start();
                interactionApp.Run();
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-auto-action")
            {
                activeTheme = ParseThemePackage(args[1]);
                WpfApplication automaticApp = new WpfApplication();
                automaticApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                window.Left = -10000;
                window.Top = -10000;
                window.Show();
                window.Left = -10000;
                window.Top = -10000;
                SetThemeAction("idle", false);
                DateTime nowLocal = DateTime.Now;
                preferences = new LocalPreferences { AutomaticActionsEnabled = false, FrequencyMinutes = 5, QuietEnabled = false };
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-5);
                bool disabledSuppressed = !TryStartAutomaticAction(nowLocal);
                preferences.AutomaticActionsEnabled = true;
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-5);
                bool fired = TryStartAutomaticAction(nowLocal);
                bool selectedAmbient = fired && activeThemeAction != null && activeThemeAction.Slot == "ambient";
                SetThemeAction("idle", false);
                preferences.QuietEnabled = true;
                preferences.QuietStart = nowLocal.AddMinutes(-1).ToString("HH:mm", CultureInfo.InvariantCulture);
                preferences.QuietEnd = nowLocal.AddMinutes(2).ToString("HH:mm", CultureInfo.InvariantCulture);
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-10);
                bool quietSuppressed = !TryStartAutomaticAction(nowLocal);
                SetThemeAction("idle", false);
                preferences.QuietEnabled = false;
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-4);
                bool frequencyRespected = !TryStartAutomaticAction(nowLocal);
                SetThemeAction("idle", false);
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-10);
                SetThemeAction("click", true);
                bool interactionProtected = !TryStartAutomaticAction(nowLocal);
                SetThemeAction("idle", false);
                ThemePackage noAutomaticSlot = new ThemePackage { Actions = new List<ThemeAction> { activeTheme.IdleAction } };
                bool missingSlotSuppressed = !CanStartAutomaticAction(noAutomaticSlot, preferences, nowLocal,
                    lastAutomaticActionUtc, false);
                ThemePackage eventOnlyTheme = new ThemePackage { Actions = new List<ThemeAction> {
                    activeTheme.IdleAction, activeTheme.FindAction("recent-activity") } };
                bool eventOnlySuppressed = !CanStartAutomaticAction(eventOnlyTheme, preferences, nowLocal,
                    lastAutomaticActionUtc, false);
                SetThemeAction("idle", false);
                window.Hide();
                lastAutomaticActionUtc = nowLocal.ToUniversalTime().AddMinutes(-10);
                bool hiddenWindowSuppressed = !TryStartAutomaticAction(nowLocal) &&
                    lastAutomaticActionUtc == nowLocal.ToUniversalTime();
                window.Show();
                SetThemeAction("idle", false);
                lastAutomaticActionUtc = DateTime.UtcNow.AddMinutes(-preferences.FrequencyMinutes);
                DateTime timerStartBoundary = DateTime.UtcNow;
                StartAutomaticActionPolling();
                automaticActionTimer.Stop();
                automaticActionTimer.Interval = TimeSpan.FromMilliseconds(50);
                automaticActionTimer.Start();
                bool schedulerTimerFired = false, schedulerReturnedToIdle = false;
                // Observe completion relative to actual dispatch, not one fixed
                // startup instant which may precede a delayed first scheduler tick.
                // Keep a hard deadline so a stuck action still fails the check.
                System.Diagnostics.Stopwatch schedulerObservationClock = System.Diagnostics.Stopwatch.StartNew();
                DispatcherTimer observation = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
                observation.Interval = TimeSpan.FromMilliseconds(25);
                observation.Tick += delegate
                {
                    schedulerTimerFired = lastAutomaticActionUtc > timerStartBoundary;
                    schedulerReturnedToIdle = schedulerTimerFired && activeThemeAction != null && activeThemeAction.Slot == "idle";
                    if (!schedulerReturnedToIdle && schedulerObservationClock.ElapsedMilliseconds < 5000) return;
                    observation.Stop();
                    bool passed = disabledSuppressed && selectedAmbient && quietSuppressed && frequencyRespected &&
                        interactionProtected && missingSlotSuppressed && eventOnlySuppressed && hiddenWindowSuppressed &&
                        schedulerTimerFired && schedulerReturnedToIdle;
                    File.WriteAllText(Path.GetFullPath(args[2]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                        ",\"automaticActionsDisabled\":" + disabledSuppressed.ToString().ToLowerInvariant() +
                        ",\"selectedAmbient\":" + selectedAmbient.ToString().ToLowerInvariant() +
                        ",\"quietSuppressed\":" + quietSuppressed.ToString().ToLowerInvariant() +
                        ",\"frequencyRespected\":" + frequencyRespected.ToString().ToLowerInvariant() +
                        ",\"interactionProtected\":" + interactionProtected.ToString().ToLowerInvariant() +
                        ",\"missingSlotSuppressed\":" + missingSlotSuppressed.ToString().ToLowerInvariant() +
                        ",\"eventOnlySuppressed\":" + eventOnlySuppressed.ToString().ToLowerInvariant() +
                        ",\"hiddenWindowSuppressed\":" + hiddenWindowSuppressed.ToString().ToLowerInvariant() +
                        ",\"schedulerTimerFired\":" + schedulerTimerFired.ToString().ToLowerInvariant() +
                        ",\"schedulerReturnedToIdle\":" + schedulerReturnedToIdle.ToString().ToLowerInvariant() +
                        ",\"observationElapsedMs\":" + schedulerObservationClock.ElapsedMilliseconds + ",\"deadlineMs\":5000}");
                    ExitApplication();
                };
                observation.Start();
                automaticApp.Run();
                bool complete = File.Exists(args[2]) && disabledSuppressed && selectedAmbient && quietSuppressed &&
                    frequencyRespected && interactionProtected && missingSlotSuppressed && eventOnlySuppressed && hiddenWindowSuppressed &&
                    schedulerTimerFired && schedulerReturnedToIdle;
                if (!complete) throw new InvalidOperationException("Automatic action scheduling violated a quiet, frequency, slot, visibility or playback gate.");
                return;
            }
            if (args.Length >= 6 && args[0] == "--test-codex-relay")
            {
                activeTheme = ParseThemePackage(args[1]);
                codexEventDirectory = Path.GetFullPath(args[2]);
                string session = args[3];
                if (!Regex.IsMatch(session, "^[a-f0-9]{64}$")) throw new InvalidDataException("Invalid test session hash.");
                WpfApplication relayApp = new WpfApplication();
                relayApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                selectedCodexSession = session;
                lastCodexEventId = null;
                selectedCodexAfterMs = 0;
                consumedCodexEventIds.Clear();
                consumedCodexEventOrder.Clear();
                int consumedCount = 0;
                while (consumedCount < 8 && ConsumeSelectedCodexEvent()) consumedCount++;
                bool eventMapped = consumedCount > 0 && activeThemeAction != null && activeThemeAction.Slot == args[4];
                bool duplicateIgnored = !ConsumeSelectedCodexEvent();
                selectedCodexSession = new string('0', 64);
                bool otherSessionIgnored = !ConsumeSelectedCodexEvent() && activeThemeAction.Slot == args[4];
                string expectedSource = args.Length >= 7 ? args[6] : "codex-hook";
                bool telemetryAccurate = lastCodexRequestedSlot == args[4] &&
                    lastCodexPlayedSlot == args[4] && lastCodexActionOutcome == "played" &&
                    lastCodexActionSource == expectedSource;
                bool noDistinctAccurate = true;
                if (args.Length >= 8)
                {
                    activeTheme = ParseThemePackage(args[7]);
                    selectedCodexSession = session;
                    selectedCodexAfterMs = 0;
                    consumedCodexEventIds.Clear();
                    consumedCodexEventOrder.Clear();
                    noDistinctAccurate = ConsumeSelectedCodexEvent() &&
                        lastCodexRequestedSlot != null && lastCodexPlayedSlot == null &&
                        lastCodexActionOutcome == "no-distinct-action";
                }
                bool passed = eventMapped && duplicateIgnored && otherSessionIgnored &&
                    telemetryAccurate && noDistinctAccurate;
                File.WriteAllText(Path.GetFullPath(args[5]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                    ",\"consumedCount\":" + consumedCount +
                    ",\"eventMapped\":" + eventMapped.ToString().ToLowerInvariant() +
                    ",\"duplicateIgnored\":" + duplicateIgnored.ToString().ToLowerInvariant() +
                    ",\"otherSessionIgnored\":" + otherSessionIgnored.ToString().ToLowerInvariant() +
                    ",\"telemetryAccurate\":" + telemetryAccurate.ToString().ToLowerInvariant() +
                    ",\"noDistinctAccurate\":" + noDistinctAccurate.ToString().ToLowerInvariant() + "}");
                ExitApplication();
                if (!passed) throw new InvalidOperationException("Sanitized Codex relay did not map correctly.");
                return;
            }
            if (args.Length >= 5 && args[0] == "--test-bundled-codex-gestures")
            {
                activeTheme = ParseThemePackage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundled-cat-motion-preview.lotheme"));
                bundledMotionPreview = true;
                codexEventDirectory = Path.GetFullPath(args[1]);
                selectedCodexSession = args[2];
                selectedCodexAfterMs = 0;
                consumedCodexEventIds.Clear();
                consumedCodexEventOrder.Clear();
                WpfApplication bundledApp = new WpfApplication();
                bundledApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                int consumed = 0;
                while (consumed < 8 && ConsumeSelectedCodexEvent()) consumed++;
                bool mapped = consumed > 0 && lastBundledPreviewGesture == args[3];
                File.WriteAllText(Path.GetFullPath(args[4]), "{\"mapped\":" + mapped.ToString().ToLowerInvariant() +
                    ",\"consumedCount\":" + consumed + ",\"gesture\":\"" + EscapeJson(lastBundledPreviewGesture ?? "") + "\"}");
                ExitApplication();
                if (!mapped) throw new InvalidOperationException("Bundled preview did not respond to the Codex event.");
                return;
            }
            if (args.Length >= 7 && (args[0] == "--test-desktop-rollout" || args[0] == "--test-desktop-hook-link"))
            {
                string id, cwd;
                if (!TryReadDesktopSession(args[2], args[1], out id, out cwd))
                    throw new InvalidDataException("Desktop rollout fixture failed source validation.");
                activeTheme = ParseThemePackage(args[3]);
                codexEventDirectory = Path.GetFullPath(args[4]);
                selectedCodexSession = DesktopSessionHash(id);
                selectedDesktopHookSession = Sha256Text(id);
                selectedDesktopRolloutPath = Path.GetFullPath(args[2]);
                if (args.Length >= 8)
                {
                    desktopSelectionFilePath = Path.GetFullPath(args[7]);
                    SaveDesktopSelection(args[1]);
                    string savedSelection = File.ReadAllText(desktopSelectionFilePath, Encoding.UTF8);
                    selectedDesktopRolloutPath = null;
                    selectedCodexSession = null;
                    File.WriteAllText(desktopSelectionFilePath,
                        savedSelection.Replace(DesktopSessionHash(id), new string('0', 64)),
                        new UTF8Encoding(false));
                    RestoreDesktopSelection(args[1]);
                    if (selectedDesktopRolloutPath != null || selectedCodexSession != null)
                        throw new InvalidOperationException("A mismatched Desktop session hash was restored.");
                    File.WriteAllText(desktopSelectionFilePath,
                        "{\"version\":1,\"relativeRollout\":\"..\\\\..\\\\escape.jsonl\",\"session\":\"" +
                        DesktopSessionHash(id) + "\"}", new UTF8Encoding(false));
                    RestoreDesktopSelection(args[1]);
                    if (selectedDesktopRolloutPath != null || selectedCodexSession != null)
                        throw new InvalidOperationException("An out-of-root Desktop session was restored.");
                    File.WriteAllText(desktopSelectionFilePath, savedSelection, new UTF8Encoding(false));
                    RestoreDesktopSelection(args[1]);
                    if (selectedDesktopRolloutPath != Path.GetFullPath(args[2]) ||
                        selectedDesktopRolloutOffset != new FileInfo(args[2]).Length)
                        throw new InvalidOperationException("Desktop session restart did not resume at EOF.");
                }
                selectedCodexAfterMs = 0;
                lastDesktopObservedAtMs = 0;
                using (FileStream stream = File.OpenRead(selectedDesktopRolloutPath))
                {
                    int next;
                    do { next = stream.ReadByte(); } while (next >= 0 && next != '\n');
                    selectedDesktopRolloutOffset = stream.Position;
                }
                WpfApplication desktopApp = new WpfApplication();
                desktopApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                PollSelectedDesktopRollout();
                System.Threading.Thread.Sleep(350);
                int consumed = 0;
                while (consumed < 8 && ConsumeSelectedCodexEvent()) consumed++;
                bool playedTelemetry = lastCodexRequestedSlot == args[5] &&
                    lastCodexPlayedSlot == args[5] && lastCodexActionOutcome == "played" &&
                    lastCodexActionSource == (args[0] == "--test-desktop-hook-link" ? "codex-hook" : "desktop-rollout");
                string[] files = Directory.GetFiles(codexEventDirectory, selectedCodexSession + ".*.event.json");
                bool expectedNoEvent = args[5] == "none";
                bool passed = expectedNoEvent
                    ? consumed == 0 && files.Length == 0 && activeThemeAction != null && activeThemeAction.Slot == "idle" &&
                      lastCodexRequestedSlot == null
                    : consumed == 1 && activeThemeAction != null && activeThemeAction.Slot == args[5] && playedTelemetry;
                bool reviewSemanticsAccurate = ThemeEventReviewNote("recent-activity").Contains("任务进行中") &&
                    ThemeEventReviewNote("needs-user").Contains("明确需要你回答或确认") &&
                    ThemeEventReviewNote("stop-unknown").Contains("结束不代表成功") &&
                    ThemeEventReviewNote("interrupt").Contains("暂停或被中断");
                bool metadataOnly = true;
                foreach (string eventPath in files)
                    if (File.ReadAllText(eventPath).Contains("SENSITIVE_DESKTOP_FIXTURE")) metadataOnly = false;
                File.WriteAllText(Path.GetFullPath(args[6]), "{\"passed\":" + passed.ToString().ToLowerInvariant() +
                    ",\"consumedCount\":" + consumed + ",\"eventFiles\":" + files.Length +
                    ",\"metadataOnly\":" + metadataOnly.ToString().ToLowerInvariant() +
                    ",\"lastActionEventId\":" + new JavaScriptSerializer().Serialize(lastCodexActionEventId) +
                    ",\"lastActionEvidence\":" + new JavaScriptSerializer().Serialize(lastCodexActionEvidence) +
                    ",\"reviewSemanticsAccurate\":" + reviewSemanticsAccurate.ToString().ToLowerInvariant() +
                    ",\"playedTelemetry\":" + playedTelemetry.ToString().ToLowerInvariant() + "}");
                ExitApplication();
                if (!passed || !metadataOnly || !reviewSemanticsAccurate)
                    throw new InvalidOperationException("Native Desktop rollout mapping or review semantics failed.");
                return;
            }
            if (args.Length >= 4 && args[0] == "--test-role-persistence")
            {
                string importedJson = ReadBoundedJson(args[1]);
                RoleCard imported = ParseRoleCard(importedJson);
                PersistRoleCardAt(args[2], importedJson);
                RoleCard reopened = ParseRoleCard(ReadBoundedJson(args[2]));
                bool persisted = reopened.Name == imported.Name && reopened.PhotoBitmap.PixelWidth == imported.PhotoBitmap.PixelWidth;
                File.WriteAllText(Path.GetFullPath(args[3]), "{\"persisted\":" + persisted.ToString().ToLowerInvariant()
                    + ",\"name\":\"" + EscapeJson(reopened.Name) + "\",\"photoDecoded\":true}");
                if (!persisted) throw new InvalidOperationException("The imported role did not survive a local save and reload.");
                return;
            }
            if (args.Length >= 7 && args[0] == "--test-theme-role-binding")
            {
                string root = Path.GetFullPath(args[4]);
                Directory.CreateDirectory(root);
                activeThemeFilePath = Path.Combine(root, "active-theme.lotheme");
                File.Copy(args[3], activeThemeFilePath, true);
                activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                SaveThemeBinding();
                bool matched = ThemeMatchesCurrentRole();
                activeRole = ParseRoleCard(ReadBoundedJson(args[6]));
                bool reencodedMatched = ThemeMatchesCurrentRole();
                activeRole = ParseRoleCard(ReadBoundedJson(args[2]));
                bool otherBlocked = !ThemeMatchesCurrentRole();
                activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                bool restored = ThemeMatchesCurrentRole();
                using (FileStream altered = new FileStream(activeThemeFilePath, FileMode.Append, FileAccess.Write)) altered.WriteByte(0);
                bool replacedPackageBlocked = !ThemeMatchesCurrentRole();
                File.Copy(args[3], activeThemeFilePath, true);
                reviewLedgerFilePath = Path.Combine(root, "candidate-review-ledger.json");
                activeTheme = ParseThemePackage(activeThemeFilePath);
                activeRole = ParseRoleCard(ReadBoundedJson(args[2]));
                RefreshThemeAfterRoleChange();
                bool runtimeSuspended = activeTheme == null;
                activeRole = ParseRoleCard(ReadBoundedJson(args[1]));
                RefreshThemeAfterRoleChange();
                bool runtimeRestored = activeTheme != null && !bundledMotionPreview;
                string result = "{\"matched\":" + matched.ToString().ToLowerInvariant() +
                    ",\"reencodedMatched\":" + reencodedMatched.ToString().ToLowerInvariant() +
                    ",\"otherBlocked\":" + otherBlocked.ToString().ToLowerInvariant() +
                    ",\"restored\":" + restored.ToString().ToLowerInvariant() +
                    ",\"replacedPackageBlocked\":" + replacedPackageBlocked.ToString().ToLowerInvariant() +
                    ",\"runtimeSuspended\":" + runtimeSuspended.ToString().ToLowerInvariant() +
                    ",\"runtimeRestored\":" + runtimeRestored.ToString().ToLowerInvariant() + "}";
                File.WriteAllText(Path.GetFullPath(args[5]), result);
                if (!matched || !reencodedMatched || !otherBlocked || !restored || !replacedPackageBlocked ||
                    !runtimeSuspended || !runtimeRestored)
                    throw new InvalidOperationException("Theme role binding failed: " + result);
                return;
            }
            if (args.Length >= 4 && args[0] == "--test-review-fingerprint")
            {
                string testRoot = Path.Combine(Path.GetFullPath(args[2]), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testRoot);
                string packagePath = Path.Combine(testRoot, "reviewed.lotheme");
                File.Copy(args[1], packagePath);
                reviewLedgerFilePath = Path.Combine(testRoot, "candidate-review-ledger.json");
                string reviewedHash = FileSha256Hex(packagePath);
                EnsureReviewedThemeUnchanged(packagePath, reviewedHash);
                File.AppendAllText(packagePath, "x");
                bool mutationRejected = false;
                try { EnsureReviewedThemeUnchanged(packagePath, reviewedHash); }
                catch (InvalidDataException) { mutationRejected = true; }
                File.Copy(args[1], packagePath, true);
                string rejection = "{\"format\":\"little-orbit.review-veto\",\"version\":1,\"denied\":[{\"sha256\":\"" +
                    reviewedHash + "\",\"candidateId\":\"" + reviewedHash.Substring(0, 32) +
                    "\",\"reason\":\"motion\",\"decidedAt\":\"" + DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}]}";
                File.WriteAllText(reviewLedgerFilePath, rejection);
                bool vetoAfterReviewBlocked = false;
                try { EnsureReviewedThemeUnchanged(packagePath, reviewedHash); }
                catch (InvalidDataException) { vetoAfterReviewBlocked = true; }
                string fingerprintResult = "{\"mutationRejected\":" + mutationRejected.ToString().ToLowerInvariant() +
                    ",\"vetoAfterReviewBlocked\":" + vetoAfterReviewBlocked.ToString().ToLowerInvariant() +
                    ",\"isolated\":true}";
                File.WriteAllText(Path.GetFullPath(args[3]), fingerprintResult);
                if (!mutationRejected || !vetoAfterReviewBlocked)
                    throw new InvalidOperationException("Theme fingerprint gate failed: " + fingerprintResult);
                return;
            }
            if (args.Length >= 6 && args[0] == "--test-pending-role-handoff")
            {
                try {
                string testRoot = Path.Combine(Path.GetFullPath(args[4]), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testRoot);
                activeRoleFilePath = Path.Combine(testRoot, "active-role.little-orbit.json");
                previousRoleFilePath = Path.Combine(testRoot, "previous-role.little-orbit.json");
                pendingRoleFilePath = Path.Combine(testRoot, "pending-role.little-orbit.json");
                activeThemeFilePath = Path.Combine(testRoot, "active-theme.lotheme");
                previousThemeFilePath = Path.Combine(testRoot, "previous-theme.lotheme");
                string originalJson = ReadBoundedJson(args[1]);
                string pendingJson = ReadBoundedJson(args[2]);
                activeRole = ParseRoleCard(originalJson);
                PersistRoleCardAt(activeRoleFilePath, originalJson);
                File.Copy(args[3], activeThemeFilePath);
                SaveThemeBinding();
                string originalThemeHash = FileSha256Hex(activeThemeFilePath);
                pendingRole = ParseRoleCard(pendingJson);
                PersistRoleCardAt(pendingRoleFilePath, pendingJson);
                bool pendingReloaded = RolePhotoKey(ParseRoleCard(ReadBoundedJson(pendingRoleFilePath))) ==
                    RolePhotoKey(pendingRole);
                bool oldRoleStayedActive = ThemeMatchesCurrentRole() &&
                    RolePhotoKey(activeRole) != RolePhotoKey(pendingRole);
                string staged = Path.Combine(testRoot, "candidate.importing");
                File.Copy(args[3], staged);
                string reviewedPendingHash = FileSha256Hex(pendingRoleFilePath);
                File.AppendAllText(pendingRoleFilePath, " ");
                bool mutationRejected = false;
                try { CommitPendingRoleWithTheme(staged, reviewedPendingHash); }
                catch (InvalidDataException) { mutationRejected = true; }
                mutationRejected = mutationRejected && ThemeMatchesCurrentRole() &&
                    FileSha256Hex(activeThemeFilePath) == originalThemeHash;
                PersistRoleCardAt(pendingRoleFilePath, pendingJson);
                string blockedTemporaryBinding = ThemeBindingPath() + ".tmp";
                Directory.CreateDirectory(blockedTemporaryBinding);
                bool failedCommit = false;
                try { CommitPendingRoleWithTheme(staged, FileSha256Hex(pendingRoleFilePath)); }
                catch (Exception) { failedCommit = true; }
                Directory.Delete(blockedTemporaryBinding);
                bool failureRestored = failedCommit && ThemeMatchesCurrentRole() &&
                    FileSha256Hex(activeThemeFilePath) == originalThemeHash &&
                    File.Exists(pendingRoleFilePath) && !File.Exists(previousRoleFilePath);
                File.Copy(args[3], staged, true);
                CommitPendingRoleWithTheme(staged, FileSha256Hex(pendingRoleFilePath));
                bool switchedTogether = pendingRole == null && !File.Exists(pendingRoleFilePath) &&
                    ThemeMatchesCurrentRole() && RolePhotoKey(activeRole) == RolePhotoKey(ParseRoleCard(pendingJson)) &&
                    RolePhotoKey(ParseRoleCard(ReadBoundedJson(previousRoleFilePath))) == RolePhotoKey(ParseRoleCard(originalJson));
                string handoffResult = "{\"oldRoleStayedActive\":" + oldRoleStayedActive.ToString().ToLowerInvariant() +
                    ",\"pendingReloaded\":" + pendingReloaded.ToString().ToLowerInvariant() +
                    ",\"mutationRejected\":" + mutationRejected.ToString().ToLowerInvariant() +
                    ",\"failureRestored\":" + failureRestored.ToString().ToLowerInvariant() +
                    ",\"switchedTogether\":" + switchedTogether.ToString().ToLowerInvariant() +
                    ",\"isolated\":true,\"visualApproval\":false}";
                File.WriteAllText(Path.GetFullPath(args[5]), handoffResult);
                if (!oldRoleStayedActive || !pendingReloaded || !mutationRejected || !failureRestored || !switchedTogether)
                    throw new InvalidOperationException("Pending role handoff failed: " + handoffResult);
                } catch (Exception error) {
                    File.WriteAllText(Path.GetFullPath(args[5]) + ".error.txt", error.ToString());
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 5 && args[0] == "--test-role-rollback")
            {
                try
                {
                    string testRoot = Path.GetFullPath(args[3]);
                    Directory.CreateDirectory(testRoot);
                    activeRoleFilePath = Path.Combine(testRoot, "active-role.little-orbit.json");
                    previousRoleFilePath = Path.Combine(testRoot, "previous-role.little-orbit.json");
                    if (File.Exists(activeRoleFilePath)) File.Delete(activeRoleFilePath);
                    if (File.Exists(previousRoleFilePath)) File.Delete(previousRoleFilePath);
                    string firstJson = ReadBoundedJson(args[1]);
                    RoleCard first = ParseRoleCard(firstJson);
                    PersistActiveRoleCard(firstJson);
                    string replacementJson = ReadBoundedJson(args[2]);
                    RoleCard replacement = ParseRoleCard(replacementJson);
                    PersistActiveRoleCard(replacementJson);
                    RoleCard installed = ParseRoleCard(ReadBoundedJson(activeRoleFilePath));
                    RoleCard savedPrevious = ParseRoleCard(ReadBoundedJson(previousRoleFilePath));
                    bool previousPreserved = savedPrevious.Name == first.Name;
                    PersistActiveRoleCard(ReadBoundedJson(previousRoleFilePath));
                    RoleCard restored = ParseRoleCard(ReadBoundedJson(activeRoleFilePath));
                    RoleCard swappedPrevious = ParseRoleCard(ReadBoundedJson(previousRoleFilePath));
                    bool rolledBack = restored.Name == first.Name && swappedPrevious.Name == installed.Name;
                    File.WriteAllText(Path.GetFullPath(args[4]), "{\"installed\":" + (installed.Name == replacement.Name).ToString().ToLowerInvariant() +
                        ",\"previousPreserved\":" + previousPreserved.ToString().ToLowerInvariant() +
                        ",\"rolledBack\":" + rolledBack.ToString().ToLowerInvariant() +
                        ",\"active\":\"" + EscapeJson(restored.Name) + "\",\"previous\":\"" + EscapeJson(swappedPrevious.Name) + "\"}");
                    if (!previousPreserved || !rolledBack || installed.Name != replacement.Name)
                        throw new InvalidDataException("The previous role card was not preserved and restored correctly.");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[4]), "{\"installed\":false,\"previousPreserved\":false,\"rolledBack\":false,\"error\":\"role-rollback-failed\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }
            if (args.Length >= 4 && args[0] == "--render-role-review")
            {
                try
                {
                    RoleCard candidate = ParseRoleCard(ReadBoundedJson(args[1]));
                    WpfApplication reviewApp = new WpfApplication();
                    reviewApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    BuildWindow(photoPath);
                    roleReviewRenderPath = Path.GetFullPath(args[2]);
                    ReviewRoleCard(candidate);
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":true,\"restored\":false,\"name\":\""
                        + EscapeJson(candidate.Name) + "\",\"size\":\"454x500\"}");
                }
                catch
                {
                    File.WriteAllText(Path.GetFullPath(args[3]), "{\"rendered\":false,\"restored\":false,\"error\":\"invalid-role-review\"}");
                    Environment.ExitCode = 2;
                }
                return;
            }

            if (args.Length >= 2 && args[0] == "--render-preview")
            {
                WpfApplication renderApp = new WpfApplication();
                renderApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildWindow(photoPath);
                RenderPreview(Path.GetFullPath(args[1]));
                return;
            }
            if (args.Length == 2 && args[0] == "--test-display-compatibility")
            { TestDisplayCompatibility(Path.GetFullPath(args[1]), photoPath); return; }
            if (args.Length >= 2 && args[0] == "--test-window-placement")
            {
                System.Windows.Rect negativeMonitor = new System.Windows.Rect(-1920, 0, 1920, 1080);
                System.Windows.Point docked = ResolveWindowPosition(negativeMonitor, 234, 366, Double.NaN, Double.NaN);
                System.Windows.Point recovered = ResolveWindowPosition(negativeMonitor, 234, 366, 5000, -5000);
                System.Windows.Point resized = ResolveWindowPosition(negativeMonitor, 190, 254, docked.X, docked.Y);
                System.Windows.Point taskbarOffset = ResolveWindowPosition(new System.Windows.Rect(50, 40, 1870, 1040),
                    234, 366, Double.NaN, Double.NaN);
                System.Windows.Point tinyArea = ResolveWindowPosition(new System.Windows.Rect(100, 200, 120, 120),
                    234, 366, Double.NaN, Double.NaN);
                bool negativeWorks = docked.X == -256 && docked.Y == 686 && recovered.X == -234 && recovered.Y == 0;
                bool resizeWorks = resized.X == docked.X && resized.Y == docked.Y;
                bool offsetWorks = taskbarOffset.X == 1664 && taskbarOffset.Y == 686 &&
                    tinyArea.X == 100 && tinyArea.Y == 200;
                string report = "{\"negativeMonitor\":" + negativeWorks.ToString().ToLowerInvariant()
                    + ",\"resizePreservesPosition\":" + resizeWorks.ToString().ToLowerInvariant()
                    + ",\"offsetAndTinyWorkArea\":" + offsetWorks.ToString().ToLowerInvariant()
                    + ",\"detectedScreens\":" + Forms.Screen.AllScreens.Length + "}";
                File.WriteAllText(Path.GetFullPath(args[1]), report);
                if (!negativeWorks || !resizeWorks || !offsetWorks) throw new InvalidOperationException(report);
                return;
            }
            if (args.Length >= 3 && args[0] == "--test-window-restore")
            {
                WpfApplication restoreApp = new WpfApplication();
                restoreApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SavedPlacement loaded = ReadPlacementAt(args[1]);
                BuildTray();
                BuildWindow(photoPath, loaded.Left, loaded.Top, loaded.ScreenName);
                window.Show();
                System.Windows.Point expected = ResolveWindowPosition(ScreenWorkAreaDips(FindScreen(loaded.ScreenName)),
                    window.Width, window.Height, ScreenExists(loaded.ScreenName) ? loaded.Left : Double.NaN,
                    ScreenExists(loaded.ScreenName) ? loaded.Top : Double.NaN);
                bool restored = Math.Abs(window.Left - expected.X) < 0.5 && Math.Abs(window.Top - expected.Y) < 0.5;
                bool photoDecoded = petBitmap.PixelWidth == 380 && petBitmap.PixelHeight > 0;
                File.WriteAllText(Path.GetFullPath(args[2]), "{\"restored\":" + restored.ToString().ToLowerInvariant() +
                    ",\"photoDecoded\":" + photoDecoded.ToString().ToLowerInvariant() + "}");
                ExitApplication();
                if (!restored || !photoDecoded) throw new InvalidOperationException("Saved placement was not restored.");
                return;
            }
            if (args.Length >= 2 && args[0] == "--smoke-test")
            {
                WpfApplication testApp = new WpfApplication();
                testApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                BuildTray();
                BuildWindow(photoPath);
                window.Show();
                window.UpdateLayout();
                bool visibleAtLaunch = window.IsVisible;
                ToggleWindow();
                bool hideWorks = !window.IsVisible && visibilityItem.Text == "显示宠物";
                ToggleWindow();
                bool showWorks = window.IsVisible && visibilityItem.Text == "隐藏宠物";
                bool trayRegistered = trayIcon.Visible;
                bool themeExportMenuPresent = exportThemeItem != null &&
                    exportThemeItem.Text == "导出当前主题包…" && !exportThemeItem.Enabled;
                bool photoDecoded = petBitmap.PixelWidth == 380 && petBitmap.PixelHeight > 0;
                bool sampleFramingWarning = photoFrame != null && photoFrame.ToolTip is string &&
                    ((string)photoFrame.ToolTip).IndexOf("示例猫身", StringComparison.Ordinal) >= 0;
                greeting.Text = "interaction test";
                preferences.InteractionEnabled = false;
                BeginPhotoPointer(new Point(20, 20));
                bool photoClickRecognized = CompletePhotoPointer(new Point(21, 21));
                bool disabledInteractionRespected = photoClickRecognized && greeting.Text == "interaction test";
                preferences.InteractionEnabled = true;
                BeginPhotoPointer(new Point(20, 20));
                bool photoDragNotClick = !CompletePhotoPointer(new Point(120, 20)) && greeting.Text == "interaction test";
                lastPhotoInteractionUtc = DateTime.MinValue;
                bool enabledInteractionResponded = HandlePhotoInteraction() && greeting.Text != "interaction test";
                string firstPhotoGreeting = greeting.Text;
                bool rapidPhotoClickSuppressed = !HandlePhotoInteraction() && greeting.Text == firstPhotoGreeting;
                lastPhotoInteractionUtc = DateTime.MinValue;
                bool laterPhotoGreetingChanged = HandlePhotoInteraction() && greeting.Text != firstPhotoGreeting;
                preferences = new LocalPreferences();
                System.Windows.Rect actualArea = ScreenWorkAreaDips(FindScreen(WindowScreenName()));
                double movedLeft = Math.Min(actualArea.Right - window.Width, actualArea.Left + 42);
                double movedTop = Math.Min(actualArea.Bottom - window.Height, actualArea.Top + 48);
                window.Left = movedLeft;
                window.Top = movedTop;
                string placementFixturePath = args[1] + ".placement.json";
                PersistPlacementAt(placementFixturePath, new SavedPlacement {
                    ScreenName = WindowScreenName(), Left = movedLeft, Top = movedTop
                });
                SavedPlacement reloadedPlacement = ReadPlacementAt(placementFixturePath);
                bool placementRoundTrip = reloadedPlacement.ScreenName == WindowScreenName() &&
                    Math.Abs(reloadedPlacement.Left - movedLeft) < 0.001 &&
                    Math.Abs(reloadedPlacement.Top - movedTop) < 0.001;
                RebuildRuntimeWindow();
                bool positionPreserved = window.IsVisible && Math.Abs(window.Left - movedLeft) < 0.5 &&
                    Math.Abs(window.Top - movedTop) < 0.5;
                PopulateScreenMenu();
                bool screenMenuPresent = moveScreenItem.DropDownItems.Count == Forms.Screen.AllScreens.Length;
                bool screenMenuMoveWorks = false;
                if (screenMenuPresent && moveScreenItem.DropDownItems.Count > 0)
                {
                    Forms.Screen targetScreen = Forms.Screen.AllScreens[0];
                    moveScreenItem.DropDownItems[0].PerformClick();
                    DisplayRect movedBounds;
                    bool haveBounds = GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out movedBounds);
                    System.Drawing.Rectangle workArea = targetScreen.WorkingArea;
                    screenMenuMoveWorks = window.IsVisible && haveBounds &&
                        movedBounds.Left >= workArea.Left && movedBounds.Top >= workArea.Top &&
                        movedBounds.Right <= workArea.Right && movedBounds.Bottom <= workArea.Bottom &&
                        WindowScreenName() == targetScreen.DeviceName;
                }
                string result = "{\"visibleAtLaunch\":" + visibleAtLaunch.ToString().ToLowerInvariant()
                    + ",\"hideWorks\":" + hideWorks.ToString().ToLowerInvariant()
                    + ",\"showWorks\":" + showWorks.ToString().ToLowerInvariant()
                    + ",\"trayRegistered\":" + trayRegistered.ToString().ToLowerInvariant()
                    + ",\"themeExportMenuPresent\":" + themeExportMenuPresent.ToString().ToLowerInvariant()
                    + ",\"photoDecoded\":" + photoDecoded.ToString().ToLowerInvariant()
                    + ",\"interactionToggle\":" + (disabledInteractionRespected && enabledInteractionResponded).ToString().ToLowerInvariant()
                    + ",\"photoClickRecognized\":" + photoClickRecognized.ToString().ToLowerInvariant()
                    + ",\"photoDragNotClick\":" + photoDragNotClick.ToString().ToLowerInvariant()
                    + ",\"photoInteractionCooldown\":" + rapidPhotoClickSuppressed.ToString().ToLowerInvariant()
                    + ",\"photoInteractionVaries\":" + laterPhotoGreetingChanged.ToString().ToLowerInvariant()
                    + ",\"sampleFramingWarning\":" + sampleFramingWarning.ToString().ToLowerInvariant()
                    + ",\"positionPreservedOnRebuild\":" + positionPreserved.ToString().ToLowerInvariant()
                    + ",\"placementRoundTrip\":" + placementRoundTrip.ToString().ToLowerInvariant()
                    + ",\"screenMenuPresent\":" + screenMenuPresent.ToString().ToLowerInvariant()
                    + ",\"screenMenuMoveWorks\":" + screenMenuMoveWorks.ToString().ToLowerInvariant()
                    + ",\"photoPixels\":\"" + petBitmap.PixelWidth + "x" + petBitmap.PixelHeight + "\"}";
                File.WriteAllText(Path.GetFullPath(args[1]), result);
                ExitApplication();
                if (!visibleAtLaunch || !hideWorks || !showWorks || !trayRegistered || !themeExportMenuPresent || !photoDecoded || !disabledInteractionRespected ||
                    !enabledInteractionResponded || !photoClickRecognized || !photoDragNotClick ||
                    !rapidPhotoClickSuppressed || !laterPhotoGreetingChanged ||
                    !sampleFramingWarning || !positionPreserved || !placementRoundTrip ||
                    !screenMenuPresent || !screenMenuMoveWorks)
                    throw new InvalidOperationException("WPF runtime smoke check failed: " + result);
                return;
            }

            bool firstDesktopInstance;
            string mutexName = @"Local\AI-Pet-Studio-Desktop";
            if (!String.Equals(Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(defaultDataRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                using (SHA256 sha = SHA256.Create())
                    mutexName += "-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                        Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
            }
            System.Threading.Mutex desktopMutex = new System.Threading.Mutex(
                true, mutexName, out firstDesktopInstance);
            if (!firstDesktopInstance)
            {
                desktopMutex.Dispose();
                if (importThemeAtLaunch || rollbackThemeAtLaunch)
                    MessageBox.Show("桌宠已在运行。请从托盘导入或回滚主题，或退出桌宠后重新打开。活动主题尚未更改。",
                        "打开主题操作", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                WpfApplication app = new WpfApplication();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                // The exact reviewed default is activated only after acquiring
                // the normal runtime mutex; parallel first launches cannot race.
                if (normalLaunch && activeTheme == null) TryActivateReviewedReferenceTheme(photoPath);
                if (normalLaunch && activeTheme == null && activeRole == null && !File.Exists(activeThemeFilePath))
                    activeTheme = CreatePublicDemoTheme();
                BuildTray();
                if (savedPlacement == null) BuildWindow(photoPath);
                else BuildWindow(photoPath, savedPlacement.Left, savedPlacement.Top, savedPlacement.ScreenName);
                app.MainWindow = window;
                ShowWindow();
                StartCodexEventPolling();
                StartAutomaticActionPolling();
                if (settingsAtLaunch) app.Dispatcher.BeginInvoke(new Action(delegate { ShowPetManager(0); }));
                if (importThemeAtLaunch)
                    app.Dispatcher.BeginInvoke(new Action(delegate { ImportThemePackage(args[1]); }));
                if (rollbackThemeAtLaunch)
                    app.Dispatcher.BeginInvoke(new Action(delegate { RollbackTheme(); }));
                app.Run();
            }
            finally
            {
                desktopMutex.ReleaseMutex();
                desktopMutex.Dispose();
            }
        }

        private static void RenderPreview(string outputPath)
        {
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            window.UpdateLayout();
            FrameworkElement content = window.Content as FrameworkElement;
            content.Measure(new System.Windows.Size(window.Width, window.Height));
            content.Arrange(new System.Windows.Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(window.Width), (int)Math.Ceiling(window.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(outputPath)) encoder.Save(stream);
            window.Hide();
        }

        private static void BuildTray()
        {
            trayMenu = new Forms.ContextMenuStrip();
            visibilityItem = new Forms.ToolStripMenuItem("隐藏宠物");
            visibilityItem.Click += delegate { ToggleWindow(); };
            trayMenu.Items.Add(visibilityItem);
            moveScreenItem = new Forms.ToolStripMenuItem("移动到显示器");
            moveScreenItem.DropDownOpening += delegate { PopulateScreenMenu(); };
            trayMenu.Items.Add(moveScreenItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem importItem = new Forms.ToolStripMenuItem("导入角色卡 JSON…");
            importItem.Click += delegate { ImportRoleCard(); };
            trayMenu.Items.Add(importItem);
            pendingRoleItem = new Forms.ToolStripMenuItem();
            pendingRoleItem.Enabled = false;
            UpdatePendingRoleMenu();
            trayMenu.Items.Add(pendingRoleItem);
            rollbackRoleItem = new Forms.ToolStripMenuItem("预览并恢复上一角色卡…");
            rollbackRoleItem.Enabled = File.Exists(previousRoleFilePath);
            rollbackRoleItem.Click += delegate { RollbackRoleCard(); };
            trayMenu.Items.Add(rollbackRoleItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem themeItem = new Forms.ToolStripMenuItem("预览并导入主题包…");
            themeItem.Click += delegate { ImportThemePackage(); };
            trayMenu.Items.Add(themeItem);
            exportThemeItem = new Forms.ToolStripMenuItem("导出当前主题包…");
            exportThemeItem.Enabled = activeTheme != null && File.Exists(activeThemeFilePath);
            exportThemeItem.Click += delegate { ExportActiveThemePackage(); };
            trayMenu.Items.Add(exportThemeItem);
            removeThemeItem = new Forms.ToolStripMenuItem("移除活动主题，返回默认预览");
            removeThemeItem.Enabled = activeTheme != null && !bundledMotionPreview;
            removeThemeItem.Click += delegate { RemoveActiveTheme(); };
            trayMenu.Items.Add(removeThemeItem);
            reviewSavedThemeItem = new Forms.ToolStripMenuItem("审核并重新启用已保存主题…");
            reviewSavedThemeItem.Enabled = File.Exists(activeThemeFilePath) && (activeTheme == null || bundledMotionPreview);
            reviewSavedThemeItem.Click += delegate { ReviewSavedTheme(); };
            trayMenu.Items.Add(reviewSavedThemeItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            rollbackThemeItem = new Forms.ToolStripMenuItem("预览并回滚到上一主题…");
            rollbackThemeItem.Enabled = File.Exists(previousThemeFilePath);
            rollbackThemeItem.Click += delegate { RollbackTheme(); };
            trayMenu.Items.Add(rollbackThemeItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            codexSessionItem = new Forms.ToolStripMenuItem("关联 Codex 会话");
            codexSessionItem.DropDownOpening += delegate { PopulateCodexSessionMenu(); };
            trayMenu.Items.Add(codexSessionItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem preferencesItem = new Forms.ToolStripMenuItem("陪伴偏好…");
            preferencesItem.Click += delegate { ShowPreferences(); };
            trayMenu.Items.Add(preferencesItem);
            Forms.ToolStripMenuItem exportPreferencesItem = new Forms.ToolStripMenuItem("导出偏好备份 JSON…");
            exportPreferencesItem.Click += delegate { ExportPreferences(); };
            trayMenu.Items.Add(exportPreferencesItem);
            Forms.ToolStripMenuItem importPreferencesItem = new Forms.ToolStripMenuItem("导入偏好备份 JSON…");
            importPreferencesItem.Click += delegate { ImportPreferences(); };
            trayMenu.Items.Add(importPreferencesItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem exitItem = new Forms.ToolStripMenuItem("退出 AI Pet Studio");
            exitItem.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(exitItem);

            SimplifyTrayMenu(exitItem);

            trayIcon = new Forms.NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "AI Pet Studio · 本地静态陪伴";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowPetManager(0); };
        }

        private static long CurrentUnixMilliseconds()
        {
            return (DateTime.UtcNow.Ticks - 621355968000000000L) / TimeSpan.TicksPerMillisecond;
        }

        private static void RecordCodexHookFromStandardInput(string allowedRoot, string eventDirectoryOverride)
        {
            string root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root)) return;
            string destination = String.IsNullOrEmpty(eventDirectoryOverride)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-events")
                : Path.GetFullPath(eventDirectoryOverride);
            if (!String.IsNullOrEmpty(eventDirectoryOverride) && !Path.IsPathRooted(eventDirectoryOverride)) return;
            const int maxInputBytes = 2 * 1024 * 1024;
            byte[] input = new byte[maxInputBytes + 1];
            int total = 0;
            using (Stream stream = Console.OpenStandardInput())
            {
                while (total < input.Length)
                {
                    int read = stream.Read(input, total, input.Length - total);
                    if (read == 0) break;
                    total += read;
                }
            }
            if (total < 2 || total > maxInputBytes) return;
            string json = new UTF8Encoding(false, true).GetString(input, 0, total);
            EnsureNoDuplicateJsonObjectKeys(json);
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = maxInputBytes, RecursionLimit = 24 };
            Dictionary<string, object> hook = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (hook == null) return;
            string eventName = hook.ContainsKey("hook_event_name") ? hook["hook_event_name"] as string : null;
            string session = hook.ContainsKey("session_id") ? hook["session_id"] as string : null;
            string cwd = hook.ContainsKey("cwd") ? hook["cwd"] as string : null;
            if (Array.IndexOf(new string[] { "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
                "Stop", "Interrupt", "SessionEnd" }, eventName) < 0 || session == null || session.Length < 1 ||
                session.Length > 128 || String.IsNullOrEmpty(cwd)) return;
            string actualCwd = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!String.Equals(actualCwd, root, StringComparison.OrdinalIgnoreCase) &&
                !actualCwd.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            string sessionHash;
            using (SHA256 hash = SHA256.Create())
                sessionHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(session))).Replace("-", "").ToLowerInvariant();
            string eventId = Guid.NewGuid().ToString("N");
            long observedAtMs = CurrentUnixMilliseconds();
            string evidence = CodexEvidenceJson("codex-hook", input, 0, total, -1, ReadString(hook, "turn_id"));
            string safeJson = "{\"version\":2,\"eventId\":\"" + eventId + "\",\"observedAtMs\":" +
                observedAtMs.ToString(CultureInfo.InvariantCulture) + ",\"event\":\"" + eventName +
                "\",\"session\":\"" + sessionHash + "\",\"evidence\":" + evidence + "}";
            Directory.CreateDirectory(destination);
            string prefix = sessionHash + "." + observedAtMs.ToString("D13", CultureInfo.InvariantCulture) + "." + eventId;
            WriteCodexEventAtomically(Path.Combine(destination, prefix + ".event.json"), safeJson);
            WriteCodexEventAtomically(Path.Combine(destination, sessionHash + ".json"), safeJson);
            string[] queued = Directory.GetFiles(destination, sessionHash + ".*.event.json");
            Array.Sort(queued, StringComparer.Ordinal);
            long expiry = observedAtMs - 10L * 60 * 1000;
            for (int i = 0; i < queued.Length; i++)
            {
                string[] parts = Path.GetFileName(queued[i]).Split('.');
                long timestamp;
                if (parts.Length >= 4 && Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out timestamp) &&
                    timestamp < expiry) TryDeleteCodexEvent(queued[i]);
            }
            queued = Directory.GetFiles(destination, sessionHash + ".*.event.json");
            Array.Sort(queued, StringComparer.Ordinal);
            for (int i = 0; i < queued.Length - 256; i++) TryDeleteCodexEvent(queued[i]);
        }

        private static void WriteCodexEventAtomically(string target, string content)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(target), ".codex-event-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, content + "\n", new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void TryDeleteCodexEvent(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static Dictionary<string, object> ReadCodexEvent(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 2 || file.Length > 2048) return null;
            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            EnsureNoDuplicateJsonObjectKeys(json);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> value = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (value == null || !value.ContainsKey("version") || !(value["version"] is int)) return null;
            int version = (int)value["version"];
            if (version == 1) {
                if (!HasExactKeys(value, new string[] { "version", "eventId", "observedAtMs", "event", "session" })) return null;
            } else if (version == 2) {
                if (!HasExactKeys(value, new string[] { "version", "eventId", "observedAtMs", "event", "session", "evidence" }) ||
                    !ValidCodexEvidence(value["evidence"], ReadString(value, "event").StartsWith("Desktop", StringComparison.Ordinal))) return null;
            } else return null;
            string id = ReadString(value, "eventId");
            string session = ReadString(value, "session");
            string eventName = ReadString(value, "event");
            if (!Regex.IsMatch(id, "^[a-f0-9]{32}$") || !Regex.IsMatch(session, "^[a-f0-9]{64}$") ||
                Array.IndexOf(new string[] { "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
                    "Stop", "Interrupt", "SessionEnd", "DesktopTurnStarted", "DesktopTurnEndedUnknown",
                    "DesktopTurnInterrupted", "DesktopToolActivity", "DesktopUserInputRequested" }, eventName) < 0) return null;
            long observedAt;
            try { observedAt = Convert.ToInt64(value["observedAtMs"], CultureInfo.InvariantCulture); }
            catch { return null; }
            long age = CurrentUnixMilliseconds() - observedAt;
            if (age < -5000 || age > 24L * 60 * 60 * 1000) return null;
            return value;
        }

        private static void PopulateCodexSessionMenu()
        {
            if (codexSessionItem == null) return;
            codexSessionItem.DropDownItems.Clear();
            Forms.ToolStripMenuItem disconnected = new Forms.ToolStripMenuItem("断开 Codex 关联");
            disconnected.Tag = "disconnect";
            disconnected.Enabled = selectedCodexSession != null;
            disconnected.Click += delegate
            {
                selectedCodexSession = null;
                selectedDesktopRolloutPath = null;
                ClearDesktopSelection();
                lastCodexEventId = null;
                consumedCodexEventIds.Clear();
                consumedCodexEventOrder.Clear();
            };
            codexSessionItem.DropDownItems.Add(disconnected);
            codexSessionItem.DropDownItems.Add(new Forms.ToolStripSeparator());
            PopulateDesktopSessionMenu();
            codexSessionItem.DropDownItems.Add(new Forms.ToolStripSeparator());
            if (!Directory.Exists(codexEventDirectory)) return;
            FileInfo[] candidates = new DirectoryInfo(codexEventDirectory).GetFiles("*.json");
            Array.Sort(candidates, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            int shown = 0;
            for (int i = 0; i < candidates.Length && shown < 8; i++)
            {
                Dictionary<string, object> record;
                try { record = ReadCodexEvent(candidates[i].FullName); }
                catch { continue; }
                if (record == null) continue;
                string session = ReadString(record, "session");
                if (!String.Equals(candidates[i].Name, session + ".json", StringComparison.Ordinal)) continue;
                if (ReadString(record, "event") == "SessionEnd") continue;
                if (ReadString(record, "event").StartsWith("Desktop", StringComparison.Ordinal)) continue;
                long age = CurrentUnixMilliseconds() - Convert.ToInt64(record["observedAtMs"], CultureInfo.InvariantCulture);
                if (age > 60L * 60 * 1000) continue;
                string label = (CodexTitleForHash(session) ?? "未命名会话") + " · 最近状态";
                Forms.ToolStripMenuItem option = new Forms.ToolStripMenuItem(label);
                option.Checked = selectedDesktopRolloutPath == null &&
                    String.Equals(selectedCodexSession, session, StringComparison.Ordinal);
                string selected = session;
                string eventId = ReadString(record, "eventId");
                option.Click += delegate
                {
                    selectedCodexSession = selected;
                    selectedDesktopRolloutPath = null;
                    ClearDesktopSelection();
                    lastCodexEventId = eventId;
                    selectedCodexAfterMs = CurrentUnixMilliseconds();
                    consumedCodexEventIds.Clear();
                    consumedCodexEventOrder.Clear();
                };
                codexSessionItem.DropDownItems.Add(option);
                shown++;
            }
        }

        private static string CodexSessionsRoot()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrEmpty(home) || !Path.IsPathRooted(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(Path.GetFullPath(home), "sessions");
        }

        private static bool TryReadDesktopSession(string path, string sessionsRoot, out string id, out string cwd, bool mainOnly = false)
        {
            id = null;
            cwd = null;
            string root = Path.GetFullPath(sessionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("rollout-", StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return false;
            byte[] first = new byte[65537];
            int count = 0;
            bool complete = false;
            using (FileStream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                while (count < first.Length)
                {
                    int value = stream.ReadByte();
                    if (value < 0) break;
                    if (value == '\n') { complete = true; break; }
                    first[count++] = (byte)value;
                }
            }
            if (!complete || count < 2 || count > 65536) return false;
            string json = new UTF8Encoding(false, true).GetString(first, 0, count).TrimEnd('\r');
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 32 };
            Dictionary<string, object> record = serializer.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, object> payload = record != null && record.ContainsKey("payload")
                ? record["payload"] as Dictionary<string, object> : null;
            if (ReadString(record, "type") != "session_meta" || ReadString(payload, "originator") != "Codex Desktop") return false;
            Dictionary<string, object> source = payload.ContainsKey("source") ? payload["source"] as Dictionary<string, object> : null;
            if (mainOnly && ((source != null && source.ContainsKey("subagent")) || !String.IsNullOrEmpty(ReadString(payload, "agent_role")))) return false;
            string candidateId = ReadString(payload, "id");
            string candidateCwd = ReadString(payload, "cwd");
            if (!Regex.IsMatch(candidateId, "^[a-f0-9-]{36}$") || !Path.IsPathRooted(candidateCwd)) return false;
            id = candidateId;
            cwd = Path.GetFullPath(candidateCwd);
            return true;
        }

        private static string DesktopSessionHash(string id)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("desktop:" + id))).Replace("-", "").ToLowerInvariant();
        }

        private static string Sha256Text(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        private static string CodexEvidenceJson(string kind, byte[] bytes, int start, int length, long offset, string turnId)
        {
            string digest;
            using (SHA256 hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(bytes, start, length)).Replace("-", "").ToLowerInvariant();
            string turn = Regex.IsMatch(turnId, "^[a-f0-9-]{36}$") ? "\"" + Sha256Text(turnId) + "\"" : "null";
            return "{\"kind\":\"" + kind + "\",\"sha256\":\"" + digest + "\",\"offset\":" +
                offset.ToString(CultureInfo.InvariantCulture) + ",\"length\":" + length.ToString(CultureInfo.InvariantCulture) +
                ",\"turn\":" + turn + "}";
        }

        private static bool ValidCodexEvidence(object raw, bool desktop)
        {
            Dictionary<string, object> evidence = raw as Dictionary<string, object>;
            if (!HasExactKeys(evidence, new string[] { "kind", "sha256", "offset", "length", "turn" }) ||
                ReadString(evidence, "kind") != (desktop ? "desktop-rollout" : "codex-hook") ||
                !Regex.IsMatch(ReadString(evidence, "sha256"), "^[a-f0-9]{64}$") ||
                !(evidence["length"] is int) || (int)evidence["length"] < 2 ||
                (int)evidence["length"] > 4 * 1024 * 1024 ||
                (!(evidence["offset"] is int) && !(evidence["offset"] is long))) return false;
            long offset = Convert.ToInt64(evidence["offset"], CultureInfo.InvariantCulture);
            if (desktop ? offset < 0 : offset != -1) return false;
            return evidence["turn"] == null ? !desktop : Regex.IsMatch(ReadString(evidence, "turn"), "^[a-f0-9]{64}$");
        }

        private static void ClearDesktopSelection()
        {
            pendingDesktopInputCalls.Clear();
            selectedDesktopHookSession = null;
            if (String.IsNullOrEmpty(desktopSelectionFilePath)) return;
            try { if (File.Exists(desktopSelectionFilePath)) File.Delete(desktopSelectionFilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void SaveDesktopSelection(string sessionsRootOverride = null)
        {
            if (String.IsNullOrEmpty(desktopSelectionFilePath) || selectedDesktopRolloutPath == null ||
                selectedCodexSession == null) return;
            string root = sessionsRootOverride ?? CodexSessionsRoot();
            string full = Path.GetFullPath(selectedDesktopRolloutPath);
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) return;
            string relative = full.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
            string json = "{\"version\":1,\"relativeRollout\":\"" + EscapeJson(relative) +
                "\",\"session\":\"" + selectedCodexSession + "\"}";
            Directory.CreateDirectory(Path.GetDirectoryName(desktopSelectionFilePath));
            WriteCodexEventAtomically(desktopSelectionFilePath, json);
        }

        private static void RestoreDesktopSelection(string sessionsRootOverride = null)
        {
            if (String.IsNullOrEmpty(desktopSelectionFilePath) || !File.Exists(desktopSelectionFilePath)) return;
            try
            {
                FileInfo state = new FileInfo(desktopSelectionFilePath);
                if (state.Length < 2 || state.Length > 4096) return;
                string json = File.ReadAllText(desktopSelectionFilePath, new UTF8Encoding(false, true));
                EnsureNoDuplicateJsonObjectKeys(json);
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 };
                Dictionary<string, object> data = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (data == null || data.Count != 3 || Convert.ToInt32(data["version"], CultureInfo.InvariantCulture) != 1) return;
                string relative = ReadString(data, "relativeRollout");
                string expected = ReadString(data, "session");
                if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) ||
                    !Regex.IsMatch(expected, "^[a-f0-9]{64}$")) return;
                string root = sessionsRootOverride ?? CodexSessionsRoot();
                string full = Path.GetFullPath(Path.Combine(root, relative));
                string id, cwd;
                if (!TryReadDesktopSession(full, root, out id, out cwd) || DesktopSessionHash(id) != expected) return;
                selectedDesktopRolloutPath = full;
                selectedDesktopRolloutOffset = new FileInfo(full).Length;
                selectedCodexSession = expected;
                selectedDesktopHookSession = Sha256Text(id);
                selectedCodexAfterMs = CurrentUnixMilliseconds();
                lastDesktopObservedAtMs = selectedCodexAfterMs;
                lastDesktopRecentActivityAtMs = 0;
                skippingDesktopOversizeLine = false;
            }
            catch (Exception) { /* Invalid or removed local selection leaves the pet disconnected. */ }
        }

        private static void PopulateDesktopSessionMenu()
        {
            Forms.ToolStripMenuItem desktop = new Forms.ToolStripMenuItem("Codex Desktop 本机会话（实验）");
            codexSessionItem.DropDownItems.Add(desktop);
            string root = CodexSessionsRoot();
            if (!Directory.Exists(root)) { desktop.Enabled = false; return; }
            FileInfo[] files;
            try { files = new DirectoryInfo(root).GetFiles("rollout-*.jsonl", SearchOption.AllDirectories); }
            catch { desktop.Enabled = false; return; }
            Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            int shown = 0;
            for (int i = 0; i < files.Length && shown < 8; i++)
            {
                if (DateTime.UtcNow - files[i].LastWriteTimeUtc > TimeSpan.FromHours(1) &&
                    !String.Equals(selectedDesktopRolloutPath, files[i].FullName, StringComparison.OrdinalIgnoreCase)) continue;
                string id, cwd;
                try { if (!TryReadDesktopSession(files[i].FullName, root, out id, out cwd)) continue; }
                catch { continue; }
                string selectedPath = files[i].FullName;
                string session = DesktopSessionHash(id);
                string hookSession = Sha256Text(id);
                CodexSessionDisplay display = DescribeCodexSession(id, cwd, files[i].LastWriteTimeUtc);
                string label = display.Title + " · " + display.Context;
                Forms.ToolStripMenuItem option = new Forms.ToolStripMenuItem(label);
                option.Tag = display;
                option.ToolTipText = label;
                option.Checked = String.Equals(selectedDesktopRolloutPath, selectedPath, StringComparison.OrdinalIgnoreCase);
                option.Click += delegate
                {
                    selectedDesktopRolloutPath = selectedPath;
                    selectedDesktopHookSession = hookSession;
                    selectedDesktopRolloutOffset = new FileInfo(selectedPath).Length;
                    skippingDesktopOversizeLine = false;
                    lastDesktopObservedAtMs = CurrentUnixMilliseconds();
                    lastDesktopRecentActivityAtMs = 0;
                    selectedCodexSession = session;
                    selectedCodexAfterMs = CurrentUnixMilliseconds();
                    lastCodexEventId = null;
                    consumedCodexEventIds.Clear();
                    consumedCodexEventOrder.Clear();
                    try { SaveDesktopSelection(); }
                    catch (IOException) { /* Keep the live selection when persistence is unavailable. */ }
                    catch (UnauthorizedAccessException) { /* Keep the live selection when persistence is unavailable. */ }
                };
                desktop.DropDownItems.Add(option);
                shown++;
            }
            if (shown == 0) desktop.DropDownItems.Add(new Forms.ToolStripMenuItem("暂无最近的 Codex Desktop 会话") { Enabled = false });
        }

        private static void WriteDesktopLifecycleEvent(string eventName, string evidence)
        {
            if (automaticCodexObserver != null) { automaticCodexObserver(eventName, evidence); return; }
            if (selectedCodexSession == null) return;
            long observedAt = Math.Max(CurrentUnixMilliseconds(), lastDesktopObservedAtMs + 1);
            lastDesktopObservedAtMs = observedAt;
            string eventId = Guid.NewGuid().ToString("N");
            string safeJson = "{\"version\":2,\"eventId\":\"" + eventId + "\",\"observedAtMs\":" +
                observedAt.ToString(CultureInfo.InvariantCulture) + ",\"event\":\"" + eventName +
                "\",\"session\":\"" + selectedCodexSession + "\",\"evidence\":" + evidence + "}";
            Directory.CreateDirectory(codexEventDirectory);
            string prefix = selectedCodexSession + "." + observedAt.ToString("D13", CultureInfo.InvariantCulture) + "." + eventId;
            WriteCodexEventAtomically(Path.Combine(codexEventDirectory, prefix + ".event.json"), safeJson);
            WriteCodexEventAtomically(Path.Combine(codexEventDirectory, selectedCodexSession + ".json"), safeJson);
            if (eventName == "DesktopToolActivity") lastDesktopToolActivityAtMs = observedAt;
            else lastDesktopLifecycleEventAtMs = observedAt;
            string[] queued = Directory.GetFiles(codexEventDirectory, selectedCodexSession + ".*.event.json");
            Array.Sort(queued, StringComparer.Ordinal);
            long expiry = observedAt - 10L * 60 * 1000;
            for (int i = 0; i < queued.Length; i++)
            {
                string[] parts = Path.GetFileName(queued[i]).Split('.');
                long stamp;
                if (parts.Length >= 4 && Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out stamp) &&
                    stamp < expiry) TryDeleteCodexEvent(queued[i]);
            }
            queued = Directory.GetFiles(codexEventDirectory, selectedCodexSession + ".*.event.json");
            Array.Sort(queued, StringComparer.Ordinal);
            for (int i = 0; i < queued.Length - 256; i++) TryDeleteCodexEvent(queued[i]);
        }

        private static void ReadDesktopLifecycleLine(byte[] bytes, int start, int length, long sourceOffset)
        {
            if (length < 32 || length > 4 * 1024 * 1024) return;
            string line = new UTF8Encoding(false, true).GetString(bytes, start, length).TrimEnd('\r');
            if (line.StartsWith("{\"timestamp\":", StringComparison.Ordinal) && length <= 128 * 1024 &&
                line.IndexOf("\"type\":\"response_item\"", 0, Math.Min(200, line.Length), StringComparison.Ordinal) >= 0) {
                EnsureNoDuplicateJsonObjectKeys(line);
                Dictionary<string, object> response = new JavaScriptSerializer { MaxJsonLength = 128 * 1024, RecursionLimit = 32 }
                    .DeserializeObject(line) as Dictionary<string, object>;
                ObserveDesktopInputRequest(response, bytes, start, length, sourceOffset); return;
            }
            if (!line.StartsWith("{\"timestamp\":", StringComparison.Ordinal) ||
                line.IndexOf("\"type\":\"event_msg\"", 0, Math.Min(200, line.Length), StringComparison.Ordinal) < 0) return;
            bool toolCandidate = line.IndexOf("\"type\":\"item_completed\"", 0,
                Math.Min(260, line.Length), StringComparison.Ordinal) >= 0;
            if (line.IndexOf("\"type\":\"task_started\"", 0, Math.Min(260, line.Length), StringComparison.Ordinal) < 0 &&
                line.IndexOf("\"type\":\"task_complete\"", 0, Math.Min(260, line.Length), StringComparison.Ordinal) < 0 &&
                line.IndexOf("\"type\":\"turn_aborted\"", 0, Math.Min(260, line.Length), StringComparison.Ordinal) < 0 &&
                !toolCandidate) return;
            // Tool records can carry full command output; skip oversized records before parsing.
            if (toolCandidate && length > 256 * 1024) return;
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 32 };
            Dictionary<string, object> record = serializer.DeserializeObject(line) as Dictionary<string, object>;
            Dictionary<string, object> payload = record != null && record.ContainsKey("payload")
                ? record["payload"] as Dictionary<string, object> : null;
            if (ReadString(record, "type") != "event_msg" ||
                !Regex.IsMatch(ReadString(payload, "turn_id"), "^[a-f0-9-]{36}$")) return;
            string kind = ReadString(payload, "type");
            if (kind == "task_started" || kind == "task_complete" || kind == "turn_aborted") pendingDesktopInputCalls.Clear();
            string eventName = kind == "task_started" ? "DesktopTurnStarted"
                : kind == "task_complete" ? "DesktopTurnEndedUnknown"
                : kind == "turn_aborted" && ReadString(payload, "reason") == "interrupted" ? "DesktopTurnInterrupted" : null;
            long now = CurrentUnixMilliseconds();
            if (kind == "item_completed")
            {
                Dictionary<string, object> item = payload.ContainsKey("item")
                    ? payload["item"] as Dictionary<string, object> : null;
                string itemType = ReadString(item, "type");
                if (itemType != "CommandExecution" && itemType != "FileChange" &&
                    itemType != "ImageView" && itemType != "McpToolCall") return;
                long completedAt = Convert.ToInt64(payload["completed_at_ms"], CultureInfo.InvariantCulture);
                if (completedAt > now + 5000 || now - completedAt > 15000 ||
                    now - lastDesktopRecentActivityAtMs < 15000) return;
                eventName = "DesktopToolActivity";
            }
            if (kind == "task_started") lastDesktopRecentActivityAtMs = now;
            else if (eventName == "DesktopToolActivity") lastDesktopRecentActivityAtMs = now;
            if (eventName != null) WriteDesktopLifecycleEvent(eventName,
                CodexEvidenceJson("desktop-rollout", bytes, start, length, sourceOffset, ReadString(payload, "turn_id")));
        }

        private static void PollSelectedDesktopRollout()
        {
            if (selectedDesktopRolloutPath == null) return;
            const int maxChunk = 4 * 1024 * 1024;
            FileInfo file = new FileInfo(selectedDesktopRolloutPath);
            if (!file.Exists || file.Length < selectedDesktopRolloutOffset)
            {
                selectedDesktopRolloutPath = null;
                selectedCodexSession = null;
                ClearDesktopSelection();
                return;
            }
            if (file.Length == selectedDesktopRolloutOffset) return;
            byte[] chunk = new byte[(int)Math.Min(file.Length - selectedDesktopRolloutOffset, maxChunk)];
            using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(selectedDesktopRolloutOffset, SeekOrigin.Begin);
                int total = 0;
                while (total < chunk.Length)
                {
                    int read = stream.Read(chunk, total, chunk.Length - total);
                    if (read == 0) break;
                    total += read;
                }
                if (total < chunk.Length) Array.Resize(ref chunk, total);
            }
            int lastNewline = Array.LastIndexOf(chunk, (byte)'\n');
            if (lastNewline < 0)
            {
                if (chunk.Length == maxChunk)
                {
                    selectedDesktopRolloutOffset += chunk.Length;
                    skippingDesktopOversizeLine = true;
                }
                return;
            }
            long chunkOffset = selectedDesktopRolloutOffset;
            selectedDesktopRolloutOffset += lastNewline + 1;
            int start = 0;
            for (int i = 0; i <= lastNewline; i++)
            {
                if (chunk[i] != (byte)'\n') continue;
                if (!skippingDesktopOversizeLine)
                {
                    try { ReadDesktopLifecycleLine(chunk, start, i - start, chunkOffset + start); }
                    catch (Exception) { /* Ignore only malformed/incompatible rollout records. */ }
                }
                skippingDesktopOversizeLine = false;
                start = i + 1;
            }
        }

        private static bool ConsumeSelectedCodexEvent()
        {
            if (selectedCodexSession == null || !Regex.IsMatch(selectedCodexSession, "^[a-f0-9]{64}$") ||
                !Directory.Exists(codexEventDirectory)) return false;
            string[] files;
            try {
                List<string> selectedFiles = new List<string>(Directory.GetFiles(codexEventDirectory, selectedCodexSession + ".*.event.json"));
                if (selectedDesktopRolloutPath != null && selectedDesktopHookSession != null)
                    selectedFiles.AddRange(Directory.GetFiles(codexEventDirectory, selectedDesktopHookSession + ".*.event.json"));
                files = selectedFiles.ToArray();
            }
            catch { return false; }
            // Hash prefixes differ between channels; merge by timestamp, then event id.
            Array.Sort(files, delegate(string left, string right) {
                string a = Path.GetFileName(left), b = Path.GetFileName(right);
                return StringComparer.Ordinal.Compare(a.Substring(a.IndexOf('.') + 1), b.Substring(b.IndexOf('.') + 1));
            });
            Dictionary<string, object> record = null;
            for (int i = 0; i < files.Length; i++)
            {
                Dictionary<string, object> candidate;
                try { candidate = ReadCodexEvent(files[i]); }
                catch { continue; }
                if (!IsSelectedCodexEvent(candidate)) continue;
                string candidateId = ReadString(candidate, "eventId");
                if (consumedCodexEventIds.Contains(candidateId)) continue;
                long timestamp = Convert.ToInt64(candidate["observedAtMs"], CultureInfo.InvariantCulture);
                long age = CurrentUnixMilliseconds() - timestamp;
                if (timestamp > selectedCodexAfterMs && ReadString(candidate, "event") == "DesktopTurnEndedUnknown" && age < 250)
                    return false;
                consumedCodexEventIds.Add(candidateId);
                consumedCodexEventOrder.Enqueue(candidateId);
                if (consumedCodexEventOrder.Count > 512)
                    consumedCodexEventIds.Remove(consumedCodexEventOrder.Dequeue());
                if (timestamp <= selectedCodexAfterMs || age > 10000) continue;
                if (ReadString(candidate, "event") == "DesktopTurnEndedUnknown")
                {
                    // Goal mode may finish one turn and start the next within milliseconds.
                    // In that case the new activity is the useful visible state.
                    for (int nextIndex = i + 1; nextIndex < files.Length; nextIndex++)
                    {
                        Dictionary<string, object> next;
                        try { next = ReadCodexEvent(files[nextIndex]); }
                        catch { continue; }
                        if (!IsSelectedCodexEvent(next)) continue;
                        long nextTimestamp = Convert.ToInt64(next["observedAtMs"], CultureInfo.InvariantCulture);
                        if (nextTimestamp < timestamp || nextTimestamp - timestamp > 1000 ||
                            ReadString(next, "event") != "DesktopTurnStarted") break;
                        string nextId = ReadString(next, "eventId");
                        if (consumedCodexEventIds.Contains(nextId)) break;
                        consumedCodexEventIds.Add(nextId);
                        consumedCodexEventOrder.Enqueue(nextId);
                        if (consumedCodexEventOrder.Count > 512)
                            consumedCodexEventIds.Remove(consumedCodexEventOrder.Dequeue());
                        candidate = next;
                        candidateId = nextId;
                        i = nextIndex;
                        break;
                    }
                }
                record = candidate;
                lastCodexEventId = candidateId;
                break;
            }
            if (record == null) return false;
            string eventName = ReadString(record, "event");
            if (connectedAiTool == "codex" && selectedCodexSession == connectedAiSession) {
                if (eventName == "SessionEnd") { SaveAiConnection(null, null); return true; }
                string normalized = SimpleCodexState(eventName);
                if (normalized == null) return false;
                bool accepted = ApplyAiEvent(new Dictionary<string, object> {
                    {"version", 1}, {"eventId", ReadString(record, "eventId")},
                    {"observedAtMs", record["observedAtMs"]}, {"tool", "codex"},
                    {"session", connectedAiSession}, {"state", normalized}
                });
                if (accepted) {
                    lastCodexActionAtMs = CurrentUnixMilliseconds(); lastCodexActionEventId = ReadString(record, "eventId");
                    lastCodexActionSession = ReadString(record, "session");
                    lastCodexActionEvidence = record.ContainsKey("evidence") ? record["evidence"] : null;
                    lastCodexActionSource = eventName.StartsWith("Desktop", StringComparison.Ordinal) ? "desktop-rollout" : "codex-hook";
                    lastCodexRequestedSlot = aiBindings[normalized]; lastCodexActionOutcome = lastAiOutcome;
                    lastCodexPlayedSlot = lastAiOutcome == "played" ? aiBindings[normalized] : null;
                }
                return accepted;
            }
            if (eventName == "SessionEnd")
            {
                selectedCodexSession = null;
                selectedDesktopRolloutPath = null;
                ClearDesktopSelection();
                consumedCodexEventIds.Clear();
                consumedCodexEventOrder.Clear();
                activeThemeOneShot = false;
                SetThemeAction("idle", false);
                return true;
            }
            lastCodexActionEventId = ReadString(record, "eventId");
            lastCodexActionSession = ReadString(record, "session");
            lastCodexActionEvidence = record.ContainsKey("evidence") ? record["evidence"] : null;
            string slot = eventName == "PermissionRequest" || eventName == "DesktopUserInputRequested" ? "needs-user"
                : eventName == "Stop" || eventName == "DesktopTurnEndedUnknown" ? "stop-unknown"
                : eventName == "Interrupt" || eventName == "DesktopTurnInterrupted" ? "interrupt" : "recent-activity";
            string state = eventName == "PermissionRequest" || eventName == "DesktopUserInputRequested" ? "needs-user"
                : eventName == "DesktopTurnStarted" ? "started"
                : eventName == "DesktopTurnEndedUnknown" ? "finished"
                : eventName == "Interrupt" || eventName == "DesktopTurnInterrupted" ? "paused"
                : eventName == "Stop" ? null : "running";
            if (state != null && aiBindings.ContainsKey(state)) slot = aiBindings[state];
            lastCodexActionAtMs = Math.Max(CurrentUnixMilliseconds(), lastCodexActionAtMs + 1);
            lastCodexActionSource = eventName.StartsWith("Desktop", StringComparison.Ordinal)
                ? "desktop-rollout" : "codex-hook";
            lastCodexRequestedSlot = slot;
            lastCodexPlayedSlot = null;
            if (IsWithinQuietHours(preferences, DateTime.Now.TimeOfDay))
            {
                lastCodexActionOutcome = "quiet-hours";
                return false;
            }
            bool started = SetThemeAction(slot, true);
            if (started && bundledMotionPreview && lastBundledPreviewGesture == slot)
                lastCodexPlayedSlot = slot;
            else if (started && activeThemeAction != null && activeThemeAction.Slot == slot)
                lastCodexPlayedSlot = slot;
            lastCodexActionOutcome = lastCodexPlayedSlot == null ? "no-distinct-action" : "played";
            return true;
        }

        private static bool IsSelectedCodexEvent(Dictionary<string, object> record)
        {
            if (record == null) return false;
            string session = ReadString(record, "session");
            bool desktop = ReadString(record, "event").StartsWith("Desktop", StringComparison.Ordinal);
            if (selectedDesktopRolloutPath == null) return session == selectedCodexSession;
            return desktop ? session == selectedCodexSession :
                selectedDesktopHookSession != null && session == selectedDesktopHookSession;
        }

        private static void StartCodexEventPolling()
        {
            codexEventTimer = new DispatcherTimer();
            codexEventTimer.Interval = TimeSpan.FromMilliseconds(500);
            codexEventTimer.Tick += delegate {
                PollAiConnection();
                string pollStatus = "ok";
                try { if (connectedAiTool == null && codexFollowMode != "auto") PollSelectedDesktopRollout(); }
                catch (IOException) { pollStatus = "retry-io"; }
                catch (UnauthorizedAccessException) { pollStatus = "retry-access"; }
                catch (Exception) { pollStatus = "retry-invalid-record"; }
                try { if (connectedAiTool == null && codexFollowMode != "auto") ConsumeSelectedCodexEvent(); }
                catch (IOException) { pollStatus = "retry-event-io"; }
                catch (UnauthorizedAccessException) { pollStatus = "retry-event-access"; }
                catch (Exception) { pollStatus = "retry-event-invalid-record"; }
                WriteDesktopWatchStatus(pollStatus);
            };
            codexEventTimer.Start();
        }

        private static void WriteDesktopWatchStatus(string pollStatus)
        {
            long now = CurrentUnixMilliseconds();
            if (now - lastDesktopWatchStatusAtMs < 10000 && pollStatus == lastDesktopWatchStatus &&
                lastCodexActionAtMs == lastReportedCodexActionAtMs) return;
            lastDesktopWatchStatusAtMs = now;
            lastDesktopWatchStatus = pollStatus;
            try
            {
                long size = -1;
                if (selectedDesktopRolloutPath != null)
                {
                    FileInfo file = new FileInfo(selectedDesktopRolloutPath);
                    if (file.Exists) size = file.Length;
                }
                string session = selectedDesktopRolloutPath != null ? selectedCodexSession : null;
                string json = "{\"version\":3,\"checkedAtMs\":" + now.ToString(CultureInfo.InvariantCulture) +
                    ",\"status\":\"" + pollStatus + "\",\"session\":" +
                    (session == null ? "null" : "\"" + session + "\"") +
                    ",\"readOffset\":" + selectedDesktopRolloutOffset.ToString(CultureInfo.InvariantCulture) +
                    ",\"fileSize\":" + size.ToString(CultureInfo.InvariantCulture) +
                    ",\"lastLifecycleEventAtMs\":" + lastDesktopLifecycleEventAtMs.ToString(CultureInfo.InvariantCulture) +
                    ",\"lastToolActivityAtMs\":" + lastDesktopToolActivityAtMs.ToString(CultureInfo.InvariantCulture) +
                    ",\"lastActionAtMs\":" + lastCodexActionAtMs.ToString(CultureInfo.InvariantCulture) +
                    ",\"lastActionEventId\":" + new JavaScriptSerializer().Serialize(lastCodexActionEventId) +
                    ",\"lastActionSession\":" + new JavaScriptSerializer().Serialize(lastCodexActionSession) +
                    ",\"lastActionEvidence\":" + new JavaScriptSerializer().Serialize(lastCodexActionEvidence) +
                    ",\"lastActionSource\":" + (lastCodexActionSource == null ? "null" : "\"" + lastCodexActionSource + "\"") +
                    ",\"lastRequestedSlot\":" + (lastCodexRequestedSlot == null ? "null" : "\"" + lastCodexRequestedSlot + "\"") +
                    ",\"lastPlayedSlot\":" + (lastCodexPlayedSlot == null ? "null" : "\"" + lastCodexPlayedSlot + "\"") +
                    ",\"lastActionOutcome\":" + (lastCodexActionOutcome == null ? "null" : "\"" + lastCodexActionOutcome + "\"") + "}";
                Directory.CreateDirectory(Path.GetDirectoryName(desktopWatchStatusFilePath));
                WriteCodexEventAtomically(desktopWatchStatusFilePath, json);
                lastReportedCodexActionAtMs = lastCodexActionAtMs;
            }
            catch (Exception) { /* Diagnostics must not affect the desktop pet. */ }
        }

        private static bool CanStartAutomaticAction(ThemePackage theme, LocalPreferences settings,
            DateTime nowLocal, DateTime lastActionUtc, bool actionInProgress)
        {
            if (theme == null || theme.FindAction("ambient") == null || settings == null ||
                !settings.AutomaticActionsEnabled || actionInProgress || IsWithinQuietHours(settings, nowLocal.TimeOfDay)) return false;
            return nowLocal.ToUniversalTime() - lastActionUtc >= TimeSpan.FromMinutes(settings.FrequencyMinutes);
        }

        private static bool TryStartAutomaticAction(DateTime nowLocal)
        {
            if (window == null || !window.IsVisible)
            {
                lastAutomaticActionUtc = nowLocal.ToUniversalTime();
                return false;
            }
            bool actionInProgress = activeThemeAction != null && activeThemeAction.Slot != "idle";
            if (!CanStartAutomaticAction(activeTheme, preferences, nowLocal, lastAutomaticActionUtc, actionInProgress)) return false;
            if (!SetThemeAction("ambient", true)) return false;
            lastAutomaticActionUtc = nowLocal.ToUniversalTime();
            return true;
        }

        private static void StartAutomaticActionPolling()
        {
            automaticActionTimer = new DispatcherTimer();
            automaticActionTimer.Interval = TimeSpan.FromMinutes(1);
            automaticActionTimer.Tick += delegate { TryStartAutomaticAction(DateTime.Now); };
            automaticActionTimer.Start();
        }

        private static Forms.Screen FindScreen(string deviceName)
        {
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
                if (!String.IsNullOrEmpty(deviceName) && screen.DeviceName == deviceName) return screen;
            return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        }

        private static bool ScreenExists(string deviceName)
        {
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
                if (screen.DeviceName == deviceName) return true;
            return false;
        }

        private static SavedPlacement ReadPlacementAt(string path)
        {
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException("窗口位置文件过大。");
            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            EnsureNoDuplicateJsonObjectKeys(json);
            Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (!HasExactKeys(root, new string[] { "left", "screen", "top", "version" }) ||
                Convert.ToInt32(root["version"]) != 1 || !(root["screen"] is string) ||
                !IsPlacementNumber(root["left"]) || !IsPlacementNumber(root["top"]))
                throw new InvalidDataException("窗口位置文件字段不符合规范。");
            SavedPlacement placement = new SavedPlacement {
                ScreenName = (string)root["screen"],
                Left = Convert.ToDouble(root["left"], CultureInfo.InvariantCulture),
                Top = Convert.ToDouble(root["top"], CultureInfo.InvariantCulture)
            };
            if (placement.ScreenName.Length > 128 || Double.IsNaN(placement.Left) || Double.IsInfinity(placement.Left) ||
                Double.IsNaN(placement.Top) || Double.IsInfinity(placement.Top) ||
                Math.Abs(placement.Left) > 1000000 || Math.Abs(placement.Top) > 1000000)
                throw new InvalidDataException("窗口位置超出允许范围。");
            return placement;
        }

        private static bool IsPlacementNumber(object value)
        {
            return value is int || value is long || value is double || value is decimal;
        }

        private static void PersistPlacementAt(string path, SavedPlacement placement)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string json = "{\"left\":" + placement.Left.ToString("R", CultureInfo.InvariantCulture) +
                ",\"screen\":\"" + EscapeJson(placement.ScreenName ?? String.Empty) +
                "\",\"top\":" + placement.Top.ToString("R", CultureInfo.InvariantCulture) + ",\"version\":1}";
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }

        private static void TrySaveCurrentPlacement()
        {
            if (!persistRuntimePlacement || String.IsNullOrEmpty(placementFilePath) || window == null || !window.IsLoaded) return;
            try
            {
                PersistPlacementAt(placementFilePath, new SavedPlacement {
                    ScreenName = WindowScreenName(), Left = window.Left, Top = window.Top
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static System.Windows.Rect ScreenWorkAreaDips(Forms.Screen screen)
        {
            System.Windows.Media.Matrix fromDevice = System.Windows.Media.Matrix.Identity;
            PresentationSource source = PresentationSource.FromVisual(window);
            if (source != null && source.CompositionTarget != null)
                fromDevice = source.CompositionTarget.TransformFromDevice;
            System.Drawing.Rectangle pixels = screen.WorkingArea;
            System.Windows.Point start = fromDevice.Transform(new System.Windows.Point(pixels.Left, pixels.Top));
            System.Windows.Point end = fromDevice.Transform(new System.Windows.Point(pixels.Right, pixels.Bottom));
            return new System.Windows.Rect(start, end);
        }

        private static System.Windows.Point ResolveWindowPosition(System.Windows.Rect area, double width, double height,
            double desiredLeft, double desiredTop)
        {
            if (Double.IsNaN(width) || Double.IsInfinity(width) || Double.IsNaN(height) || Double.IsInfinity(height) ||
                width <= 0 || height <= 0 ||
                area.Width <= 0 || area.Height <= 0)
                throw new InvalidDataException("显示器工作区或窗口尺寸无效。");
            double maxLeft = Math.Max(area.Left, area.Right - width);
            double maxTop = Math.Max(area.Top, area.Bottom - height);
            double defaultLeft = area.Right - width - 22;
            double defaultTop = area.Bottom - height - 28;
            double left = Double.IsNaN(desiredLeft) || Double.IsInfinity(desiredLeft) ? defaultLeft : desiredLeft;
            double top = Double.IsNaN(desiredTop) || Double.IsInfinity(desiredTop) ? defaultTop : desiredTop;
            return new System.Windows.Point(Math.Max(area.Left, Math.Min(maxLeft, left)),
                                            Math.Max(area.Top, Math.Min(maxTop, top)));
        }

        private static string WindowScreenName()
        {
            try
            {
                System.Windows.Point center = window.PointToScreen(new System.Windows.Point(window.Width / 2, window.Height / 2));
                return Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(center.X), (int)Math.Round(center.Y))).DeviceName;
            }
            catch { return preferredScreenName; }
        }

        private static void PopulateScreenMenu()
        {
            moveScreenItem.DropDownItems.Clear();
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            for (int index = 0; index < screens.Length; index++)
            {
                Forms.Screen target = screens[index];
                string label = "显示器 " + (index + 1) + (target.Primary ? " · 主屏" : "") +
                    " · " + target.Bounds.Width + "×" + target.Bounds.Height;
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(label);
                item.Checked = target.DeviceName == WindowScreenName();
                item.Click += delegate
                {
                    MovePetToScreen(target);
                };
                moveScreenItem.DropDownItems.Add(item);
            }
        }

        private static void BuildWindow(string photoPath, double preservedLeft = Double.NaN,
            double preservedTop = Double.NaN, string preservedScreenName = null)
        {
            waitingForTransparentTheme = runtimeSpriteOnly && activeTheme == null;
            window = new Window();
            AttachDisplayRecovery(window);
            window.ShowActivated = false;
            window.Title = "AI Pet Studio · 桌面预览";
            window.Width = 234;
            window.Height = 366;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = MediaBrushes.Transparent;
            window.ShowInTaskbar = false;
            window.Topmost = true;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.SourceInitialized += delegate
            {
                string targetScreenName = preservedScreenName ?? preferredScreenName;
                Forms.Screen screen = FindScreen(targetScreenName);
                bool missingScreen = !String.IsNullOrEmpty(targetScreenName) && !ScreenExists(targetScreenName);
                System.Windows.Point position = ResolveWindowPosition(ScreenWorkAreaDips(screen),
                    window.Width, window.Height, missingScreen ? Double.NaN : preservedLeft,
                    missingScreen ? Double.NaN : preservedTop);
                window.Left = position.X;
                window.Top = position.Y;
            };
            window.PreviewMouseRightButtonUp += delegate { trayMenu.Show(Forms.Cursor.Position); };
            window.Closing += OnWindowClosing;

            if (activeTheme != null)
            {
                BuildThemeSpriteWindow();
                return;
            }

            // Photo cards remain available only to explicit legacy review commands.
            // A missing, suspended or rejected runtime theme stays in the tray.
            if (waitingForTransparentTheme)
            {
                photoFrame = null;
                greeting = null;
                themeImage = null;
                window.Width = 190;
                window.Height = 254;
                window.Content = new Grid();
                return;
            }

            Border card = new Border();
            card.Margin = new Thickness(8);
            card.Padding = new Thickness(10);
            card.CornerRadius = new CornerRadius(15);
            card.Background = new SolidColorBrush(MediaColor.FromRgb(255, 254, 250));
            card.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(232, 228, 218));
            card.BorderThickness = new Thickness(1);
            card.Effect = new DropShadowEffect { Color = MediaColor.FromRgb(55, 47, 37), BlurRadius = 18, ShadowDepth = 4, Opacity = 0.22 };

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(254) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
            header.Cursor = Cursors.SizeAll;
            TextBlock title = new TextBlock();
            title.Text = "✳  " + (activeRole == null ? "橘子" : activeRole.Name) + "的小小空间";
            title.VerticalAlignment = VerticalAlignment.Center;
            title.FontSize = 10;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = new SolidColorBrush(MediaColor.FromRgb(83, 79, 68));
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    window.DragMove();
                    TrySaveCurrentPlacement();
                }
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            Button hideButton = new Button();
            hideButton.Content = "×";
            hideButton.FontSize = 15;
            hideButton.Foreground = new SolidColorBrush(MediaColor.FromRgb(145, 139, 127));
            hideButton.Background = MediaBrushes.Transparent;
            hideButton.BorderThickness = new Thickness(0);
            hideButton.ToolTip = "隐藏到系统托盘";
            hideButton.Click += delegate { window.Hide(); UpdateVisibilityLabel(); };
            Grid.SetColumn(hideButton, 1);
            header.Children.Add(hideButton);
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            petBitmap = activeRole == null ? LoadReferenceBitmap(photoPath) : activeRole.PhotoBitmap;

            photoFrame = new Border();
            photoFrame.Width = 190;
            photoFrame.Height = 254;
            photoFrame.HorizontalAlignment = HorizontalAlignment.Center;
            photoFrame.VerticalAlignment = VerticalAlignment.Center;
            photoFrame.CornerRadius = new CornerRadius(9);
            photoFrame.Background = new SolidColorBrush(MediaColor.FromRgb(241, 234, 221));
            photoFrame.ClipToBounds = true;
            photoFrame.Cursor = Cursors.Hand;
            photoFrame.ToolTip = activeRole == null
                ? "单击向小伙伴问好，拖动照片卡移动；示例猫身被右侧画框裁断并与木架相连，画面外的身体不会自动补画。"
                : "单击向小伙伴问好，拖动照片卡移动；角色卡记录的可见部位由用户确认，附带 " + activeRole.ReferencePhotos.Count
                    + " 张补充视角；照片主体数量：" + PhotoReviewLabel(activeRole.SubjectCountReview)
                    + "；遮挡/裁切：" + PhotoReviewLabel(activeRole.OcclusionReview)
                    + "（由用户确认）；当前动画生成服务仍只读取主照片。";
            WpfImage photo = new WpfImage();
            photo.Source = petBitmap;
            photo.Stretch = Stretch.UniformToFill;
            photo.Width = 190;
            photo.Height = 254;
            photoFrame.Child = photo;
            photoFrame.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                BeginPhotoPointer(e.GetPosition(window));
                photoFrame.CaptureMouse();
                e.Handled = true;
            };
            photoFrame.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!photoPointerPressed || e.LeftButton != MouseButtonState.Pressed ||
                    !PointerDragThresholdExceeded(photoPointerDown, e.GetPosition(window))) return;
                photoPointerPressed = false;
                Vector initialDrag = e.GetPosition(window) - photoPointerDown;
                if (photoFrame.IsMouseCaptured) photoFrame.ReleaseMouseCapture();
                window.Left += initialDrag.X;
                window.Top += initialDrag.Y;
                TraceFloatingReview("drag-start");
                try { window.DragMove(); }
                catch (InvalidOperationException) { /* A release during drag startup is not a click. */ }
                finally { TrySaveCurrentPlacement(); TraceFloatingReview("drag-end"); }
                e.Handled = true;
            };
            photoFrame.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (photoFrame.IsMouseCaptured) photoFrame.ReleaseMouseCapture();
                CompletePhotoPointer(e.GetPosition(window));
                e.Handled = true;
            };
            Grid.SetRow(photoFrame, 1);
            layout.Children.Add(photoFrame);

            StackPanel footer = new StackPanel();
            footer.VerticalAlignment = VerticalAlignment.Center;
            footer.Margin = new Thickness(1, 3, 1, 0);
            greeting = new TextBlock();
            greeting.Text = "静态照片陪伴 · 左键问候 · 右键菜单";
            greeting.FontSize = 8;
            greeting.TextAlignment = TextAlignment.Center;
            greeting.Foreground = new SolidColorBrush(MediaColor.FromRgb(135, 128, 113));
            footer.Children.Add(greeting);
            TextBlock runtimeStatus = new TextBlock();
            runtimeStatus.Text = activeRole == null ? "示例猫身被画框裁切 · 照片不会上传"
                : Array.IndexOf(activeRole.SourceEvidence, "fullSilhouette") >= 0
                ? "用户确认照片完整轮廓 · 素材仍待审" : "完整轮廓未确认 · 全身需推断";
            runtimeStatus.ToolTip = activeRole == null
                ? "这张样例只适合确认脸部、花纹和项圈；身体右侧被裁断并与木架相连。"
                : "角色卡照片保存在本机，不会上传；未拍到的部位不能作为真实宠物证据。";
            runtimeStatus.Margin = new Thickness(0, 4, 0, 0);
            runtimeStatus.FontSize = 7;
            runtimeStatus.TextAlignment = TextAlignment.Center;
            runtimeStatus.Foreground = new SolidColorBrush(MediaColor.FromRgb(168, 160, 145));
            footer.Children.Add(runtimeStatus);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            card.Child = layout;
            window.Content = card;
        }

        private static void BuildThemeSpriteWindow()
        {
            ThemeAction idle = activeTheme.IdleAction;
            if (idle == null) throw new InvalidDataException("活动主题缺少 idle 动作。");
            photoFrame = null;
            if (permissionCueTimer != null) permissionCueTimer.Stop();
            permissionCue = null;
            permissionCueTimer = null;
            activeThemeOneShot = false;
            themePointerPressed = false;
            window.Width = bundledMotionPreview ? 214 : 190;
            window.Height = bundledMotionPreview ? 280 : 254;
            window.Title = bundledMotionPreview ? "AI Pet Studio · 猫咪动态预览" : "AI Pet Studio · " + activeTheme.Name;
            themeImage = new WpfImage();
            themeImage.Width = 190;
            themeImage.Height = 254;
            themeImage.Stretch = Stretch.Uniform;
            themeImage.HorizontalAlignment = HorizontalAlignment.Center;
            themeImage.VerticalAlignment = VerticalAlignment.Center;
            // Keep the floating character unobstructed during hover animation.
            if (bundledMotionPreview)
            {
                bundledPreviewRotation = new RotateTransform();
                bundledPreviewScale = new ScaleTransform();
                bundledPreviewOffset = new TranslateTransform();
                TranslateTransform floating = new TranslateTransform();
                TransformGroup motion = new TransformGroup();
                motion.Children.Add(bundledPreviewScale);
                motion.Children.Add(bundledPreviewRotation);
                motion.Children.Add(bundledPreviewOffset);
                motion.Children.Add(floating);
                themeImage.RenderTransformOrigin = new Point(0.5, 0.55);
                themeImage.RenderTransform = motion;
                DoubleAnimation drift = new DoubleAnimation(3, -8, TimeSpan.FromSeconds(1.45));
                drift.AutoReverse = true;
                drift.RepeatBehavior = RepeatBehavior.Forever;
                drift.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                floating.BeginAnimation(TranslateTransform.YProperty, drift);
            }
            themeImage.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                BeginThemePointer(e.GetPosition(window));
                themeImage.CaptureMouse();
                e.Handled = true;
            };
            themeImage.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!themePointerPressed || e.LeftButton != MouseButtonState.Pressed ||
                    !PointerDragThresholdExceeded(themePointerDown, e.GetPosition(window))) return;
                themePointerPressed = false;
                // Retain motion already accumulated before the system drag starts.
                // A fast drag can otherwise reach its endpoint before DragMove.
                Vector initialDrag = e.GetPosition(window) - themePointerDown;
                if (themeImage.IsMouseCaptured) themeImage.ReleaseMouseCapture();
                window.Left += initialDrag.X;
                window.Top += initialDrag.Y;
                TraceFloatingReview("drag-start");
                try { window.DragMove(); }
                catch (InvalidOperationException) { /* A release during drag startup is not a click. */ }
                finally { TrySaveCurrentPlacement(); TraceFloatingReview("drag-end"); }
                e.Handled = true;
            };
            themeImage.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                Point releasedAt = e.GetPosition(window);
                if (themeImage.IsMouseCaptured) themeImage.ReleaseMouseCapture();
                CompleteThemePointer(releasedAt);
                e.Handled = true;
            };
            themeImage.MouseEnter += delegate { if (preferences.InteractionEnabled && !HasConnectedThemeState()) SetThemeAction("hover", false); };
            themeImage.MouseLeave += delegate { if (!HasConnectedThemeState()) SetThemeAction("idle", false); };
            Grid floatingLayer = new Grid();
            floatingLayer.Children.Add(themeImage);
            TextBlock cueText = new TextBlock { Text = runtimeSpriteOnly ? "需要你回答或确认" : "审核演示 · 权限请求", Foreground = MediaBrushes.White,
                FontSize = 10, FontWeight = FontWeights.Medium };
            permissionCue = new Border { Child = cueText,
                Background = new SolidColorBrush(MediaColor.FromArgb(226, 31, 37, 43)),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 9, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            floatingLayer.Children.Add(permissionCue);
            BuildPetActivityCue(floatingLayer);
            window.Content = floatingLayer;
            SetThemeAction("idle", false);
        }

        private static string floatingReviewTracePath;
        private static System.Diagnostics.Stopwatch floatingReviewClock;
        private static bool floatingReviewTraceFailed;

        private static void TraceFloatingReview(string kind)
        {
            if (floatingReviewTracePath == null) return;
            try
            {
                string slot = activeThemeAction == null ? "" : activeThemeAction.Slot;
                File.AppendAllText(floatingReviewTracePath, "{\"event\":\"" + kind
                    + "\",\"elapsedMs\":" + floatingReviewClock.ElapsedMilliseconds
                    + ",\"slot\":\"" + EscapeJson(slot) + "\",\"frame\":" + themeFrameIndex
                    + ",\"oneShot\":" + activeThemeOneShot.ToString().ToLowerInvariant()
                    + ",\"left\":" + (Double.IsNaN(window.Left) ? "null" : window.Left.ToString(CultureInfo.InvariantCulture))
                    + ",\"top\":" + (Double.IsNaN(window.Top) ? "null" : window.Top.ToString(CultureInfo.InvariantCulture)) + "}\n",
                    new UTF8Encoding(false));
            }
            catch { floatingReviewTraceFailed = true; }
        }

        private static bool PointerDragThresholdExceeded(Point start, Point current)
        {
            return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private static void BeginThemePointer(Point point)
        {
            themePointerDown = point;
            themePointerPressed = true;
            TraceFloatingReview("pointer-down");
        }

        private static void BeginPhotoPointer(Point point)
        {
            photoPointerDown = point;
            photoPointerPressed = true;
        }

        private static bool CompletePhotoPointer(Point point)
        {
            bool clicked = photoPointerPressed && !PointerDragThresholdExceeded(photoPointerDown, point);
            photoPointerPressed = false;
            if (clicked) HandlePhotoInteraction();
            return clicked;
        }

        private static bool CompleteThemePointer(Point point)
        {
            bool clicked = themePointerPressed && !PointerDragThresholdExceeded(themePointerDown, point);
            themePointerPressed = false;
            if (clicked && preferences.InteractionEnabled) SetThemeAction("click", true);
            TraceFloatingReview(clicked ? "pointer-click" : "pointer-release-no-click");
            return clicked;
        }

        private static Border petActivityCue;
        private static TextBlock petActivityText;
        private static Border petActivityDot;
        private static TextBlock petActivityTitle;
        private static TextBlock petActivityTool;
        private static System.Windows.Shapes.Polygon petActivityTail;
        private static DispatcherTimer petActivityTimer;
        private static string petActivityLabel;
        private static bool petActivityAnimating;
        private static int petActivityPhase;

        private static bool HasPersistentAiAnimation()
        {
            return HasConnectedThemeState() && (lastAiState == "started" || lastAiState == "running" || lastAiState == "needs-user");
        }

        private static bool HasConnectedThemeState()
        {
            return connectedAiTool != null && Array.IndexOf(AiStates, lastAiState) >= 0 &&
                lastAiOutcome != "signal-stale" && lastAiOutcome != "hidden" && lastAiOutcome != "quiet-hours" &&
                window != null && window.IsVisible && !IsWithinQuietHours(preferences, DateTime.Now.TimeOfDay);
        }

        private static void ResumeConnectedThemeOrIdle()
        {
            if (HasConnectedThemeState() && aiBindings.ContainsKey(lastAiState)) {
                SetThemeAction(aiBindings[lastAiState], false);
                if ((lastAiState == "paused" || lastAiState == "finished") && activeThemeAction != null) {
                    if (themeTimer != null) themeTimer.Stop();
                    int first, count; double fps; ThemePlaybackTiming(activeThemeAction, out first, out count, out fps);
                    themeFrameIndex = first + count - 1; themeImage.Source = activeThemeAction.Frames[themeFrameIndex];
                    activeThemeOneShot = false;
                }
            } else SetThemeAction("idle", false);
        }

        private static void ThemePlaybackTiming(ThemeAction action, out int first, out int count, out double fps, ThemePackage package = null)
        {
            first = 0; count = action.Frames.Count; fps = action.FramesPerSecond;
            package = package ?? activeTheme;
            if (package == null || !package.CompactMotion) return;
            // Keep all source images intact. Trim only the slow anticipation and
            // near-static tail of this known package; do not guess for user themes.
            switch (action.Slot) {
                case "recent-activity": first = 1; count = 42; fps = 30; break;
                case "hover": first = 1; count = 42; fps = 28; break;
                case "click": case "needs-user": first = 5; count = 45; fps = 30; break;
                case "interrupt": first = 3; count = 32; fps = 30; break;
                case "stop-unknown": first = 6; count = 54; fps = 30; break;
            }
        }

        private static void BuildPetActivityCue(Grid layer)
        {
            if (petActivityTimer != null) petActivityTimer.Stop();
            petActivityAnimating = false; petActivityPhase = 0; petActivityLabel = null;
            petActivityTitle = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 28 };
            petActivityText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Width = 112,
                VerticalAlignment = VerticalAlignment.Center };
            petActivityDot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            petActivityTool = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right };
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0), Height = 18 };
            row.Children.Add(petActivityDot); row.Children.Add(petActivityText);
            row.Children.Add(petActivityTool);
            StackPanel body = new StackPanel(); body.Children.Add(petActivityTitle); body.Children.Add(row);
            petActivityCue = new Border { Child = body, CornerRadius = new CornerRadius(15), Width = 182,
                BorderThickness = new Thickness(1), Padding = new Thickness(11, 7, 11, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            petActivityTail = new System.Windows.Shapes.Polygon { Points = new PointCollection {
                new Point(29, 0), new Point(39, 9), new Point(44, 0) }, Height = 9, Width = 182,
                HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            StackPanel bubble = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false };
            bubble.Children.Add(petActivityCue); bubble.Children.Add(petActivityTail); layer.Children.Add(bubble);
            petActivityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            petActivityTimer.Tick += delegate {
                if (!petActivityAnimating || window == null || !window.IsVisible || petActivityCue.Visibility != Visibility.Visible) {
                    petActivityTimer.Stop(); return;
                }
                petActivityPhase = (petActivityPhase + 1) % 4;
                petActivityText.Text = petActivityLabel + new string('.', petActivityPhase);
            };
            DispatcherTimer ownedTimer = petActivityTimer;
            window.Closed += delegate { ownedTimer.Stop(); };
            window.IsVisibleChanged += delegate { UpdatePetActivityCue(); };
        }

        private static void UpdatePetActivityCue()
        {
            if (petActivityCue == null) return;
            string label = null;
            bool dark = PersonalizeValue("AppsUseLightTheme", 1) == 0;
            bool contrast = SystemParameters.HighContrast;
            MediaColor color = dark ? MediaColor.FromRgb(179, 188, 201) : MediaColor.FromRgb(88, 101, 121);
            if (connectedAiTool != null) {
                if (permissionCue != null) permissionCue.Visibility = Visibility.Collapsed;
                if (lastAiOutcome == "signal-stale") label = "状态待确认";
                else if (lastAiOutcome != "hidden" && lastAiOutcome != "quiet-hours") {
                    if (lastAiState == null) label = "等待任务";
                    if (lastAiState == "started") { label = "任务开始"; color = dark ? MediaColor.FromRgb(104, 203, 255) : MediaColor.FromRgb(0, 104, 201); }
                    if (lastAiState == "running") { label = "运行中"; color = dark ? MediaColor.FromRgb(104, 203, 255) : MediaColor.FromRgb(0, 104, 201); }
                    if (lastAiState == "needs-user") { label = "等你回答"; color = dark ? MediaColor.FromRgb(255, 196, 93) : MediaColor.FromRgb(156, 88, 0); }
                    if (lastAiState == "paused") label = "已暂停";
                    if (lastAiState == "finished") label = "任务结束";
                }
            }
            else if (activeThemeAction != null && activeThemeAction.Slot != "idle")
                label = "动作预览 · " + SimpleActionLabel(activeThemeAction.Slot);
            string title = connectedAiTool == "codex" ? CodexTitleForHash(connectedAiSession) ?? "未命名会话"
                : connectedAiTool != null ? "当前 AI 任务" : "动作预览";
            // Only the status line animates; a long title wraps at most twice and
            // remains still. Re-polling or receiving a heartbeat cannot reset it.
            if (petActivityTitle.Text != title) petActivityTitle.Text = title;
            petActivityCue.ToolTip = title;
            petActivityTool.Text = connectedAiTool == "codex" ? "Codex" : connectedAiTool == null ? "预览" : "AI";
            SolidColorBrush background = contrast ? System.Windows.SystemColors.WindowBrush : new SolidColorBrush(
                dark ? MediaColor.FromArgb(245, 29, 34, 43) : MediaColor.FromArgb(247, 249, 252, 255));
            petActivityCue.Background = background; petActivityTail.Fill = background;
            petActivityCue.BorderBrush = contrast ? System.Windows.SystemColors.WindowTextBrush : new SolidColorBrush(
                dark ? MediaColor.FromArgb(110, 140, 179, 221) : MediaColor.FromArgb(95, 117, 151, 190));
            petActivityTitle.Foreground = contrast ? System.Windows.SystemColors.WindowTextBrush : dark ? MediaBrushes.White :
                new SolidColorBrush(MediaColor.FromRgb(34, 48, 68));
            petActivityTool.Foreground = contrast ? System.Windows.SystemColors.WindowTextBrush : new SolidColorBrush(color);
            petActivityText.Foreground = petActivityDot.Background = contrast ? System.Windows.SystemColors.WindowTextBrush : new SolidColorBrush(color);
            bool visible = label != null && (window == null || window.IsVisible);
            petActivityCue.Visibility = petActivityTail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bool animate = visible && lastAiState == "running" && connectedAiTool != null &&
                lastAiOutcome != "signal-stale" && !contrast && SystemParameters.ClientAreaAnimation;
            if (label != petActivityLabel || animate != petActivityAnimating) {
                petActivityLabel = label; petActivityAnimating = animate; petActivityPhase = 0;
                petActivityText.Text = label ?? "";
                petActivityText.BeginAnimation(UIElement.OpacityProperty, null);
                petActivityDot.BeginAnimation(UIElement.OpacityProperty, null);
                if (animate) {
                    DoubleAnimation glow = new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(850)) {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                    petActivityText.BeginAnimation(UIElement.OpacityProperty, glow);
                    petActivityDot.BeginAnimation(UIElement.OpacityProperty, glow);
                    petActivityTimer.Start();
                } else petActivityTimer.Stop();
            }
            petActivityText.Effect = animate ? new DropShadowEffect { Color = color, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.6 } : null;
        }

        private static bool SetThemeAction(string slot, bool oneShot)
        {
            if (activeTheme == null || themeImage == null) return false;
            if (activeThemeOneShot && (slot == "hover" || slot == "idle")) return false;
            ThemeAction action = activeTheme.FindAction(slot);
            if (bundledMotionPreview && action == null && slot != "idle")
            {
                if ((slot == "click" || slot == "hover") && DateTime.UtcNow - lastThemeInteractionUtc < TimeSpan.FromMilliseconds(700))
                    return false;
                if (slot == "click" || slot == "hover")
                {
                    lastThemeInteractionUtc = DateTime.UtcNow;
                    themeInteractionSequence++;
                }
                return PlayBundledPreviewGesture(slot);
            }
            if (action == null && slot != "idle") action = activeTheme.IdleAction;
            if (action == null || action.Frames.Count == 0) return false;
            if (slot == "needs-user") ShowPermissionCue();
            if ((slot == "click" || slot == "hover") && action.Slot == slot)
            {
                DateTime now = DateTime.UtcNow;
                if (now - lastThemeInteractionUtc < TimeSpan.FromMilliseconds(700)) return false;
                lastThemeInteractionUtc = now;
                themeInteractionSequence++;
            }
            if (themeTimer != null) themeTimer.Stop();
            activeThemeAction = action;
            activeThemeOneShot = oneShot && action.Slot != "idle";
            int firstFrame, playbackFrames;
            double playbackFps;
            ThemePlaybackTiming(action, out firstFrame, out playbackFrames, out playbackFps);
            themeFrameIndex = firstFrame;
            themeImage.Source = action.Frames[firstFrame];
            themeActionClock = System.Diagnostics.Stopwatch.StartNew();
            UpdatePetActivityCue();
            TraceFloatingReview("action-start");
            if (action.Frames.Count < 2)
            {
                if (activeThemeOneShot)
                {
                    DispatcherTimer singleFrameTimer = new DispatcherTimer();
                    singleFrameTimer.Interval = TimeSpan.FromMilliseconds(350);
                    singleFrameTimer.Tick += delegate
                    {
                        singleFrameTimer.Stop();
                        if (activeThemeAction != action) return;
                        activeThemeOneShot = false;
                        ResumeConnectedThemeOrIdle();
                    };
                    themeTimer = singleFrameTimer;
                    singleFrameTimer.Start();
                }
                return true;
            }
            bool shouldLoop = !oneShot && action.Loop;
            System.Diagnostics.Stopwatch actionClock = themeActionClock;
            // Sample the monotonic clock twice per frame at render priority.
            // A full-frame background timer can itself arrive a frame late;
            // the source still changes only when the intended frame advances.
            DispatcherTimer actionTimer = new DispatcherTimer(DispatcherPriority.Render);
            actionTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, 500.0 / playbackFps));
            actionTimer.Tick += delegate
            {
                if (activeThemeAction != action || activeTheme == null) { actionTimer.Stop(); return; }
                long elapsedFrame = (long)(actionClock.Elapsed.TotalSeconds * playbackFps);
                bool connectedRunning = connectedAiTool != null && (lastAiState == "running" || lastAiState == "started") &&
                    action.Slot == aiBindings[lastAiState];
                bool waitingForUser = HasPersistentAiAnimation() && lastAiState == "needs-user" &&
                    action.Slot == aiBindings["needs-user"];
                if (!shouldLoop && !connectedRunning && !waitingForUser && elapsedFrame >= playbackFrames)
                {
                    actionTimer.Stop();
                    activeThemeOneShot = false;
                    if (HasConnectedThemeState() && (lastAiState == "paused" || lastAiState == "finished") &&
                        action.Slot == aiBindings[lastAiState]) {
                        themeFrameIndex = firstFrame + playbackFrames - 1;
                        themeImage.Source = action.Frames[themeFrameIndex];
                        return;
                    }
                    if (action.Slot != "idle") {
                        ResumeConnectedThemeOrIdle();
                    }
                    return;
                }
                // A waiting gesture repeats with a quiet two-second rest, rather
                // than disappearing while the host still needs an answer.
                long cycleFrames = playbackFrames + (waitingForUser ? (long)(playbackFps * 2) : 0);
                int nextFrame = firstFrame + (int)Math.Min(playbackFrames - 1, elapsedFrame % cycleFrames);
                if (nextFrame != themeFrameIndex)
                {
                    themeFrameIndex = nextFrame;
                    themeImage.Source = action.Frames[themeFrameIndex];
                }
            };
            themeTimer = actionTimer;
            actionTimer.Start();
            return true;
        }

        private static bool PlayBundledPreviewGesture(string slot)
        {
            if (!bundledMotionPreview || bundledPreviewRotation == null || bundledPreviewScale == null || bundledPreviewOffset == null)
                return false;
            double angle = 0, lift = 0, scale = 1;
            switch (slot)
            {
                case "recent-activity": angle = -7; lift = -4; break;
                case "needs-user": lift = -14; scale = 1.07; break;
                case "stop-unknown": angle = 4; lift = 3; break;
                case "interrupt": angle = -10; lift = 5; break;
                case "hover": scale = 1.05; lift = -3; break;
                case "click": lift = -12; scale = 1.08; break;
                default: return false;
            }
            lastBundledPreviewGesture = slot;
            if (slot == "needs-user") ShowPermissionCue();
            TimeSpan half = TimeSpan.FromMilliseconds(slot == "interrupt" ? 190 : 360);
            DoubleAnimation rotation = new DoubleAnimation(0, angle, half) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
            DoubleAnimation offset = new DoubleAnimation(0, lift, half) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
            DoubleAnimation enlargement = new DoubleAnimation(1, scale, half) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
            bundledPreviewRotation.BeginAnimation(RotateTransform.AngleProperty, rotation);
            bundledPreviewOffset.BeginAnimation(TranslateTransform.YProperty, offset);
            bundledPreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, enlargement);
            bundledPreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, enlargement);
            return true;
        }

        private static void ShowPermissionCue()
        {
            if (permissionCue == null) return;
            if (permissionCueTimer != null) permissionCueTimer.Stop();
            permissionCue.Visibility = Visibility.Visible;
            permissionCueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            permissionCueTimer.Tick += delegate
            {
                permissionCueTimer.Stop();
                if (permissionCue != null) permissionCue.Visibility = Visibility.Collapsed;
            };
            permissionCueTimer.Start();
        }

        private static BitmapImage LoadReferenceBitmap(string photoPath)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 380;
            bitmap.UriSource = new Uri(photoPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static bool SameReferencePhoto(RoleCard role, string photoPath)
        {
            if (role == null) return true;
            if (role.PhotoData == null) return false;
            int comma = role.PhotoData.IndexOf(',');
            if (comma < 0) return false;
            byte[] embedded = Convert.FromBase64String(role.PhotoData.Substring(comma + 1));
            using (MemoryStream roleStream = new MemoryStream(embedded, false))
            using (FileStream referenceStream = File.OpenRead(photoPath))
            {
                BitmapSource roleFrame = BitmapDecoder.Create(roleStream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                BitmapSource referenceFrame = BitmapDecoder.Create(referenceStream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                if (roleFrame.PixelWidth != referenceFrame.PixelWidth || roleFrame.PixelHeight != referenceFrame.PixelHeight)
                    return false;
                FormatConvertedBitmap first = new FormatConvertedBitmap(roleFrame, PixelFormats.Bgra32, null, 0);
                FormatConvertedBitmap second = new FormatConvertedBitmap(referenceFrame, PixelFormats.Bgra32, null, 0);
                int stride = checked(first.PixelWidth * 4);
                byte[] a = new byte[checked(stride * first.PixelHeight)];
                byte[] b = new byte[a.Length];
                first.CopyPixels(a, stride, 0);
                second.CopyPixels(b, stride, 0);
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                return true;
            }
        }

        private static string RolePhotoKey(RoleCard role)
        {
            if (role == null) return "no-role";
            int comma = role.PhotoData.IndexOf(',');
            byte[] embedded = Convert.FromBase64String(role.PhotoData.Substring(comma + 1));
            using (MemoryStream stream = new MemoryStream(embedded, false))
            using (SHA256 sha = SHA256.Create())
            {
                BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                FormatConvertedBitmap image = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int stride = checked(image.PixelWidth * 4);
                byte[] pixels = new byte[checked(stride * image.PixelHeight)];
                image.CopyPixels(pixels, stride, 0);
                byte[] dimensions = new byte[8];
                Array.Copy(BitConverter.GetBytes(image.PixelWidth), 0, dimensions, 0, 4);
                Array.Copy(BitConverter.GetBytes(image.PixelHeight), 0, dimensions, 4, 4);
                sha.TransformBlock(dimensions, 0, dimensions.Length, dimensions, 0);
                sha.TransformFinalBlock(pixels, 0, pixels.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string ThemeBindingPath()
        {
            return activeThemeFilePath + ".binding.json";
        }

        private static bool ThemeMatchesCurrentRole()
        {
            if (!File.Exists(activeThemeFilePath) || !File.Exists(ThemeBindingPath())) return false;
            string data = File.ReadAllText(ThemeBindingPath(), new UTF8Encoding(false, true));
            Dictionary<string, object> binding = new JavaScriptSerializer().DeserializeObject(data) as Dictionary<string, object>;
            return HasExactKeys(binding, new string[] { "version", "rolePhotoKey", "packageSha256" }) &&
                Convert.ToInt32(binding["version"]) == 1 &&
                ReadString(binding, "rolePhotoKey") == RolePhotoKey(activeRole) &&
                ReadString(binding, "packageSha256") == FileSha256Hex(activeThemeFilePath);
        }

        private static void SaveThemeBinding()
        {
            string path = ThemeBindingPath();
            string data = "{\"version\":1,\"rolePhotoKey\":\"" + RolePhotoKey(activeRole) +
                "\",\"packageSha256\":\"" + FileSha256Hex(activeThemeFilePath) + "\"}";
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, data, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void RefreshThemeAfterRoleChange()
        {
            if (bundledMotionPreview) { activeTheme = null; bundledMotionPreview = false; }
            if (File.Exists(activeThemeFilePath))
            {
                try
                {
                    if (ThemeMatchesCurrentRole())
                    {
                        EnsureThemeNotRejected(activeThemeFilePath);
                        activeTheme = ParseThemePackage(activeThemeFilePath);
                    }
                    else activeTheme = null;
                }
                catch { activeTheme = null; }
            }
            else activeTheme = null;
            if (activeTheme == null)
            {
                string referencePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg");
                if (runtimeSpriteOnly) TryActivateReviewedReferenceTheme(referencePath);
                else TryActivateBundledPreview(referencePath);
            }
            if (exportThemeItem != null) exportThemeItem.Enabled = activeTheme != null && !bundledMotionPreview;
            if (removeThemeItem != null) removeThemeItem.Enabled = activeTheme != null && !bundledMotionPreview;
            if (reviewSavedThemeItem != null)
                reviewSavedThemeItem.Enabled = File.Exists(activeThemeFilePath) && (activeTheme == null || bundledMotionPreview);
        }

        private static void TryActivateBundledPreview(string photoPath)
        {
            if (activeTheme != null || !SameReferencePhoto(activeRole, photoPath)) return;
            string previewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundled-cat-motion-preview.lotheme");
            if (!File.Exists(previewPath)) return;
            try
            {
                EnsureThemeNotRejected(previewPath);
                activeTheme = ParseThemePackage(previewPath);
                bundledMotionPreview = true;
            }
            catch { activeTheme = null; bundledMotionPreview = false; }
        }

        private static bool TryActivateReviewedReferenceTheme(string photoPath, string sourceOverride = null)
        {
            // This is a shipped, already visually reviewed asset with a pinned
            // fingerprint, not a shortcut for generated/user-provided candidates.
            // Existing user files (including rejected or suspended themes) win.
            if (activeTheme != null || File.Exists(activeThemeFilePath) ||
                !SameReferencePhoto(activeRole, photoPath)) return false;
            string source = sourceOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reviewed-reference-cat.lotheme");
            string staged = null;
            try
            {
                if (!File.Exists(source) || FileSha256Hex(source) != ReviewedReferenceThemeSha256 ||
                    FileSha256Hex(photoPath) != ReferencePhotoSha256) return false;
                EnsureThemeNotRejected(source);
                Directory.CreateDirectory(Path.GetDirectoryName(activeThemeFilePath));
                staged = activeThemeFilePath + ".default-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(source, staged);
                ThemePackage candidate = ParseThemePackage(staged);
                if (candidate.IdleAction == null || candidate.Actions.Count != 7)
                    throw new InvalidDataException("内置角色缺少已审核动作。");
                EnsureReviewedThemeUnchanged(staged, ReviewedReferenceThemeSha256);
                // No overwrite: a concurrently created user theme is preserved.
                File.Move(staged, activeThemeFilePath);
                SaveThemeBinding();
                activeTheme = candidate;
                bundledMotionPreview = false;
                return true;
            }
            catch { activeTheme = null; bundledMotionPreview = false; return false; }
            finally { if (staged != null && File.Exists(staged)) File.Delete(staged); }
        }

        private static string ReadBoundedJson(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到角色卡文件。", path);
            if (info.Length > 12 * 1024 * 1024) throw new InvalidDataException("角色卡文件超过 12 MB。");
            return File.ReadAllText(path, new UTF8Encoding(false, true));
        }

        private static RoleCard ParseRoleCard(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 12 * 1024 * 1024;
            serializer.RecursionLimit = 16;
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            int version = root != null && root.ContainsKey("version") ? Convert.ToInt32(root["version"]) : -1;
            if (!HasExactKeys(root, new string[] { "character", "kind", "status", "version" }) ||
                !String.Equals(ReadString(root, "kind"), "little-orbit.character-card", StringComparison.Ordinal) ||
                (version != 1 && version != 2 && version != 3 && version != 4) ||
                (ReadString(root, "status") != "draft" && ReadString(root, "status") != "confirmed"))
                throw new InvalidDataException("角色卡版本或顶层字段不受支持。");

            Dictionary<string, object> card = root["character"] as Dictionary<string, object>;
            string[] legacyKeys = new string[] { "action", "mode", "name", "photo", "photoName", "prompt", "traits" };
            bool legacyWithLocalCandidate = version == 1 && HasExactKeys(card,
                new string[] { "action", "mode", "name", "photo", "photoName", "prompt", "traits", "reviewCandidate" });
            bool multiPhoto = version == 3 && HasExactKeys(card,
                new string[] { "action", "mode", "name", "photo", "photoName", "prompt", "traits", "sourceEvidence", "referencePhotos" });
            bool photoReview = version == 4 && HasExactKeys(card,
                new string[] { "action", "mode", "name", "photo", "photoName", "prompt", "traits", "sourceEvidence", "referencePhotos", "photoReview" });
            if (!(version == 1 && (HasExactKeys(card, legacyKeys) || legacyWithLocalCandidate) ||
                  version == 2 && HasExactKeys(card,
                    new string[] { "action", "mode", "name", "photo", "photoName", "prompt", "traits", "sourceEvidence" }) || multiPhoto || photoReview))
                throw new InvalidDataException("角色卡包含缺失或不受支持的字段。");
            string name = ReadString(card, "name").Trim();
            string mode = ReadString(card, "mode");
            string photoName = ReadString(card, "photoName");
            string photoData = ReadString(card, "photo");
            string action = ReadString(card, "action");
            string prompt = ReadString(card, "prompt");
            if (name.Length < 1 || name.Length > 18 || (mode != "real" && mode != "original") ||
                photoName.Length > 180 || prompt.Length > 1200 ||
                (action != "眨眨眼" && action != "歪头看看" && action != "打个招呼"))
                throw new InvalidDataException("角色卡中的名称、模式或动作字段不符合规范。");
            if (legacyWithLocalCandidate && card["reviewCandidate"] != null)
            {
                Dictionary<string, object> candidate = card["reviewCandidate"] as Dictionary<string, object>;
                if (!HasExactKeys(candidate, new string[] { "action", "fps", "frameCount", "id" }) ||
                    !Regex.IsMatch(ReadString(candidate, "id"), "^[a-f0-9]{32}$") ||
                    ReadString(candidate, "action") != action ||
                    Convert.ToInt32(candidate["fps"]) < 1 || Convert.ToInt32(candidate["fps"]) > 30 ||
                    Convert.ToInt32(candidate["frameCount"]) < 2 || Convert.ToInt32(candidate["frameCount"]) > 120)
                    throw new InvalidDataException("旧角色卡的本地候选引用不符合规范。");
            }

            IList traits = card["traits"] as IList;
            if (traits == null || traits.Count > 3) throw new InvalidDataException("角色特征列表不符合规范。");
            string[] allowedTraits = new string[] { "橘白相间的短毛", "粉色鼻头与圆眼睛", "脖子上戴着项圈" };
            for (int i = 0; i < traits.Count; i++)
            {
                string trait = traits[i] as string;
                if (trait == null || Array.IndexOf(allowedTraits, trait) < 0)
                    throw new InvalidDataException("角色卡包含未确认的特征字段。");
            }

            List<string> sourceEvidence = new List<string>();
            if (version == 2 || version == 3 || version == 4)
            {
                IList evidence = card["sourceEvidence"] as IList;
                string[] allowed = new string[] { "face", "frontPaws", "rearPaws", "tail", "fullSilhouette" };
                if (evidence == null || evidence.Count > allowed.Length)
                    throw new InvalidDataException("照片可见部位字段不符合规范。");
                for (int i = 0; i < evidence.Count; i++)
                {
                    string part = evidence[i] as string;
                    if (part == null || Array.IndexOf(allowed, part) < 0 || sourceEvidence.Contains(part))
                        throw new InvalidDataException("照片可见部位包含未知或重复字段。");
                    sourceEvidence.Add(part);
                }
            }

            BitmapImage photoBitmap = DecodePortablePhoto(photoData);
            List<ReferencePhoto> referencePhotos = new List<ReferencePhoto>();
            if (multiPhoto || photoReview)
            {
                IList references = card["referencePhotos"] as IList;
                if (references == null || (multiPhoto && references.Count < 1) || references.Count > 3)
                    throw new InvalidDataException("补充视角照片数量不符合规范。");
                long referenceBytes = 0;
                for (int i = 0; i < references.Count; i++)
                {
                    Dictionary<string, object> reference = references[i] as Dictionary<string, object>;
                    if (!HasExactKeys(reference, new string[] { "photo", "photoName" }))
                        throw new InvalidDataException("补充视角照片字段不符合规范。");
                    string referenceName = ReadString(reference, "photoName");
                    string referenceData = ReadString(reference, "photo");
                    if (referenceName.Length < 1 || referenceName.Length > 180 || referenceData.Length > 1500000)
                        throw new InvalidDataException("补充视角照片名称或数据超过限制。");
                    referenceBytes += referenceData.Length;
                    if (referenceBytes > 4500000) throw new InvalidDataException("补充视角照片总大小超过限制。");
                    referencePhotos.Add(new ReferencePhoto { PhotoName = referenceName, PhotoData = referenceData,
                        PhotoBitmap = DecodePortablePhoto(referenceData) });
                }
            }
            string subjectCountReview = "uncertain", occlusionReview = "uncertain";
            if (photoReview)
            {
                Dictionary<string, object> review = card["photoReview"] as Dictionary<string, object>;
                if (!HasExactKeys(review, new string[] { "subjectCount", "occlusion" }))
                    throw new InvalidDataException("照片人工检查记录字段不符合规范。");
                subjectCountReview = ReadString(review, "subjectCount");
                occlusionReview = ReadString(review, "occlusion");
                if (Array.IndexOf(new string[] { "one", "multiple", "uncertain" }, subjectCountReview) < 0 ||
                    Array.IndexOf(new string[] { "clear", "present", "uncertain" }, occlusionReview) < 0)
                    throw new InvalidDataException("照片人工检查记录值不符合规范。");
            }
            return new RoleCard { Name = name, Mode = mode, PhotoName = photoName, PhotoData = photoData,
                PhotoBitmap = photoBitmap, SourceEvidence = sourceEvidence.ToArray(), ReferencePhotos = referencePhotos,
                SubjectCountReview = subjectCountReview, OcclusionReview = occlusionReview };
        }

        private static ThemePackage ParseThemePackage(string packagePath)
        {
            FileInfo packageInfo = new FileInfo(packagePath);
            if (!packageInfo.Exists || packageInfo.Length < 22 || packageInfo.Length > 120L * 1024 * 1024)
                throw new InvalidDataException("主题包不存在或超过 120 MiB。");
            using (FileStream file = File.OpenRead(packagePath))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count < 2 || archive.Entries.Count > 841)
                    throw new InvalidDataException("主题包文件数量不符合规范。");
                Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                long totalExpandedBytes = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string entryName = entry.FullName;
                    if (String.IsNullOrEmpty(entryName) || entryName.IndexOf('\\') >= 0 || entryName.IndexOf(':') >= 0 ||
                        entryName.StartsWith("/", StringComparison.Ordinal) || entryName.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                        entryName.IndexOf('\0') >= 0 || entryName.EndsWith("/", StringComparison.Ordinal))
                        throw new InvalidDataException("主题包包含不安全路径。");
                    int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixType == 0xA000) throw new InvalidDataException("主题包不接受符号链接。");
                    if (entries.ContainsKey(entryName)) throw new InvalidDataException("主题包包含重复路径。");
                    totalExpandedBytes += entry.Length;
                    if (totalExpandedBytes > 120L * 1024 * 1024) throw new InvalidDataException("主题包展开后超过 120 MiB。");
                    if (entry.Length > 0 && entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > 300)
                        throw new InvalidDataException("主题包压缩比例异常。");
                    entries.Add(entryName, entry);
                }

                ZipArchiveEntry manifestEntry;
                if (!entries.TryGetValue("manifest.json", out manifestEntry) || manifestEntry.Length > 64 * 1024)
                    throw new InvalidDataException("主题包缺少有效 manifest.json。");
                string manifestJson;
                using (StreamReader reader = new StreamReader(manifestEntry.Open(), new UTF8Encoding(false, true)))
                    manifestJson = reader.ReadToEnd();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 64 * 1024;
                serializer.RecursionLimit = 16;
                EnsureNoDuplicateJsonObjectKeys(manifestJson);
                Dictionary<string, object> root = serializer.DeserializeObject(manifestJson) as Dictionary<string, object>;
                if (!HasExactKeys(root, new string[] { "actions", "format", "name", "review", "version" }) ||
                    ReadString(root, "format") != "little-orbit.theme" || Convert.ToInt32(root["version"]) != 1 ||
                    ReadString(root, "review") != "required")
                    throw new InvalidDataException("主题格式、审核状态或顶层字段不受支持。");
                string themeName = ReadString(root, "name").Trim();
                if (themeName.Length < 1 || themeName.Length > 48) throw new InvalidDataException("主题名称长度不符合规范。");
                IList actions = root["actions"] as IList;
                if (actions == null || actions.Count < 1 || actions.Count > SupportedThemeSlots.Length)
                    throw new InvalidDataException("主题动作槽数量不符合规范。");

                HashSet<string> seenSlots = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> expectedEntries = new HashSet<string>(StringComparer.Ordinal);
                expectedEntries.Add("manifest.json");
                List<ThemeAction> parsedActions = new List<ThemeAction>();
                int totalFrames = 0;
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    Dictionary<string, object> actionData = actions[actionIndex] as Dictionary<string, object>;
                    if (!HasExactKeys(actionData, new string[] { "fps", "frames", "loop", "slot" }))
                        throw new InvalidDataException("主题动作包含未知或缺失字段。");
                    string slot = ReadString(actionData, "slot");
                    if (Array.IndexOf(SupportedThemeSlots, slot) < 0 || !seenSlots.Add(slot))
                        throw new InvalidDataException("主题包含不支持或重复的动作槽。");
                    int fps = Convert.ToInt32(actionData["fps"]);
                    if (fps < 1 || fps > 30 || !(actionData["loop"] is bool))
                        throw new InvalidDataException("动作帧率或循环字段不符合规范。");
                    IList framePaths = actionData["frames"] as IList;
                    if (framePaths == null || framePaths.Count < 1 || framePaths.Count > 120)
                        throw new InvalidDataException("每个动作必须包含 1 至 120 帧。");
                    totalFrames += framePaths.Count;
                    if (totalFrames > SupportedThemeSlots.Length * 120) throw new InvalidDataException("主题总帧数超过上限。");

                    ThemeAction parsed = new ThemeAction();
                    parsed.Slot = slot;
                    parsed.FramesPerSecond = fps;
                    parsed.Loop = (bool)actionData["loop"];
                    parsed.Frames = new List<BitmapSource>();
                    for (int frameIndex = 0; frameIndex < framePaths.Count; frameIndex++)
                    {
                        string expectedPath = "frames/" + slot + "/" + (frameIndex + 1).ToString("D4") + ".png";
                        string relativePath = framePaths[frameIndex] as string;
                        ZipArchiveEntry frameEntry;
                        if (relativePath != expectedPath || !entries.TryGetValue(expectedPath, out frameEntry))
                            throw new InvalidDataException("动作帧必须使用包内连续编号 PNG 路径。");
                        if (frameEntry.Length < 24 || frameEntry.Length > 8L * 1024 * 1024)
                            throw new InvalidDataException("主题帧必须小于 8 MiB。");
                        byte[] frameBytes = ReadThemeEntry(frameEntry, 8 * 1024 * 1024);
                        int canvasWidth;
                        int canvasHeight;
                        BitmapSource bitmap = DecodeThemeFrame(frameBytes, expectedPath, out canvasWidth, out canvasHeight);
                        if (frameIndex == 0)
                        {
                            parsed.CanvasWidth = canvasWidth;
                            parsed.CanvasHeight = canvasHeight;
                        }
                        else if (parsed.CanvasWidth != canvasWidth || parsed.CanvasHeight != canvasHeight)
                            throw new InvalidDataException("同一个动作的所有帧必须使用相同画布。");
                        parsed.Frames.Add(bitmap);
                        expectedEntries.Add(expectedPath);
                    }
                    parsedActions.Add(parsed);
                }
                if (entries.Count != expectedEntries.Count)
                    throw new InvalidDataException("主题包包含清单之外的文件。");
                foreach (string expected in expectedEntries)
                    if (!entries.ContainsKey(expected)) throw new InvalidDataException("主题包缺少清单引用的文件。");
                return new ThemePackage { Name = themeName, Actions = parsedActions,
                    CompactMotion = FileSha256Hex(packagePath) == ReviewedReferenceThemeSha256 };
            }
        }

        private static void EnsureThemeNotRejected(string packagePath)
        {
            if (Directory.Exists(reviewLedgerFilePath))
                throw new InvalidDataException("无法核验候选审核记录，已停止安装。审核记录路径不是文件。");
            if (!File.Exists(reviewLedgerFilePath)) return;
            try
            {
                FileInfo ledgerInfo = new FileInfo(reviewLedgerFilePath);
                if (ledgerInfo.Length < 2 || ledgerInfo.Length > 128 * 1024)
                    throw new InvalidDataException("候选审核记录大小不符合规范。");
                string json = File.ReadAllText(reviewLedgerFilePath, new UTF8Encoding(false, true));
                EnsureNoDuplicateJsonObjectKeys(json);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 128 * 1024;
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (!HasExactKeys(root, new string[] { "format", "version", "denied" }) ||
                    ReadString(root, "format") != "little-orbit.review-veto" || !(root["version"] is int) || (int)root["version"] != 1 ||
                    !(root["denied"] is IList))
                    throw new InvalidDataException("候选审核记录格式不符合规范。");
                IList denied = (IList)root["denied"];
                if (denied.Count > 128) throw new InvalidDataException("候选审核记录超过上限。");
                string digest;
                using (FileStream stream = File.OpenRead(packagePath))
                using (SHA256 sha = SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string rejectionReason = null;
                foreach (object raw in denied)
                {
                    Dictionary<string, object> item = raw as Dictionary<string, object>;
                    if (!HasExactKeys(item, new string[] { "sha256", "candidateId", "reason", "decidedAt" }))
                        throw new InvalidDataException("候选审核条目字段不符合规范。");
                    string hash = ReadString(item, "sha256");
                    string candidateId = ReadString(item, "candidateId");
                    string reason = ReadString(item, "reason");
                    string decidedAt = ReadString(item, "decidedAt");
                    DateTimeOffset parsedAt;
                    if (!Regex.IsMatch(hash, "\\A[a-f0-9]{64}\\z") || !seen.Add(hash) ||
                        !Regex.IsMatch(candidateId, "\\A[a-f0-9]{32}\\z") ||
                        Array.IndexOf(new string[] { "identity", "motion", "anatomy", "crop", "edge" }, reason) < 0 ||
                        decidedAt.Length > 40 || !DateTimeOffset.TryParse(decidedAt, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out parsedAt))
                        throw new InvalidDataException("候选审核条目内容不符合规范。");
                    if (hash == digest) rejectionReason = reason;
                }
                if (rejectionReason != null)
                    throw new InvalidDataException("该主题包已被标记为需修改（" + rejectionReason + "），不能安装。请修正后重新生成候选。");
            }
            catch (InvalidDataException error)
            {
                if (error.Message.Contains("不能安装")) throw;
                throw new InvalidDataException("无法核验候选审核记录，已停止安装。" + error.Message, error);
            }
            catch (Exception error)
            {
                throw new InvalidDataException("无法核验候选审核记录，已停止安装。" + error.Message, error);
            }
        }

        private static byte[] ReadThemeEntry(ZipArchiveEntry entry, int maximumBytes)
        {
            using (Stream input = entry.Open())
            using (MemoryStream output = new MemoryStream((int)entry.Length))
            {
                byte[] buffer = new byte[32768];
                int read;
                int total = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > maximumBytes) throw new InvalidDataException("主题包条目超出解码限制。");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static void EnsureNoDuplicateJsonObjectKeys(string json)
        {
            JavaScriptSerializer stringSerializer = new JavaScriptSerializer();
            Stack<HashSet<string>> objectKeys = new Stack<HashSet<string>>();
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{')
                {
                    objectKeys.Push(new HashSet<string>(StringComparer.Ordinal));
                    continue;
                }
                if (json[i] == '}')
                {
                    if (objectKeys.Count == 0) throw new InvalidDataException("主题清单 JSON 对象结构无效。");
                    objectKeys.Pop();
                    continue;
                }
                if (json[i] != '"') continue;
                int stringEnd = i + 1;
                bool escaped = false;
                for (; stringEnd < json.Length; stringEnd++)
                {
                    char value = json[stringEnd];
                    if (escaped) { escaped = false; continue; }
                    if (value == '\\') { escaped = true; continue; }
                    if (value == '"') break;
                }
                if (stringEnd >= json.Length) throw new InvalidDataException("主题清单包含未结束的 JSON 字符串。");
                int next = stringEnd + 1;
                while (next < json.Length && Char.IsWhiteSpace(json[next])) next++;
                if (objectKeys.Count > 0 && next < json.Length && json[next] == ':')
                {
                    string key = stringSerializer.Deserialize<string>(json.Substring(i, stringEnd - i + 1));
                    if (!objectKeys.Peek().Add(key)) throw new InvalidDataException("主题清单包含重复 JSON 字段。");
                }
                i = stringEnd;
            }
            if (objectKeys.Count != 0) throw new InvalidDataException("主题清单 JSON 对象未闭合。");
        }

        private static BitmapSource DecodeThemeFrame(byte[] bytes, string label, out int canvasWidth, out int canvasHeight)
        {
            byte[] pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes.Length < pngSignature.Length) throw new InvalidDataException(label + " 不是完整 PNG。");
            for (int i = 0; i < pngSignature.Length; i++)
                if (bytes[i] != pngSignature[i]) throw new InvalidDataException(label + " 不是 PNG 格式。");
            BitmapSource decoded;
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1) throw new InvalidDataException(label + " 必须包含单张静态 PNG。");
                decoded = decoder.Frames[0];
                canvasWidth = decoded.PixelWidth;
                canvasHeight = decoded.PixelHeight;
                if (canvasWidth < 1 || canvasHeight < 1 || canvasWidth > 4096 || canvasHeight > 4096 ||
                    (long)canvasWidth * canvasHeight > 16 * 1024 * 1024)
                    throw new InvalidDataException(label + " 画布超过 16 MP 或 4096 像素边长限制。");
                decoded.Freeze();
            }
            BitmapImage preview = new BitmapImage();
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                preview.BeginInit();
                preview.CacheOption = BitmapCacheOption.OnLoad;
                preview.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                if ((long)canvasWidth * 254 > (long)canvasHeight * 760)
                    preview.DecodePixelWidth = Math.Min(canvasWidth, 760);
                else preview.DecodePixelHeight = 254;
                preview.StreamSource = stream;
                preview.EndInit();
                preview.Freeze();
            }
            FormatConvertedBitmap bgra = new FormatConvertedBitmap(preview, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
            byte[] pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
            bool hasTransparent = false;
            bool hasVisible = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] < 255) hasTransparent = true;
                if (pixels[i] > 0) hasVisible = true;
            }
            if (!hasTransparent) throw new InvalidDataException(label + " 不含透明像素。");
            if (!hasVisible) throw new InvalidDataException(label + " 完全透明。");
            return preview;
        }

        private static BitmapImage DecodePortablePhoto(string dataUri)
        {
            const string pngPrefix = "data:image/png;base64,";
            const string jpegPrefix = "data:image/jpeg;base64,";
            string base64;
            if (dataUri != null && dataUri.StartsWith(pngPrefix, StringComparison.Ordinal)) base64 = dataUri.Substring(pngPrefix.Length);
            else if (dataUri != null && dataUri.StartsWith(jpegPrefix, StringComparison.Ordinal)) base64 = dataUri.Substring(jpegPrefix.Length);
            else throw new InvalidDataException("角色卡照片必须是内嵌 PNG 或 JPEG；不接受外部路径或 URL。");
            if (dataUri.Length > 11200000) throw new InvalidDataException("内嵌照片超过 8 MB 限制。");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { throw new InvalidDataException("照片数据不是有效的 Base64。"); }
            if (bytes.Length < 24 || bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("照片大小不符合规范。");
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth < 1 || decoder.Frames[0].PixelHeight < 1 ||
                    decoder.Frames[0].PixelWidth > 4096 || decoder.Frames[0].PixelHeight > 4096 ||
                    (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > 16 * 1024 * 1024)
                    throw new InvalidDataException("照片尺寸超出桌面预览限制。");
                stream.Position = 0;
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 380;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private static bool HasExactKeys(Dictionary<string, object> value, string[] keys)
        {
            if (value == null || value.Count != keys.Length) return false;
            for (int i = 0; i < keys.Length; i++) if (!value.ContainsKey(keys[i])) return false;
            return true;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item) && item is string ? (string)item : String.Empty;
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void ImportRoleCard()
        {
            using (Forms.OpenFileDialog dialog = new Forms.OpenFileDialog())
            {
                dialog.Title = "导入本地角色卡";
                dialog.Filter = "AI Pet Studio 角色卡 (*.little-orbit.json)|*.little-orbit.json|JSON 文件 (*.json)|*.json";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    string json = ReadBoundedJson(dialog.FileName);
                    RoleCard imported = ParseRoleCard(json);
                    PersistRoleCardAt(pendingRoleFilePath, json);
                    pendingRole = imported;
                    UpdatePendingRoleMenu();
                    trayIcon.ShowBalloonTip(3000, "角色卡已保存待制作",
                        imported.Name + " 已保存在本机。当前桌宠保持不变；审核并安装此角色的透明主题后一起启用。",
                        Forms.ToolTipIcon.Info);
                }
                catch (Exception error)
                {
                    MessageBox.Show("角色卡未导入。\n\n" + error.Message, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void UpdatePendingRoleMenu()
        {
            if (pendingRoleItem != null)
                pendingRoleItem.Text = pendingRole == null ? "没有待制作角色" : "待制作角色：" + pendingRole.Name;
        }

        private static bool ReviewRoleCard(RoleCard candidate)
        {
            Window review = new Window();
            review.Title = "审核上一角色卡版本";
            review.Width = 454;
            review.Height = 500;
            review.ResizeMode = ResizeMode.NoResize;
            review.WindowStyle = WindowStyle.ToolWindow;
            review.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (String.IsNullOrEmpty(roleReviewRenderPath)) review.Owner = window;
            review.Topmost = true;
            review.Background = new SolidColorBrush(MediaColor.FromRgb(250, 248, 242));

            Grid layout = new Grid();
            layout.Background = review.Background;
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(278) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

            StackPanel heading = new StackPanel { Margin = new Thickness(16, 7, 16, 0) };
            TextBlock title = new TextBlock { Text = "检查要恢复的角色卡", FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 48, 41)) };
            heading.Children.Add(title);
            TextBlock subtitle = new TextBlock { Text = candidate.Name + " · " + (candidate.Mode == "real" ? "真实照片" : "原创角色"), FontSize = 9,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(132, 125, 111)) };
            heading.Children.Add(subtitle);
            Grid.SetRow(heading, 0);
            layout.Children.Add(heading);

            Border surface = new Border { Width = 190, Height = 254, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(MediaColor.FromRgb(244, 238, 224)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(224, 220, 210)), BorderThickness = new Thickness(1) };
            WpfImage preview = new WpfImage { Source = candidate.PhotoBitmap, Width = 190, Height = 254, Stretch = Stretch.Uniform };
            surface.Child = preview;
            Grid.SetRow(surface, 1);
            layout.Children.Add(surface);

            TextBlock note = new TextBlock { Text = "确认照片和裁切效果。此版本先进入待制作状态；审核并安装匹配的透明主题后才切换桌面角色。",
                FontSize = 9, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 16, 0), Foreground = new SolidColorBrush(MediaColor.FromRgb(111, 93, 72)) };
            Grid.SetRow(note, 2);
            layout.Children.Add(note);

            CheckBox reviewed = new CheckBox { Content = "我已检查照片预览，确认设为待制作", FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
            Grid.SetRow(reviewed, 3);
            layout.Children.Add(reviewed);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 0) };
            Button cancel = new Button { Content = "取消", Width = 82, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            buttons.Children.Add(cancel);
            Button restore = new Button { Content = "设为待制作", Width = 104, IsDefault = true, IsEnabled = false,
                Background = new SolidColorBrush(MediaColor.FromRgb(222, 126, 77)),
                Foreground = new SolidColorBrush(MediaColor.FromRgb(89, 79, 64)) };
            reviewed.Checked += delegate { restore.IsEnabled = true; restore.SetResourceReference(Control.ForegroundProperty, "PetAccentText"); };
            reviewed.Unchecked += delegate
            {
                restore.IsEnabled = false;
                restore.SetResourceReference(Control.ForegroundProperty, "PetAccentText");
            };
            restore.Click += delegate { review.DialogResult = true; };
            buttons.Children.Add(restore);
            Grid.SetRow(buttons, 4);
            layout.Children.Add(buttons);

            review.Content = layout;
            AttachModernDialog(review);
            if (!String.IsNullOrEmpty(roleReviewRenderPath))
            {
                string outputPath = roleReviewRenderPath;
                roleReviewRenderPath = null;
                review.Left = -10000;
                review.Top = -10000;
                review.Show();
                review.UpdateLayout();
                FrameworkElement content = review.Content as FrameworkElement;
                content.Measure(new System.Windows.Size(review.Width, review.Height));
                content.Arrange(new System.Windows.Rect(0, 0, review.Width, review.Height));
                content.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(review.Width), (int)Math.Ceiling(review.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(outputPath)) encoder.Save(stream);
                review.Close();
                return false;
            }
            return review.ShowDialog() == true;
        }

        private static void RollbackRoleCard()
        {
            if (!File.Exists(previousRoleFilePath))
            {
                MessageBox.Show("还没有可恢复的角色卡版本。", "角色卡恢复", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string stagedPath = activeRoleFilePath + ".rollback.importing";
            try
            {
                File.Copy(previousRoleFilePath, stagedPath, true);
                RoleCard candidate = ParseRoleCard(ReadBoundedJson(stagedPath));
                if (!ReviewRoleCard(candidate))
                {
                    File.Delete(stagedPath);
                    trayIcon.ShowBalloonTip(1800, "恢复已取消", "当前桌面角色卡没有更改。", Forms.ToolTipIcon.Info);
                    return;
                }
                PersistRoleCardAt(pendingRoleFilePath, ReadBoundedJson(stagedPath));
                File.Delete(stagedPath);
                pendingRole = candidate;
                UpdatePendingRoleMenu();
                trayIcon.ShowBalloonTip(3000, "上一角色卡已设为待制作",
                    candidate.Name + " 已保存；当前桌宠保持不变。审核并安装匹配主题后一起启用。",
                    Forms.ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                if (File.Exists(stagedPath)) File.Delete(stagedPath);
                MessageBox.Show("上一角色卡未恢复。\n\n" + error.Message, "角色卡恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void ImportThemePackage()
        {
            using (Forms.OpenFileDialog dialog = new Forms.OpenFileDialog())
            {
                dialog.Title = "选择本地主题包并检查预览";
                dialog.Filter = "Little Orbit 主题包 (*.lotheme)|*.lotheme|ZIP 包 (*.zip)|*.zip";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                ImportThemePackage(dialog.FileName);
            }
        }

        private static void ImportThemePackage(string sourcePath)
        {
            string stagedPath = activeThemeFilePath + ".importing";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(activeThemeFilePath));
                File.Copy(sourcePath, stagedPath, true);
                string reviewedThemeHash = FileSha256Hex(stagedPath);
                string reviewedRoleHash = pendingRole == null ? null : FileSha256Hex(pendingRoleFilePath);
                ThemePackage candidate = ParseThemePackage(stagedPath);
                EnsureThemeNotRejected(stagedPath);
                if (candidate.IdleAction == null)
                    throw new InvalidDataException("主题包需要 idle 待机帧；没有新事件时桌宠必须有安全回退姿态。");
                RoleCard installRole = pendingRole ?? activeRole;
                if (!ReviewThemePackage(candidate, installRole))
                {
                    File.Delete(stagedPath);
                    trayIcon.ShowBalloonTip(1800, "暂未使用新动作", "当前宠物没有改变，可以修改后重做或稍后再预览。", Forms.ToolTipIcon.Info);
                    return;
                }
                EnsureReviewedThemeUnchanged(stagedPath, reviewedThemeHash);
                if (pendingRole != null) CommitPendingRoleWithTheme(stagedPath, reviewedRoleHash);
                else
                {
                    PersistThemeFile(stagedPath);
                    SaveThemeBinding();
                }
                ShowInstalledTheme(candidate);
            }
            catch (Exception error)
            {
                if (File.Exists(stagedPath)) File.Delete(stagedPath);
                MessageBox.Show("主题包未安装。\n\n" + error.Message, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void ShowInstalledTheme(ThemePackage candidate)
        {
            activeTheme = candidate;
            bundledMotionPreview = false;
            lastAutomaticActionUtc = DateTime.UtcNow;
            if (rollbackThemeItem != null) rollbackThemeItem.Enabled = File.Exists(previousThemeFilePath);
            if (rollbackRoleItem != null) rollbackRoleItem.Enabled = File.Exists(previousRoleFilePath);
            if (exportThemeItem != null) exportThemeItem.Enabled = true;
            if (removeThemeItem != null) removeThemeItem.Enabled = true;
            if (reviewSavedThemeItem != null) reviewSavedThemeItem.Enabled = false;
            RebuildRuntimeWindow();
            if (HasConnectedThemeState()) ResumeConnectedThemeOrIdle();
            trayIcon.Text = "AI Pet Studio · 本地主题陪伴";
            trayIcon.ShowBalloonTip(2200, "已使用新动作", candidate.Name + " 已在桌面启用。", Forms.ToolTipIcon.Info);
        }

        private static string FileSha256Hex(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void EnsureReviewedThemeUnchanged(string path, string reviewedHash)
        {
            if (String.IsNullOrEmpty(reviewedHash) || FileSha256Hex(path) != reviewedHash)
                throw new InvalidDataException("主题包在审核期间发生变化，请重新检查。");
            EnsureThemeNotRejected(path);
        }

        private static string ExportThemePackageFile(string sourcePath, string destinationPath)
        {
            string source = Path.GetFullPath(sourcePath);
            string destination = Path.GetFullPath(destinationPath);
            if (!File.Exists(source)) throw new FileNotFoundException("当前主题包已不在本机。", source);
            if (!String.Equals(Path.GetExtension(destination), ".lotheme", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("主题备份必须保存为 .lotheme 文件。");
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(destination, Path.GetFullPath(previousThemeFilePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("不能覆盖当前主题或上一主题的本机副本。请选择其他保存位置。");
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary);
                EnsureThemeNotRejected(temporary);
                ThemePackage verified = ParseThemePackage(temporary);
                if (verified.IdleAction == null) throw new InvalidDataException("主题包缺少待机帧，无法导出。");
                string digest = FileSha256Hex(temporary);
                if (digest != FileSha256Hex(source)) throw new InvalidDataException("主题包在导出过程中发生变化，请重试。");
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                return digest;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void ExportActiveThemePackage()
        {
            if (activeTheme == null || !File.Exists(activeThemeFilePath))
            {
                MessageBox.Show("当前没有可导出的活动主题。", "导出主题", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (Forms.SaveFileDialog dialog = new Forms.SaveFileDialog())
            {
                dialog.Title = "保存当前主题包备份";
                dialog.Filter = "Little Orbit 主题包 (*.lotheme)|*.lotheme";
                dialog.DefaultExt = "lotheme";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = "ai-pet-theme.lotheme";
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    string digest = ExportThemePackageFile(activeThemeFilePath, dialog.FileName);
                    trayIcon.ShowBalloonTip(2200, "主题包已导出", "已保存原始 .lotheme 字节；SHA-256：" + digest.Substring(0, 12) + "…", Forms.ToolTipIcon.Info);
                }
                catch (Exception error)
                {
                    MessageBox.Show("主题包未导出。\n\n" + error.Message, "导出主题失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static string ThemeSlotLabel(string slot)
        {
            if (slot == "idle") return "待机";
            if (slot == "ambient") return "自动陪伴";
            if (slot == "recent-activity") return "近期活动";
            if (slot == "needs-user") return "工具权限请求";
            if (slot == "stop-unknown") return "停止 · 结果未知";
            if (slot == "interrupt") return "明确中断";
            if (slot == "hover") return "悬停";
            if (slot == "click") return "点击";
            throw new InvalidDataException("主题动作槽不受支持。");
        }

        private static string ThemeEventReviewNote(string slot)
        {
            string common = "请看完一遍，确认形象、动作和轮廓符合预期。";
            if (slot == "ambient") return "这是宠物日常自由活动时的动作。" + common;
            if (slot == "recent-activity")
                return "连接工具后，任务进行中可以播放这个动作。" + common;
            if (slot == "needs-user")
                return "工具明确需要你回答或确认时，可以播放这个动作。" + common;
            if (slot == "stop-unknown")
                return "任务结束时可以播放这个动作；结束不代表成功。" + common;
            if (slot == "interrupt")
                return "任务暂停或被中断时，可以播放这个动作。" + common;
            throw new InvalidDataException("事件审核槽不受支持。");
        }

        private static bool ReviewThemePackage(ThemePackage candidate, RoleCard targetRole = null, string onlySlot = null)
        {
            ThemeAction idle = onlySlot == null ? candidate.IdleAction : candidate.FindAction(onlySlot);
            List<ThemeAction> reviewActions = new List<ThemeAction>();
            reviewActions.Add(idle);
            for (int i = 0; onlySlot == null && i < candidate.Actions.Count; i++)
                if (candidate.Actions[i].Slot != "idle") reviewActions.Add(candidate.Actions[i]);
            Window review = new Window();
            review.Title = onlySlot == null ? "预览宠物和动作" : "预览视频动作";
            review.Width = 650;
            review.Height = 600;
            review.ResizeMode = ResizeMode.NoResize;
            // A sprite has no taskbar/Alt-Tab entry. Its approval dialog must be
            // independently reachable rather than inherit that hidden owner.
            review.WindowStyle = WindowStyle.SingleBorderWindow;
            review.ShowInTaskbar = true;
            review.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (String.IsNullOrEmpty(themeReviewRenderPath) && window.ShowInTaskbar)
            {
                review.Owner = window;
                review.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            review.Topmost = true;
            review.Background = new SolidColorBrush(MediaColor.FromRgb(250, 248, 242));

            Grid layout = new Grid();
            layout.Margin = new Thickness(0);
            layout.Background = review.Background;
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(278) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            heading.Margin = new Thickness(16, 14, 16, 0);
            TextBlock title = new TextBlock();
            title.Text = onlySlot == null ? "预览这套宠物动作" : "预览视频动作";
            title.FontSize = 20;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 48, 41));
            heading.Children.Add(title);
            TextBlock subtitle = new TextBlock();
            subtitle.Text = onlySlot == null ? candidate.Name : "使用后将替换「" + candidate.Name + "」的「" + SimpleActionLabel(onlySlot) + "」动作。";
            subtitle.FontSize = 12;
            subtitle.Foreground = new SolidColorBrush(MediaColor.FromRgb(132, 125, 111));
            heading.Children.Add(subtitle);
            Grid.SetRow(heading, 0);
            layout.Children.Add(heading);

            ComboBox actionSelector = new ComboBox();
            actionSelector.Margin = new Thickness(16, 4, 16, 4);
            actionSelector.FontSize = 12;
            for (int i = 0; i < reviewActions.Count; i++)
            {
                ThemeAction action = reviewActions[i];
                actionSelector.Items.Add(SimpleActionLabel(action.Slot));
            }
            Grid.SetRow(actionSelector, 1);
            layout.Children.Add(actionSelector);
            if (reviewActions.Count == 1) {
                actionSelector.Visibility = Visibility.Collapsed;
                TextBlock actionName = new TextBlock { Text = SimpleActionLabel(idle.Slot),
                    Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold, FontSize = 14 };
                Grid.SetRow(actionName, 1);
                layout.Children.Add(actionName);
            }

            Grid previews = new Grid();
            previews.Margin = new Thickness(16, 0, 16, 0);
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            List<WpfImage> previewImages = new List<WpfImage>();
            previewImages.Add(AddThemePreview(previews, 0, idle.Frames[0], MediaColor.FromRgb(244, 238, 224), "暖色背景"));
            previewImages.Add(AddThemePreview(previews, 1, idle.Frames[0], MediaColor.FromRgb(185, 215, 238), "蓝色背景"));
            previewImages.Add(AddThemePreview(previews, 2, idle.Frames[0], MediaColor.FromRgb(38, 42, 49), "深色桌面"));
            Grid.SetRow(previews, 2);
            layout.Children.Add(previews);

            TextBlock reviewNote = new TextBlock();
            RoleCard comparedRole = targetRole ?? activeRole;
            string identityNote = comparedRole == null ? "" : "请对照「" + comparedRole.Name + "」的参考照片检查形象。";
            reviewNote.FontSize = 13;
            reviewNote.TextWrapping = TextWrapping.Wrap;
            reviewNote.VerticalAlignment = VerticalAlignment.Center;
            reviewNote.Margin = new Thickness(16, 8, 16, 0);
            reviewNote.Foreground = new SolidColorBrush(MediaColor.FromRgb(111, 93, 72));
            Grid.SetRow(reviewNote, 3);
            layout.Children.Add(reviewNote);

            TextBlock reviewProgress = new TextBlock();
            reviewProgress.FontSize = 12;
            reviewProgress.Margin = new Thickness(16, 8, 16, 0);
            Grid.SetRow(reviewProgress, 4);
            layout.Children.Add(reviewProgress);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(16, 12, 16, 16);
            Button replay = new Button { Content = "重播动作", Margin = new Thickness(0, 0, 8, 0) };
            buttons.Children.Add(replay);
            Button cancel = new Button();
            cancel.Content = "返回修改";
            cancel.Width = 94;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            Button install = new Button();
            install.Content = onlySlot == null ? "使用这只宠物" : "使用这个动作";
            install.MinWidth = 120;
            install.IsDefault = true;
            install.IsEnabled = false;
            install.Background = new SolidColorBrush(MediaColor.FromRgb(222, 126, 77));
            install.Foreground = MediaBrushes.White;
            buttons.Children.Add(install);
            Grid.SetRow(buttons, 5);
            layout.Children.Add(buttons);

            review.Content = layout;
            AttachModernDialog(review);
            HashSet<string> playedSlots = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> reviewedSlots = new HashSet<string>(StringComparer.Ordinal);
            ThemeAction selectedAction = idle;
            int reviewFrameIndex = 0;
            int reviewFirst = 0, reviewCount = idle.Frames.Count;
            double reviewFps = idle.FramesPerSecond;
            System.Diagnostics.Stopwatch reviewClock = null;
            bool presented = false;
            DispatcherTimer previewTimer = new DispatcherTimer(DispatcherPriority.Render);
            Action refreshReviewState = delegate
            {
                bool played = playedSlots.Contains(selectedAction.Slot);
                bool alreadyConfirmed = reviewedSlots.Contains(selectedAction.Slot);
                install.IsEnabled = played;
                if (onlySlot != null) install.Content = "使用这个动作";
                else if (alreadyConfirmed) install.Content = "查看待确认动作";
                else install.Content = reviewedSlots.Count + 1 == reviewActions.Count ? "确认并使用整套动作" : "确认并查看下一个";
                reviewProgress.Text = played ? "已播放一遍" : "正在播放，播完即可确认";
                if (reviewActions.Count > 1) reviewProgress.Text += "  ·  已确认 " + reviewedSlots.Count + " / " + reviewActions.Count;
                replay.Visibility = reviewCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            previewTimer.Tick += delegate
            {
                long elapsed = (long)(reviewClock.Elapsed.TotalSeconds * reviewFps);
                reviewFrameIndex = reviewFirst + (int)(selectedAction.Loop ? elapsed % reviewCount : Math.Min(reviewCount - 1, elapsed));
                for (int i = 0; i < previewImages.Count; i++) previewImages[i].Source = selectedAction.Frames[reviewFrameIndex];
                if (elapsed >= reviewCount && playedSlots.Add(selectedAction.Slot)) refreshReviewState();
                if (!selectedAction.Loop && elapsed >= reviewCount) previewTimer.Stop();
            };
            Action playSelection = delegate
            {
                if (actionSelector.SelectedIndex < 0) return;
                previewTimer.Stop();
                selectedAction = reviewActions[actionSelector.SelectedIndex];
                ThemePlaybackTiming(selectedAction, out reviewFirst, out reviewCount, out reviewFps, candidate);
                reviewFrameIndex = reviewFirst;
                reviewNote.Text = identityNote + (selectedAction.Slot == "idle" || selectedAction.Slot == "hover" || selectedAction.Slot == "click"
                    ? "在三种背景上查看动作和毛发边缘。"
                    : ThemeEventReviewNote(selectedAction.Slot));
                for (int i = 0; i < previewImages.Count; i++) previewImages[i].Source = selectedAction.Frames[reviewFirst];
                if (presented && reviewCount == 1) playedSlots.Add(selectedAction.Slot);
                reviewClock = System.Diagnostics.Stopwatch.StartNew();
                previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16, 500.0 / reviewFps));
                if (presented && reviewCount > 1) previewTimer.Start();
                refreshReviewState();
            };
            actionSelector.SelectionChanged += delegate { playSelection(); };
            replay.Click += delegate { playSelection(); };
            if (String.IsNullOrEmpty(themeReviewRenderPath))
                review.ContentRendered += delegate { presented = true; playSelection(); };
            install.Click += delegate
            {
                if (!playedSlots.Contains(selectedAction.Slot)) return;
                reviewedSlots.Add(selectedAction.Slot);
                if (reviewedSlots.Count == reviewActions.Count) { review.DialogResult = true; return; }
                for (int offset = 1; offset <= reviewActions.Count; offset++) {
                    int index = (actionSelector.SelectedIndex + offset) % reviewActions.Count;
                    if (reviewedSlots.Contains(reviewActions[index].Slot)) continue;
                    actionSelector.SelectedIndex = index;
                    break;
                }
            };
            review.Closed += delegate { previewTimer.Stop(); };
            actionSelector.SelectedIndex = 0;
            if (!String.IsNullOrEmpty(themeReviewRenderPath))
            {
                int requestedActionIndex = reviewActions.FindIndex(delegate(ThemeAction action) {
                    return action.Slot == themeReviewRenderSlot;
                });
                if (requestedActionIndex < 0) throw new InvalidDataException("Requested review slot is missing.");
                actionSelector.SelectedIndex = requestedActionIndex;
                if (themeReviewRenderFrame < 0 || themeReviewRenderFrame >= selectedAction.Frames.Count)
                    throw new InvalidDataException("Requested review frame is out of range.");
                previewTimer.Stop();
                reviewFrameIndex = themeReviewRenderFrame;
                for (int i = 0; i < previewImages.Count; i++)
                    previewImages[i].Source = selectedAction.Frames[reviewFrameIndex];
                string outputPath = themeReviewRenderPath;
                themeReviewRenderPath = null;
                review.Left = -10000;
                review.Top = -10000;
                review.Show();
                review.UpdateLayout();
                FrameworkElement content = review.Content as FrameworkElement;
                content.Measure(new System.Windows.Size(review.Width, review.Height));
                content.Arrange(new System.Windows.Rect(0, 0, review.Width, review.Height));
                content.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(review.Width), (int)Math.Ceiling(review.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(outputPath)) encoder.Save(stream);
                review.Close();
                return false;
            }
            return review.ShowDialog() == true;
        }

        private static WpfImage AddThemePreview(Grid parent, int column, BitmapSource frame, MediaColor background, string label)
        {
            StackPanel panel = new StackPanel();
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            Border surface = new Border();
            surface.Width = 190;
            surface.Height = 254;
            surface.Background = new SolidColorBrush(background);
            surface.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(224, 220, 210));
            surface.BorderThickness = new Thickness(1);
            WpfImage image = new WpfImage();
            image.Source = frame;
            image.Width = 190;
            image.Height = 254;
            image.Stretch = Stretch.Uniform;
            surface.Child = image;
            panel.Children.Add(surface);
            TextBlock caption = new TextBlock();
            caption.Text = label;
            caption.FontSize = 8;
            caption.Margin = new Thickness(0, 4, 0, 0);
            caption.TextAlignment = TextAlignment.Center;
            caption.Foreground = new SolidColorBrush(MediaColor.FromRgb(132, 125, 111));
            panel.Children.Add(caption);
            Grid.SetColumn(panel, column);
            parent.Children.Add(panel);
            return image;
        }

        private static void ShowPreferences()
        {
            Window dialog = new Window();
            dialog.Title = "陪伴偏好";
            dialog.Width = 470;
            dialog.Height = 550;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.WindowStyle = WindowStyle.ToolWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (String.IsNullOrEmpty(preferencesRenderPath)) dialog.Owner = window;
            dialog.Background = new SolidColorBrush(MediaColor.FromRgb(250, 248, 242));

            StackPanel content = new StackPanel();
            content.Background = dialog.Background;
            TextBlock title = new TextBlock { Text = "陪伴偏好", FontSize = 20, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 48, 41)), Margin = new Thickness(0, 0, 0, 3) };
            content.Children.Add(title);
            TextBlock intro = new TextBlock { Text = "决定桌宠如何回应，以及安静时段的安排。", FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(132, 125, 111)), Margin = new Thickness(0, 0, 0, 16) };
            content.Children.Add(intro);

            CheckBox automaticActions = new CheckBox { Content = "允许自动陪伴动作（默认关闭）", IsChecked = preferences.AutomaticActionsEnabled,
                FontSize = 12, Margin = new Thickness(0, 4, 0, 12) };
            content.Children.Add(automaticActions);

            CheckBox interaction = new CheckBox { Content = "允许本地点击、悬停互动", IsChecked = preferences.InteractionEnabled,
                FontSize = 12, Margin = new Thickness(0, 4, 0, 12) };
            content.Children.Add(interaction);
            CheckBox quiet = new CheckBox { Content = "启用安静时段（限制自动陪伴动作）", IsChecked = preferences.QuietEnabled,
                FontSize = 12, Margin = new Thickness(0, 4, 0, 8) };
            content.Children.Add(quiet);

            StackPanel quietTimes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 14) };
            TextBox quietStart = new TextBox { Text = preferences.QuietStart, Width = 80, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            TextBox quietEnd = new TextBox { Text = preferences.QuietEnd, Width = 80, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
            quietTimes.Children.Add(new TextBlock { Text = "从", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            quietTimes.Children.Add(quietStart);
            quietTimes.Children.Add(new TextBlock { Text = "到", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
            quietTimes.Children.Add(quietEnd);
            content.Children.Add(quietTimes);

            TextBlock frequencyLabel = new TextBlock { Text = "自动动作频率", FontWeight = FontWeights.SemiBold, FontSize = 12,
                Margin = new Thickness(0, 4, 0, 4) };
            content.Children.Add(frequencyLabel);
            Slider frequency = new Slider { Minimum = 5, Maximum = 30, TickFrequency = 5, IsSnapToTickEnabled = true,
                Value = preferences.FrequencyMinutes, Margin = new Thickness(0, 0, 0, 0) };
            content.Children.Add(frequency);
            TextBlock frequencyValue = new TextBlock { Text = "每 " + preferences.FrequencyMinutes + " 分钟", FontSize = 10,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(111, 93, 72)), Margin = new Thickness(0, 0, 0, 10) };
            frequency.ValueChanged += delegate { frequencyValue.Text = "每 " + ((int)frequency.Value) + " 分钟"; };
            content.Children.Add(frequencyValue);

            bool hasAutomaticAction = activeTheme != null && activeTheme.FindAction("ambient") != null;
            TextBlock status = new TextBlock {
                Text = !automaticActions.IsChecked.GetValueOrDefault()
                    ? "自动陪伴动作当前关闭。开启后，只会播放已审核安装主题中的 ambient 素材，并遵守频率、安静时段与桌宠空闲状态。"
                    : hasAutomaticAction
                        ? "自动动作已开启；当前主题含 ambient 素材，达到设定频率、非安静时段且桌宠空闲时触发。"
                        : "自动动作已开启，但当前主题没有 ambient 素材；安装含该动作且已逐项审核的主题后才会触发。",
                TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = new SolidColorBrush(MediaColor.FromRgb(111, 93, 72)),
                Margin = new Thickness(0, 4, 0, 18)
            };
            content.Children.Add(status);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = new Button { Content = "取消", Width = 84, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            Button save = new Button { Content = "保存偏好", Width = 100, Height = 30, IsDefault = true,
                Background = new SolidColorBrush(MediaColor.FromRgb(222, 126, 77)), Foreground = MediaBrushes.White };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            content.Children.Add(buttons);
            Border shell = new Border { Padding = new Thickness(24), Child = content };
            dialog.Content = new ScrollViewer { Content = shell, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AttachModernDialog(dialog);

            save.Click += delegate
            {
                TimeSpan startTime, endTime;
                DateTime parsedStart, parsedEnd;
                if (!DateTime.TryParseExact(quietStart.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedStart) ||
                    !DateTime.TryParseExact(quietEnd.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEnd))
                {
                    MessageBox.Show(dialog, "请使用 24 小时制时间，例如 22:00。", "时间格式不正确", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                TimeSpan.TryParseExact(quietStart.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out startTime);
                TimeSpan.TryParseExact(quietEnd.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out endTime);
                LocalPreferences updated = new LocalPreferences {
                    InteractionEnabled = interaction.IsChecked == true,
                    AutomaticActionsEnabled = automaticActions.IsChecked == true,
                    QuietEnabled = quiet.IsChecked == true,
                    QuietStart = startTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    QuietEnd = endTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    FrequencyMinutes = Math.Max(5, Math.Min(30, (int)Math.Round(frequency.Value / 5.0) * 5))
                };
                try
                {
                    PersistPreferencesAt(preferencesFilePath, updated);
                    preferences = updated;
                    if (trayIcon != null) trayIcon.ShowBalloonTip(1600, "偏好已保存", "桌宠互动偏好已保存在本机。", Forms.ToolTipIcon.Info);
                    dialog.DialogResult = true;
                }
                catch (Exception error)
                {
                    MessageBox.Show(dialog, "偏好未保存。\n\n" + error.Message, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            if (!String.IsNullOrEmpty(preferencesRenderPath))
            {
                string outputPath = preferencesRenderPath;
                preferencesRenderPath = null;
                dialog.Left = -10000;
                dialog.Top = -10000;
                dialog.Show();
                dialog.UpdateLayout();
                FrameworkElement root = dialog.Content as FrameworkElement;
                root.Measure(new System.Windows.Size(dialog.Width, dialog.Height));
                root.Arrange(new System.Windows.Rect(0, 0, dialog.Width, dialog.Height));
                root.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.Width), (int)Math.Ceiling(dialog.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(outputPath)) encoder.Save(stream);
                dialog.Close();
                return;
            }
            dialog.ShowDialog();
        }

        private static string PhotoReviewLabel(string value)
        {
            if (value == "one") return "确认一只";
            if (value == "multiple") return "确认多只";
            if (value == "clear") return "未发现";
            if (value == "present") return "发现遮挡或裁切";
            return "不确定";
        }

        private static bool HandlePhotoInteraction()
        {
            if (!preferences.InteractionEnabled || greeting == null) return false;
            DateTime now = DateTime.UtcNow;
            if (now - lastPhotoInteractionUtc < TimeSpan.FromMilliseconds(900)) return false;
            string[] greetings = new string[] { "你好呀，我在这里陪你。", "今天也要好好陪伴。", "见到你啦，慢慢来。", "我会安静地陪着你。" };
            int index = photoGreetingRandom.Next(greetings.Length - 1);
            if (greetings[index] == lastPhotoGreeting) index = greetings.Length - 1;
            lastPhotoGreeting = greetings[index];
            greeting.Text = lastPhotoGreeting;
            lastPhotoInteractionUtc = now;
            return true;
        }

        private static bool IsWithinQuietHours(LocalPreferences value, TimeSpan time)
        {
            if (value == null || !value.QuietEnabled) return false;
            TimeSpan start, end;
            if (!TimeSpan.TryParseExact(value.QuietStart, "hh\\:mm", CultureInfo.InvariantCulture, out start) ||
                !TimeSpan.TryParseExact(value.QuietEnd, "hh\\:mm", CultureInfo.InvariantCulture, out end)) return false;
            if (start == end) return false;
            return start < end ? (time >= start && time < end) : (time >= start || time < end);
        }

        private static LocalPreferences ParsePreferences(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, object> values = root;
            bool portable = false;
            int version = 1;
            if (root != null && root.ContainsKey("kind"))
            {
                if (!HasExactKeys(root, new string[] { "kind", "preferences", "version" }) ||
                    ReadString(root, "kind") != "little-orbit.preferences")
                    throw new InvalidDataException("偏好备份类型或版本不受支持。");
                version = Convert.ToInt32(root["version"]);
                if (version < 1 || version > 2) throw new InvalidDataException("偏好备份类型或版本不受支持。");
                values = root["preferences"] as Dictionary<string, object>;
                portable = true;
            }
            else if (values != null && values.ContainsKey("version")) version = Convert.ToInt32(values["version"]);
            if (version < 1 || version > 2) throw new InvalidDataException("本地偏好文件版本不受支持。");
            string[] baseKeys = new string[] { "frequencyMinutes", "interactionEnabled", "quietEnabled", "quietEnd", "quietStart" };
            List<string> expectedKeys = new List<string>(baseKeys);
            if (version == 2) expectedKeys.Add("automaticActionsEnabled");
            if (!portable) expectedKeys.Add("version");
            if (!HasExactKeys(values, expectedKeys.ToArray()) ||
                (!portable && Convert.ToInt32(values["version"]) != version) ||
                !(values["interactionEnabled"] is bool) || !(values["quietEnabled"] is bool) ||
                (version == 2 && !(values["automaticActionsEnabled"] is bool)))
                throw new InvalidDataException("本地偏好文件字段不符合规范。");
            string start = ReadString(values, "quietStart"), end = ReadString(values, "quietEnd");
            TimeSpan ignored;
            if (!TimeSpan.TryParseExact(start, "hh\\:mm", CultureInfo.InvariantCulture, out ignored) ||
                !TimeSpan.TryParseExact(end, "hh\\:mm", CultureInfo.InvariantCulture, out ignored))
                throw new InvalidDataException("安静时段时间不符合 HH:mm 格式。");
            int minutes = Convert.ToInt32(values["frequencyMinutes"]);
            if (minutes < 5 || minutes > 30 || minutes % 5 != 0) throw new InvalidDataException("动作频率必须是 5 到 30 分钟，且按 5 分钟递增。");
            return new LocalPreferences { InteractionEnabled = (bool)values["interactionEnabled"],
                AutomaticActionsEnabled = version == 2 && (bool)values["automaticActionsEnabled"], QuietEnabled = (bool)values["quietEnabled"],
                QuietStart = start, QuietEnd = end, FrequencyMinutes = minutes };
        }

        private static string PortablePreferencesJson(LocalPreferences value)
        {
            return "{\"kind\":\"little-orbit.preferences\",\"version\":2,\"preferences\":{\"automaticActionsEnabled\":" + value.AutomaticActionsEnabled.ToString().ToLowerInvariant() +
                ",\"frequencyMinutes\":" + value.FrequencyMinutes +
                ",\"interactionEnabled\":" + value.InteractionEnabled.ToString().ToLowerInvariant() +
                ",\"quietEnabled\":" + value.QuietEnabled.ToString().ToLowerInvariant() +
                ",\"quietEnd\":\"" + EscapeJson(value.QuietEnd) + "\",\"quietStart\":\"" + EscapeJson(value.QuietStart) + "\"}}";
        }

        private static void ExportPreferences()
        {
            using (Forms.SaveFileDialog dialog = new Forms.SaveFileDialog())
            {
                dialog.Title = "导出本地陪伴偏好";
                dialog.Filter = "Little Orbit 偏好备份 (*.little-orbit.preferences.json)|*.little-orbit.preferences.json|JSON 文件 (*.json)|*.json";
                dialog.FileName = "little-orbit.preferences.json";
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, PortablePreferencesJson(preferences), new UTF8Encoding(false));
                    trayIcon.ShowBalloonTip(1800, "偏好备份已导出", "JSON 备份只包含开关、时间和频率设置。", Forms.ToolTipIcon.Info);
                }
                catch (Exception error)
                {
                    MessageBox.Show("偏好备份未导出。\n\n" + error.Message, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void ImportPreferences()
        {
            using (Forms.OpenFileDialog dialog = new Forms.OpenFileDialog())
            {
                dialog.Title = "导入本地偏好备份";
                dialog.Filter = "Little Orbit 偏好备份 (*.little-orbit.preferences.json)|*.little-orbit.preferences.json|JSON 文件 (*.json)|*.json";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                try
                {
                    FileInfo file = new FileInfo(dialog.FileName);
                    if (file.Length > 64 * 1024) throw new InvalidDataException("偏好文件超过 64 KiB。");
                    LocalPreferences candidate = ParsePreferences(File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)));
                    string summary = "互动：" + (candidate.InteractionEnabled ? "开启" : "关闭") +
                        "\n自动陪伴动作：" + (candidate.AutomaticActionsEnabled ? "开启" : "关闭") +
                        "\n安静时段：" + (candidate.QuietEnabled ? candidate.QuietStart + "–" + candidate.QuietEnd : "关闭") +
                        "\n动作频率：每 " + candidate.FrequencyMinutes + " 分钟（有已审核的 ambient 素材时生效）";
                    MessageBoxResult confirm = MessageBox.Show("应用这份本地偏好备份？\n\n" + summary, "确认导入偏好",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;
                    PersistPreferencesAt(preferencesFilePath, candidate);
                    preferences = candidate;
                    trayIcon.ShowBalloonTip(1800, "偏好备份已导入", "桌面端偏好已更新。", Forms.ToolTipIcon.Info);
                }
                catch (Exception error)
                {
                    MessageBox.Show("偏好备份未导入。\n\n" + error.Message, "AI Pet Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void PersistPreferencesAt(string path, LocalPreferences value)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string json = "{\"automaticActionsEnabled\":" + value.AutomaticActionsEnabled.ToString().ToLowerInvariant() +
                ",\"frequencyMinutes\":" + value.FrequencyMinutes + ",\"interactionEnabled\":" + value.InteractionEnabled.ToString().ToLowerInvariant() +
                ",\"quietEnabled\":" + value.QuietEnabled.ToString().ToLowerInvariant() + ",\"quietEnd\":\"" + EscapeJson(value.QuietEnd) +
                "\",\"quietStart\":\"" + EscapeJson(value.QuietStart) + "\",\"version\":2}";
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }

        private static void RemoveActiveTheme()
        {
            if (activeTheme == null) return;
            MessageBoxResult result = MessageBox.Show("移除活动主题并返回默认预览？\n\n如需保留当前安装的主题包，请先从托盘导出备份。",
                "返回默认预览", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            if (File.Exists(activeThemeFilePath)) File.Delete(activeThemeFilePath);
            if (File.Exists(ThemeBindingPath())) File.Delete(ThemeBindingPath());
            activeTheme = null;
            TryActivateReviewedReferenceTheme(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"));
            if (exportThemeItem != null) exportThemeItem.Enabled = activeTheme != null;
            if (removeThemeItem != null) removeThemeItem.Enabled = activeTheme != null;
            if (reviewSavedThemeItem != null) reviewSavedThemeItem.Enabled = false;
            RebuildRuntimeWindow();
            trayIcon.Text = "AI Pet Studio · 本地主题陪伴";
            trayIcon.ShowBalloonTip(1800, "已返回默认预览", "原始角色卡仍保存在本机。", Forms.ToolTipIcon.Info);
        }

        private static void ReviewSavedTheme()
        {
            if (!File.Exists(activeThemeFilePath)) return;
            try
            {
                EnsureThemeNotRejected(activeThemeFilePath);
                string reviewedThemeHash = FileSha256Hex(activeThemeFilePath);
                ThemePackage candidate = ParseThemePackage(activeThemeFilePath);
                if (candidate.IdleAction == null) throw new InvalidDataException("已保存主题缺少 idle 动作。");
                if (!ReviewThemePackage(candidate)) return;
                EnsureReviewedThemeUnchanged(activeThemeFilePath, reviewedThemeHash);
                SaveThemeBinding();
                activeTheme = candidate;
                bundledMotionPreview = false;
                lastAutomaticActionUtc = DateTime.UtcNow;
                if (exportThemeItem != null) exportThemeItem.Enabled = true;
                if (removeThemeItem != null) removeThemeItem.Enabled = true;
                if (reviewSavedThemeItem != null) reviewSavedThemeItem.Enabled = false;
                RebuildRuntimeWindow();
                trayIcon.ShowBalloonTip(2200, "主题已重新启用", "已在当前角色下完成逐动作审核。", Forms.ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                MessageBox.Show("主题未重新启用。\n\n" + error.Message, "审核已保存主题", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void RollbackTheme()
        {
            if (!File.Exists(previousThemeFilePath))
            {
                MessageBox.Show("还没有可回滚的主题版本。", "主题回滚", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string stagedPath = activeThemeFilePath + ".rollback.importing";
            try
            {
                File.Copy(previousThemeFilePath, stagedPath, true);
                string reviewedThemeHash = FileSha256Hex(stagedPath);
                string reviewedRoleHash = pendingRole == null ? null : FileSha256Hex(pendingRoleFilePath);
                ThemePackage candidate = ParseThemePackage(stagedPath);
                EnsureThemeNotRejected(stagedPath);
                if (candidate.IdleAction == null) throw new InvalidDataException("上一主题缺少 idle 动作，无法恢复。");
                if (!ReviewThemePackage(candidate, pendingRole ?? activeRole))
                {
                    File.Delete(stagedPath);
                    trayIcon.ShowBalloonTip(1800, "回滚已放弃", "当前活动主题没有更改。", Forms.ToolTipIcon.Info);
                    return;
                }
                EnsureReviewedThemeUnchanged(stagedPath, reviewedThemeHash);
                if (pendingRole != null) CommitPendingRoleWithTheme(stagedPath, reviewedRoleHash);
                else
                {
                    PersistThemeFile(stagedPath);
                    SaveThemeBinding();
                }
                activeTheme = candidate;
                bundledMotionPreview = false;
                lastAutomaticActionUtc = DateTime.UtcNow;
                if (rollbackThemeItem != null) rollbackThemeItem.Enabled = File.Exists(previousThemeFilePath);
                if (rollbackRoleItem != null) rollbackRoleItem.Enabled = File.Exists(previousRoleFilePath);
                if (exportThemeItem != null) exportThemeItem.Enabled = true;
                if (removeThemeItem != null) removeThemeItem.Enabled = true;
                if (reviewSavedThemeItem != null) reviewSavedThemeItem.Enabled = false;
                RebuildRuntimeWindow();
                trayIcon.Text = "AI Pet Studio · 本地主题陪伴";
                trayIcon.ShowBalloonTip(2200, "已恢复上一主题", candidate.Name + " 已在本机启用。", Forms.ToolTipIcon.Info);
            }
            catch (Exception error)
            {
                if (File.Exists(stagedPath)) File.Delete(stagedPath);
                MessageBox.Show("上一主题未恢复。\n\n" + error.Message, "主题回滚失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void RebuildRuntimeWindow()
        {
            bool wasVisible = window.IsVisible;
            bool wasWaiting = waitingForTransparentTheme;
            double previousLeft = window.Left;
            double previousTop = window.Top;
            string previousScreenName = WindowScreenName();
            if (themeTimer != null) themeTimer.Stop();
            themeImage = null;
            window.Closing -= OnWindowClosing;
            window.Close();
            BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg"),
                previousLeft, previousTop, previousScreenName);
            if ((wasVisible || wasWaiting) && !waitingForTransparentTheme) window.Show();
            UpdateVisibilityLabel();
        }

        private static void PersistThemeFile(string stagedPath)
        {
            string directory = Path.GetDirectoryName(activeThemeFilePath);
            Directory.CreateDirectory(directory);
            if (File.Exists(activeThemeFilePath))
            {
                string pendingPreviousPath = previousThemeFilePath + ".tmp";
                File.Copy(activeThemeFilePath, pendingPreviousPath, true);
                if (File.Exists(previousThemeFilePath)) File.Replace(pendingPreviousPath, previousThemeFilePath, null);
                else File.Move(pendingPreviousPath, previousThemeFilePath);
                File.Replace(stagedPath, activeThemeFilePath, null);
            }
            else File.Move(stagedPath, activeThemeFilePath);
        }

        private static void CommitPendingRoleWithTheme(string stagedThemePath, string reviewedRoleHash)
        {
            if (pendingRole == null || !File.Exists(pendingRoleFilePath))
                throw new InvalidDataException("待制作角色卡已不存在，主题未切换。");
            if (String.IsNullOrEmpty(reviewedRoleHash) || FileSha256Hex(pendingRoleFilePath) != reviewedRoleHash)
                throw new InvalidDataException("待制作角色卡在主题审核期间发生变化，请重新检查身份。");
            string pendingJson = ReadBoundedJson(pendingRoleFilePath);
            RoleCard target = ParseRoleCard(pendingJson);
            if (RolePhotoKey(target) != RolePhotoKey(pendingRole))
                throw new InvalidDataException("待制作角色照片已变化，请重新审核主题身份。");
            string directory = Path.GetDirectoryName(activeRoleFilePath);
            Directory.CreateDirectory(directory);
            string backupDirectory = Path.Combine(directory, ".pending-switch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDirectory);
            string[] paths = new string[] { activeRoleFilePath, previousRoleFilePath,
                activeThemeFilePath, previousThemeFilePath, ThemeBindingPath() };
            string[] backups = new string[paths.Length];
            RoleCard formerRole = activeRole;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!File.Exists(paths[i])) continue;
                    backups[i] = Path.Combine(backupDirectory, i.ToString(CultureInfo.InvariantCulture) + ".bak");
                    File.Copy(paths[i], backups[i]);
                }
                PersistActiveRoleCard(pendingJson);
                activeRole = target;
                PersistThemeFile(stagedThemePath);
                SaveThemeBinding();
                if (!ThemeMatchesCurrentRole())
                    throw new InvalidDataException("新主题与待制作角色绑定失败。");
                File.Delete(pendingRoleFilePath);
                pendingRole = null;
                UpdatePendingRoleMenu();
            }
            catch
            {
                activeRole = formerRole;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (backups[i] != null) File.Copy(backups[i], paths[i], true);
                    else if (File.Exists(paths[i])) File.Delete(paths[i]);
                }
                throw;
            }
            finally
            {
                for (int i = 0; i < backups.Length; i++)
                    if (backups[i] != null && File.Exists(backups[i])) File.Delete(backups[i]);
                Directory.Delete(backupDirectory);
            }
        }

        private static void PersistRoleCardAt(string destinationPath, string json)
        {
            string directory = Path.GetDirectoryName(destinationPath);
            Directory.CreateDirectory(directory);
            string temporaryPath = destinationPath + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
            else File.Move(temporaryPath, destinationPath);
        }

        private static void PersistActiveRoleCard(string json)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(activeRoleFilePath));
            Directory.CreateDirectory(directory);
            string activeTemporaryPath = activeRoleFilePath + ".tmp";
            string previousTemporaryPath = previousRoleFilePath + ".tmp";
            try
            {
                File.WriteAllText(activeTemporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(activeRoleFilePath))
                {
                    File.Copy(activeRoleFilePath, previousTemporaryPath, true);
                    if (File.Exists(previousRoleFilePath)) File.Replace(previousTemporaryPath, previousRoleFilePath, null);
                    else File.Move(previousTemporaryPath, previousRoleFilePath);
                    File.Replace(activeTemporaryPath, activeRoleFilePath, null);
                }
                else File.Move(activeTemporaryPath, activeRoleFilePath);
            }
            finally
            {
                if (File.Exists(activeTemporaryPath)) File.Delete(activeTemporaryPath);
                if (File.Exists(previousTemporaryPath)) File.Delete(previousTemporaryPath);
            }
        }

        private static void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (exiting) return;
            e.Cancel = true;
            window.Hide();
            UpdateVisibilityLabel();
        }

        private static void ToggleWindow()
        {
            if (window.IsVisible)
            {
                window.Hide();
                UpdateVisibilityLabel();
            }
            else ShowWindow();
        }

        private static void ShowWindow()
        {
            if (waitingForTransparentTheme)
            {
                UpdateVisibilityLabel();
                trayIcon.ShowBalloonTip(3500, "角色需要透明主题",
                    "角色照片和审核记录已保留。请从托盘导入并审核透明主题后再显示桌宠。",
                    Forms.ToolTipIcon.Info);
                return;
            }
            if (!window.IsVisible) window.Show();
            Forms.Screen screen = FindScreen(WindowScreenName());
            System.Windows.Point visible = ResolveWindowPosition(ScreenWorkAreaDips(screen),
                window.Width, window.Height, window.Left, window.Top);
            window.Left = visible.X;
            window.Top = visible.Y;
            window.Activate();
            window.Topmost = true;
            UpdateVisibilityLabel();
        }

        private static void UpdateVisibilityLabel()
        {
            if (visibilityItem != null) visibilityItem.Text = waitingForTransparentTheme
                ? "角色待制作 · 需要透明主题" : window.IsVisible ? "隐藏宠物" : "显示宠物";
            if (trayIcon != null) trayIcon.Text = waitingForTransparentTheme
                ? "AI Pet · 角色需要透明主题" : "AI Pet · 桌面伙伴";
        }

        private static void ExitApplication()
        {
            TrySaveCurrentPlacement();
            exiting = true;
            if (petManager != null) petManager.Close();
            if (themeTimer != null) themeTimer.Stop();
            if (codexEventTimer != null) codexEventTimer.Stop();
            if (automaticActionTimer != null) automaticActionTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            window.Close();
            WpfApplication.Current.Shutdown();
        }
    }
}
