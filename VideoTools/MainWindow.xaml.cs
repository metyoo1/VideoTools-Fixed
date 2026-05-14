using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
//using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

using System.Diagnostics;
using System.Collections.ObjectModel;

using IOPath = System.IO.Path;
using System.Threading;
using System.Windows.Controls.Primitives;

namespace VideoTools
{
    /* 软件配置文件解析 */
    public class IniConfig
    {
        private readonly string _filePath;

        public IniConfig()
        {
            /* 获取用户目录路径，C:\Users\xxxxx\AppData\Roaming */
            var userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            /* 定义配置文件的路径 */
            var appName = ".VideoTools_250405"; /* 使用这种特殊的目录避免程序名和其他程序冲突 */
            var appDataPath = IOPath.Combine(userDir, appName);
            _filePath = IOPath.Combine(appDataPath, "config.ini");

            /* 如果目录不存在，则创建 */
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            /* 如果文件不存在则创建默认配置 */
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "[Program]\nFFmpegPath=\nGpuUse=enbale\nCpuMax=disable");
            }
        }

        public string Read(string section, string key)
        {
            var lines = File.ReadAllLines(_filePath);
            var sectionPattern = string.Format("[{0}]", section);
            var inSection = false;

            foreach (var line in lines)
            {
                if (line.Trim() == sectionPattern)
                {
                    inSection = true;
                    continue;
                }

                if (inSection && line.Contains('='))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts[0].Trim() == key)
                        return parts[1].Trim();
                }
            }
            return null;
        }

        public void Write(string section, string key, string value)
        {
            var lines = new List<string>(File.ReadAllLines(_filePath));
            var sectionPattern = string.Format("[{0}]", section);
            var foundSection = false;
            var sectionStart = -1;
            var sectionEnd = lines.Count;

            /* 查找section的起始和结束位置 */
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() == sectionPattern)
                {
                    foundSection = true;
                    sectionStart = i;
                    /* 寻找section的结束位置（下一个section的起始或文件末尾） */
                    for (var j = i + 1; j < lines.Count; j++)
                    {
                        if (lines[j].Trim().StartsWith("[") && lines[j].Trim().EndsWith("]"))
                        {
                            sectionEnd = j;
                            break;
                        }
                    }
                    break;
                }
            }

            if (foundSection)
            {
                var keyFound = false;
                /* 在section范围内查找键 */
                for (var i = sectionStart + 1; i < sectionEnd; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Contains('='))
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts[0].Trim() == key)
                        {
                            lines[i] = string.Format("{0}={1}", key, value);
                            keyFound = true;
                            break;
                        }
                    }
                }

                if (!keyFound)
                {
                    /* 在section的末尾插入新行 */
                    lines.Insert(sectionEnd, string.Format("{0}={1}", key, value));
                }
            }
            else
            {
                /* 添加新section和键值对到文件末尾 */
                lines.Add(sectionPattern);
                lines.Add(string.Format("{0}={1}", key, value));
            }

            File.WriteAllLines(_filePath, lines.ToArray());
        }
    }
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer uiTimer, hideControlTimer, labelClearTimer;
        private bool isDraggingSlider = false, isPlaying = false, isInited = false, isFileOpened = false;
        private string sVideoFilePath, sFFmpegFilePath, sVideoSaveFolder;
        private string videoCodeChoose = "radiobtn_compress_264", sOutVideoCode = " -c:v libx264";
        private string sConvertVideoCode = " -c:v libx264", sVideoFormat = ".mp4", sCrf = "25", sCompressFormat = " -crf 25 ";
        private string sAnimatedImageFormat = ".gif"; /* .gif 或 .webp */
        private string sToolChoose = "compress";
        private CancellationTokenSource _cancellationTokenSource; /* 全局的取消事件 */
        private double baseBitrate = 0.0;
        private int maxThreads = 0;
        private bool isBatchMode = false; /* 批量压缩模式 */
        private bool isCompressSubExpanded = false; /* 侧栏“视频压缩”子项展开状态 */

        /* 批量文件项 */
        private class BatchFileItem
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
        }

        private ObservableCollection<BatchFileItem> batchFiles = new ObservableCollection<BatchFileItem>();

        /* 合并文件项 */
        private class MergeFileItem
        {
            public int Index { get; set; }
            public string FileName { get; set; }
            public string FilePath { get; set; }
        }

        private ObservableCollection<MergeFileItem> mergeFiles = new ObservableCollection<MergeFileItem>();
        private static readonly HashSet<string> _videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",".avi",".wmv",".mov",".mkv",".webm",".flv",".ts",".m2ts",".mts",".mpg",".mpeg",".m4v",".3gp",".3g2",".ogv",".vob",".asf",".mxf",".rmvb",".rm",".y4m"
        };

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();

            // 强制固定窗口尺寸
            this.MaxHeight = this.Height;
            this.MaxWidth = this.Width;
            this.MinHeight = this.Height;
            this.MinWidth = this.Width;

            maxThreads = Environment.ProcessorCount;
        }

        /* 全局: 软件启动引导 */
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var config = new IniConfig();
            sFFmpegFilePath = config.Read("Program", "FFmpegPath");
            sVideoSaveFolder = config.Read("Program", "VideoSaveFloder");

            var gpuuse = config.Read("Program", "GpuUse");
            if (string.IsNullOrEmpty(gpuuse)) {
                config.Write("Program", "GpuUse", "disable");
                checkBox_Setting_GpuUse.IsChecked = false;
            }
            else { 
                checkBox_Setting_GpuUse.IsChecked = (gpuuse == "enable");
            }

            var gpuselect = config.Read("Program", "GpuSelect");
            if (string.IsNullOrEmpty(gpuuse))
            {
                config.Write("Program", "GpuSelect", (-1).ToString());
                comboBox_Setting_GpuSelect.Visibility = Visibility.Collapsed;
            }

            if (true == checkBox_Setting_GpuUse.IsChecked)
            {
                comboBox_Setting_GpuSelect.Visibility = Visibility.Visible;
                switch (gpuselect)
                {
                    case "0":
                        sOutVideoCode = " -c:v h264_qsv ";
                        comboBox_Setting_GpuSelect.SelectedIndex = int.Parse(gpuselect);
                        break;
                    case "1":
                        sOutVideoCode = " -c:v h264_nvenc ";
                        comboBox_Setting_GpuSelect.SelectedIndex = int.Parse(gpuselect);
                        break;
                    case "2":
                        sOutVideoCode = " -c:v h264_amf ";
                        comboBox_Setting_GpuSelect.SelectedIndex = int.Parse(gpuselect);
                        break;
                    default:
                        comboBox_Setting_GpuSelect.SelectedIndex = -1;
                        break;
                }
            }
            else {
                comboBox_Setting_GpuSelect.Visibility = Visibility.Collapsed;
            }

            var cpumax = config.Read("Program", "CpuMax");
            if (string.IsNullOrEmpty(cpumax))
            {
                config.Write("Program", "CpuMax", "disable");
                checkBox_Setting_CpuMax.IsChecked = false;
            }
            else
            {
                checkBox_Setting_CpuMax.IsChecked = (cpumax == "enable");
            }

            if (!isFFmpegExist())
            {
                MessageBox.Show("ffmpeg未配置，请在设置中设置ffmpeg路径", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                radiobtn_setting.IsChecked = true;
                settingPanel.Visibility = Visibility.Visible;
                videoPlayerFrom.Visibility = Visibility.Collapsed;
            }
            else
            {
                /* 软件启动默认选中视频压缩RadioButton */
                radiobtn_compress.IsChecked = true;
                /* 同时显示对应的面板 */
                compressPanel.Visibility = Visibility.Visible;
                textBoxSettingFmmpegPath.Text = sFFmpegFilePath;
            }

            if (string.IsNullOrEmpty(sVideoSaveFolder)) {
                sVideoSaveFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }

            textBoxVideoDir.Text = sVideoSaveFolder;

            config = null;

            isInited = true;
        }

        /* 全局: 播放视频 */
        private void PlayVideo(string filePath)
        {
            try
            {
                if (IsVideoFile(filePath))
                {
                    var uri = new Uri(filePath);
                    mediaPlayer.Source = uri;
                    mediaPlayer.Pause();
                    isPlaying = false;

                    /* 显示文件名 */
                    txtFileName.Text = System.IO.Path.GetFileName(filePath);

                    /* 显示控制栏 */
                    hideControlTimer.Stop();
                    overlayGrid.Visibility = Visibility.Visible;
                    /* 显示顶部信息栏 */
                    topBar.Visibility = Visibility.Visible;
                }
                else
                {
                    MessageBox.Show("不支持的文件格式");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("播放失败: {0}", ex.Message));
            }
        }

        /* 全局: 验证视频格式 */
        private bool IsVideoFile(string filePath)
        {
            try
            {
                var ext = IOPath.GetExtension(filePath).ToLower();
                return _videoExts.Contains(ext);
            }
            catch { return false; }
        }

        /* 全局: 初始化定时器 */
        private void InitializeTimer()
        {
            uiTimer = new DispatcherTimer(DispatcherPriority.Render);
            uiTimer.Interval = TimeSpan.FromMilliseconds(33); /* 约30FPS */
            uiTimer.Tick += Timer_Tick;

            hideControlTimer = new DispatcherTimer();
            hideControlTimer.Interval = TimeSpan.FromSeconds(0.5); /* 0.5秒后隐藏 */
            hideControlTimer.Tick += (s, e) =>
            {
                controlBar.Visibility = Visibility.Collapsed;
                hideControlTimer.Stop();
            };

            // 初始化labelClearTimer
            labelClearTimer = new DispatcherTimer();
            labelClearTimer.Interval = TimeSpan.FromSeconds(2);
            labelClearTimer.Tick += (s, e) =>
            {
                labelSettingInfo.Content = "";
                labelClearTimer.Stop();
            };

        }

        /* 视频窗口: 拖放文件处理（支持批量模式） */
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    if (isBatchMode)
                    {
                        AddBatchFiles(files);
                    }
                    else
                    {
                        PlayVideo(files[0]);
                        sVideoFilePath = files[0];
                    }
                }
            }
        }

        /* 视频窗口: 按钮点击打开文件 */
        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm;*.flv;*.ts;*.m2ts;*.mts;*.mpg;*.mpeg;*.m4v;*.3gp;*.3g2;*.ogv;*.vob;*.asf;*.mxf;*.rmvb;*.rm;*.y4m|所有文件|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                mediaPlayer.Stop();
                playPauseButton.Content = "\ue87c"; /* 播放图标 */
                isPlaying = false;
                /* 暂停时显示控制栏并停止隐藏计时器 */
                controlBar.Visibility = Visibility.Visible;
                hideControlTimer.Stop();
                PlayVideo(openFileDialog.FileName);
                sVideoFilePath = openFileDialog.FileName;
            }
        }

        /* 批量模式：辅助方法与事件 */
        private void AddBatchFiles(IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                if (!IsVideoFile(f)) continue;
                if (batchFiles.Any(x => string.Equals(x.FilePath, f, StringComparison.OrdinalIgnoreCase))) continue;
                batchFiles.Add(new BatchFileItem
                {
                    FilePath = f,
                    FileName = IOPath.GetFileName(f)
                });
            }

            Dispatcher.Invoke(() =>
            {
                batchListView.ItemsSource = batchFiles;
            });
        }

        /* 侧边栏：展开/收起“视频压缩”的子项（批量压缩） */
        private void ToggleCompressSub_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isCompressSubExpanded = !isCompressSubExpanded;
            var panel = this.FindName("compressSubPanel") as FrameworkElement;
            var arrow = this.FindName("txtCompressArrow") as TextBlock;
            if (panel != null)
            {
                panel.Visibility = isCompressSubExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (arrow != null)
            {
                arrow.Text = isCompressSubExpanded ? "⯅" : "⯆";
            }
            e.Handled = true;
        }

        /* 侧边栏：箭头按钮点击切换展开状态 */
        private void ToggleCompressSub_Click(object sender, RoutedEventArgs e)
        {
            isCompressSubExpanded = !isCompressSubExpanded;
            var panel = this.FindName("compressSubPanel") as FrameworkElement;
            var arrow = this.FindName("txtCompressArrow") as TextBlock;
            if (panel != null)
            {
                panel.Visibility = isCompressSubExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (arrow != null)
            {
                arrow.Text = isCompressSubExpanded ? "⯅" : "⯆";
            }
            e.Handled = true;
        }

        private void UpdateVideoAreaForBatchMode()
        {
            if (isBatchMode)
            {
                overlayGrid.Visibility = Visibility.Collapsed;
                controlBar.Visibility = Visibility.Collapsed;
                mediaPlayer.Visibility = Visibility.Collapsed;
                topBar.Visibility = Visibility.Collapsed;
                batchArea.Visibility = Visibility.Visible;
            }
            else
            {
                batchArea.Visibility = Visibility.Collapsed;
                mediaPlayer.Visibility = Visibility.Visible;
                topBar.Visibility = isFileOpened ? Visibility.Visible : Visibility.Collapsed;
                overlayGrid.Visibility = isFileOpened ? Visibility.Collapsed : Visibility.Visible;
                controlBar.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /* 视频窗口: 如果鼠标进入视频区域 */
        private void PlayerArea_MouseEnter(object sender, MouseEventArgs e)
        {
            if (isFileOpened) {
                controlBar.Visibility = Visibility.Visible;
                hideControlTimer.Stop();
            }
        }

        /* 视频窗口: 如果鼠标离开视频区域 */
        private void PlayerArea_MouseLeave(object sender, MouseEventArgs e)
        {
            if (isPlaying)
            {
                hideControlTimer.Start();
            }
        }

        /* 视频窗口: 在控制栏本身补充事件（防止操作时隐藏）*/
        private void ControlBar_MouseEnter(object sender, MouseEventArgs e)
        {
            hideControlTimer.Stop();
        }

        private void ControlBar_MouseLeave(object sender, MouseEventArgs e)
        {
            if (isPlaying)
            {
                hideControlTimer.Start();
            }
        }

        /* 视频窗口: 定时器事件（更新进度）*/
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!isDraggingSlider && mediaPlayer.Source != null)
            {
                Dispatcher.Invoke(DispatcherPriority.Render, new Action(() =>
                {
                    progressSlider.Value = mediaPlayer.Position.TotalSeconds;
                    timeText.Text = string.Format("{0:hh\\:mm\\:ss} / {1:hh\\:mm\\:ss}", mediaPlayer.Position, mediaPlayer.NaturalDuration);
                }));
            }
        }

        /* 视频窗口: 播放/暂停按钮 */
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isPlaying)
            {
                mediaPlayer.Pause();
                playPauseButton.Content = "\ue87c"; /* 播放图标 */
                isPlaying = false;
                /* 暂停时显示控制栏并停止隐藏计时器 */
                controlBar.Visibility = Visibility.Visible;
                hideControlTimer.Stop();
            }
            else
            {
                Trace.WriteLine("play...");
                mediaPlayer.Play();
                playPauseButton.Content = "\ue87a"; /* 暂停图标 */
                uiTimer.Start();
                isPlaying = true;
            }
        }

        /* 视频窗口: 停止按钮 */
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            playPauseButton.Content = "\ue87c";
            uiTimer.Stop();
            isPlaying = false;
            controlBar.Visibility = Visibility.Visible;
        }

        /* 视频窗口: 实现一个数学函数 */
        public static class MathHelper {
            public static double Clamp(double value, double min, double max) {
                return Math.Max(min, Math.Min(max, value));
            }
        }

        /* 视频窗口: 进度条值改变 */
        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //Trace.WriteLine("ProgressSlider_ValueChanged... isDraggingSlider = " + isDraggingSlider.ToString());
            if (isDraggingSlider && mediaPlayer.NaturalDuration.HasTimeSpan) { 
                double newPosition = MathHelper.Clamp(
                    progressSlider.Value,
                    0,
                    mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds
                );
                mediaPlayer.Position = TimeSpan.FromSeconds(newPosition);

                // 实时更新时间显示
                timeText.Text = string.Format("{0:hh\\:mm\\:ss} / {1:hh\\:mm\\:ss}", TimeSpan.FromSeconds(newPosition), mediaPlayer.NaturalDuration.TimeSpan);
            }
        }

        /* 视频窗口: 进度条拖动按下开始 */
        private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            //Trace.WriteLine("ProgressSlider_PreviewMouseDown...");
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                isDraggingSlider = true;
                uiTimer.Stop(); // 关键：暂停定时器
                mediaPlayer.Pause(); // 暂停播放避免冲突
            }
        }

        /* 视频窗口: 进度条拖动/点击结束 */
        private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            //Trace.WriteLine("ProgressSlider_PreviewMouseUp...");
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                isDraggingSlider = false;
                mediaPlayer.Play();
                uiTimer.Start();
            }
        }

        /* 视频窗口: 音量控制 */
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = volumeSlider.Value;
        }

        /* 视频窗口: 计算基准比特率 */
        private double CalculateBaseBitrate(double height)
        {
            // 根据高度计算基准比特率
            if (height <= 320) return 0.75; // 320p 或更低
            if (height <= 480) return 1.5;  // 480p
            if (height <= 720) return 3.0;  // 720p
            if (height <= 1080) return 5.0; // 1080p
            if (height <= 1440) return 10.0; // 1440p
            if (height <= 2160) return 20.0; // 2160p（4K）
            return 20.0; // 高于 4K
        }

        /* 视频窗口: 视频加载成功 */
        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            /* 显示下方控制栏 */
            hideControlTimer.Stop();
            controlBar.Visibility = Visibility.Visible;

            /* 隐藏叠加层 */
            overlayGrid.Visibility = Visibility.Collapsed;
            
            isFileOpened = true;

            // 获取视频的宽度和高度
            int videoWidth = mediaPlayer.NaturalVideoWidth;
            int videoHeight = mediaPlayer.NaturalVideoHeight;

            if ((videoHeight > 0) && (videoWidth > 0))
            {
                textBox_Size_Height.Text = textBox_Gif_Height.Text = videoHeight.ToString();
                textBox_Size_Width.Text = textBox_Gif_Width.Text = videoWidth.ToString();
            }

            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                baseBitrate = CalculateBaseBitrate(videoHeight);
                progressSlider.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                /* 导入后初始化裁剪范围与文本框 */
                cut_start_time.Text = "00:00:00";
                cut_end_time.Text = mediaPlayer.NaturalDuration.TimeSpan.ToString(@"hh\:mm\:ss");
                startPositionSlider.Value = 0;
                endPositionSlider.Value = 100;
                /* 同步 GIF 文本框默认值（便于其它功能） */
                if (txtStartTime != null) txtStartTime.Text = "00:00:00";
                if (txtEndTime != null) txtEndTime.Text = cut_end_time.Text;
                uiTimer.Start();
            }
        }

        /* 视频窗口: 视频加载失败 */
        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // 捕获并识别常见的 HRESULT 错误码
            int hr = (e.ErrorException == null ? 0 : e.ErrorException.HResult);
            string reason = (e.ErrorException == null ? "未知错误" : e.ErrorException.Message);

            if (hr == unchecked((int)0xC00D109B))
            {
                MessageBox.Show(
                    "该视频无法在预览窗口播放（可能码率/分辨率过高或解码器不支持，例如8K）。\n但仍可使用FFmpeg进行压缩或转换。",
                    "预览不支持",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(string.Format("视频预览失败，但仍可使用FFmpeg进行处理。\n原因: {0}", reason), "预览失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 恢复叠加层显示（预览不可用），但不清空路径，以便压缩/转换等功能继续工作
            overlayGrid.Visibility = Visibility.Visible;
            // 在叠加层上提示用户仍可处理
            var tip = this.FindName("txtInstruction") as TextBlock;
            if (tip != null)
            {
                tip.Text = "预览不可用，但仍可进行压缩/转换";
            }
        }

        /* 视频窗口: 视频播放结束 */
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            isPlaying = false;
            playPauseButton.Content = "";
            uiTimer.Stop();
        }

        /* 侧边栏选择事件 */
        private void RadioBtn_Compress_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            string tag = button.Tag.ToString();
            string panelName = tag + "Panel";
            sToolChoose = tag;

            if ("compress_batch" == tag)
            {
                isBatchMode = true;
                panelName = "compressPanel"; // 使用压缩面板配置
                sToolChoose = "compress";    // 复用压缩逻辑
            }
            else
            {
                isBatchMode = false;
            }

            if ("setting" != sToolChoose && "merge" != sToolChoose)
            {
                videoPlayerFrom.Visibility = Visibility.Visible;
            }
            else
            {
                videoPlayerFrom.Visibility = Visibility.Collapsed;
            }

            /* 使用反射找到对应面板 */
            var panel = this.FindName(panelName) as FrameworkElement;
            if (panel != null)
            {
                /* 隐藏所有面板 */
                foreach (var child in (panel.Parent as Grid).Children)
                {
                    FrameworkElement element = child as FrameworkElement; if (element != null && element != panel)
                    {
                        element.Visibility = Visibility.Collapsed;
                    }
                }

                /* 显示当前面板 */
                panel.Visibility = Visibility.Visible;
            }

            if ("merge" == tag)
            {
                overlayGrid.Visibility = Visibility.Collapsed;
                controlBar.Visibility = Visibility.Collapsed;
                mediaPlayer.Visibility = Visibility.Collapsed;
                topBar.Visibility = Visibility.Collapsed;
                batchArea.Visibility = Visibility.Collapsed;
                listBoxMergeFiles.ItemsSource = mergeFiles;
            }

            UpdateVideoAreaForBatchMode();
        }

        /* ffmpeg: 判断ffmpeg文件是否存在 */
        private bool isFFmpegExist()
        {

            if (string.IsNullOrEmpty(sFFmpegFilePath))
            {
                return false;
            }

            return File.Exists(sFFmpegFilePath);
        }

        /* ffmpeg: 调用ffmpeg执行命令 */
        public Int32 ffmpegProcess(CancellationToken cancellationToken, string sCmd)
        {

            if (!isFFmpegExist())
            {
                return -1;
            }

            var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sFFmpegFilePath,
                    Arguments = sCmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            try
            {
                // 获取视频总时长
                var durationProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = sFFmpegFilePath,
                        Arguments = string.Format("-i \"{0}\"", sVideoFilePath),
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                durationProcess.Start();
                string output = durationProcess.StandardError.ReadToEnd();
                durationProcess.WaitForExit();

                // 解析视频时长
                var durationMatch = System.Text.RegularExpressions.Regex.Match(output, @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                double totalSeconds = 0;
                if (durationMatch.Success)
                {
                    totalSeconds = TimeSpan.Parse(string.Format("{0}:{1}:{2}.{3}", durationMatch.Groups[1], durationMatch.Groups[2], durationMatch.Groups[3], durationMatch.Groups[4])).TotalSeconds;
                }

                // 检查是否有时间截取参数
                var ssMatch = System.Text.RegularExpressions.Regex.Match(sCmd, @"-ss (\d{2}:\d{2}:\d{2})");
                var tMatch = System.Text.RegularExpressions.Regex.Match(sCmd, @"-t (\d+)");

                if (ssMatch.Success && tMatch.Success)
                {
                    var startTime = TimeSpan.Parse(ssMatch.Groups[1].Value);
                    var duration = int.Parse(tMatch.Groups[1].Value);
                    totalSeconds = duration; // 使用截取的时长作为总时长
                }

                // 解析总帧数（用于GIF转换）
                var frameMatch = System.Text.RegularExpressions.Regex.Match(output, @"Stream.*Video:.*,\s*(\d+)\s*fps");
                double totalFrames = 0;
                if (frameMatch.Success)
                {
                    double fps = double.Parse(frameMatch.Groups[1].Value);
                    totalFrames = fps * totalSeconds;
                }

                ffmpeg.Start();
                // 设置进程优先级
                ffmpeg.PriorityClass = ProcessPriorityClass.AboveNormal;

                // 注册取消事件
                cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!ffmpeg.HasExited)
                        {
                            ffmpeg.Kill();
                        }
                    }
                    catch { }
                });

                // 异步读取错误输出
                ffmpeg.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        bool isGifConversion = sCmd.Contains("palettegen") || sCmd.Contains("paletteuse");

                        if (isGifConversion)
                        {
                            var _frameMatch = System.Text.RegularExpressions.Regex.Match(args.Data, @"frame=\s*(\d+)");
                            if (_frameMatch.Success && totalFrames > 0)
                            {
                                int currentFrame = int.Parse(_frameMatch.Groups[1].Value);
                                double progress = (currentFrame / totalFrames) * 100;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    progressBarOverlayRunning.Value = Math.Min(progress, 100);
                                });
                            }
                        }
                        else
                        {
                            var timeMatch = System.Text.RegularExpressions.Regex.Match(args.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                            if (timeMatch.Success && totalSeconds > 0)
                            {
                                var currentTime = TimeSpan.Parse(string.Format("{0}:{1}:{2}.{3}", timeMatch.Groups[1], timeMatch.Groups[2], timeMatch.Groups[3], timeMatch.Groups[4]));
                                double progress = (currentTime.TotalSeconds / totalSeconds) * 100;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    progressBarOverlayRunning.Value = Math.Min(progress, 100);
                                });
                            }
                        }

                        // 输出处理速度信息
                        var speedMatch = System.Text.RegularExpressions.Regex.Match(args.Data, @"speed=\s*(\d+\.?\d*)x");
                        if (speedMatch.Success)
                        {
                            string speed = speedMatch.Groups[1].Value;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                textBoxOverlayRunning.Text = string.Format("处理中，速度 {0}x···", speed);
                            });
                        }
                        else
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (isGifConversion && (true == checkBox_Gif_EnablePalettegen.IsChecked))
                                {
                                    textBoxOverlayRunning.Text = "启用着色器, 耗时较久请耐心等待！\n正在准备开始处理任务···";
                                }
                                else
                                {
                                    textBoxOverlayRunning.Text = "正在准备开始处理任务···";
                                }
                            });
                        }
                    }
                };

                ffmpeg.BeginErrorReadLine();
                ffmpeg.WaitForExit();
            }
            finally
            {
                ffmpeg.Dispose();
            }

            return 0;
        }

        /* ffmpeg: 根据不同事件组合不同的ffmpeg命令行参数 */
        private string makeFfmpegCmd(out string sOutDir, out string sOutFilePath)
        {
            string sCmd = "", suffix = "", extension = "", newFileName = "", destinationPath = "";
            string formattedTime = DateTime.Now.ToString("yyMMddHHmmss");
            string fileName = IOPath.GetFileNameWithoutExtension(sVideoFilePath);
            string targetFolder = textBoxVideoDir.Text;
            /* 目录不在则新建 */
            Directory.CreateDirectory(targetFolder);

            parseCompressVideoCode();
            parseConvertVideoCode();

            /* 解码端不强制硬件加速，避免部分视频或驱动不兼容导致失败。
             * 保留（下方）编码端的 GPU 选择以获取性能。*/

            if (true == checkBox_Setting_CpuMax.IsChecked)
            {
                sCmd += " -threads 0 ";
            }
            else
            {
                sCmd += " -threads ";
                sCmd += (maxThreads / 2).ToString();
                sCmd += " ";
            }

            sCmd += "-i ";
            sCmd += string.Format("\"{0}\"", sVideoFilePath);

            switch (sToolChoose)
            {
                case "compress":
                    suffix = "_compress_" + formattedTime;
                    extension = IOPath.GetExtension(sVideoFilePath);
                    break;
                case "convert":
                    suffix = "_format_" + formattedTime;
                    extension = sVideoFormat;
                    break;
                case "gif":
                    if (sAnimatedImageFormat == ".webp")
                    {
                        suffix = "_webp_" + formattedTime;
                        extension = ".webp";
                    }
                    else
                    {
                        suffix = "_gif_" + formattedTime;
                        extension = ".gif";
                    }
                    break;
                case "cut":
                    suffix = "_cut_" + formattedTime;
                    extension = IOPath.GetExtension(sVideoFilePath);
                    break;
                case "resize":
                    suffix = "_resize_" + textBox_Size_Width.Text.ToString() + "x" + textBox_Size_Height.Text.ToString() + "_" + formattedTime;
                    extension = IOPath.GetExtension(sVideoFilePath);
                    break;
                case "multiple":
                    suffix = "_multiple_x" + textBoxMultiple.Text.ToString() + "_" + formattedTime;
                    extension = IOPath.GetExtension(sVideoFilePath);
                    break;
                case "voice":
                    if (true == radiobtnVoiceExtract.IsChecked)
                    {
                        suffix = "_voice_" + formattedTime;
                        extension = ".aac";
                    }
                    else if (true == radiobtnVoiceDelete.IsChecked)
                    {
                        suffix = "_mute_" + formattedTime;
                        extension = IOPath.GetExtension(sVideoFilePath);
                    }
                    else if (true == radiobtnVoiceReplace.IsChecked)
                    {
                        suffix = "_replaceaudio_" + formattedTime;
                        extension = IOPath.GetExtension(sVideoFilePath);
                    }
                    break;
                default:
                    sOutDir = "";
                    sOutFilePath = "";
                    return "";
            }

            /* 组合文件名 */
            newFileName = string.Format("{0}{1}{2}", fileName, suffix, extension);
            /* 组合文件路径 */
            destinationPath = IOPath.Combine(targetFolder, newFileName);

            /* 根据是否实际使用硬件编码设置码率：硬件用固定码率，CPU用CRF */
            bool isHardwareEncoder = !string.IsNullOrEmpty(sOutVideoCode) &&
                (sOutVideoCode.Contains("_nvenc") || sOutVideoCode.Contains("_qsv") || sOutVideoCode.Contains("_amf"));
            if (true == checkBox_Setting_GpuUse.IsChecked && isHardwareEncoder)
            {
                sCompressFormat = " -b:v ";
                /* 当未能获取视频高度时（预览失败），回退一个合理的基准比特率以避免0M导致编码失败 */
                double baseForCalc = baseBitrate > 0 ? baseBitrate : 5.0; // 默认按1080p基准
                double targetBitrate = baseForCalc * Math.Pow(2, (25 - double.Parse(sCrf)) / 6.0) * 0.4;
                sCompressFormat += targetBitrate.ToString("F2");
                sCompressFormat += "M";
            }
            else
            {
                sCompressFormat = " -crf ";
                sCompressFormat += sCrf;
            }

            /* 如果是压缩任务，完善输出文件名后缀，便于比较压缩效果 */
            if (sToolChoose == "compress")
            {
                string codecLabel = "video";
                string lowerCode = sOutVideoCode.ToLower();
                if (lowerCode.Contains("h264") || lowerCode.Contains("libx264"))
                {
                    codecLabel = "h264";
                }
                else if (lowerCode.Contains("hevc") || lowerCode.Contains("libx265") || lowerCode.Contains("x265"))
                {
                    codecLabel = "h265";
                }
                else if (lowerCode.Contains("av1"))
                {
                    codecLabel = "av1";
                }
                else if (lowerCode.Contains("mpeg4"))
                {
                    codecLabel = "mpeg4";
                }

                string hwLabel = (true == checkBox_Setting_GpuUse.IsChecked) ?
                    (lowerCode.Contains("nvenc") ? "nvenc" : (lowerCode.Contains("qsv") ? "qsv" : (lowerCode.Contains("amf") ? "amf" : "gpu")))
                    : "cpu";

                string paramLabel;
                if (sCompressFormat.StartsWith(" -b:v "))
                {
                    string bv = sCompressFormat.Replace(" -b:v ", string.Empty).Trim(); // 形如 "4.50M"
                    paramLabel = string.Format("bv{0}", bv);
                }
                else
                {
                    paramLabel = string.Format("crf{0}", sCrf);
                }

                suffix = string.Format("_compress_{0}_{1}_{2}_", codecLabel, hwLabel, paramLabel) + formattedTime;
                newFileName = string.Format("{0}{1}{2}", fileName, suffix, extension);
                destinationPath = IOPath.Combine(targetFolder, newFileName);
            }

            switch (sToolChoose)
            {
                case "compress":
                    sCmd += sOutVideoCode;
                    sCmd += sCompressFormat; /* crf or bv */
                    sCmd += " -preset medium -c:a copy ";
                    break;
                case "convert":
                    sCmd += sConvertVideoCode;
                    //sCmd += " -crf 25 "; /* 转码不启用crf压缩 */
                    break;
                case "gif":

                    int timeRangeInSeconds = GetTimeRangeInSeconds();
                    if (timeRangeInSeconds > 0)
                    {
                        sCmd += " -ss ";
                        sCmd += txtStartTime.Text.ToString();
                        sCmd += " -t ";
                        sCmd += timeRangeInSeconds.ToString();
                    }
                    if (sAnimatedImageFormat == ".webp")
                    {
                        sCmd += " -vf \"fps=";
                        sCmd += textBox_Gif_Fps.Text.ToString();
                        sCmd += ",scale=";
                        sCmd += textBox_Gif_Width.Text.ToString();
                        sCmd += ":-1:flags=lanczos\" -an ";
                        sCmd += " -vcodec libwebp -lossless 0 -compression_level 6 -q:v 65 -loop ";
                        sCmd += (true == checkBox_Gif_Loop.IsChecked) ? "0" : "1";
                    }
                    else
                    {
                        sCmd += " -vf \"fps=";
                        sCmd += textBox_Gif_Fps.Text.ToString();
                        sCmd += ",scale=";
                        sCmd += textBox_Gif_Width.Text.ToString();
                        if (true == checkBox_Gif_EnablePalettegen.IsChecked)
                        {
                            sCmd += ":-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=none\" -an -loop ";
                        }
                        else
                        {
                            sCmd += ":-1:flags=lanczos\" -an -loop ";
                        }
                        sCmd += (true == checkBox_Gif_Loop.IsChecked) ? "0" : "1";
                    }
                    break;
                case "cut":
                    int cutRangeSeconds = GetCutTimeRangeInSeconds();
                    if (cutRangeSeconds <= 0)
                    {
                        sOutDir = "";
                        sOutFilePath = "";
                        return ""; /* 无效区间 */
                    }
                    /* 反选模式不走这里，由单独方法处理 */
                    if (chkReverse.IsChecked == true)
                    {
                        sOutDir = "";
                        sOutFilePath = "";
                        return ""; /* 反选模式单独处理 */
                    }
                    sCmd += " -ss ";
                    sCmd += cut_start_time.Text.ToString();
                    sCmd += " -t ";
                    sCmd += cutRangeSeconds.ToString();
                    sCmd += " -c copy ";
                    break;
                case "resize":
                    sCmd += " -vf \"scale=";
                    sCmd += textBox_Size_Width.Text.ToString();
                    sCmd += ":";
                    sCmd += textBox_Size_Height.Text.ToString();
                    sCmd += "\" ";
                    break;
                case "multiple":
                    double value; if (double.TryParse(textBoxMultiple.Text, out value) && value > 0)
                    {
                        double ptsFactor = 1.0 / value;
                        // 视频加速编码参数：强制关键帧、规范像素格式、medium预设，避免花屏
                        string videoEncodeParams = " -c:v libx264 -preset medium -g 30 -keyint_min 30 -force_key_frames \"expr:gte(t,n_forced*2)\" -pix_fmt yuv420p ";
                        string timestampFix = " -avoid_negative_ts make_zero ";
                        if (true == checkBox_Multiple_AccelerateAudio.IsChecked)
                        {
                            // 构建 atempo 链，确保每段在 [0.5, 2.0] 范围内
                            var atempos = new System.Collections.Generic.List<double>();
                            double remaining = value;
                            const double EPS = 1e-6;
                            if (remaining >= 1.0)
                            {
                                while (remaining > 2.0 + EPS)
                                {
                                    atempos.Add(2.0);
                                    remaining /= 2.0;
                                }
                                atempos.Add(remaining);
                            }
                            else
                            {
                                while (remaining < 0.5 - EPS)
                                {
                                    atempos.Add(0.5);
                                    remaining /= 0.5;
                                }
                                atempos.Add(remaining);
                            }
                            string audioChain = string.Join(",", atempos.Select(v => string.Format("atempo={0:0.###}", v)));
                            // 使用 setpts 滤镜重新计算时间戳，避免花屏
                            sCmd += string.Format(" -filter_complex \"[0:v]setpts={0:0.##}*PTS[v];[0:a]{1}[a]\" -map \"[v]\" -map \"[a]\" ", ptsFactor, audioChain);
                            sCmd += videoEncodeParams + " -c:a aac ";
                            sCmd += sCompressFormat + timestampFix; /* crf 或 bv */
                        }
                        else
                        {
                            // 仅加速视频，关闭音频
                            sCmd += string.Format(" -vf \"setpts={0:0.##}*PTS\" -an ", ptsFactor);
                            sCmd += videoEncodeParams;
                            sCmd += sCompressFormat + timestampFix; /* crf 或 bv */
                        }
                    }
                    else
                    {
                        // 非法或缺省倍速值时的兜底（原速输出）
                        string videoEncodeParams = " -c:v libx264 -preset medium -g 30 -keyint_min 30 -force_key_frames \"expr:gte(t,n_forced*2)\" -pix_fmt yuv420p ";
                        string timestampFix = " -avoid_negative_ts make_zero ";
                        if (true == checkBox_Multiple_AccelerateAudio.IsChecked)
                        {
                            sCmd += " -filter_complex \"[0:v]setpts=PTS[v];[0:a]atempo=1.0[a]\" -map \"[v]\" -map \"[a]\" ";
                            sCmd += videoEncodeParams + " -c:a aac ";
                            sCmd += sCompressFormat + timestampFix; /* crf 或 bv */
                        }
                        else
                        {
                            sCmd += " -vf \"setpts=PTS\" -an ";
                            sCmd += videoEncodeParams;
                            sCmd += sCompressFormat + timestampFix; /* crf 或 bv */
                        }
                    }
                    break;
                case "voice":
                    if (true == radiobtnVoiceExtract.IsChecked)
                    {
                        sCmd += " -vn -acodec copy ";
                    }
                    else if (true == radiobtnVoiceDelete.IsChecked)
                    {
                        sCmd += " -an -c:v copy ";
                    }
                    else if (true == radiobtnVoiceReplace.IsChecked)
                    {
                        // 校验外部音频路径
                        string audioPath = (textBoxVoiceReplacePath.Text == null ? "" : textBoxVoiceReplacePath.Text.Trim());
                        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
                        {
                            MessageBox.Show("请选择有效的音频文件以进行替换！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            sOutDir = ""; sOutFilePath = ""; return "";
                        }
                        // 添加第二路输入（音频）
                        sCmd += " -i \"" + audioPath + "\"";
                        // 选择合适的音频编码（与容器兼容）
                        string extLower = extension.ToLower();
                        string audioCodec = " -c:a aac ";
                        if (extLower == ".webm")
                        {
                            audioCodec = " -c:a libopus ";
                        }
                        else if (extLower == ".ogv")
                        {
                            audioCodec = " -c:a libvorbis ";
                        }
                        else if (extLower == ".avi" || extLower == ".mpg" || extLower == ".mpeg")
                        {
                            audioCodec = " -c:a libmp3lame ";
                        }
                        else
                        {
                            audioCodec = " -c:a aac ";
                        }

                        // 构建：视频原样拷贝，音频替换；使用 apad 补足音频长度，-shortest 保持视频时长并在音频更长时裁切
                        sCmd += " -filter_complex \"[1:a]apad[a]\" -map 0:v:0 -map \"[a]\" -shortest ";
                        sCmd += " -c:v copy ";
                        sCmd += audioCodec;
                    }
                    break;
            }

            if (true == checkBox_Setting_CpuMax.IsChecked)
            {
                sCmd += " -threads 0 ";
            }
            else
            {
                sCmd += " -threads ";
                sCmd += (maxThreads / 2).ToString();
                sCmd += " ";
            }

            /* 输出文件路径（需加引号以支持含空格路径） */
            sCmd += string.Format(" \"{0}\"", destinationPath);

            sOutDir = targetFolder;
            sOutFilePath = destinationPath;

            return sCmd;
        }

        /* 压缩: 压缩中进度条事件 */
        private void CompressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInited)
            {
                sCrf = Math.Round(sliderCompressCrf.Value).ToString();
                labelCompressCrf.Content = sCrf;
            }
        }

        private void parseCompressVideoCode() {
            string sCodeGpuSelect = "qsv";

            switch (comboBox_Setting_GpuSelect.SelectedIndex)
            {
                case 0:
                    sCodeGpuSelect = "qsv";
                    break;
                case 1:
                    sCodeGpuSelect = "nvenc";
                    break;
                case 2:
                    sCodeGpuSelect = "amf";
                    break;
                default:
                    break;
            }

            if (true == checkBox_Setting_GpuUse.IsChecked)
            {
                switch (videoCodeChoose)
                {
                    case "radiobtn_compress_264":
                        sOutVideoCode = string.Format(" -c:v h264_{0}", sCodeGpuSelect);
                        break;
                    case "radiobtn_compress_265":
                        sOutVideoCode = string.Format(" -c:v hevc_{0}", sCodeGpuSelect);
                        break;
                    case "radiobtn_compress_av1":
                        sOutVideoCode = string.Format(" -c:v av1_{0}", sCodeGpuSelect);
                        break;
                    default:
                        sOutVideoCode = string.Format(" -c:v h264_{0}", sCodeGpuSelect);
                        break;
                }

                /* 兼容性检查：如果所选硬件编码器不可用或设备不支持，则安全回退 */
                string desiredEncoder = sOutVideoCode.Replace("-c:v", string.Empty).Trim();
                if (!FfmpegHasEncoder(desiredEncoder))
                {
                    // NVENC 的 AV1 在多数旧卡（如 GTX1650）不支持，优先回退到 H.264 NVENC
                    if (sCodeGpuSelect == "nvenc" && videoCodeChoose == "radiobtn_compress_av1")
                    {
                        sOutVideoCode = " -c:v h264_nvenc";
                        // 若 h264_nvenc 也不可用，则彻底回退到 CPU 编码
                        if (!FfmpegHasEncoder("h264_nvenc"))
                        {
                            sOutVideoCode = " -c:v libx264";
                        }
                    }
                    else
                    {
                        // 其它硬件编码器不可用时，回退到对应的 CPU 编码
                        switch (videoCodeChoose)
                        {
                            case "radiobtn_compress_264":
                                sOutVideoCode = " -c:v libx264";
                                break;
                            case "radiobtn_compress_265":
                                sOutVideoCode = " -c:v libx265";
                                break;
                            case "radiobtn_compress_av1":
                                // CPU 下暂不启用 libaom-av1，保持兼容与速度；使用 mpeg4 兜底
                                sOutVideoCode = " -c:v mpeg4";
                                break;
                            default:
                                sOutVideoCode = " -c:v libx264";
                                break;
                        }
                    }
                }
            }
            else
            {
                switch (videoCodeChoose)
                {
                    case "radiobtn_compress_264":
                        sOutVideoCode = " -c:v libx264";
                        break;
                    case "radiobtn_compress_265":
                        sOutVideoCode = " -c:v libx265";
                        break;
                    case "radiobtn_compress_av1":
                        sOutVideoCode = " -c:v mpeg4";
                        break;
                    default:
                        sOutVideoCode = " -c:v libx264";
                        break;
                }
            }
        }

        /* 压缩: 压缩中选择不同选项 */
        private void RadioBtn_CompressChoose_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            videoCodeChoose = button.Tag.ToString();

            parseCompressVideoCode();
        }

        /* 音频: 选择替换音频文件 */
        private void BtnVoiceChooseAudio_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Title = "选择音频文件";
            dlg.Filter = "音频文件|*.mp3;*.aac;*.wav;*.flac;*.ogg;*.opus;*.m4a;*.wma|所有文件|*.*";
            dlg.CheckFileExists = true;
            dlg.Multiselect = false;
            if (dlg.ShowDialog() == true)
            {
                textBoxVoiceReplacePath.Text = dlg.FileName;
            }
        }

        /* 音频：三个功能互斥，且根据选择显示/隐藏替换音频的文件选择控件 */
        private void RadioBtn_VoiceAction_Click(object sender, RoutedEventArgs e)
        {
            bool isReplace = radiobtnVoiceReplace.IsChecked == true;
            textBoxVoiceReplacePath.Visibility = isReplace ? Visibility.Visible : Visibility.Collapsed;
            btnVoiceChooseAudio.Visibility = isReplace ? Visibility.Visible : Visibility.Collapsed;
        }

        private void parseConvertVideoCode() {
            string sCodeGpuSelect = "qsv";

            switch (comboBox_Setting_GpuSelect.SelectedIndex)
            {
                case 0:
                    sCodeGpuSelect = "qsv";
                    break;
                case 1:
                    sCodeGpuSelect = "nvenc";
                    break;
                case 2:
                    sCodeGpuSelect = "amf";
                    break;
                default:
                    break;
            }

            if (true == checkBox_Setting_GpuUse.IsChecked)
            {
                switch (sVideoFormat)
                {
                    case ".mp4":
                        sConvertVideoCode = string.Format(" -c:v h264_{0} -c:a aac ", sCodeGpuSelect);
                        break;
                    case ".avi":
                        /* AVI容器更常见的是MPEG-4 + MP3，避免使用可能不可用的AV1硬件编码器 */
                        sConvertVideoCode = " -c:v mpeg4 -c:a libmp3lame ";
                        break;
                    case ".mkv":
                        sConvertVideoCode = string.Format(" -c:v h264_{0} -c:a copy ", sCodeGpuSelect);
                        break;
                    case ".mov":
                        sConvertVideoCode = " -c:v libx264 -preset medium -c:a aac ";
                        break;
                    case ".webm":
                        sConvertVideoCode = " -c:v libvpx-vp9 -c:a libopus ";
                        break;
                    default:
                        sConvertVideoCode = string.Format(" -c:v h264_{0} -c:a aac ", sCodeGpuSelect);
                        break;
                }

                // 如果选择了 NVENC/QSV/AMF，但 ffmpeg 缺少相应编码器，安全回退到 CPU
                string encoderToken = sConvertVideoCode.ToLower().Contains("h264_") ? string.Format("h264_{0}", sCodeGpuSelect) :
                                      (sConvertVideoCode.ToLower().Contains("hevc_") ? string.Format("hevc_{0}", sCodeGpuSelect) :
                                       (sConvertVideoCode.ToLower().Contains("av1_") ? string.Format("av1_{0}", sCodeGpuSelect) : ""));
                if (!string.IsNullOrEmpty(encoderToken) && !FfmpegHasEncoder(encoderToken))
                {
                    // 回退为 libx264 + 合适音频编码
                    sConvertVideoCode = " -c:v libx264 -c:a aac ";
                }
            }
            else
            {
                switch (sVideoFormat)
                {
                    case ".mp4":
                        sConvertVideoCode = " -c:v libx264 -c:a aac ";
                        break;
                    case ".avi":
                        sConvertVideoCode = " -c:v mpeg4 -c:a libmp3lame ";
                        break;
                    case ".mkv":
                        sConvertVideoCode = " -c:v libx264 -c:a copy ";
                        break;
                    case ".mov":
                        sConvertVideoCode = " -c:v libx264 -preset medium -c:a aac ";
                        break;
                    case ".webm":
                        sConvertVideoCode = " -c:v libvpx-vp9 -c:a libopus ";
                        break;
                    default:
                        sConvertVideoCode = " -c:v libx264 -c:a aac ";
                        break;
                }
            }
        }

        /* 检测 ffmpeg 是否包含指定编码器（如 h264_nvenc、hevc_qsv 等） */
        private bool FfmpegHasEncoder(string encoderName)
        {
            try
            {
                if (string.IsNullOrEmpty(sFFmpegFilePath) || !File.Exists(sFFmpegFilePath))
                {
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sFFmpegFilePath,
                    Arguments = " -hide_banner -v 0 -encoders",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    // 避免阻塞过久
                    p.WaitForExit(3000);
                    return !string.IsNullOrEmpty(output) && output.IndexOf(encoderName, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /* 裁剪：裁剪按钮事件 */
        private void BtnCut_Click(object sender, RoutedEventArgs e)
        {
            /* 统一走全局处理流程，但明确设置工具类型为 cut */
            sToolChoose = "cut";

            if (chkReverse.IsChecked == true)
            {
                BtnCutReverse_Click(sender, e);
                return;
            }

            BtnProcess_Click(sender, e);
        }

        /* 裁剪：反选模式（删除选中片段，保留前后） */
        private async void BtnCutReverse_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(sVideoFilePath))
            {
                MessageBox.Show("请先导入视频！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                MessageBox.Show("无法获取视频时长，请尝试重新导入视频。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TimeSpan startTime, endTime;
            if (!TimeSpan.TryParse(cut_start_time.Text, out startTime) ||
                !TimeSpan.TryParse(cut_end_time.Text, out endTime))
            {
                MessageBox.Show("时间格式无效！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var totalDuration = mediaPlayer.NaturalDuration.TimeSpan;

            if (startTime <= TimeSpan.Zero && endTime >= totalDuration)
            {
                MessageBox.Show("反选范围不能覆盖整个视频！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string formattedTime = DateTime.Now.ToString("yyMMddHHmmss");
            string fileName = IOPath.GetFileNameWithoutExtension(sVideoFilePath);
            string extension = IOPath.GetExtension(sVideoFilePath);
            string targetFolder = textBoxVideoDir.Text;
            Directory.CreateDirectory(targetFolder);

            string newFileName = string.Format("{0}_cut_reverse_{1}{2}", fileName, formattedTime, extension);
            string destinationPath = IOPath.Combine(targetFolder, newFileName);
            string tempFolder = IOPath.Combine(targetFolder, string.Format("temp_{0}", formattedTime));
            Directory.CreateDirectory(tempFolder);

            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                ProcessingOverlay.Visibility = Visibility.Visible;
                progressBarOverlayRunning.Value = 1;
                textBoxOverlayRunning.Text = "反选裁剪中，请稍等···";
                _cancellationTokenSource = new CancellationTokenSource();

                await Task.Run(() =>
                {
                    var token = _cancellationTokenSource.Token;
                    var tempFiles = new List<string>();

                    try
                    {
                        int segmentIndex = 0;

                        // 第一段：从开头到 startTime
                        if (startTime > TimeSpan.Zero)
                        {
                            string segPath = IOPath.Combine(tempFolder, string.Format("seg_{0}.ts", segmentIndex));
                            tempFiles.Add(segPath);
                            string cmd1 = string.Format("-i \"{0}\" -ss 00:00:00 -t {1:F2} -c copy -bsf:v h264_mp4toannexb -f mpegts \"{2}\"", sVideoFilePath, startTime.TotalSeconds, segPath);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                textBoxOverlayRunning.Text = string.Format("正在提取第一段（0 ~ {0:hh\\:mm\\:ss}）···", startTime);
                            });

                            RunFFmpegSimple(cmd1, token);
                            if (token.IsCancellationRequested) return;
                            segmentIndex++;
                        }

                        // 第二段：从 endTime 到结尾
                        if (endTime < totalDuration)
                        {
                            string segPath = IOPath.Combine(tempFolder, string.Format("seg_{0}.ts", segmentIndex));
                            tempFiles.Add(segPath);
                            double duration2 = (totalDuration - endTime).TotalSeconds;
                            string cmd2 = string.Format("-i \"{0}\" -ss {1:hh\\:mm\\:ss} -t {2:F2} -c copy -bsf:v h264_mp4toannexb -f mpegts \"{3}\"", sVideoFilePath, endTime, duration2, segPath);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                textBoxOverlayRunning.Text = string.Format("正在提取第二段（{0:hh\\:mm\\:ss} ~ 结尾）···", endTime);
                            });

                            RunFFmpegSimple(cmd2, token);
                            if (token.IsCancellationRequested) return;
                            segmentIndex++;
                        }

                        if (tempFiles.Count == 0)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("没有可合并的片段！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                            return;
                        }

                        if (tempFiles.Count == 1)
                        {
                            // 只有一段，直接复制
                            File.Copy(tempFiles[0], destinationPath, true);
                        }
                        else
                        {
                            // 使用 concat 合并
                            string concatInput = string.Join("|", tempFiles.Select(f => f.Replace("\\", "/")));
                            string cmdMerge = string.Format("-i \"concat:{0}\" -c copy -bsf:a aac_adtstoasc \"{1}\"", concatInput, destinationPath);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                textBoxOverlayRunning.Text = "正在合并片段···";
                            });

                            RunFFmpegSimple(cmdMerge, token);
                        }

                        if (!token.IsCancellationRequested)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (File.Exists(destinationPath))
                                {
                                    MessageBox.Show("反选裁剪完成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "explorer.exe",
                                        Arguments = targetFolder,
                                        UseShellExecute = false
                                    });
                                }
                            });
                        }
                    }
                    finally
                    {
                        // 清理临时文件
                        try
                        {
                            if (Directory.Exists(tempFolder))
                            {
                                Directory.Delete(tempFolder, true);
                            }
                        }
                        catch { }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("处理出错：{0}", ex.Message));
            }
            finally
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        /* 辅助：执行简单 FFmpeg 命令（无进度条） */
        private void RunFFmpegSimple(string arguments, CancellationToken token)
        {
            if (!isFFmpegExist()) return;

            var psi = new ProcessStartInfo
            {
                FileName = sFFmpegFilePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var proc = new Process { StartInfo = psi })
            {
                token.Register(() =>
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                });

                proc.Start();
                
                // 异步读取输出，避免缓冲区满导致死锁
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();
                
                proc.WaitForExit();
                
                // 等待读取完成
                Task.WaitAll(outputTask, errorTask);
            }
        }

        /* 裁剪：开始时间输入框事件 */
        private void StartPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInited && mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                double totalSeconds = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                double currentSeconds = (startPositionSlider.Value / 100.0) * totalSeconds;
                TimeSpan currentPosition = TimeSpan.FromSeconds(currentSeconds);

                // 确保开始时间不会超过结束时间
                if (currentPosition.TotalSeconds < TimeSpan.Parse(cut_end_time.Text).TotalSeconds)
                {
                    cut_start_time.Text = currentPosition.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    startPositionSlider.Value = e.OldValue;
                }
            }
        }

        /* 裁剪：结束时间输入框事件 */
        private void EndPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInited && mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                double totalSeconds = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                double currentSeconds = (endPositionSlider.Value / 100.0) * totalSeconds;
                TimeSpan currentPosition = TimeSpan.FromSeconds(currentSeconds);

                // 确保结束时间不会小于开始时间
                if (currentPosition.TotalSeconds > TimeSpan.Parse(cut_start_time.Text).TotalSeconds)
                {
                    cut_end_time.Text = currentPosition.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    endPositionSlider.Value = e.OldValue;
                }
            }
        }

        /* 转换: 处理目标视频格式 */
        private void RadioBtn_ConvertChoose_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            string videoFormatChoose = button.Tag.ToString();

            if ("mp4" == videoFormatChoose)
            {
                sVideoFormat = ".mp4";
            }
            else if ("avi" == videoFormatChoose)
            {
                sVideoFormat = ".avi";
            }
            else if ("mkv" == videoFormatChoose)
            {
                sVideoFormat = ".mkv";
            }
            else
            {
                sVideoFormat = ".mp4";
            }

            parseConvertVideoCode();
        }

        /* 动图：格式选择（GIF/WebP） */
        private void RadioBtn_GifFormatChoose_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            string formatTag = (button == null || button.Tag == null ? ".gif" : button.Tag.ToString().ToLower());
            if (formatTag == ".webp")
            {
                sAnimatedImageFormat = ".webp";
            }
            else
            {
                sAnimatedImageFormat = ".gif";
            }
        }

        /* GIF: 一些判断处理 */
        private void TimeTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 只允许输入数字和冒号
            e.Handled = !IsTimeInputValid(e.Text);
        }
        private bool IsTimeInputValid(string text)
        {
            return text.All(c => char.IsDigit(c) || c == ':');
        }
        private void ValidateTimeRange()
        {
            /* GIF 文本框校验 */
            if (txtStartTime != null && txtEndTime != null)
            {
                TimeSpan startTime, endTime;
                if (TimeSpan.TryParse(txtStartTime.Text, out startTime) &&
                    TimeSpan.TryParse(txtEndTime.Text, out endTime))
                {
                    if (endTime < startTime)
                    {
                        txtEndTime.Text = txtStartTime.Text;
                    }
                }
            }

            /* 裁剪文本框校验 */
            ValidateCutTimeRange();
        }

        private void ValidateCutTimeRange()
        {
            if (!isInited) return;
            if (cut_start_time == null || cut_end_time == null) return;
            if (mediaPlayer == null) return;

            TimeSpan startTime, endTime;
            if (TimeSpan.TryParse(cut_start_time.Text, out startTime) &&
                TimeSpan.TryParse(cut_end_time.Text, out endTime))
            {
                if (mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    var duration = mediaPlayer.NaturalDuration.TimeSpan;
                    if (startTime > duration) startTime = duration;
                    if (endTime > duration) endTime = duration;
                }

                if (endTime < startTime)
                {
                    cut_end_time.Text = cut_start_time.Text;
                }
            }
        }

        /* GIF: gif起始位置处理 */
        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // 格式化时间文本
            string text = textBox.Text.Replace(":", "");
            if (text.Length > 6) text = text.Substring(0, 6);

            while (text.Length < 6) text = "0" + text;

            textBox.Text = string.Format("{0}:{1}:{2}", text.Substring(0, 2), text.Substring(2, 2), text.Substring(4, 2));
            textBox.CaretIndex = textBox.Text.Length;

            // 验证开始时间和结束时间
            ValidateTimeRange();

            // 如果是裁剪文本框，联动更新区间滑块
            if (textBox == cut_start_time || textBox == cut_end_time)
            {
                SyncCutSlidersFromTextBoxes();
            }
        }

        /* GIF: 获取起始位置和结束位置相差秒数 */
        private int GetTimeRangeInSeconds()
        {
            TimeSpan startTime, endTime;
            if (TimeSpan.TryParse(txtStartTime.Text, out startTime) &&
                TimeSpan.TryParse(txtEndTime.Text, out endTime))
            {
                return (int)(endTime - startTime).TotalSeconds;
            }
            return 0;
        }

        /* GIF: 时间+1s */
        private void TimeSpinButton_Up_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RepeatButton;
            var grid = button.Parent as StackPanel;
            var parentGrid = grid.Parent as Grid;
            var textBox = parentGrid.Children[0] as TextBox;

            // 解析当前时间
            TimeSpan currentTime;
            if (TimeSpan.TryParse(textBox.Text, out currentTime))
            {
                currentTime = currentTime.Add(TimeSpan.FromSeconds(1));

                /* 确保时间不会超过视频时长 */
                if (mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    if (currentTime > mediaPlayer.NaturalDuration.TimeSpan)
                    {
                        currentTime = mediaPlayer.NaturalDuration.TimeSpan;
                    }
                }

                // 确保时间不为负
                if (currentTime.TotalSeconds < 0)
                    currentTime = TimeSpan.Zero;

                // 如果是开始时间，确保不大于结束时间
                if (textBox == txtStartTime || textBox == cut_start_time)
                {
                    TimeSpan endTime;
                    var endText = (textBox == txtStartTime) ? txtEndTime.Text : cut_end_time.Text;
                    if (TimeSpan.TryParse(endText, out endTime) && currentTime > endTime)
                    {
                        currentTime = endTime;
                    }
                }
                // 如果是结束时间，确保不小于开始时间
                else if (textBox == txtEndTime || textBox == cut_end_time)
                {
                    TimeSpan startTime;
                    var startText = (textBox == txtEndTime) ? txtStartTime.Text : cut_start_time.Text;
                    if (TimeSpan.TryParse(startText, out startTime) && currentTime < startTime)
                    {
                        currentTime = startTime;
                    }
                }

                textBox.Text = currentTime.ToString(@"hh\:mm\:ss");

                // 裁剪文本框变更后联动滑块
                if (textBox == cut_start_time || textBox == cut_end_time)
                {
                    SyncCutSlidersFromTextBoxes();
                }
            }
        }

        /* GIF: 时间-1s */
        private void TimeSpinButton_Down_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as RepeatButton;
            var grid = button.Parent as StackPanel;
            var parentGrid = grid.Parent as Grid;
            var textBox = parentGrid.Children[0] as TextBox;

            // 解析当前时间
            TimeSpan currentTime;
            if (TimeSpan.TryParse(textBox.Text, out currentTime))
            {
                // 增加或减少一秒
                currentTime = currentTime.Subtract(TimeSpan.FromSeconds(1));

                // 确保时间不为负
                if (currentTime.TotalSeconds < 0)
                    currentTime = TimeSpan.Zero;

                textBox.Text = currentTime.ToString(@"hh\:mm\:ss");

                // 裁剪文本框变更后联动滑块
                if (textBox == cut_start_time || textBox == cut_end_time)
                {
                    SyncCutSlidersFromTextBoxes();
                }
            }
        }

        /* 裁剪: 根据文本框同步区间滑块 */
        private void SyncCutSlidersFromTextBoxes()
        {
            if (!isInited || !mediaPlayer.NaturalDuration.HasTimeSpan) return;

            TimeSpan startTime, endTime;
            if (TimeSpan.TryParse(cut_start_time.Text, out startTime) &&
                TimeSpan.TryParse(cut_end_time.Text, out endTime))
            {
                var duration = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                var startSeconds = Math.Max(0, Math.Min(duration, startTime.TotalSeconds));
                var endSeconds = Math.Max(0, Math.Min(duration, endTime.TotalSeconds));

                // 保证单调
                if (endSeconds < startSeconds)
                {
                    endSeconds = startSeconds;
                    cut_end_time.Text = TimeSpan.FromSeconds(endSeconds).ToString(@"hh\:mm\:ss");
                }

                startPositionSlider.Value = (startSeconds / duration) * 100.0;
                endPositionSlider.Value = (endSeconds / duration) * 100.0;
            }
        }

        /* 裁剪: 获取区间秒数 */
        private int GetCutTimeRangeInSeconds()
        {
            TimeSpan startTime, endTime;
            if (TimeSpan.TryParse(cut_start_time.Text, out startTime) &&
                TimeSpan.TryParse(cut_end_time.Text, out endTime))
            {
                var seconds = (int)(endTime - startTime).TotalSeconds;
                return Math.Max(0, seconds);
            }
            return 0;
        }

        /* GIF: gif处理界面的进度条事件 */
        private void GifSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInited)
            {
                sCrf = Math.Round(sliderGifFps.Value).ToString();
                textBox_Gif_Fps.Text = sCrf;
            }
        }

        /* GIF: 重设gif大小 */
        private void BtnGifResize_Click(object sender, RoutedEventArgs e)
        {
            if (!isFileOpened) {
                return;
            }

            // 获取视频的宽度和高度
            int _originalWidth = mediaPlayer.NaturalVideoWidth;
            int _originalHeight = mediaPlayer.NaturalVideoHeight;
            int targetHeight = 0;

            // 获取触发事件的按钮
            Button button = sender as Button; if (button != null)
            {
                /* 根据按钮的 Tag 属性区分 */
                var tag = (button == null || button.Tag == null ? null : button.Tag.ToString());
                switch (tag)
                {
                    case "320":
                        targetHeight = 320;
                        break;
                    case "480":
                        targetHeight = 480;
                        break;
                    case "720":
                        targetHeight = 720;
                        break;
                    case "1080":
                        targetHeight = 1080;
                        break;
                    case "Orig":
                        targetHeight = _originalHeight;
                        break;
                    default:
                        targetHeight = _originalHeight;
                        break;
                }

                /* 判断原始高度是否大于目标高度 */
                if (_originalHeight < targetHeight && tag != "Original") {
                    return;
                }

                /* 计算等比例宽度 */
                int newWidth = (int)(_originalWidth * (targetHeight / (double)_originalHeight));

                /* 更新界面 */
                textBox_Gif_Width.Text = newWidth.ToString();
                textBox_Gif_Height.Text = targetHeight.ToString();
            }
        }

        /* 加速: 视频加速界面进度条事件 */
        private void SliderMultiple_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isInited)
            {
                sCrf = sliderMultiple.Value.ToString("F1");
                textBoxMultiple.Text = sCrf;
            }
        }

        /* 尺寸: 重设视频大小 */
        private void BtnSizeResize_Click(object sender, RoutedEventArgs e)
        {
            if (!isFileOpened)
            {
                return;
            }

            // 获取视频的宽度和高度
            int _originalWidth = mediaPlayer.NaturalVideoWidth;
            int _originalHeight = mediaPlayer.NaturalVideoHeight;
            int targetHeight = 0;

            // 获取触发事件的按钮
            Button button = sender as Button; if (button != null)
            {
                /* 根据按钮的 Tag 属性区分 */
                var tag = (button == null || button.Tag == null ? null : button.Tag.ToString());
                switch (tag)
                {
                    case "320":
                        targetHeight = 320;
                        break;
                    case "480":
                        targetHeight = 480;
                        break;
                    case "720":
                        targetHeight = 720;
                        break;
                    case "1080":
                        targetHeight = 1080;
                        break;
                    case "Orig":
                        targetHeight = _originalHeight;
                        break;
                    default:
                        targetHeight = _originalHeight;
                        break;
                }

                /* 判断原始高度是否大于目标高度 */
                if (_originalHeight < targetHeight && tag != "Original")
                {
                    return;
                }

                /* 计算等比例宽度 */
                int newWidth = (int)(_originalWidth * (targetHeight / (double)_originalHeight));

                /* 更新界面 */
                textBox_Size_Width.Text = newWidth.ToString();
                textBox_Size_Height.Text = targetHeight.ToString();
            }
        }

        /* 尺寸: 重设视频大小处理函数 */
        private async void BtnSizeConvert_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(sVideoFilePath))
            {
                MessageBox.Show("请先导入视频！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sCmd = "";
            string formattedTime = DateTime.Now.ToString("yyMMddHHmmss");
            string suffix = "_resize_" + textBox_Size_Width.Text.ToString() + "x" + textBox_Size_Height.Text.ToString() + "_" + formattedTime;
            string fileName = IOPath.GetFileNameWithoutExtension(sVideoFilePath);
            string extension = IOPath.GetExtension(sVideoFilePath); ;
            string newFileName = string.Format("{0}{1}{2}", fileName, suffix, extension);
            string targetFolder = textBoxVideoDir.Text;

            Directory.CreateDirectory(targetFolder);

            string destinationPath = IOPath.Combine(targetFolder, newFileName);
            int timeRangeInSeconds = GetTimeRangeInSeconds();

            sCmd += "-i ";
            sCmd += sVideoFilePath;
            sCmd += " -vf \"scale=";
            sCmd += textBox_Size_Width.Text.ToString();
            sCmd += ":";
            sCmd += textBox_Size_Height.Text.ToString();
            sCmd += "\" ";
            sCmd += destinationPath;

            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                /* 显示处理遮罩 */
                ProcessingOverlay.Visibility = Visibility.Visible;
                progressBarOverlayRunning.Value = 1;
                _cancellationTokenSource = new CancellationTokenSource();

                /* 异步执行任务 */
                await Task.Run(() =>
                {
                    //MessageBox.Show(sCmd, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    ffmpegProcess(_cancellationTokenSource.Token, sCmd);

                    /* 只有在没有取消的情况下才显示完成消息 */
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        if (File.Exists(destinationPath))
                        {
                            /* 启动资源管理器进程 */
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = targetFolder,
                                UseShellExecute = false
                            });
                        }
                    }
                    else
                    {
                        /* 如果用户取消，删除生成的文件 */
                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }
                    }
                }
                );
            }
            catch (OperationCanceledException)
            {
                /* 用户取消了操作 */
                ProcessingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("处理出错：{0}", ex.Message));
            }
            finally
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        /* 设置: 设置界面中选择ffmpeg程序路径的按钮事件 */
        private void BtnSettingChoose_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ffmpeg可执行程序|*.exe|所有文件|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                sFFmpegFilePath = openFileDialog.FileName;
                textBoxSettingFmmpegPath.Text = openFileDialog.FileName;

                //var iniConfig = new IniConfig();
                //iniConfig.Write("Program", "FFmpegPath", sFFmpegFilePath);
            }
        }

        /* 设置: 设置界面中在线下载ffmpeg程序路径的按钮事件 */
        private async void BtnSettingDownload_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                FileName = "选择保存目录",
                Title = "选择ffmpeg程序的保存路径",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string sFFmpegSaveFolder = IOPath.GetDirectoryName(openFileDialog.FileName);
                string fileUrl = "https://gitee.com/is-zhou/ffmpeg/releases/download/7.1.1/ffmpeg.exe";
                string fileName = IOPath.GetFileName(fileUrl);
                string filePath = IOPath.Combine(sFFmpegSaveFolder, fileName);

                /* 目录下存在相同文件，不下载 */
                if (File.Exists(filePath))
                {
                    sFFmpegFilePath = filePath;
                    textBoxSettingFmmpegPath.Text = filePath;

                    var iniConfig = new IniConfig();
                    iniConfig.Write("Program", "FFmpegPath", filePath);

                    MessageBox.Show("已经存在FFmpeg，跳过下载！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Dispose();
                    }
                    _cancellationTokenSource = new CancellationTokenSource();

                    ProcessingOverlay.Visibility = Visibility.Visible;
                    progressBarOverlayRunning.Value = 1;
                    textBoxOverlayRunning.Text = "下载中，请稍等···";

                    using (var httpClient = new HttpClient())
                    {
                        using (var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();

                            long? totalBytes = response.Content.Headers.ContentLength;
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            {
                                byte[] buffer = new byte[8192];
                                long totalBytesRead = 0;
                                int bytesRead;

                                while ((bytesRead = await contentStream.ReadAsync(
                                    buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    totalBytesRead += bytesRead;

                                    if (totalBytes.HasValue)
                                    {
                                        double progress = (double)totalBytesRead / totalBytes.Value * 100;
                                        await Dispatcher.InvokeAsync(() =>
                                        {
                                            progressBarOverlayRunning.Value = progress;
                                        });
                                    }
                                }
                            }
                        }
                    }

                    // 下载成功后更新配置
                    sFFmpegFilePath = filePath;
                    textBoxSettingFmmpegPath.Text = filePath;

                    var iniConfig = new IniConfig();
                    iniConfig.Write("Program", "FFmpegPath", filePath);

                    MessageBox.Show("FFmpeg下载成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (OperationCanceledException)
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("下载出错: {0}", ex.Message), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ProcessingOverlay.Visibility = Visibility.Collapsed;
                    textBoxOverlayRunning.Text = "处理中，请稍等···";
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Dispose();
                        _cancellationTokenSource = null;
                    }
                }
            }
        }

        /* 设置: 设置界面中选择视频保存目录的按钮事件 */
        private void BtnSettingChooseDir_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                // 设置为只选择目录
                Filter = "文件夹|*.none",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "选择文件夹"
            };

            // 显示对话框并检查用户是否点击了“确定”
            if (openFileDialog.ShowDialog() == true)
            {
                // 获取用户选择的目录路径
                sVideoSaveFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);

                // 更新 UI 显示选择的目录
                textBoxVideoDir.Text = sVideoSaveFolder;
            }
        }

        /* 设置: 设置界面中gpu启用事件 */
        private void CheckBoxGpu_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                if (checkBox.IsChecked == true)
                {
                    comboBox_Setting_GpuSelect.Visibility = Visibility.Visible;
                }
                else
                {
                    comboBox_Setting_GpuSelect.Visibility = Visibility.Collapsed;
                }
            }
        }

        /* 设置: 设置中保存的按钮事件 */
        private void BtnSettingSave_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(sFFmpegFilePath) && isFFmpegExist())
            {
                var iniConfig = new IniConfig();
                iniConfig.Write("Program", "FFmpegPath", sFFmpegFilePath);
                labelSettingInfo.Foreground = Brushes.Green;

                if (sVideoSaveFolder != Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
                {
                    iniConfig.Write("Program", "VideoSaveFloder", sVideoSaveFolder);
                    labelSettingInfo.Foreground = Brushes.Green;
                }

                iniConfig.Write("Program", "GpuUse", (true == checkBox_Setting_GpuUse.IsChecked) ? "enable" : "disable");
                iniConfig.Write("Program", "GpuSelect", comboBox_Setting_GpuSelect.SelectedIndex.ToString());
                iniConfig.Write("Program", "CpuMax", (true == checkBox_Setting_CpuMax.IsChecked) ? "enable" : "disable");

                labelSettingInfo.Content = "保存成功！";

                labelClearTimer.Stop(); // 停止之前的计时器(如果在运行)
                labelClearTimer.Start(); // 开始新的2秒计时
            }
            else
            {
                MessageBox.Show("请选择FFmpeg程序路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /* 全局: 所有的处理任务按钮事件 */
        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            if (!isBatchMode)
            {
                if (string.IsNullOrEmpty(sVideoFilePath))
                {
                    MessageBox.Show("请先导入视频！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                if (batchFiles.Count == 0)
                {
                    MessageBox.Show("请先添加要批量压缩的文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string targetFolder = "", destinationPath = "";
            string sCmd = "";
            if (!isBatchMode)
            {
                sCmd = makeFfmpegCmd(out targetFolder, out destinationPath);

                if (string.IsNullOrEmpty(sCmd))
                {
                    MessageBox.Show("创建任务失败！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            //MessageBox.Show(string.Format("{0} = {1}", sToolChoose, sCmd), "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            //return;

            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                /* 显示处理遮罩 */
                ProcessingOverlay.Visibility = Visibility.Visible;
                progressBarOverlayRunning.Value = 1;
                _cancellationTokenSource = new CancellationTokenSource();

                /* 异步执行任务 */
                await Task.Run(() =>
                {
                    if (!isBatchMode)
                    {
                        ffmpegProcess(_cancellationTokenSource.Token, sCmd);

                        if (!_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            if (File.Exists(destinationPath))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = targetFolder,
                                    UseShellExecute = false
                                });
                            }
                        }
                        else
                        {
                            if (File.Exists(destinationPath))
                            {
                                File.Delete(destinationPath);
                            }
                        }
                    }
                    else
                    {
                        int total = batchFiles.Count;
                        string lastFolder = "";
                        for (int i = 0; i < total; i++)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested) break;
                            var item = batchFiles[i];

                            string cmdLocal = null;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                // 在 UI 线程更新界面并生成命令，避免跨线程访问控件
                                sVideoFilePath = item.FilePath;
                                textBoxOverlayRunning.Text = string.Format("正在处理 {0}/{1}：{2}", i + 1, total, item.FileName);
                                progressBarOverlayRunning.Value = 1;

                                string tf2 = "", dp2 = "";
                                cmdLocal = makeFfmpegCmd(out tf2, out dp2);
                                lastFolder = tf2;
                            });

                            // 在后台线程执行 ffmpeg 处理
                            ffmpegProcess(_cancellationTokenSource.Token, cmdLocal);
                        }

                        if (!_cancellationTokenSource.Token.IsCancellationRequested && !string.IsNullOrEmpty(lastFolder))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = lastFolder,
                                UseShellExecute = false
                            });
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                /* 用户取消了操作 */
                ProcessingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("处理出错：{0}", ex.Message));
            }
            finally
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        /* 全局: 任务处理中遮罩上的取消正在处理的任务按钮事件 */
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                {
                    _cancellationTokenSource.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // 忽略已释放的异常
            }
        }

        /* 批量模式：列表拖拽导入事件 */
        private void BatchListView_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void BatchListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddBatchFiles(files);
            }
        }

        /* 批量模式：选择多个文件 */
        private void BtnBatchOpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm;*.flv;*.ts;*.m2ts;*.mts;*.mpg;*.mpeg;*.m4v;*.3gp;*.3g2;*.ogv;*.vob;*.asf;*.mxf;*.rmvb;*.rm;*.y4m|所有文件|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                AddBatchFiles(openFileDialog.FileNames);
            }
        }

        /* 批量模式：清空当前列表 */
        private void BtnBatchClearFiles_Click(object sender, RoutedEventArgs e)
        {
            batchFiles.Clear();
        }

        /* 批量模式：右键移除某个文件 */
        private void MenuItemRemoveFromBatch_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem; if (mi != null)
            {
                var item = mi.CommandParameter as BatchFileItem;
                if (item != null)
                {
                    batchFiles.Remove(item);
                }
            }
        }

        /* 关于: 关于按钮事件 */
        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        /* ========== 视频合并功能模块 ========== */

        private void RefreshMergeIndices()
        {
            for (int i = 0; i < mergeFiles.Count; i++)
            {
                mergeFiles[i].Index = i + 1;
            }
        }

        private void AddMergeFiles(IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                if (!IsVideoFile(f)) continue;
                if (mergeFiles.Any(x => string.Equals(x.FilePath, f, StringComparison.OrdinalIgnoreCase))) continue;
                mergeFiles.Add(new MergeFileItem
                {
                    Index = mergeFiles.Count + 1,
                    FilePath = f,
                    FileName = IOPath.GetFileName(f)
                });
            }
        }

        private void BtnMergeAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.wmv;*.mov;*.mkv;*.webm;*.flv;*.ts;*.m2ts;*.mts;*.mpg;*.mpeg;*.m4v;*.3gp;*.3g2;*.ogv;*.vob;*.asf;*.mxf;*.rmvb;*.rm;*.y4m|所有文件|*.*",
                Multiselect = true,
                Title = "选择要合并的视频文件"
            };

            if (dlg.ShowDialog() == true)
            {
                AddMergeFiles(dlg.FileNames);
            }
        }

        private void BtnMergeUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = listBoxMergeFiles.SelectedIndex;
            if (idx > 0)
            {
                var item = mergeFiles[idx];
                mergeFiles.RemoveAt(idx);
                mergeFiles.Insert(idx - 1, item);
                RefreshMergeIndices();
                listBoxMergeFiles.SelectedIndex = idx - 1;
            }
        }

        private void BtnMergeDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = listBoxMergeFiles.SelectedIndex;
            if (idx >= 0 && idx < mergeFiles.Count - 1)
            {
                var item = mergeFiles[idx];
                mergeFiles.RemoveAt(idx);
                mergeFiles.Insert(idx + 1, item);
                RefreshMergeIndices();
                listBoxMergeFiles.SelectedIndex = idx + 1;
            }
        }

        private void BtnMergeRemove_Click(object sender, RoutedEventArgs e)
        {
            int idx = listBoxMergeFiles.SelectedIndex;
            if (idx >= 0)
            {
                mergeFiles.RemoveAt(idx);
                RefreshMergeIndices();
            }
        }

        private void BtnMergeClear_Click(object sender, RoutedEventArgs e)
        {
            mergeFiles.Clear();
        }

        /* 合并模式：列表拖拽导入事件 */
        private void MergeListView_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void MergeListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddMergeFiles(files);
            }
        }

        /* 合并模式：右键菜单事件 */
        private void MenuItemMergeMoveUp_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem; if (mi != null)
            {
                var item = mi.CommandParameter as MergeFileItem;
                if (item != null)
                {
                    int idx = mergeFiles.IndexOf(item);
                    if (idx > 0)
                    {
                        mergeFiles.RemoveAt(idx);
                        mergeFiles.Insert(idx - 1, item);
                        RefreshMergeIndices();
                    }
                }
            }
        }

        private void MenuItemMergeMoveDown_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem; if (mi != null)
            {
                var item = mi.CommandParameter as MergeFileItem;
                if (item != null)
                {
                    int idx = mergeFiles.IndexOf(item);
                    if (idx >= 0 && idx < mergeFiles.Count - 1)
                    {
                        mergeFiles.RemoveAt(idx);
                        mergeFiles.Insert(idx + 1, item);
                        RefreshMergeIndices();
                    }
                }
            }
        }

        private void MenuItemMergeRemove_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem; if (mi != null)
            {
                var item = mi.CommandParameter as MergeFileItem;
                if (item != null)
                {
                    mergeFiles.Remove(item);
                    RefreshMergeIndices();
                }
            }
        }

        private void BtnMergeBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "MP4视频|*.mp4|所有文件|*.*",
                DefaultExt = ".mp4",
                Title = "选择合并后视频的保存位置"
            };

            if (dlg.ShowDialog() == true)
            {
                textBoxMergeOutput.Text = dlg.FileName;
            }
        }

        private async void BtnMergeStart_Click(object sender, RoutedEventArgs e)
        {
            if (mergeFiles.Count < 2)
            {
                MessageBox.Show("请至少添加两个视频文件进行合并！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputPath = (textBoxMergeOutput.Text == null ? "" : textBoxMergeOutput.Text.Trim());
            if (string.IsNullOrEmpty(outputPath))
            {
                MessageBox.Show("请选择输出路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!isFFmpegExist())
            {
                MessageBox.Show("FFmpeg 未配置，请在设置中配置 FFmpeg 路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetFolder = IOPath.GetDirectoryName(outputPath);
            Directory.CreateDirectory(targetFolder);

            string formattedTime = DateTime.Now.ToString("yyMMddHHmmss");
            string tempFolder = IOPath.Combine(targetFolder, string.Format("merge_temp_{0}", formattedTime));
            Directory.CreateDirectory(tempFolder);

            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                ProcessingOverlay.Visibility = Visibility.Visible;
                progressBarOverlayRunning.Value = 1;
                textBoxOverlayRunning.Text = "视频合并中，请稍等···";
                _cancellationTokenSource = new CancellationTokenSource();
                btnMergeStart.IsEnabled = false;

                await Task.Run(() =>
                {
                    var token = _cancellationTokenSource.Token;
                    var tempFiles = new List<string>();

                    try
                    {
                        // 步骤1：将所有视频转换为统一格式的 MP4 文件（使用重新编码确保编码一致性）
                        for (int i = 0; i < mergeFiles.Count; i++)
                        {
                            if (token.IsCancellationRequested) return;

                            string filePath = mergeFiles[i].FilePath;
                            string tempMp4 = IOPath.Combine(tempFolder, string.Format("part_{0:D3}.mp4", i));
                            tempFiles.Add(tempMp4);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                textBoxOverlayRunning.Text = string.Format("正在预处理 {0}/{1} ···", i + 1, mergeFiles.Count);
                            });

                            // 使用重新编码确保所有视频编码参数一致，避免合并失败
                            // 统一为H.264编码，固定帧率、关键帧间隔，确保兼容性
                            // 使用 -y 强制覆盖，避免交互式询问
                            // 使用 +faststart 优化MP4格式
                            string cmd = string.Format("-y -i \"{0}\" -c:v libx264 -preset medium -crf 18 -r 30 -g 30 -keyint_min 30 -pix_fmt yuv420p -c:a aac -b:a 128k -ar 44100 -movflags +faststart \"{1}\"", filePath, tempMp4);
                            RunFFmpegSimple(cmd, token);

                            // 检查是否成功生成临时文件
                            if (!File.Exists(tempMp4) || new FileInfo(tempMp4).Length == 0)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show(string.Format("预处理第 {0} 个视频失败，请检查视频文件是否有效。", i + 1), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                });
                                return;
                            }

                            if (token.IsCancellationRequested) return;
                        }

                        if (token.IsCancellationRequested) return;

                        // 步骤2：生成 concat filelist（使用UTF-8无BOM格式）
                        string fileListPath = IOPath.Combine(tempFolder, "filelist.txt");
                        var sb = new StringBuilder();
                        foreach (var mp4File in tempFiles)
                        {
                            // 使用正斜杠路径，避免转义问题
                            string normalizedPath = mp4File.Replace("\\", "/");
                            sb.AppendLine(string.Format("file '{0}'", normalizedPath));
                        }
                        // 使用无BOM的UTF-8编码
                        File.WriteAllText(fileListPath, sb.ToString(), new UTF8Encoding(false));

                        // 步骤3：使用 concat demuxer 合并，并重新编码确保输出兼容性
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            textBoxOverlayRunning.Text = "正在合并所有视频···";
                        });

                        // 使用 -y 强制覆盖，避免交互式询问
                        // 使用 concat demuxer 合并，并重新编码确保输出兼容性
                        // 避免使用 -c copy，改用重新编码确保输出文件格式正确
                        string mergeCmd = string.Format("-y -f concat -safe 0 -i \"{0}\" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart \"{1}\"", fileListPath, outputPath);
                        RunFFmpegSimple(mergeCmd, token);

                        if (!token.IsCancellationRequested)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                                {
                                    MessageBox.Show("视频合并完成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "explorer.exe",
                                        Arguments = targetFolder,
                                        UseShellExecute = false
                                    });
                                }
                                else
                                {
                                    MessageBox.Show("合并失败，输出文件未生成。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            });
                        }
                    }
                    finally
                    {
                        // 清理临时文件
                        try
                        {
                            if (Directory.Exists(tempFolder))
                            {
                                Directory.Delete(tempFolder, true);
                            }
                        }
                        catch { }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("合并出错：{0}", ex.Message));
            }
            finally
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                btnMergeStart.IsEnabled = true;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        /* 水印功能：Slider值变化事件 */
        private void SliderWatermarkSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (textBlockWatermarkSize != null)
            {
                textBlockWatermarkSize.Content = ((int)sliderWatermarkSize.Value).ToString();
            }
        }

        private void SliderWatermarkOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (textBlockWatermarkOpacity != null)
            {
                textBlockWatermarkOpacity.Content = sliderWatermarkOpacity.Value.ToString("F1");
            }
        }

        /* 水印功能：开始添加水印 */
        private async void BtnWatermarkStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(sVideoFilePath) || !File.Exists(sVideoFilePath))
            {
                MessageBox.Show("请先选择视频文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string watermarkText = textBoxWatermarkText.Text.Trim();
            if (string.IsNullOrEmpty(watermarkText))
            {
                MessageBox.Show("请输入水印文字！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!isFFmpegExist())
            {
                MessageBox.Show("FFmpeg 未配置，请在设置中配置 FFmpeg 路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 获取水印参数
            int fontSize = (int)sliderWatermarkSize.Value;
            double opacity = sliderWatermarkOpacity.Value;
            int positionIndex = comboBoxWatermarkPosition.SelectedIndex;
            int colorIndex = comboBoxWatermarkColor.SelectedIndex;

            // 构建输出路径 - 使用原视频所在文件夹，并添加时间戳
            string fileNameWithoutExt = IOPath.GetFileNameWithoutExtension(sVideoFilePath);
            string ext = IOPath.GetExtension(sVideoFilePath);
            string videoDir = IOPath.GetDirectoryName(sVideoFilePath);
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outputPath = IOPath.Combine(videoDir, string.Format("{0}_水印_{1}{2}", fileNameWithoutExt, timeStamp, ext));

            // 颜色映射
            string[] colors = { "white", "black", "red", "yellow", "blue" };
            string fontColor = colors[colorIndex];

            // 位置映射 (x:y)
            string position;
            switch (positionIndex)
            {
                case 0: // 左上角
                    position = string.Format("x=10:y=10");
                    break;
                case 1: // 右上角
                    position = string.Format("x=w-text_w-10:y=10");
                    break;
                case 2: // 左下角
                    position = string.Format("x=10:y=h-text_h-10");
                    break;
                case 3: // 右下角
                    position = string.Format("x=w-text_w-10:y=h-text_h-10");
                    break;
                case 4: // 居中
                    position = string.Format("x=(w-text_w)/2:y=(h-text_h)/2");
                    break;
                default:
                    position = string.Format("x=10:y=h-text_h-10");
                    break;
            }

            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                ProcessingOverlay.Visibility = Visibility.Visible;
                progressBarOverlayRunning.IsIndeterminate = true;
                textBoxOverlayRunning.Text = "正在添加水印···";
                _cancellationTokenSource = new CancellationTokenSource();

                await Task.Run(() =>
                {
                    var token = _cancellationTokenSource.Token;

                    // 构建 FFmpeg 命令
                    // 使用 drawtext 滤镜添加文字水印 - 使用系统默认中文字体
                    // 尝试使用Windows系统自带的微软雅黑字体
                    string fontPath = "C\\:/Windows/Fonts/msyh.ttc";
                    string drawtextFilter = string.Format(
                        "drawtext=fontfile='{0}':text='{1}':fontsize={2}:{3}:fontcolor={4}@{5}",
                        fontPath,
                        watermarkText.Replace("'", "\\'").Replace(":", "\\:"),
                        fontSize,
                        position,
                        fontColor,
                        opacity.ToString("F1")
                    );

                    string arguments = string.Format(
                        "-y -i \"{0}\" -vf \"{1}\" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a copy \"{2}\"",
                        sVideoFilePath,
                        drawtextFilter,
                        outputPath
                    );

                    RunFFmpegSimple(arguments, token);

                    if (!token.IsCancellationRequested)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                MessageBox.Show("水印添加完成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = "/select,\"" + outputPath + "\"",
                                    UseShellExecute = false
                                });
                            }
                            else
                            {
                                MessageBox.Show("水印添加失败，输出文件未生成。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        });
                    }
                }, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // 用户取消，删除不完整输出
                try
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("添加水印出错：{0}", ex.Message), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                progressBarOverlayRunning.IsIndeterminate = false;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }
    }
}
