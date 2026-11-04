using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AIPet.Desktop
{
    internal static partial class DesktopPet
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GlassMargins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref GlassMargins margins);

        private static int PersonalizeValue(string name, int fallback)
        {
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key == null ? fallback : Convert.ToInt32(key.GetValue(name, fallback)); }
            catch { return fallback; }
        }

        private static void SetAppearanceBrush(Window target, string key, string value)
        { target.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }

        private static void RefreshModernWindow(Window target, bool? forceDark)
        {
            bool contrast = SystemParameters.HighContrast;
            bool dark = forceDark ?? (PersonalizeValue("AppsUseLightTheme", 1) == 0);
            string[,] palette = {
                {"PetText", "#1A1A1A", "#F5F5F5"}, {"PetMuted", "#616161", "#BDBDBD"},
                {"PetBase", "#F3F3F3", "#202020"}, {"PetSurface", "#CCFFFFFF", "#CC2B2B2B"},
                {"PetNav", "#40FFFFFF", "#40202020"}, {"PetControl", "#EFFFFFFF", "#EF333333"},
                {"PetBorder", "#26000000", "#30FFFFFF"}, {"PetHover", "#E5E5E5", "#454545"},
                {"PetAccent", "#005FB8", "#60CDFF"}, {"PetAccentText", "#FFFFFF", "#002536"},
                {"PetSelected", "#DCEBFA", "#243D4B"} };
            for (int i = 0; i < palette.GetLength(0); i++) SetAppearanceBrush(target, palette[i, 0], palette[i, dark ? 2 : 1]);
            if (contrast) {
                foreach (string key in new [] {"PetBase", "PetSurface", "PetNav", "PetControl", "PetHover"}) target.Resources[key] = SystemColors.WindowBrush;
                foreach (string key in new [] {"PetText", "PetMuted", "PetBorder"}) target.Resources[key] = SystemColors.WindowTextBrush;
                target.Resources["PetSelected"] = SystemColors.ControlBrush;
                target.Resources["PetAccent"] = SystemColors.HighlightBrush;
                target.Resources["PetAccentText"] = SystemColors.HighlightTextBrush;
            }
            target.SetResourceReference(Control.ForegroundProperty, "PetText");
            IntPtr hwnd = new WindowInteropHelper(target).Handle;
            bool material = false;
            if (hwnd != IntPtr.Zero) {
                try {
                    int mode = dark ? 1 : 0, corners = 2;
                    DwmSetWindowAttribute(hwnd, 20, ref mode, 4);
                    DwmSetWindowAttribute(hwnd, 33, ref corners, 4);
                    int backdrop = !contrast && PersonalizeValue("EnableTransparency", 1) != 0 ? 2 : 1;
                    material = DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4) == 0 && backdrop == 2;
                    int margin = material ? -1 : 0;
                    GlassMargins margins = new GlassMargins { Left = margin, Right = margin, Top = margin, Bottom = margin };
                    if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) material = false;
                    HwndSource source = HwndSource.FromHwnd(hwnd);
                    if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = material ? Colors.Transparent : ((SolidColorBrush)target.Resources["PetBase"]).Color;
                } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
            target.Background = material ? Brushes.Transparent : (Brush)target.Resources["PetBase"];
        }

        private static void AttachModernAppearance(Window target)
        {
            target.FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
            target.FontSize = 14; target.UseLayoutRounding = true;
            target.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(ModernControls));
            RefreshModernWindow(target, null);
            target.SourceInitialized += delegate {
                RefreshModernWindow(target, null);
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(target).Handle);
                HwndSourceHook hook = delegate(IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled) {
                    if (message == 0x001A || message == 0x031A || message == 0x031E || message == 0x0320)
                        target.Dispatcher.BeginInvoke(new Action(delegate { if (target.IsLoaded) RefreshModernWindow(target, null); }));
                    return IntPtr.Zero;
                };
                source.AddHook(hook);
                target.Closed += delegate { if (!source.IsDisposed) source.RemoveHook(hook); };
            };
        }

        private static void AttachModernDialog(Window target)
        {
            AttachModernAppearance(target);
            Action<DependencyObject> visit = null;
            visit = delegate(DependencyObject item) {
                TextBlock text = item as TextBlock;
                if (text != null) text.SetResourceReference(TextBlock.ForegroundProperty, "PetText");
                Button button = item as Button;
                if (button != null) {
                    bool primary = button.IsDefault;
                    button.ClearValue(Control.BackgroundProperty); button.ClearValue(Control.ForegroundProperty);
                    if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PetPrimary");
                }
                Panel panel = item as Panel;
                if (panel != null && panel.Background != null) panel.Background = Brushes.Transparent;
                foreach (object child in LogicalTreeHelper.GetChildren(item)) if (child is DependencyObject) visit((DependencyObject)child);
            };
            if (target.Content is DependencyObject) visit((DependencyObject)target.Content);
        }

        private static void CaptureAppearanceContent(Window target, string path)
        {
            target.UpdateLayout();
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) {
                dc.DrawRectangle((Brush)target.Resources["PetBase"], null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                dc.DrawRectangle(new VisualBrush(target), null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (Stream stream = File.Create(path)) png.Save(stream);
        }

        private static void TestModernAppearance(string output)
        {
            string data = Environment.GetEnvironmentVariable("AI_PET_DATA_DIR");
            if (String.IsNullOrEmpty(data) || String.Equals(Path.GetFullPath(data).TrimEnd('\\'),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Pet Studio"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Appearance QA requires isolated data.");
            if (Directory.Exists(output)) throw new InvalidDataException("Refusing to overwrite appearance evidence.");
            Directory.CreateDirectory(output);
            System.Windows.Application app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            activeTheme = CreatePublicDemoTheme(); BuildTray(); BuildWindow(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference-cat.jpg")); ShowPetManager(0);
            bool resources = true, popup = false, selection = false;
            foreach (bool dark in new [] {false, true}) {
                RefreshModernWindow(petManager, dark);
                resources &= ((SolidColorBrush)petManager.Resources["PetText"]).Color == (Color)ColorConverter.ConvertFromString(dark ? "#F5F5F5" : "#1A1A1A");
                for (int i = 0; i < 3; i++) {
                    petManagerTabs.SelectedIndex = i;
                    CaptureAppearanceContent(petManager, Path.Combine(output, (dark ? "dark-" : "light-") + i + ".png"));
                }
            }
            petManagerTabs.SelectedIndex = 1; petManager.UpdateLayout();
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            petManager.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(delegate { frame.Continue = false; }));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Action<DependencyObject> find = null;
            find = delegate(DependencyObject item) {
                Expander mappings = item as Expander;
                if (mappings != null && System.Windows.Automation.AutomationProperties.GetAutomationId(mappings) == "StateActionBindings") mappings.IsExpanded = true;
                ComboBox combo = item as ComboBox;
                if (combo != null && Array.IndexOf(AiStates, combo.Tag as string) >= 0 && !popup) {
                    int originalIndex = combo.SelectedIndex;
                    combo.ApplyTemplate(); combo.IsDropDownOpen = true; combo.UpdateLayout();
                    System.Windows.Threading.DispatcherFrame popupFrame = new System.Windows.Threading.DispatcherFrame();
                    combo.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(delegate { popupFrame.Continue = false; }));
                    System.Windows.Threading.Dispatcher.PushFrame(popupFrame);
                    System.Windows.Controls.Primitives.Popup list = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup;
                    popup = list != null && list.IsOpen && combo.Items.Count > 1;
                    combo.SelectedIndex = combo.SelectedIndex == 0 ? 1 : 0;
                    ComboBoxItem selected = combo.SelectedItem as ComboBoxItem;
                    selection = selected != null && aiBindings[(string)combo.Tag] == (string)selected.Tag;
                    combo.IsDropDownOpen = false;
                    combo.SelectedIndex = originalIndex;
                }
                foreach (object child in LogicalTreeHelper.GetChildren(item)) if (child is DependencyObject) find((DependencyObject)child);
            }; find(petManagerTabs);
            // Simulate notification delivery, without modifying Windows preferences.
            bool systemDark = PersonalizeValue("AppsUseLightTheme", 1) == 0;
            RefreshModernWindow(petManager, !systemDark);
            IntPtr hwnd = new WindowInteropHelper(petManager).Handle; int backdrop, corners;
            SendMessage(hwnd, 0x001A, IntPtr.Zero, IntPtr.Zero);
            System.Windows.Threading.DispatcherFrame notificationFrame = new System.Windows.Threading.DispatcherFrame();
            petManager.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(delegate { notificationFrame.Continue = false; }));
            System.Windows.Threading.Dispatcher.PushFrame(notificationFrame);
            bool systemRefresh = ((SolidColorBrush)petManager.Resources["PetText"]).Color == (Color)ColorConverter.ConvertFromString(systemDark ? "#F5F5F5" : "#1A1A1A");
            int hr = DwmGetWindowAttribute(hwnd, 38, out backdrop, 4);
            DwmGetWindowAttribute(hwnd, 33, out corners, 4);
            File.WriteAllText(Path.Combine(output, "result.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                passed = resources && popup && selection && systemRefresh, lightDarkResources = resources, popupOpened = popup, mappingSelection = selection, systemNotificationRefresh = systemRefresh,
                systemBackdropReadSucceeded = hr == 0, systemBackdrop = backdrop, roundedCorners = corners, highContrast = SystemParameters.HighContrast,
                systemLightTheme = PersonalizeValue("AppsUseLightTheme", 1), transparency = PersonalizeValue("EnableTransparency", 1) }));
            ExitApplication();
            if (!resources || !popup || !selection || !systemRefresh) throw new InvalidOperationException("Appearance QA failed.");
        }

        // Scoped to our settings/review windows. The transparent sprite and image
        // review backgrounds keep their rendering and approval semantics.
        private const string ModernControls = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='12'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'>
    <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='Grip' Margin='4,2' Background='{DynamicResource PetMuted}' CornerRadius='2' Opacity='0.5'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Opacity' Value='0.9'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
   </Track></Grid>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Height' Value='12'/><Setter Property='Width' Value='Auto'/></Trigger></Style.Triggers>
 </Style>
 <Style TargetType='TextBlock'><Setter Property='Foreground' Value='{DynamicResource PetText}'/></Style>
 <Style TargetType='Slider'>
  <Setter Property='MinHeight' Value='24'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
   <Grid Margin='0,4'><Track x:Name='PART_Track'>
    <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='{DynamicResource PetAccent}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb Width='20' Height='20'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{DynamicResource PetBase}' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1' CornerRadius='10'><Ellipse Fill='{DynamicResource PetAccent}' Margin='4'/></Border></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='{DynamicResource PetMuted}' Opacity='0.4'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
   </Track></Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='Button'>
  <Setter Property='Background' Value='{DynamicResource PetControl}'/><Setter Property='Foreground' Value='{DynamicResource PetText}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource PetBorder}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,8'/>
  <Setter Property='MinHeight' Value='34'/><Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'>
    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
   </Border><ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.85'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.7'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource PetAccent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='PetPrimary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
  <Setter Property='Background' Value='{DynamicResource PetAccent}'/><Setter Property='Foreground' Value='{DynamicResource PetAccentText}'/>
 </Style>
 <Style x:Key='PetSegment' TargetType='RadioButton'>
  <Setter Property='MinWidth' Value='104'/><Setter Property='MinHeight' Value='34'/>
  <Setter Property='Foreground' Value='{DynamicResource PetMuted}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
   <Border x:Name='Segment' CornerRadius='6' Padding='12,5' BorderThickness='1' BorderBrush='Transparent' Background='Transparent'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
   </Border><ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Segment' Property='Background' Value='{DynamicResource PetHover}'/></Trigger>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Segment' Property='Background' Value='{DynamicResource PetSelected}'/><Setter TargetName='Segment' Property='BorderBrush' Value='{DynamicResource PetBorder}'/><Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Segment' Property='BorderBrush' Value='{DynamicResource PetAccent}'/></Trigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TextBox'>
  <Setter Property='Background' Value='{DynamicResource PetControl}'/><Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='CaretBrush' Value='{DynamicResource PetText}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource PetBorder}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='10,7'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'>
    <ScrollViewer x:Name='PART_ContentHost' Margin='0' Padding='{TemplateBinding Padding}' Focusable='False' HorizontalScrollBarVisibility='{TemplateBinding HorizontalScrollBarVisibility}' VerticalScrollBarVisibility='{TemplateBinding VerticalScrollBarVisibility}'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource PetAccent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='Padding' Value='10,7'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
   <Border x:Name='Chrome' CornerRadius='4' Background='Transparent' Margin='3,1' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PetHover}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PetSelected}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox'>
  <Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='Background' Value='{DynamicResource PetControl}'/><Setter Property='MinHeight' Value='36'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
   <Grid><ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
    <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='Chrome' Background='{DynamicResource PetControl}' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1' CornerRadius='6'>
     <TextBlock Text='⌄' HorizontalAlignment='Right' Margin='0,0,12,0' VerticalAlignment='Center'/></Border>
     <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PetHover}'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></ToggleButton.Template></ToggleButton>
    <ContentPresenter Margin='12,7,32,7' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' VerticalAlignment='Center'/>
    <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
     <Border Background='{DynamicResource PetBase}' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1' CornerRadius='7' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
      <ScrollViewer Margin='3' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
     </Border></Popup>
   </Grid><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='CheckBox'>
  <Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <StackPanel Orientation='Horizontal'><Border x:Name='Box' Width='19' Height='19' CornerRadius='4' Background='{DynamicResource PetControl}' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1' VerticalAlignment='Center'>
    <TextBlock x:Name='Mark' Text='✓' Visibility='Hidden' Foreground='{DynamicResource PetAccentText}' HorizontalAlignment='Center' VerticalAlignment='Center' FontSize='13'/></Border>
    <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/></StackPanel>
   <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource PetAccent}'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource PetAccent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='Expander'>
  <Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Expander'>
   <Border CornerRadius='8' Background='{DynamicResource PetSurface}' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1'>
    <StackPanel><ToggleButton Foreground='{DynamicResource PetText}' IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' HorizontalContentAlignment='Left'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>
      <Border x:Name='Head' BorderThickness='1' BorderBrush='Transparent' CornerRadius='7' Background='Transparent' Padding='13,10'>
       <DockPanel><Path x:Name='Chevron' DockPanel.Dock='Right' Data='M 1,2 L 5,6 L 9,2' Stroke='{DynamicResource PetMuted}' StrokeThickness='1.5' Width='10' Height='8' Margin='12,0,0,0' VerticalAlignment='Center'/><ContentPresenter/></DockPanel>
      </Border><ControlTemplate.Triggers>
       <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chevron' Property='Data' Value='M 1,6 L 5,2 L 9,6'/></Trigger>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Head' Property='Background' Value='{DynamicResource PetHover}'/></Trigger>
       <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Head' Property='BorderBrush' Value='{DynamicResource PetAccent}'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
     <ContentPresenter ContentSource='Header'/></ToggleButton>
     <ContentPresenter x:Name='Body' ContentSource='Content' Margin='14,4,14,12' Visibility='Collapsed'/>
    </StackPanel></Border><ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='Body' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TabItem'>
  <Setter Property='Foreground' Value='{DynamicResource PetText}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'>
   <Border x:Name='Chrome' Background='Transparent' CornerRadius='6' Padding='14,12' Margin='10,3'>
    <Grid><Border x:Name='Indicator' Width='3' Height='18' Background='{DynamicResource PetAccent}' CornerRadius='2' HorizontalAlignment='Left' Margin='-14,0,0,0' Visibility='Hidden'/>
     <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/></Grid></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PetHover}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PetSelected}'/><Setter TargetName='Indicator' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='TabControl'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabControl'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='170'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <Border Background='{DynamicResource PetNav}' Padding='0,14'><ItemsPresenter KeyboardNavigation.TabNavigation='Once'/></Border>
    <Border Grid.Column='1' Margin='0,10,12,12' Background='{DynamicResource PetSurface}' CornerRadius='12' BorderBrush='{DynamicResource PetBorder}' BorderThickness='1'>
     <ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent' Margin='4'/></Border>
   </Grid></ControlTemplate></Setter.Value></Setter>
  <Setter Property='ItemsPanel'><Setter.Value><ItemsPanelTemplate><StackPanel IsItemsHost='True'/></ItemsPanelTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
    }
}
