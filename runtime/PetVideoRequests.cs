using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi" };
        private static readonly UTF8Encoding VideoJsonEncoding = new UTF8Encoding(false, true);
        private const long MaxVideoSourceBytes = 1024L * 1024 * 1024;

        private sealed class VideoSourceInfo
        {
            public string path { get; set; }
            public string fileName { get; set; }
            public string sha256 { get; set; }
            public long sizeBytes { get; set; }
        }

        private sealed class VideoRequestData
        {
            public string kind { get; set; }
            public int version { get; set; }
            public string id { get; set; }
            public VideoSourceInfo source { get; set; }
            public double startSeconds { get; set; }
            public double durationSeconds { get; set; }
            public int fps { get; set; }
            public string slot { get; set; }
            public bool loop { get; set; }
            public string instructions { get; set; }
            public long createdAtMs { get; set; }
        }

        private static string VideoRequestFolder(string id)
        {
            if (String.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"\A[a-f0-9]{32}\z"))
                throw new InvalidDataException("视频请求编号无效。");
            return Path.Combine(experienceRoot, "video-requests", id);
        }

        private static void CheckVideoSettings(double start, double duration, int fps, string slot, string instructions)
        {
            if (!(start >= 0 && start <= Int32.MaxValue && duration > 0 && duration <= 12) ||
                fps < 1 || fps > 30 || Math.Ceiling(duration * fps) > 120 || duration * fps < 2)
                throw new InvalidDataException("请选择有效的起始时间、最多 12 秒的片段和 1–30 fps，片段需含 2–120 帧。");
            if (slot == "idle" || Array.IndexOf(SupportedThemeSlots, slot) < 0)
                throw new InvalidDataException("请选择要添加或替换的单个动作。");
            if (instructions != null && instructions.Length > 2000)
                throw new InvalidDataException("抠像说明最多 2000 个字符。");
        }

        private static string SaveVideoRequest(string videoPath, double startSeconds, double durationSeconds, int fps,
            string slot, bool loop, string instructions, System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken))
        {
            CheckVideoSettings(startSeconds, durationSeconds, fps, slot, instructions);
            string source = Path.GetFullPath(videoPath);
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (Array.IndexOf(VideoExtensions, extension) < 0)
                throw new InvalidDataException("请选择 MP4、MOV、M4V、WebM、MKV 或 AVI 视频。");
            string id = Guid.NewGuid().ToString("N");
            string target = VideoRequestFolder(id);
            string staging = target + ".saving";
            string name = "source" + extension;
            Directory.CreateDirectory(staging);
            try {
                string copy = Path.Combine(staging, name);
                using (FileStream input = File.OpenRead(source)) {
                    if (input.Length < 12 || input.Length > MaxVideoSourceBytes)
                        throw new InvalidDataException("视频为空、不完整或超过 1 GiB。");
                    byte[] header = new byte[12];
                    int length = input.Read(header, 0, header.Length);
                    string container = Encoding.ASCII.GetString(header, 4, 4);
                    bool supported = container == "ftyp" || container == "moov" || container == "mdat" ||
                        container == "wide" || container == "free" || container == "skip" ||
                        (Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "AVI ") ||
                        (header[0] == 0x1a && header[1] == 0x45 && header[2] == 0xdf && header[3] == 0xa3);
                    if (length != header.Length || !supported) throw new InvalidDataException("文件不是可识别的视频容器。");
                    input.Position = 0;
                    using (FileStream output = File.Open(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyToAsync(output, 81920, cancel).GetAwaiter().GetResult();
                }
                VideoRequestData request = new VideoRequestData {
                    kind = "pet-video-cutout", version = 1, id = id, startSeconds = startSeconds,
                    durationSeconds = durationSeconds, fps = fps, slot = slot, loop = loop,
                    instructions = instructions ?? "", createdAtMs = CurrentUnixMilliseconds(),
                    source = new VideoSourceInfo { path = name, fileName = Path.GetFileName(source),
                        sizeBytes = new FileInfo(copy).Length, sha256 = FileSha256Hex(copy) }
                };
                File.WriteAllText(Path.Combine(staging, "request.json"), new JavaScriptSerializer().Serialize(request), VideoJsonEncoding);
                File.WriteAllText(Path.Combine(staging, "agent-instructions.txt"), VideoHandoffText(id), VideoJsonEncoding);
                cancel.ThrowIfCancellationRequested();
                Directory.Move(staging, target);
                return id;
            } finally {
                if (Directory.Exists(staging)) {
                    File.Delete(Path.Combine(staging, name));
                    File.Delete(Path.Combine(staging, "request.json"));
                    File.Delete(Path.Combine(staging, "agent-instructions.txt"));
                    Directory.Delete(staging);
                }
            }
        }

        private static VideoRequestData ReadVideoRequest(string id)
        {
            string path = Path.Combine(VideoRequestFolder(id), "request.json");
            VideoRequestData data = new JavaScriptSerializer().Deserialize<VideoRequestData>(ReadBoundedJson(path));
            if (data == null || data.kind != "pet-video-cutout" || data.version != 1 || data.id != id || data.source == null ||
                !VideoExtensions.Any(ext => data.source.path == "source" + ext) || data.source.sizeBytes <= 0 ||
                data.source.sizeBytes > MaxVideoSourceBytes || !Regex.IsMatch(data.source.sha256 ?? "", @"\A[a-f0-9]{64}\z"))
                throw new InvalidDataException("视频请求内容或来源无效。");
            CheckVideoSettings(data.startSeconds, data.durationSeconds, data.fps, data.slot, data.instructions);
            return data;
        }

        private static string SavedVideoSourcePath(string id)
        {
            return Path.Combine(VideoRequestFolder(id), ReadVideoRequest(id).source.path);
        }

        private static string VideoHandoffText(string id)
        {
            string folder = VideoRequestFolder(id);
            return "请使用 $ai-pet Skill 从我的视频提取这个真实动作。\r\n" +
                "请求编号：" + id + "\r\n请求文件：" + Path.Combine(folder, "request.json") +
                "\r\n桌宠程序：" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName +
                "\r\n数据目录：" + experienceRoot + "\r\n" +
                "在请求目录内读取 source.path 并核对 source.sha256。按 startSeconds、durationSeconds、fps、slot、loop 和我的说明抠取原视频帧。" +
                "仅使用分割、遮罩或已有透明通道；保留原动作、时序、画面比例和宠物形象，不生成或重画新姿势。" +
                "用本地工具或你已有的抠像能力制作透明动作。需要把视频交给第三方云端服务时先询问我。" +
                "候选仅包含目标动作和可选的单帧 idle。将文件用 ai-pet.ps1 -Action video-result -RequestId " + id +
                " -Theme <候选包路径> 提交，使用上述桌宠程序和数据目录。我会在视频入口预览后选择使用。";
        }

        private static string[] VideoRequestIds()
        {
            string root = Path.Combine(experienceRoot, "video-requests");
            if (!Directory.Exists(root)) return new string[0];
            return Directory.GetDirectories(root).Select(Path.GetFileName)
                .Where(id => Regex.IsMatch(id, @"\A[a-f0-9]{32}\z") && File.Exists(Path.Combine(VideoRequestFolder(id), "request.json")))
                .OrderByDescending(id => File.GetLastWriteTimeUtc(Path.Combine(VideoRequestFolder(id), "request.json")))
                .ThenBy(id => id, StringComparer.Ordinal).ToArray();
        }

        private static string LatestVideoRequestId()
        {
            return VideoRequestIds().FirstOrDefault();
        }

        private static string VideoCandidatePath(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            string path = Path.Combine(VideoRequestFolder(id), "result", "candidate.lotheme");
            return File.Exists(path) ? path : null;
        }

        private static bool HasDistinctVideoFrames(ThemeAction action)
        {
            byte[] first = null;
            foreach (BitmapSource frame in action.Frames) {
                // Premultiplication ignores RGB noise in completely transparent pixels.
                BitmapSource image = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                int stride = image.PixelWidth * 4;
                byte[] pixels = new byte[stride * image.PixelHeight];
                image.CopyPixels(pixels, stride, 0);
                if (first == null) first = pixels;
                else if (!first.SequenceEqual(pixels)) return true;
            }
            return false;
        }

        private static void SubmitVideoCandidate(string id, string path)
        {
            string folder = VideoRequestFolder(id);
            string requestHash = FileSha256Hex(Path.Combine(folder, "request.json"));
            VideoRequestData request = ReadVideoRequest(id);
            string source = Path.Combine(folder, request.source.path);
            if (new FileInfo(source).Length != request.source.sizeBytes || FileSha256Hex(source) != request.source.sha256)
                throw new InvalidDataException("请求的视频已改变，请重新选择原视频。");
            string staging = Path.Combine(folder, ".submit-" + Guid.NewGuid().ToString("N"));
            string staged = Path.Combine(staging, "candidate.lotheme");
            string receipt = Path.Combine(staging, "receipt.json");
            Directory.CreateDirectory(staging);
            try {
                using (FileStream input = File.OpenRead(Path.GetFullPath(path))) {
                    if (input.Length < 22 || input.Length > 120L * 1024 * 1024)
                        throw new InvalidDataException("候选主题为空或超过 120 MiB。");
                    using (FileStream output = File.Open(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
                EnsureThemeNotRejected(staged);
                ThemePackage package = ParseThemePackage(staged);
                ThemeAction action = package.FindAction(request.slot);
                double expectedFrames = request.durationSeconds * request.fps;
                if (package.Actions.Any(a => a.Slot != request.slot && (a.Slot != "idle" || a.Frames.Count != 1)) ||
                    action == null || action.FramesPerSecond != request.fps || action.Loop != request.loop ||
                    action.Frames.Count < 2 || Math.Abs(action.Frames.Count - expectedFrames) > 1.000001 ||
                    !HasDistinctVideoFrames(action))
                    throw new InvalidDataException("视频候选的动作、时长、帧率和循环需与请求一致，至少两张可见帧应有变化。可附单帧待机图。");
                string hash = FileSha256Hex(staged);
                if (FileSha256Hex(Path.Combine(folder, "request.json")) != requestHash)
                    throw new InvalidDataException("提交期间视频请求已改变。");
                File.WriteAllText(receipt, new JavaScriptSerializer().Serialize(new {
                    requestId = id, requestSha256 = requestHash, sourceSha256 = request.source.sha256, candidateSha256 = hash
                }), VideoJsonEncoding);
                try { Directory.Move(staging, Path.Combine(folder, "result")); }
                catch (IOException) {
                    string existing = VideoCandidatePath(id);
                    if (existing == null) throw;
                    if (FileSha256Hex(existing) != hash) throw new InvalidDataException("此请求已有另一个候选，请新建请求以保留两个版本。");
                    CheckVideoReceipt(id, existing);
                }
            } finally {
                if (Directory.Exists(staging)) {
                    File.Delete(staged);
                    File.Delete(receipt);
                    Directory.Delete(staging);
                }
            }
        }

        private static void CheckVideoReceipt(string id, string candidate)
        {
            Dictionary<string, object> receipt = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                ReadBoundedJson(Path.Combine(Path.GetDirectoryName(candidate), "receipt.json")));
            if (receipt == null || ReadString(receipt, "requestId") != id ||
                ReadString(receipt, "requestSha256") != FileSha256Hex(Path.Combine(VideoRequestFolder(id), "request.json")) ||
                ReadString(receipt, "candidateSha256") != FileSha256Hex(candidate))
                throw new InvalidDataException("视频候选或对应的请求已改变，请重新提交。");
        }

        private static void PreviewVideoCandidate(string id)
        {
            string candidate = VideoCandidatePath(id);
            if (candidate == null) throw new InvalidDataException("此视频请求还没有可预览的候选。");
            // Hold the immutable receipt-bound input through the review/merge.
            using (FileStream pin = File.OpenRead(candidate)) {
                CheckVideoReceipt(id, candidate);
                EnsureThemeNotRejected(candidate);
                ReviewVideoAction(candidate, ReadVideoRequest(id).slot);
            }
        }

        private static bool HandleVideoRequestCommand(string[] args)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            if (args[0] == "--ai-video-create" && args.Length == 3) {
                Dictionary<string, object> spec = json.Deserialize<Dictionary<string, object>>(ReadBoundedJson(Path.GetFullPath(args[1])));
                if (spec == null || !(spec["fps"] is int) || !(spec["loop"] is bool) ||
                    spec["startSeconds"] == null || spec["durationSeconds"] == null)
                    throw new InvalidDataException("视频制作参数不完整。");
                string id = SaveVideoRequest(ReadString(spec, "video"), Convert.ToDouble(spec["startSeconds"], CultureInfo.InvariantCulture),
                    Convert.ToDouble(spec["durationSeconds"], CultureInfo.InvariantCulture), (int)spec["fps"],
                    ReadString(spec, "slot"), (bool)spec["loop"], ReadString(spec, "instructions"));
                File.WriteAllText(Path.GetFullPath(args[2]), json.Serialize(new { id = id }), VideoJsonEncoding);
            } else if (args[0] == "--ai-video-requests" && args.Length == 2) {
                var requests = VideoRequestIds().Select(id => new {
                    id = id, request = Path.Combine(VideoRequestFolder(id), "request.json"),
                    instructions = Path.Combine(VideoRequestFolder(id), "agent-instructions.txt"),
                    status = VideoCandidatePath(id) == null ? "pending" : "ready"
                }).ToArray();
                File.WriteAllText(Path.GetFullPath(args[1]), json.Serialize(requests), VideoJsonEncoding);
            } else if (args[0] == "--ai-video-result" && args.Length == 3) {
                SubmitVideoCandidate(args[1], args[2]);
            } else return false;
            return true;
        }
    }
}
