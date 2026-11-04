using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        // Action-only import. Snapshot the live theme at review time, since a
        // different pet may have been selected while the Agent was working.
        private static void ReviewVideoAction(string candidatePath, string slot)
        {
            Directory.CreateDirectory(experienceRoot);
            string work = Path.Combine(experienceRoot, ".video-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string merged = Path.Combine(work, "merged.lotheme");
            string fallback = Path.Combine(work, "base.lotheme");
            try
            {
                string basePath = File.Exists(activeThemeFilePath) ? activeThemeFilePath : null;
                string baseHash = basePath == null ? null : FileSha256Hex(basePath);
                string roleKey = RolePhotoKey(activeRole);
                if (basePath != null && !ThemeMatchesCurrentRole())
                    throw new InvalidDataException("请先在桌宠中恢复或选择当前角色的主题，再加入视频动作。");
                if (basePath == null && activeTheme != null)
                {
                    if (bundledMotionPreview)
                        basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundled-cat-motion-preview.lotheme");
                    else { ExportPublicDemoTheme(fallback); basePath = fallback; }
                }
                string clipHash = FileSha256Hex(candidatePath);
                EnsureThemeNotRejected(candidatePath);
                WriteMergedVideoTheme(basePath, candidatePath, slot, merged);
                EnsureThemeNotRejected(merged);
                string reviewed = FileSha256Hex(merged);
                ThemePackage theme = ParseThemePackage(merged);
                if (!ReviewThemePackage(theme, activeRole, slot)) return;
                EnsureReviewedThemeUnchanged(candidatePath, clipHash);
                EnsureReviewedThemeUnchanged(merged, reviewed);
                string currentHash = File.Exists(activeThemeFilePath) ? FileSha256Hex(activeThemeFilePath) : null;
                if (baseHash != currentHash || roleKey != RolePhotoKey(activeRole) ||
                    (currentHash != null && !ThemeMatchesCurrentRole()))
                    throw new InvalidOperationException("预览期间当前宠物发生了变化，请重新打开这个视频动作。");
                CommitVideoAction(merged, work);
                ShowInstalledTheme(theme);
            }
            finally
            {
                if (File.Exists(merged)) File.Delete(merged);
                if (File.Exists(fallback)) File.Delete(fallback);
                if (Directory.GetFileSystemEntries(work).Length == 0) Directory.Delete(work);
            }
        }

        private static void CommitVideoAction(string merged, string work)
        {
            string[] paths = { activeThemeFilePath, previousThemeFilePath, ThemeBindingPath() };
            string[] backups = new string[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                if (!File.Exists(paths[i])) continue;
                backups[i] = Path.Combine(work, i + ".bak");
                File.Copy(paths[i], backups[i]);
            }
            bool releaseBackups = false;
            try
            {
                PersistThemeFile(merged);
                SaveThemeBinding();
                releaseBackups = true;
            }
            catch
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (backups[i] != null) File.Copy(backups[i], paths[i], true);
                    else if (File.Exists(paths[i])) File.Delete(paths[i]);
                }
                releaseBackups = true;
                throw;
            }
            finally
            {
                // Leave recovery copies available if restoring them ever fails.
                if (releaseBackups)
                    foreach (string path in backups) if (path != null) File.Delete(path);
            }
        }

        private static void WriteMergedVideoTheme(string basePath, string clipPath, string slot, string outputPath)
        {
            if (slot == "idle" || Array.IndexOf(SupportedThemeSlots, slot) < 0)
                throw new InvalidDataException("请选择要加入的宠物动作。");
            ThemePackage clip = ParseThemePackage(clipPath);
            ThemeAction motion = clip.FindAction(slot);
            if (motion == null || motion.Frames.Count < 2)
                throw new InvalidDataException("视频结果缺少所选动作，或有效帧不足。");
            foreach (ThemeAction action in clip.Actions)
                if (action.Slot != slot && action.Slot != "idle") throw new InvalidDataException("视频结果中包含未请求的动作。");

            List<object> manifestActions = new List<object>();
            using (FileStream stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive output = new ZipArchive(stream, ZipArchiveMode.Create))
            using (ZipArchive video = ZipFile.OpenRead(clipPath))
            {
                string name = clip.Name;
                if (basePath == null)
                    AddVideoArchiveAction(video, output, manifestActions, clip.IdleAction ?? motion, "idle", 0, 1, 1, true);
                else
                {
                    EnsureThemeNotRejected(basePath);
                    ThemePackage original = ParseThemePackage(basePath);
                    name = original.Name;
                    if (original.IdleAction == null) throw new InvalidDataException("当前主题没有待机动作。");
                    using (ZipArchive prior = ZipFile.OpenRead(basePath))
                        foreach (ThemeAction action in original.Actions)
                        {
                            if (action.Slot == slot) continue;
                            int first, count; double fps;
                            // Preserve effective timing even for older themes with
                            // a fingerprint-specific playback profile.
                            ThemePlaybackTiming(action, out first, out count, out fps, original);
                            AddVideoArchiveAction(prior, output, manifestActions, action, action.Slot,
                                first, count, (int)fps, action.Loop);
                        }
                }
                AddVideoArchiveAction(video, output, manifestActions, motion, slot, 0,
                    motion.Frames.Count, motion.FramesPerSecond, motion.Loop);
                var manifest = new { format = "little-orbit.theme", version = 1, name = name,
                    review = "required", actions = manifestActions };
                using (StreamWriter writer = new StreamWriter(output.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
                    writer.Write(new JavaScriptSerializer().Serialize(manifest));
            }
        }

        private static void AddVideoArchiveAction(ZipArchive source, ZipArchive output, List<object> actions,
            ThemeAction action, string slot, int first, int count, int fps, bool loop)
        {
            List<string> frames = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string from = "frames/" + action.Slot + "/" + (first + i + 1).ToString("D4", CultureInfo.InvariantCulture) + ".png";
                string to = "frames/" + slot + "/" + (i + 1).ToString("D4", CultureInfo.InvariantCulture) + ".png";
                using (Stream input = source.GetEntry(from).Open())
                using (Stream target = output.CreateEntry(to, CompressionLevel.NoCompression).Open())
                    input.CopyTo(target);
                frames.Add(to);
            }
            actions.Add(new { slot = slot, fps = fps, loop = loop, frames = frames });
        }
    }
}
