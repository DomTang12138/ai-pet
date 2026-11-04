using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        private static bool videoSourceSelected;

        private static void AddVideoSourcePicker(StackPanel page, StackPanel photos)
        {
            var source = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 2 };
            RadioButton photoOption = new RadioButton { Content = "照片制作", ToolTip = "用参考照片和描述制作动作" };
            RadioButton videoOption = new RadioButton { Content = "视频提取", ToolTip = "从拍摄的视频中提取真实动作" };
            foreach (RadioButton option in new[] {photoOption, videoOption}) {
                option.SetResourceReference(FrameworkElement.StyleProperty, "PetSegment"); source.Children.Add(option);
            }
            System.Windows.Automation.AutomationProperties.SetName(source, "制作方式");
            Border picker = new Border { Child = source, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 16) };
            picker.SetResourceReference(Border.BackgroundProperty, "PetBase"); picker.SetResourceReference(Border.BorderBrushProperty, "PetBorder");
            page.Children.Add(picker); page.Children.Add(photos);
            ContentControl video = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            page.Children.Add(video);
            RoutedEventHandler chooseSource = delegate {
                videoSourceSelected = videoOption.IsChecked == true;
                photos.Visibility = videoSourceSelected ? Visibility.Collapsed : Visibility.Visible;
                if (videoSourceSelected && video.Content == null) video.Content = new VideoExtractionPanel(petManager);
                video.Visibility = videoSourceSelected ? Visibility.Visible : Visibility.Collapsed;
            };
            photoOption.Checked += chooseSource;
            videoOption.Checked += chooseSource;
            (videoSourceSelected ? videoOption : photoOption).IsChecked = true;
        }

        private sealed class VideoExtractionPanel : StackPanel
        {
            private readonly StackPanel form = new StackPanel(), editor = new StackPanel();
            private readonly Expander options = new Expander { Margin = new Thickness(0, 0, 0, 10) };
            private readonly ComboBox method = new ComboBox(), action = new ComboBox();
            private readonly TextBox start = SecondsBox("0"), duration = SecondsBox("2");
            private readonly TextBox instructions = new TextBox { MaxLength = 2000, MinHeight = 42, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            private readonly CheckBox loop = new CheckBox { Content = "循环播放", Margin = new Thickness(12, 4, 0, 0) };
            private readonly StackPanel maskControls = new StackPanel(), keyControls = new StackPanel(),
                agentControls = new StackPanel();
            private readonly Slider tolerance = new Slider { Minimum = 0, Maximum = 200, Value = 40, TickFrequency = 1,
                IsSnapToTickEnabled = true, Width = 150, Margin = new Thickness(8, 0, 0, 0) };
            private readonly TextBlock fileLabel = ManagerText("选一段拍到宠物动作的视频。", false),
                hint = ManagerText("", false), status = ManagerText("", false);
            private readonly Image still = new Image { Stretch = Stretch.Uniform };
            private readonly Grid frame = new Grid { Height = 200, Background = Brushes.Black, ClipToBounds = true, Visibility = Visibility.Collapsed };
            private readonly Canvas overlay = new Canvas { IsHitTestVisible = false };
            private readonly System.Windows.Shapes.Rectangle outline = new System.Windows.Shapes.Rectangle {
                Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Visibility = Visibility.Collapsed };
            private readonly Button pick, submit, preview, copyAgain, cancel, chooseColor, reload;
            private readonly DispatcherTimer refresh;
            private CancellationTokenSource operation;
            private string path, request, previousCandidate, keyColor = "auto";
            private double selectedFrameAt = -1, length;
            private Rect rect = Rect.Empty;
            private Point? drag;
            private bool closed, readyAnnounced, matchesDraft;
            private static readonly string[] Slots = { "recent-activity", "needs-user", "interrupt", "stop-unknown", "click", "hover", "ambient" };

            internal VideoExtractionPanel(Window owner)
            {
                Children.Add(form);
                form.Children.Add(fileLabel);
                pick = ManagerButton("选择宠物视频…", PickVideo); form.Children.Add(pick);
                form.Children.Add(editor);
                StackPanel detail = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                WrapPanel trim = new WrapPanel(); AddField(trim, "起点（秒）", start); AddField(trim, "时长（秒）", duration);
                detail.Children.Add(trim);
                foreach (string slot in Slots) action.Items.Add(SimpleActionLabel(slot));
                action.SelectedIndex = 0; action.Width = 150; loop.IsChecked = true;
                WrapPanel target = new WrapPanel(); AddField(target, "用于动作", action); target.Children.Add(loop); detail.Children.Add(target);
                options.Content = detail; editor.Children.Add(options);
                method.Items.Add("自己框选宠物"); method.Items.Add("按背景颜色抠像"); method.Items.Add("交给我的 Agent 抠像");
                method.SelectedIndex = 0; method.Width = 190;
                WrapPanel choices = new WrapPanel(); AddField(choices, "抠像方式", method); editor.Children.Add(choices);
                maskControls.Children.Add(hint);
                frame.Children.Add(still); frame.Children.Add(overlay); overlay.Children.Add(outline);
                maskControls.Children.Add(frame);
                reload = ManagerButton("读取起始画面", async delegate { await LoadFrame(length <= 0); });
                maskControls.Children.Add(reload);
                WrapPanel colors = new WrapPanel(); chooseColor = ManagerButton("背景色：自动取色", PickColor);
                colors.Children.Add(chooseColor); AddField(colors, "容差", tolerance); keyControls.Children.Add(colors);
                maskControls.Children.Add(keyControls); editor.Children.Add(maskControls);
                agentControls.Children.Add(ManagerText("说明画面中要保留哪只宠物（可选）", false));
                agentControls.Children.Add(instructions); editor.Children.Add(agentControls);
                submit = ManagerButton("提取并预览…", ExtractAndPreview);
                submit.SetResourceReference(FrameworkElement.StyleProperty, "PetPrimary");
                Children.Add(status); WrapPanel results = new WrapPanel();
                preview = ManagerButton("预览提取的动作…", delegate { PreviewVideoCandidate(previousCandidate); });
                copyAgain = ManagerButton("复制这份抠像说明", CopyInstructions);
                cancel = ManagerButton("取消", delegate { if (operation != null) { operation.Cancel(); status.Text = "正在取消…"; } });
                results.Children.Add(submit); results.Children.Add(preview); results.Children.Add(copyAgain); results.Children.Add(cancel); Children.Add(results);
                action.SelectionChanged += delegate { loop.IsChecked = action.SelectedIndex == 0 || action.SelectedIndex == 6; DraftChanged(); };
                loop.Checked += delegate { DraftChanged(); }; loop.Unchecked += delegate { DraftChanged(); };
                duration.TextChanged += delegate { DraftChanged(); };
                instructions.TextChanged += delegate { DraftChanged(); }; tolerance.ValueChanged += delegate { DraftChanged(); };
                method.SelectionChanged += async delegate {
                    DraftChanged(); UpdateMode();
                    if (method.SelectedIndex != 2 && path != null && still.Source == null && HasValidStart()) await LoadFrame(length <= 0);
                };
                start.TextChanged += delegate {
                    rect = Rect.Empty; selectedFrameAt = -1; still.Source = null; frame.Visibility = Visibility.Collapsed; DrawSelection(); DraftChanged();
                };
                start.LostKeyboardFocus += delegate {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async delegate {
                        if (!closed && IsVisible && operation == null && path != null && method.SelectedIndex != 2 && selectedFrameAt < 0 && HasValidStart())
                            await LoadFrame(length <= 0);
                    }));
                };
                frame.MouseLeftButtonDown += BeginSelection; frame.MouseMove += MoveSelection; frame.MouseLeftButtonUp += EndSelection;
                frame.SizeChanged += delegate { DrawSelection(); };
                string[] saved = VideoRequestIds();
                if (saved.Length > 0) request = saved[0];
                previousCandidate = Array.Find(saved, delegate(string id) { return VideoCandidatePath(id) != null; });
                matchesDraft = request != null;
                if (request != null && VideoCandidatePath(request) == null) status.Text = "上次的视频制作说明已保存，可复制给 Agent。";
                refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                refresh.Tick += delegate {
                    if (IsVisible && !readyAnnounced && request != null && VideoCandidatePath(request) != null) RefreshButtons();
                };
                owner.Closed += delegate { closed = true; refresh.Stop(); if (operation != null) operation.Cancel(); };
                UpdateMode(); refresh.Start();
            }

            private static TextBox SecondsBox(string text)
            { return new TextBox { Text = text, MinWidth = 65, MaxLength = 12 }; }

            private static void AddField(Panel row, string label, FrameworkElement editor)
            {
                StackPanel field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 10) };
                TextBlock caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                caption.SetResourceReference(TextBlock.ForegroundProperty, "PetText"); field.Children.Add(caption); field.Children.Add(editor); row.Children.Add(field);
            }

            private void UpdateMode()
            {
                bool agent = method.SelectedIndex == 2;
                maskControls.Visibility = agent ? Visibility.Collapsed : Visibility.Visible;
                agentControls.Visibility = agent ? Visibility.Visible : Visibility.Collapsed;
                keyControls.Visibility = method.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                hint.Text = method.SelectedIndex == 0 ? "在起始画面拖框，圈住整只宠物，框外留一些背景。"
                    : "点击起始画面的背景取色。调高容差可去除更多相近颜色。";
                submit.Content = agent ? "复制给 Agent 的抠像说明" : "提取并预览…";
                DrawSelection(); RefreshButtons();
            }

            private bool HasValidStart()
            {
                double at;
                return TrySeconds(start, out at) && (length <= 0 || at < length);
            }

            private void DraftChanged()
            {
                matchesDraft = false;
                if (operation == null) status.Text = "";
                RefreshButtons();
            }

            private void RefreshButtons()
            {
                bool busy = operation != null;
                form.IsEnabled = !busy;
                bool selected = path != null;
                editor.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                pick.Content = selected ? "更换视频…" : "选择宠物视频…";
                bool ready = request != null && VideoCandidatePath(request) != null;
                if (ready) previousCandidate = request;
                pick.SetResourceReference(StyleProperty, selected || previousCandidate != null ? (object)typeof(Button) : "PetPrimary");
                bool currentResult = matchesDraft && ready;
                double at, seconds;
                bool hasStart = TrySeconds(start, out at);
                bool hasDuration = TrySeconds(duration, out seconds);
                bool validTrim = hasStart && hasDuration && seconds >= 1.0 / 6 && seconds <= 12 && (length <= 0 || at + seconds <= length + .001);
                bool hasSelection = !rect.IsEmpty && rect.Width >= .02 && rect.Height >= .02 && rect.Width * rect.Height <= .98;
                submit.Visibility = selected && !busy && !currentResult && !(matchesDraft && method.SelectedIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
                submit.IsEnabled = validTrim && (method.SelectedIndex != 0 || hasSelection && Math.Abs(at-selectedFrameAt) < .0001);
                options.Header = "调整片段与用途　　" + (validTrim ? start.Text + " 秒起 · " + duration.Text + " 秒" : "请调整时间")
                    + " · " + action.SelectedItem + (loop.IsChecked == true ? " · 循环" : "");
                reload.Visibility = still.Source == null && !busy ? Visibility.Visible : Visibility.Collapsed;
                preview.Visibility = !busy && previousCandidate != null ? Visibility.Visible : Visibility.Collapsed;
                preview.IsEnabled = !busy;
                preview.Content = currentResult ? "预览提取的动作…" : "预览上次的动作…";
                preview.SetResourceReference(StyleProperty, currentResult || !selected ? (object)"PetPrimary" : typeof(Button));
                copyAgain.Content = matchesDraft ? "再次复制制作说明" : "复制上次的制作说明";
                copyAgain.Visibility = !busy && !ready && request != null ? Visibility.Visible : Visibility.Collapsed;
                cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                if (ready && !busy && !readyAnnounced) { readyAnnounced = true; if (currentResult) status.Text = "动作已准备好。预览轮廓和播放效果后，就可以使用。"; }
            }

            private async void PickVideo()
            {
                using (Forms.OpenFileDialog picker = new Forms.OpenFileDialog { Filter = "宠物视频|*.mp4;*.mov;*.m4v;*.webm;*.mkv;*.avi" }) {
                    if (picker.ShowDialog() != Forms.DialogResult.OK) return;
                    path = picker.FileName;
                }
                matchesDraft = false; length = 0; start.Text = "0"; duration.Text = "2"; selectedFrameAt = -1;
                still.Source = null; frame.Visibility = Visibility.Collapsed; rect = Rect.Empty; keyColor = "auto"; chooseColor.Content = "背景色：自动取色";
                fileLabel.Text = Path.GetFileName(path); status.Text = ""; UpdateMode();
                if (method.SelectedIndex != 2) await LoadFrame(true);
            }

            private static bool TrySeconds(TextBox box, out double value)
            {
                return (Double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                    Double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    && !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0;
            }

            private static double ReadSeconds(TextBox box, string label)
            {
                double value;
                if (!TrySeconds(box, out value)) throw new InvalidDataException(label + "需要填写有效秒数。");
                return value;
            }

            private CancellationToken BeginWork(string message)
            {
                operation = new CancellationTokenSource(); status.Text = message; RefreshButtons(); return operation.Token;
            }

            private void EndWork()
            {
                operation.Dispose(); operation = null; if (!closed) RefreshButtons();
            }

            private async Task LoadFrame(bool probe)
            {
                if (operation != null || path == null) return;
                CancellationToken token = BeginWork("正在读取本地视频画面…");
                string temporary = Path.Combine(Path.GetTempPath(), "ai-pet-frame-" + Guid.NewGuid().ToString("N") + ".png");
                try {
                    double at = ReadSeconds(start, "开始时间");
                    if (probe) {
                        string json = await Task.Run(() => RunVideoWorker(token, "--probe", "--video", path));
                        Dictionary<string, object> info = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                        if (info != null && info.ContainsKey("duration")) length = Convert.ToDouble(info["duration"], CultureInfo.InvariantCulture);
                        if (length > 0) {
                            fileLabel.Text = Path.GetFileName(path) + "  ·  " + length.ToString("0.##", CultureInfo.CurrentCulture) + " 秒";
                            if (duration.Text == "2" && length < 2) duration.Text = Math.Min(2, length).ToString("0.###", CultureInfo.CurrentCulture);
                        }
                    }
                    await Task.Run(() => RunVideoWorker(token, "--thumbnail", temporary, "--video", path, "--start", Number(at)));
                    token.ThrowIfCancellationRequested();
                    BitmapImage bitmap = new BitmapImage();
                    using (FileStream stream = File.OpenRead(temporary)) {
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    }
                    still.Source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    selectedFrameAt = at; frame.Visibility = Visibility.Visible; rect = Rect.Empty; DrawSelection(); status.Text = "";
                } catch (OperationCanceledException) { if (!closed) status.Text = "已取消。"; }
                catch (Exception error) { if (!closed) status.Text = error.Message; }
                finally { try { File.Delete(temporary); } catch (IOException) { } EndWork(); }
            }

            private static string Number(double value) { return value.ToString("0.########", CultureInfo.InvariantCulture); }

            private Rect ImageArea()
            {
                BitmapSource bitmap = still.Source as BitmapSource;
                if (bitmap == null || frame.ActualWidth <= 0 || frame.ActualHeight <= 0) return Rect.Empty;
                double scale = Math.Min(frame.ActualWidth / bitmap.PixelWidth, frame.ActualHeight / bitmap.PixelHeight);
                double w = scale * bitmap.PixelWidth, h = scale * bitmap.PixelHeight;
                return new Rect((frame.ActualWidth - w) / 2, (frame.ActualHeight - h) / 2, w, h);
            }

            private static Point Normalized(Point p, Rect area)
            {
                return new Point(Math.Max(0, Math.Min(1, (p.X - area.X) / area.Width)),
                    Math.Max(0, Math.Min(1, (p.Y - area.Y) / area.Height)));
            }

            private void BeginSelection(object sender, MouseButtonEventArgs e)
            {
                Rect area = ImageArea(); Point p = e.GetPosition(frame);
                if (area.IsEmpty || !area.Contains(p)) return;
                Point n = Normalized(p, area);
                if (method.SelectedIndex == 1) {
                    BitmapSource bitmap = (BitmapSource)still.Source; byte[] pixel = new byte[4];
                    bitmap.CopyPixels(new Int32Rect(Math.Min(bitmap.PixelWidth-1,(int)(n.X*bitmap.PixelWidth)),
                        Math.Min(bitmap.PixelHeight-1,(int)(n.Y*bitmap.PixelHeight)),1,1),pixel,4,0);
                    SetColor(String.Format("#{0:X2}{1:X2}{2:X2}",pixel[2],pixel[1],pixel[0])); return;
                }
                drag = n; rect = Rect.Empty; DrawSelection(); DraftChanged(); frame.CaptureMouse(); e.Handled = true;
            }

            private void MoveSelection(object sender, MouseEventArgs e)
            {
                if (!drag.HasValue || ImageArea().IsEmpty) return;
                rect = new Rect(drag.Value, Normalized(e.GetPosition(frame), ImageArea())); DrawSelection();
            }

            private void EndSelection(object sender, MouseButtonEventArgs e)
            {
                if (!drag.HasValue) return;
                MoveSelection(sender, e); drag = null; frame.ReleaseMouseCapture(); RefreshButtons(); e.Handled = true;
            }

            private void DrawSelection()
            {
                Rect area = ImageArea();
                if (method.SelectedIndex != 0 || rect.IsEmpty || area.IsEmpty) { outline.Visibility = Visibility.Collapsed; return; }
                Canvas.SetLeft(outline, area.X + rect.X * area.Width); Canvas.SetTop(outline, area.Y + rect.Y * area.Height);
                outline.Width = rect.Width * area.Width; outline.Height = rect.Height * area.Height; outline.Visibility = Visibility.Visible;
            }

            private void PickColor()
            {
                using (Forms.ColorDialog dialog = new Forms.ColorDialog()) {
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                        SetColor(String.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
            private void SetColor(string value) { keyColor = value; chooseColor.Content = "背景色：" + value; DraftChanged(); }
            private void CopyInstructions()
            {
                Clipboard.SetText(VideoHandoffText(request));
                status.Text = "已复制。粘贴给你的 Agent，完成后回到这里预览。";
            }

            private async void ExtractAndPreview()
            {
                string previous = request;
                await MakeRequest();
                Window owner = Window.GetWindow(this);
                if (closed || !IsVisible || owner == null || !owner.IsActive || method.SelectedIndex == 2 ||
                    !matchesDraft || request == previous || VideoCandidatePath(request) == null) return;
                try { PreviewVideoCandidate(request); }
                catch (Exception error) { status.Text = error.Message; }
            }

            private async Task MakeRequest()
            {
                if (operation != null || path == null) return;
                CancellationToken token = BeginWork("正在保存视频片段的制作请求…");
                try {
                    double at = ReadSeconds(start,"开始时间"), seconds = ReadSeconds(duration,"片段时长");
                    if (seconds <= 0 || seconds > 12) throw new InvalidDataException("请选择大于 0、最多 12 秒的片段。");
                    if (length > 0 && at + seconds > length + .002) throw new InvalidDataException("片段超出了视频时长，请调整开始时间或时长。");
                    int fps = Math.Min(12, (int)Math.Floor(120.0 / seconds));
                    if (seconds * fps < 2) throw new InvalidDataException("片段过短，请至少选择约 0.17 秒。");
                    bool agent = method.SelectedIndex == 2, isLoop = loop.IsChecked == true;
                    string slot = Slots[action.SelectedIndex], description = agent ? instructions.Text : "";
                    string localMethod = method.SelectedIndex == 0 ? "grabcut" : "chroma";
                    if (!agent && localMethod == "grabcut" && (Math.Abs(at - selectedFrameAt) > .0001 || rect.IsEmpty ||
                        rect.Width < .02 || rect.Height < .02 || rect.Width * rect.Height > .98))
                        throw new InvalidDataException("请查看当前片段的起始画面，再拖出宠物框，保留少量框外背景。");
                    string id = await Task.Run(() => SaveVideoRequest(path, at, seconds, fps, slot, isLoop, description, token));
                    request = id; matchesDraft = true; readyAnnounced = false; token.ThrowIfCancellationRequested();
                    if (agent) { CopyInstructions(); return; }
                    string folder = VideoRequestFolder(id), output = Path.Combine(folder,"local-" + Guid.NewGuid().ToString("N") + ".lotheme");
                    List<string> args = new List<string> { "--video", SavedVideoSourcePath(id), "--output", output,
                        "--name", "视频动作", "--method", localMethod, "--start", Number(at), "--duration", Number(seconds),
                        "--fps", fps.ToString(CultureInfo.InvariantCulture), "--slot", slot };
                    if (isLoop) args.Add("--loop");
                    if (localMethod == "grabcut") {
                        args.Add("--rect"); args.Add(String.Join(",", new[] { Number(rect.X),Number(rect.Y),Number(rect.Width),Number(rect.Height) }));
                    } else {
                        args.Add("--color"); args.Add(keyColor); args.Add("--tolerance"); args.Add(Number(Math.Round(tolerance.Value)));
                    }
                    status.Text = "正在逐帧提取宠物动作…";
                    await Task.Run(delegate {
                        try { RunVideoWorker(token, args.ToArray()); token.ThrowIfCancellationRequested(); SubmitVideoCandidate(id, output); }
                        finally { if (File.Exists(output)) File.Delete(output); }
                    });
                } catch (OperationCanceledException) { matchesDraft = false; if (!closed) status.Text = "已取消，可调整后重试。"; }
                catch (Exception error) { matchesDraft = false; if (!closed) status.Text = error.Message; }
                finally { EndWork(); }
            }
        }

        private static string RunVideoWorker(CancellationToken token, params string[] arguments)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory, relative = Path.Combine("tools", "extract_pet_video.py");
            string script = Path.Combine(root, relative);
            if (!File.Exists(script)) script = Path.GetFullPath(Path.Combine(root, "..", "..", relative));
            if (!File.Exists(script)) throw new FileNotFoundException("本机提取组件未安装。可交给 Agent 抠像，或重新安装含视频工具的版本。");
            string python = Environment.GetEnvironmentVariable("AI_PET_VIDEO_PYTHON");
            if (String.IsNullOrWhiteSpace(python)) {
                foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                    string dir = entry.Trim().Trim('"');
                    if (!Path.IsPathRooted(dir) || dir.IndexOf("\\WindowsApps",StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string exe = Path.Combine(dir, "python.exe"); if (File.Exists(exe)) { python = exe; break; }
                }
            }
            if (String.IsNullOrWhiteSpace(python) || !Path.IsPathRooted(python) || !File.Exists(python))
                throw new FileNotFoundException("本机提取需要 Python 环境。可交给 Agent 抠像，或让 Agent 配置 AI_PET_VIDEO_PYTHON 后重启。");
            List<string> quoted = new List<string> { QuoteGeneratorArgument(script) };
            foreach (string value in arguments) quoted.Add(QuoteGeneratorArgument(value));
            ProcessStartInfo info = new ProcessStartInfo(python,String.Join(" ",quoted.ToArray())) {
                CreateNoWindow=true, UseShellExecute=false, RedirectStandardError=true, RedirectStandardOutput=true,
                StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8, WorkingDirectory=Path.GetDirectoryName(script) };
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            using (GeneratorJob job = new GeneratorJob())
            using (Process process = new Process { StartInfo = info }) {
                token.ThrowIfCancellationRequested(); process.Start();
                try { job.Attach(process); } catch { if (!process.HasExited) process.Kill(); throw; }
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
                Stopwatch clock = Stopwatch.StartNew();
                while (!process.WaitForExit(150)) {
                    token.ThrowIfCancellationRequested();
                    if (clock.Elapsed.TotalMinutes > 5) throw new TimeoutException("提取超时。可以缩短片段，或交给 Agent 抠像。");
                }
                token.ThrowIfCancellationRequested();
                string output = stdout.GetAwaiter().GetResult().Trim(), error = stderr.GetAwaiter().GetResult().Trim();
                if (process.ExitCode == 0) return output;
                try { error = ReadString(new JavaScriptSerializer().DeserializeObject(output) as Dictionary<string, object>, "error") ?? error; }
                catch (Exception) { }
                if (error.Length > 350) error = error.Substring(error.Length - 350);
                throw new InvalidDataException("本机视频工具未完成。 " + error + " 可改用 Agent 抠像。");
            }
        }
    }
}
