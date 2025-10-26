using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoConverter
{
    public partial class Form1 : Form
    {
        private List<ConversionJob> conversionQueue = new List<ConversionJob>();
        private BindingList<ConversionJob> jobBindingList;
        private ToolTip navigationToolTip;
        private int maxConcurrentConversions = 3;
        private int activeConversions = 0;
        private string ffmpegPath = "ffmpeg.exe";
        private bool ffmpegAvailable;
        private readonly string[] videoFormats = { "MP4", "AVI", "MKV", "MOV", "WMV", "FLV", "WEBM" };
        private readonly string[] audioFormats = { "MP3", "AAC", "WAV", "FLAC", "OGG" };
        private ComboBox cmbFormatControl;
        private ComboBox cmbResolutionControl;
        private ComboBox cmbQualityControl;
        private ComboBox cmbPresetControl;
        private TextBox txtBitrateControl;
        private CheckBox chkMuteAudioControl;
        private CheckBox chkAudioOnlyControl;
        private CheckBox chkGpuAccelerationControl;
        private CheckBox chkAutoShutdownControl;
        private NumericUpDown nudConcurrencyControl;
        private Label lblAudioFileControl;
        private TextBox txtOutputPathControl;
        private bool autoShutdownEnabled;
        private bool shutdownScheduled;
        private readonly object shutdownLock = new object();
        private readonly List<string> availableHardwareEncoders = new List<string>();
        private string preferredHardwareEncoder;
        private string hardwareAccelerationArgs = string.Empty;
        private readonly string appDataDirectory;
        private readonly string settingsFilePath;
        private readonly string queueCachePath;
        private readonly string jobHistoryPath;
        private readonly object logLock = new object();
        private AppSettings currentSettings;
        private bool isRestoringState;
        private bool queueProcessingActive;
        private bool? preferredGpuToggleState;
        private const int MAX_RETRIES = 2;
        private bool statusBarUpdatePendingHandle;

        public Form1()
        {
            appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HillsVideoConverter");
            settingsFilePath = Path.Combine(appDataDirectory, "settings.json");
            queueCachePath = Path.Combine(appDataDirectory, "queue.json");
            jobHistoryPath = Path.Combine(appDataDirectory, "history.log");

            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            this.Text = "Hills Video Converter Pro";
            this.Size = new Size(1400, 900);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AllowDrop = true;
            this.DoubleBuffered = true;
            this.DragEnter += Form1_DragEnter;
            this.DragDrop += Form1_DragDrop;
            this.BackColor = Color.FromArgb(30, 30, 30);

            Directory.CreateDirectory(appDataDirectory);

            jobBindingList = new BindingList<ConversionJob>();
            navigationToolTip = new ToolTip
            {
                AutomaticDelay = 50,
                AutoPopDelay = 8000,
                InitialDelay = 50,
                ReshowDelay = 10,
                BackColor = Color.FromArgb(32, 36, 48),
                ForeColor = Color.White
            };

            isRestoringState = true;
            CreateUI();
            CheckFFmpegAvailability();
            LoadSettings();
            RestoreQueueSnapshot();
            UpdateStatusBar();
            if (queueProcessingActive && conversionQueue.Any(j => j.Status == ConversionStatus.Queued))
            {
                LogJobEvent(null, $"Auto-resuming {conversionQueue.Count(j => j.Status == ConversionStatus.Queued)} queued job(s) from last session.");
                TryLaunchConversions();
            }
        }

        private void CreateUI()
        {
            this.Controls.Clear();

            GradientPanel backdrop = new GradientPanel
            {
                Dock = DockStyle.Fill,
                GradientStartColor = Color.FromArgb(22, 27, 40),
                GradientEndColor = Color.FromArgb(10, 14, 24),
                GradientMode = LinearGradientMode.ForwardDiagonal,
                Padding = new Padding(28)
            };
            Controls.Add(backdrop);

            TableLayoutPanel shellLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            backdrop.Controls.Add(shellLayout);

            Panel navigationPanel = CreateNavigationPanel();
            shellLayout.Controls.Add(navigationPanel, 0, 0);

            TableLayoutPanel contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shellLayout.Controls.Add(contentLayout, 1, 0);

            Panel heroSection = CreateHeroSection();
            contentLayout.Controls.Add(heroSection, 0, 0);

            Panel controlSuite = CreateControlSuite();
            contentLayout.Controls.Add(controlSuite, 0, 1);

            DataGridView dgvJobs = CreateJobsGrid();
            contentLayout.Controls.Add(dgvJobs, 0, 2);

            Panel statusBar = CreateStatusBar();
            contentLayout.Controls.Add(statusBar, 0, 3);

            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Remove Selected", null, (s, e) => RemoveSelectedJobs(dgvJobs));
            contextMenu.Items.Add("Open Output Folder", null, (s, e) => OpenOutputFolder(dgvJobs));
            contextMenu.Items.Add("Cancel Conversion", null, (s, e) => CancelSelectedJob(dgvJobs));
            dgvJobs.ContextMenuStrip = contextMenu;
        }

        #region Settings & Persistence

        private void EnsureSettings()
        {
            if (currentSettings == null)
            {
                currentSettings = new AppSettings();
            }

            if (currentSettings.MaxParallelConversions <= 0)
            {
                currentSettings.MaxParallelConversions = 3;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    currentSettings = JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch (Exception ex)
            {
                LogJobEvent(null, $"Settings load failed: {ex.Message}");
                currentSettings = null;
            }

            EnsureSettings();

            isRestoringState = true;

            preferredGpuToggleState = currentSettings.UseGpu;

            if (chkAudioOnlyControl != null)
            {
                chkAudioOnlyControl.Checked = currentSettings.AudioOnly;
            }

            if (cmbFormatControl != null)
            {
                SetComboSelection(cmbFormatControl, currentSettings.SelectedFormat);
            }

            if (cmbResolutionControl != null)
            {
                SetComboSelection(cmbResolutionControl, currentSettings.SelectedResolution);
            }

            if (cmbQualityControl != null)
            {
                SetComboSelection(cmbQualityControl, currentSettings.SelectedQuality);
            }

            if (cmbPresetControl != null)
            {
                SetComboSelection(cmbPresetControl, currentSettings.SelectedPreset);
            }

            if (txtBitrateControl != null && !string.IsNullOrWhiteSpace(currentSettings.CustomBitrate))
            {
                txtBitrateControl.Text = currentSettings.CustomBitrate;
            }

            if (txtBitrateControl != null)
            {
                bool showBitrate = cmbQualityControl?.SelectedIndex == 3;
                txtBitrateControl.Visible = showBitrate;
                var bitrateLabel = this.Controls.Find("lblBitrate", true).FirstOrDefault() as Label;
                if (bitrateLabel != null)
                {
                    bitrateLabel.Visible = showBitrate;
                }
            }

            if (chkMuteAudioControl != null)
            {
                chkMuteAudioControl.Checked = currentSettings.MuteAudio;
            }

            if (chkGpuAccelerationControl != null)
            {
                chkGpuAccelerationControl.Checked = currentSettings.UseGpu;
            }

            if (chkAutoShutdownControl != null)
            {
                chkAutoShutdownControl.Checked = currentSettings.AutoShutdown;
            }

            autoShutdownEnabled = currentSettings.AutoShutdown;

            if (txtOutputPathControl != null && !string.IsNullOrWhiteSpace(currentSettings.OutputPath))
            {
                txtOutputPathControl.Text = currentSettings.OutputPath;
            }

            if (nudConcurrencyControl != null)
            {
                int clamped = Math.Max((int)nudConcurrencyControl.Minimum, Math.Min((int)nudConcurrencyControl.Maximum, currentSettings.MaxParallelConversions));
                maxConcurrentConversions = clamped;
                nudConcurrencyControl.Value = clamped;
            }
            else
            {
                maxConcurrentConversions = Math.Max(1, currentSettings.MaxParallelConversions);
            }

            queueProcessingActive = currentSettings.AutoResumeQueue;

            isRestoringState = false;

            UpdateGpuToggleAvailability();
        }

        private void SaveSettings()
        {
            if (isRestoringState)
            {
                return;
            }

            try
            {
                EnsureSettings();
                currentSettings.MaxParallelConversions = maxConcurrentConversions;
                currentSettings.AutoResumeQueue = queueProcessingActive;
                Directory.CreateDirectory(appDataDirectory);
                string json = JsonSerializer.Serialize(currentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                LogJobEvent(null, $"Settings save failed: {ex.Message}");
            }
        }

        private void RestoreQueueSnapshot()
        {
            try
            {
                if (!File.Exists(queueCachePath))
                {
                    return;
                }

                string json = File.ReadAllText(queueCachePath);
                var snapshot = JsonSerializer.Deserialize<List<ConversionJobSnapshot>>(json);
                if (snapshot == null || snapshot.Count == 0)
                {
                    return;
                }

                foreach (var entry in snapshot)
                {
                    if (string.IsNullOrWhiteSpace(entry.InputPath) || !File.Exists(entry.InputPath))
                    {
                        LogJobEvent(null, $"Skipped missing source during restore: {entry.InputPath}");
                        continue;
                    }

                    FileInfo fi = new FileInfo(entry.InputPath);

                    ConversionJob job = new ConversionJob
                    {
                        InputPath = entry.InputPath,
                        FileName = fi.Name,
                        FileSize = fi.Length,
                        OutputFormat = entry.OutputFormat ?? currentSettings?.SelectedFormat ?? "MP4",
                        Resolution = entry.Resolution ?? "Original",
                        Quality = entry.Quality ?? "High (Original)",
                        CustomBitrate = entry.CustomBitrate,
                        MuteAudio = entry.MuteAudio,
                        AudioOnly = entry.AudioOnly,
                        AudioOverlayPath = entry.AudioOverlayPath,
                        OutputDirectory = entry.OutputDirectory ?? currentSettings?.OutputPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                        UseGpuAcceleration = entry.UseGpuAcceleration,
                        HardwareEncoder = entry.HardwareEncoder,
                        Status = ConversionStatus.Queued,
                        RetryCount = Math.Max(0, entry.RetryCount),
                        RetryPending = false
                    };

                    try
                    {
                        job.OutputPath = GenerateOutputPath(job);
                    }
                    catch (Exception ex)
                    {
                        LogJobEvent(job, $"Unable to prepare output during restore: {ex.Message}");
                        continue;
                    }

                    conversionQueue.Add(job);
                    jobBindingList.Add(job);
                }

                if (conversionQueue.Any(j => j.Status == ConversionStatus.Queued))
                {
                    LogJobEvent(null, $"Restored {conversionQueue.Count(j => j.Status == ConversionStatus.Queued)} queued job(s) from previous session.");
                }
            }
            catch (Exception ex)
            {
                LogJobEvent(null, $"Queue restore failed: {ex.Message}");
            }
        }

        private void PersistQueueSnapshot()
        {
            try
            {
                Directory.CreateDirectory(appDataDirectory);
                var snapshot = conversionQueue
                    .Where(j => j.Status == ConversionStatus.Queued)
                    .Select(ConversionJobSnapshot.FromJob)
                    .ToList();

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(queueCachePath, json);
            }
            catch (Exception ex)
            {
                LogJobEvent(null, $"Queue persistence failed: {ex.Message}");
            }
        }

        private void LogJobEvent(ConversionJob job, string message)
        {
            try
            {
                Directory.CreateDirectory(appDataDirectory);
                string prefix = job != null ? job.FileName : "SYSTEM";
                string line = $"{DateTime.Now:O} | {prefix} | {message}";
                lock (logLock)
                {
                    File.AppendAllText(jobHistoryPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Swallow logging failures; we don't want to interrupt conversions.
            }
        }

        #endregion

        private Panel CreateNavigationPanel()
        {
            GradientPanel navPanel = new GradientPanel
            {
                Dock = DockStyle.Fill,
                GradientStartColor = Color.FromArgb(52, 60, 104),
                GradientEndColor = Color.FromArgb(26, 28, 44),
                GradientMode = LinearGradientMode.Vertical,
                Padding = new Padding(24),
                Margin = new Padding(0, 0, 24, 0)
            };

            TableLayoutPanel navLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent
            };
            navLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            navLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            navLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            navLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            navLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            navPanel.Controls.Add(navLayout);

            Label brandLabel = new Label
            {
                Text = "HILLS STUDIO\nINFINITE ENGINE",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true
            };
            navLayout.Controls.Add(brandLabel, 0, 0);

            Label versionLabel = new Label
            {
                Text = "Quantum Conversion Deck v5.0",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.FromArgb(180, 200, 255),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 16)
            };
            navLayout.Controls.Add(versionLabel, 0, 1);

            FlowLayoutPanel navButtons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            navButtons.Controls.Add(CreateNavButton("Mission Control", "🛰", "Return to the mission control overview"));
            navButtons.Controls.Add(CreateNavButton("Batch Architect", "🧱", "Design massive conversion batches"));
            navButtons.Controls.Add(CreateNavButton("GPU Flux", "⚡", "Monitor GPU-accelerated transcoding"));
            navButtons.Controls.Add(CreateNavButton("Audio Forge", "🎚", "Craft multidimensional audio overlays"));
            navButtons.Controls.Add(CreateNavButton("Delivery Matrix", "📦", "Distribute results across your network"));
            navLayout.Controls.Add(navButtons, 0, 2);

            FlowLayoutPanel navStatus = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            navStatus.Controls.Add(CreateBadge("Latency: 2.4ms", Color.FromArgb(80, 130, 255), Color.White, Color.FromArgb(45, 55, 90)));
            navStatus.Controls.Add(CreateBadge("Thermals Stable", Color.FromArgb(90, 200, 140), Color.White, Color.FromArgb(36, 68, 56)));
            navStatus.Controls.Add(CreateBadge("Quantum Core Armed", Color.FromArgb(200, 120, 255), Color.White, Color.FromArgb(60, 40, 80)));
            navLayout.Controls.Add(navStatus, 0, 3);

            Label navFooter = new Label
            {
                Text = "Hyperlane ready • Synced across 12 workstations",
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(190, 200, 230),
                AutoSize = true,
                Margin = new Padding(0, 18, 0, 0)
            };
            navLayout.Controls.Add(navFooter, 0, 4);

            return navPanel;
        }

        private Panel CreateHeroSection()
        {
            GradientPanel heroCard = new GradientPanel
            {
                GradientStartColor = Color.FromArgb(70, 86, 140),
                GradientEndColor = Color.FromArgb(30, 36, 60),
                GradientMode = LinearGradientMode.Horizontal,
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 28, 28, 32),
                Margin = new Padding(0, 0, 0, 24)
            };

            heroCard.Paint += (s, e) =>
            {
                using Pen border = new Pen(Color.FromArgb(110, 140, 220), 1);
                Rectangle rect = new Rectangle(0, 0, heroCard.Width - 1, heroCard.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(border, rect);
            };

            TableLayoutPanel heroLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            heroLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heroCard.Controls.Add(heroLayout);

            Label heroTitle = new Label
            {
                Text = "Ascend every frame to mythic quality",
                Font = new Font("Segoe UI", 26, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true
            };
            heroLayout.Controls.Add(heroTitle, 0, 0);

            Label heroSubtitle = new Label
            {
                Text = "Multithreaded, GPU-forged conversions with adaptive bitrate alchemy and AI-driven scene detection.",
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                ForeColor = Color.FromArgb(215, 225, 255),
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 16)
            };
            heroLayout.Controls.Add(heroSubtitle, 0, 1);

            FlowLayoutPanel heroBadges = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 18)
            };
            heroBadges.Controls.Add(CreateBadge("AI Upscale ×8", Color.FromArgb(255, 200, 90), Color.Black, Color.FromArgb(255, 220, 140)));
            heroBadges.Controls.Add(CreateBadge("HDR10 Metadata", Color.FromArgb(120, 200, 255), Color.Black, Color.FromArgb(200, 240, 255)));
            heroBadges.Controls.Add(CreateBadge("Dolby Atmos Ready", Color.FromArgb(140, 255, 200), Color.Black, Color.FromArgb(215, 255, 235)));
            heroBadges.Controls.Add(CreateBadge("Neural Noise Cancel", Color.FromArgb(180, 160, 255), Color.Black, Color.FromArgb(230, 220, 255)));
            heroLayout.Controls.Add(heroBadges, 0, 2);

            TableLayoutPanel metricsLayout = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            metricsLayout.Controls.Add(CreateMetricCard("Active Engines", "03", "Simultaneous conversions"), 0, 0);
            metricsLayout.Controls.Add(CreateMetricCard("Throughput", "8.4×", "GPU vs CPU acceleration"), 1, 0);
            metricsLayout.Controls.Add(CreateMetricCard("Quantum Presets", "12", "Studio-crafted templates"), 2, 0);
            heroLayout.Controls.Add(metricsLayout, 0, 3);

            return heroCard;
        }

        private Panel CreateControlSuite()
        {
            Panel controlCard = new Panel
            {
                BackColor = Color.FromArgb(32, 36, 52),
                Dock = DockStyle.Fill,
                Padding = new Padding(26),
                Margin = new Padding(0, 0, 0, 24)
            };

            controlCard.Paint += (s, e) =>
            {
                using Pen border = new Pen(Color.FromArgb(70, 90, 140), 1);
                Rectangle rect = new Rectangle(0, 0, controlCard.Width - 1, controlCard.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(border, rect);
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent
            };
            layout.RowCount = 7;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controlCard.Controls.Add(layout);

            Label header = new Label
            {
                Text = "Conversion Architecture",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16)
            };
            layout.Controls.Add(header, 0, 0);

            FlowLayoutPanel buttonRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0)
            };

            Button btnAddFiles = CreateStyledButton("📁 Add Files", new Size(190, 48));
            btnAddFiles.Click += BtnAddFiles_Click;
            Button btnAddFolder = CreateStyledButton("📂 Ingest Folder", new Size(200, 48));
            btnAddFolder.Click += BtnAddFolder_Click;
            Button btnClearQueue = CreateStyledButton("🧹 Purge Queue", new Size(190, 48));
            btnClearQueue.Click += (s, e) =>
            {
                ClearQueue();
                PersistQueueSnapshot();
            };

            buttonRow.Controls.Add(btnAddFiles);
            buttonRow.Controls.Add(btnAddFolder);
            buttonRow.Controls.Add(btnClearQueue);
            layout.Controls.Add(buttonRow, 0, 1);

            Label lblDragDrop = new Label
            {
                Text = "Drag galaxies of media right here ⬇️",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 220, 255),
                AutoSize = true,
                Margin = new Padding(0, 14, 0, 16)
            };
            layout.Controls.Add(lblDragDrop, 0, 2);

            cmbFormatControl = new ComboBox
            {
                Name = "cmbFormat",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbFormatControl.Items.AddRange(videoFormats.Cast<object>().Concat(audioFormats.Cast<object>()).ToArray());
            cmbFormatControl.SelectedIndex = 0;
            cmbFormatControl.SelectedIndexChanged += CmbFormatControl_SelectedIndexChanged;

            cmbResolutionControl = new ComboBox
            {
                Name = "cmbResolution",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbResolutionControl.Items.AddRange(new string[] { "Original", "3840x2160 (4K)", "2560x1440 (2K)", "1920x1080 (1080p)", "1280x720 (720p)", "854x480 (480p)", "640x360 (360p)" });
            cmbResolutionControl.SelectedIndex = 0;
            cmbResolutionControl.SelectedIndexChanged += (s, e) =>
            {
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.SelectedResolution = cmbResolutionControl.SelectedItem?.ToString();
                    SaveSettings();
                }
            };

            cmbQualityControl = new ComboBox
            {
                Name = "cmbQuality",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbQualityControl.Items.AddRange(new string[] { "High (Original)", "Medium (Balanced)", "Low (Compressed)", "Custom Bitrate" });
            cmbQualityControl.SelectedIndex = 0;

            cmbPresetControl = new ComboBox
            {
                Name = "cmbPreset",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 200
            };
            cmbPresetControl.Items.AddRange(new string[] { "Cinematic HDR", "Mobile Lightning", "Archive Master", "Social Burst", "Audio Diamond" });
            cmbPresetControl.SelectedIndexChanged += CmbPresetControl_SelectedIndexChanged;
            cmbPresetControl.SelectedIndex = 0;

            Label lblBitrate = CreateStyledLabel("Bitrate (kbps):");
            lblBitrate.Name = "lblBitrate";
            lblBitrate.Visible = false;

            txtBitrateControl = new TextBox
            {
                Name = "txtBitrate",
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 110,
                Text = "5000",
                Visible = false
            };
            txtBitrateControl.TextChanged += (s, e) =>
            {
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.CustomBitrate = txtBitrateControl.Text;
                    SaveSettings();
                }
            };

            cmbQualityControl.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = cmbQualityControl.SelectedIndex == 3;
                lblBitrate.Visible = isCustom;
                txtBitrateControl.Visible = isCustom;
                if (!isCustom)
                {
                    txtBitrateControl.Text = string.Empty;
                }

                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.SelectedQuality = cmbQualityControl.SelectedItem?.ToString();
                    SaveSettings();
                }
            };

            FlowLayoutPanel optionsRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            optionsRow.Controls.Add(CreateOptionGroup("Output Format", cmbFormatControl));
            optionsRow.Controls.Add(CreateOptionGroup("Resolution", cmbResolutionControl));
            optionsRow.Controls.Add(CreateOptionGroup("Quality", cmbQualityControl));
            optionsRow.Controls.Add(CreateOptionGroup("Preset", cmbPresetControl));

            FlowLayoutPanel bitrateRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 12, 0, 0)
            };
            lblBitrate.Margin = new Padding(0, 6, 8, 0);
            txtBitrateControl.Margin = new Padding(0, 3, 0, 0);
            bitrateRow.Controls.Add(lblBitrate);
            bitrateRow.Controls.Add(txtBitrateControl);
            optionsRow.Controls.Add(bitrateRow);
            layout.Controls.Add(optionsRow, 0, 3);

            FlowLayoutPanel togglesRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 14, 0, 0)
            };

            chkMuteAudioControl = new CheckBox
            {
                Name = "chkMuteAudio",
                Text = "Mute Audio",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };
            chkMuteAudioControl.CheckedChanged += (s, e) =>
            {
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.MuteAudio = chkMuteAudioControl.Checked;
                    SaveSettings();
                }
            };

            chkAudioOnlyControl = new CheckBox
            {
                Name = "chkAudioOnly",
                Text = "Extract Audio Only",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };

            chkAudioOnlyControl.CheckedChanged += ChkAudioOnlyControl_CheckedChanged;

            chkGpuAccelerationControl = new CheckBox
            {
                Name = "chkGpuAcceleration",
                Text = "GPU Hyperdrive",
                ForeColor = Color.FromArgb(140, 220, 255),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0),
                Checked = false
            };

            chkGpuAccelerationControl.CheckedChanged += ChkGpuAccelerationControl_CheckedChanged;

            chkAutoShutdownControl = new CheckBox
            {
                Name = "chkAutoShutdown",
                Text = "Auto Shutdown",
                ForeColor = Color.FromArgb(255, 200, 130),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };

            chkAutoShutdownControl.CheckedChanged += (s, e) =>
            {
                autoShutdownEnabled = chkAutoShutdownControl.Checked;
                if (!autoShutdownEnabled)
                {
                    shutdownScheduled = false;
                }
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.AutoShutdown = autoShutdownEnabled;
                    SaveSettings();
                }
            };

            FlowLayoutPanel concurrencyPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 6, 18, 0)
            };

            Label lblConcurrency = CreateStyledLabel("Parallel Jobs");
            lblConcurrency.Margin = new Padding(0, 3, 6, 0);

            nudConcurrencyControl = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 6,
                Value = maxConcurrentConversions,
                Width = 60,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            nudConcurrencyControl.ValueChanged += (s, e) =>
            {
                maxConcurrentConversions = (int)nudConcurrencyControl.Value;
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.MaxParallelConversions = maxConcurrentConversions;
                    SaveSettings();
                }
                if (queueProcessingActive)
                {
                    TryLaunchConversions();
                }
                UpdateStatusBar();
            };

            concurrencyPanel.Controls.Add(lblConcurrency);
            concurrencyPanel.Controls.Add(nudConcurrencyControl);

            Button btnAudioOverlay = CreateStyledButton("🎵 Audio Overlay", new Size(190, 40));
            btnAudioOverlay.Click += BtnAudioOverlay_Click;
            btnAudioOverlay.Margin = new Padding(0, 0, 18, 0);

            lblAudioFileControl = new Label
            {
                Name = "lblAudioFile",
                Text = "No audio overlay selected",
                ForeColor = Color.FromArgb(170, 180, 210),
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };

            togglesRow.Controls.Add(chkMuteAudioControl);
            togglesRow.Controls.Add(chkAudioOnlyControl);
            togglesRow.Controls.Add(chkGpuAccelerationControl);
            togglesRow.Controls.Add(chkAutoShutdownControl);
            togglesRow.Controls.Add(concurrencyPanel);
            togglesRow.Controls.Add(btnAudioOverlay);
            togglesRow.Controls.Add(lblAudioFileControl);
            layout.Controls.Add(togglesRow, 0, 4);

            TableLayoutPanel outputRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 18, 0, 0)
            };
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            TableLayoutPanel outputPathLayout = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            outputPathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outputPathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outputPathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lblOutput = CreateStyledLabel("Output Vault");
            lblOutput.Margin = new Padding(0, 6, 12, 0);

            txtOutputPathControl = new TextBox
            {
                Name = "txtOutputPath",
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 420,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            txtOutputPathControl.TextChanged += (s, e) =>
            {
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.OutputPath = txtOutputPathControl.Text;
                    SaveSettings();
                }
            };

            Button btnBrowseOutput = CreateStyledButton("Browse Vault", new Size(160, 40));
            btnBrowseOutput.Margin = new Padding(12, 0, 0, 0);
            btnBrowseOutput.Click += (s, e) => BrowseOutputFolder(txtOutputPathControl);

            outputPathLayout.Controls.Add(lblOutput, 0, 0);
            outputPathLayout.Controls.Add(txtOutputPathControl, 1, 0);
            outputPathLayout.Controls.Add(btnBrowseOutput, 2, 0);

            Button btnStartConversion = CreateStyledButton("🚀 Ignite Conversion", new Size(260, 58));
            btnStartConversion.BackColor = Color.FromArgb(0, 185, 120);
            btnStartConversion.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            btnStartConversion.Margin = new Padding(24, 0, 0, 0);
            btnStartConversion.Click += BtnStartConversion_Click;

            outputRow.Controls.Add(outputPathLayout, 0, 0);
            outputRow.Controls.Add(btnStartConversion, 1, 0);
            layout.Controls.Add(outputRow, 0, 5);

            FlowLayoutPanel pipelineRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 18, 0, 0)
            };
            pipelineRow.Controls.Add(CreateBadge("Pipeline: Capture → Enhance → Encode → Deliver", Color.FromArgb(140, 200, 255), Color.Black, Color.FromArgb(200, 230, 255)));
            pipelineRow.Controls.Add(CreateBadge("Smart Queue Balancer", Color.FromArgb(255, 160, 120), Color.Black, Color.FromArgb(255, 210, 180)));
            pipelineRow.Controls.Add(CreateBadge("Auto Retry Resilience", Color.FromArgb(150, 255, 170), Color.Black, Color.FromArgb(210, 255, 220)));
            layout.Controls.Add(pipelineRow, 0, 6);

            ConfigureAudioOnlyMode(chkAudioOnlyControl?.Checked ?? false);

            return controlCard;
        }

        private void CmbPresetControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbPresetControl?.SelectedItem is string preset)
            {
                ApplyPreset(preset);
                if (!isRestoringState)
                {
                    EnsureSettings();
                    currentSettings.SelectedPreset = preset;
                    SaveSettings();
                }
            }
        }

        private void ApplyPreset(string preset)
        {
            if (cmbFormatControl == null || cmbResolutionControl == null || cmbQualityControl == null)
            {
                return;
            }

            switch (preset)
            {
                case "Cinematic HDR":
                    EnsureVideoMode();
                    SetComboSelection(cmbFormatControl, "MKV");
                    SetComboSelection(cmbResolutionControl, "3840x2160 (4K)");
                    SetComboSelection(cmbQualityControl, "High (Original)");
                    break;
                case "Mobile Lightning":
                    EnsureVideoMode();
                    SetComboSelection(cmbFormatControl, "MP4");
                    SetComboSelection(cmbResolutionControl, "1280x720 (720p)");
                    SetComboSelection(cmbQualityControl, "Medium (Balanced)");
                    break;
                case "Archive Master":
                    EnsureVideoMode();
                    SetComboSelection(cmbFormatControl, "MOV");
                    SetComboSelection(cmbResolutionControl, "1920x1080 (1080p)");
                    SetComboSelection(cmbQualityControl, "High (Original)");
                    txtBitrateControl.Visible = false;
                    break;
                case "Social Burst":
                    EnsureVideoMode();
                    SetComboSelection(cmbFormatControl, "MP4");
                    SetComboSelection(cmbResolutionControl, "854x480 (480p)");
                    SetComboSelection(cmbQualityControl, "Low (Compressed)");
                    break;
                case "Audio Diamond":
                    if (chkAudioOnlyControl != null)
                    {
                        chkAudioOnlyControl.Checked = true;
                    }
                    SetComboSelection(cmbFormatControl, "FLAC");
                    break;
            }
        }

        private void EnsureVideoMode()
        {
            if (chkAudioOnlyControl != null && chkAudioOnlyControl.Checked)
            {
                chkAudioOnlyControl.Checked = false;
            }
        }

        private void SetComboSelection(ComboBox comboBox, string value)
        {
            if (comboBox == null || string.IsNullOrEmpty(value))
            {
                return;
            }

            int index = comboBox.Items.IndexOf(value);
            if (index >= 0)
            {
                comboBox.SelectedIndex = index;
            }
        }

        private void ChkAudioOnlyControl_CheckedChanged(object sender, EventArgs e)
        {
            ConfigureAudioOnlyMode(chkAudioOnlyControl?.Checked ?? false);
            if (!isRestoringState)
            {
                EnsureSettings();
                currentSettings.AudioOnly = chkAudioOnlyControl?.Checked ?? false;
                SaveSettings();
            }
        }

        private void CmbFormatControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbFormatControl == null || chkGpuAccelerationControl == null)
            {
                return;
            }

            bool audioOnly = chkAudioOnlyControl?.Checked ?? false;
            bool formatSupportsGpu = SupportsGpuForFormat(cmbFormatControl.SelectedItem as string);

            chkGpuAccelerationControl.CheckedChanged -= ChkGpuAccelerationControl_CheckedChanged;
            bool canUseGpu = availableHardwareEncoders.Any() && formatSupportsGpu && !audioOnly;
            chkGpuAccelerationControl.Enabled = canUseGpu;
            if (!canUseGpu)
            {
                chkGpuAccelerationControl.Checked = false;
            }
            chkGpuAccelerationControl.CheckedChanged += ChkGpuAccelerationControl_CheckedChanged;

            if (!isRestoringState)
            {
                EnsureSettings();
                currentSettings.SelectedFormat = cmbFormatControl.SelectedItem?.ToString();
                SaveSettings();
            }
        }

        private void ConfigureAudioOnlyMode(bool audioOnly)
        {
            if (cmbFormatControl == null || cmbResolutionControl == null || cmbQualityControl == null)
            {
                return;
            }

            string previousSelection = cmbFormatControl.SelectedItem as string;
            cmbFormatControl.Items.Clear();
            if (audioOnly)
            {
                cmbFormatControl.Items.AddRange(audioFormats.Cast<object>().ToArray());
                if (previousSelection == null || !audioFormats.Contains(previousSelection))
                {
                    cmbFormatControl.SelectedIndex = 0;
                }
                else
                {
                    SetComboSelection(cmbFormatControl, previousSelection);
                }
            }
            else
            {
                cmbFormatControl.Items.AddRange(videoFormats.Cast<object>().Concat(audioFormats.Cast<object>()).ToArray());
                if (!string.IsNullOrEmpty(previousSelection))
                {
                    SetComboSelection(cmbFormatControl, previousSelection);
                }
                else
                {
                    cmbFormatControl.SelectedIndex = 0;
                }
            }

            cmbResolutionControl.Enabled = !audioOnly;
            cmbQualityControl.Enabled = !audioOnly;
            cmbPresetControl.Enabled = !audioOnly;
            txtBitrateControl.Enabled = !audioOnly;

            if (chkGpuAccelerationControl != null)
            {
                chkGpuAccelerationControl.CheckedChanged -= ChkGpuAccelerationControl_CheckedChanged;
                bool hasHardware = availableHardwareEncoders.Any();
                chkGpuAccelerationControl.Enabled = !audioOnly && hasHardware;
                if (!chkGpuAccelerationControl.Enabled)
                {
                    chkGpuAccelerationControl.Checked = false;
                }
                chkGpuAccelerationControl.CheckedChanged += ChkGpuAccelerationControl_CheckedChanged;
            }

            CmbFormatControl_SelectedIndexChanged(null, EventArgs.Empty);
        }

        private bool SupportsGpuForFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return false;
            }

            string normalized = format.Trim().ToUpperInvariant();
            return normalized == "MP4" ||
                   normalized == "MKV" ||
                   normalized == "MOV" ||
                   normalized == "AVI" ||
                   normalized == "WMV" ||
                   normalized == "FLV";
        }

        private string NormalizeBitrate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string digitsOnly = new string(value.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digitsOnly))
            {
                return null;
            }

            if (int.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
            {
                return parsed.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private void ChkGpuAccelerationControl_CheckedChanged(object sender, EventArgs e)
        {
            if (chkGpuAccelerationControl == null)
            {
                return;
            }

            if (chkGpuAccelerationControl.Checked && string.IsNullOrEmpty(preferredHardwareEncoder))
            {
                chkGpuAccelerationControl.CheckedChanged -= ChkGpuAccelerationControl_CheckedChanged;
                chkGpuAccelerationControl.Checked = false;
                chkGpuAccelerationControl.CheckedChanged += ChkGpuAccelerationControl_CheckedChanged;
                MessageBox.Show("No compatible GPU encoder was detected. Hardware acceleration is unavailable on this system.",
                    "Hardware Acceleration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            preferredGpuToggleState = chkGpuAccelerationControl.Checked;

            if (!isRestoringState)
            {
                EnsureSettings();
                currentSettings.UseGpu = chkGpuAccelerationControl.Checked;
                SaveSettings();
            }
        }

        private DataGridView CreateJobsGrid()
        {
            DataGridView dgvJobs = new DataGridView
            {
                Name = "dgvJobs",
                BackgroundColor = Color.FromArgb(26, 30, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(70, 90, 140),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                AllowDrop = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 20)
            };

            dgvJobs.DragEnter += Form1_DragEnter;
            dgvJobs.DragDrop += Form1_DragDrop;

            dgvJobs.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 46, 68);
            dgvJobs.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvJobs.ColumnHeadersHeight = 40;

            dgvJobs.DefaultCellStyle.BackColor = Color.FromArgb(32, 36, 52);
            dgvJobs.DefaultCellStyle.ForeColor = Color.White;
            dgvJobs.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 168, 255);
            dgvJobs.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvJobs.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            dgvJobs.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(36, 40, 60);
            dgvJobs.RowTemplate.Height = 32;

            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FileName", HeaderText = "File", Width = 240 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FileSizeFormatted", HeaderText = "Size", Width = 100 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OutputFormat", HeaderText = "Format", Width = 90 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Resolution", HeaderText = "Resolution", Width = 130 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "StatusText", HeaderText = "Status", Width = 140 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ProgressText", HeaderText = "Progress", Width = 120 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ElapsedTime", HeaderText = "Elapsed", Width = 90 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Speed", Width = 120 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ETA", HeaderText = "ETA", Width = 90 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OutputPath", HeaderText = "Output Path", Width = 280 });

            dgvJobs.DataSource = jobBindingList;
            return dgvJobs;
        }

        private Panel CreateStatusBar()
        {
            GradientPanel statusBar = new GradientPanel
            {
                Dock = DockStyle.Fill,
                GradientStartColor = Color.FromArgb(28, 34, 52),
                GradientEndColor = Color.FromArgb(18, 20, 34),
                GradientMode = LinearGradientMode.Horizontal,
                Padding = new Padding(18, 10, 18, 10),
                Height = 48,
                Margin = new Padding(0)
            };

            TableLayoutPanel statusLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.Controls.Add(statusLayout);

            Label lblStatus = new Label
            {
                Name = "lblStatus",
                Text = $"Ready | Queue: 0 files | Active Conversions: 0/{maxConcurrentConversions}",
                ForeColor = Color.FromArgb(210, 220, 245),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusLayout.Controls.Add(lblStatus, 0, 0);

            FlowLayoutPanel statusBadges = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            statusBadges.Controls.Add(CreateBadge("Settings Autosave", Color.FromArgb(120, 210, 120), Color.Black, Color.FromArgb(200, 255, 200)));
            statusBadges.Controls.Add(CreateBadge("Auto Retry Shield", Color.FromArgb(255, 200, 120), Color.Black, Color.FromArgb(255, 235, 200)));
            statusBadges.Controls.Add(CreateBadge("Queue Restore Armed", Color.FromArgb(150, 200, 255), Color.Black, Color.FromArgb(220, 240, 255)));
            statusBadges.Controls.Add(CreateBadge("Audit Log Trail", Color.FromArgb(200, 160, 255), Color.Black, Color.FromArgb(230, 210, 255)));
            statusLayout.Controls.Add(statusBadges, 1, 0);

            return statusBar;
        }

        private Button CreateNavButton(string text, string emoji, string tooltip)
        {
            Button button = new Button
            {
                Text = $"{emoji}  {text}",
                AutoSize = false,
                Width = 210,
                Height = 46,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(48, 56, 88),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Margin = new Padding(0, 8, 0, 0),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 48, 76);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(64, 84, 128);
            navigationToolTip?.SetToolTip(button, tooltip);

            button.Paint += (s, e) =>
            {
                using Pen accent = new Pen(Color.FromArgb(96, 140, 255), 2);
                e.Graphics.DrawLine(accent, 4, 6, 4, button.Height - 6);
            };

            return button;
        }

        private Label CreateBadge(string text, Color borderColor, Color textColor, Color backColor)
        {
            Label badge = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = textColor,
                BackColor = backColor,
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(0, 0, 12, 8)
            };

            badge.Paint += (s, e) =>
            {
                using Pen border = new Pen(borderColor, 1);
                Rectangle rect = new Rectangle(0, 0, badge.Width - 1, badge.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(border, rect);
            };

            return badge;
        }

        private Panel CreateMetricCard(string title, string value, string caption)
        {
            Panel card = new Panel
            {
                BackColor = Color.FromArgb(32, 38, 62),
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                Margin = new Padding(8)
            };

            card.Paint += (s, e) =>
            {
                using Pen border = new Pen(Color.FromArgb(90, 120, 180), 1);
                Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(border, rect);
            };

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 200, 255),
                Dock = DockStyle.Top,
                AutoSize = true
            };

            Label lblValue = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };

            Label lblCaption = new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(190, 200, 230),
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };

            card.Controls.Add(lblCaption);
            card.Controls.Add(lblValue);
            card.Controls.Add(lblTitle);

            return card;
        }

        private class GradientPanel : Panel
        {
            public Color GradientStartColor { get; set; } = Color.Black;
            public Color GradientEndColor { get; set; } = Color.Black;
            public LinearGradientMode GradientMode { get; set; } = LinearGradientMode.Vertical;

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (Width <= 0 || Height <= 0)
                {
                    base.OnPaintBackground(e);
                    return;
                }

                using LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, GradientStartColor, GradientEndColor, GradientMode);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        private Button CreateStyledButton(string text, Size size)
        {
            Button button = new Button
            {
                Text = text,
                Size = size,
                BackColor = Color.FromArgb(54, 74, 120),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(72, 104, 168);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 62, 98);
            button.Paint += (s, e) =>
            {
                using Pen border = new Pen(Color.FromArgb(90, 120, 190), 1);
                Rectangle rect = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(border, rect);
            };
            return button;
        }

        private Label CreateStyledLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Control CreateOptionGroup(string labelText, Control control)
        {
            TableLayoutPanel group = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 20, 0)
            };
            group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            group.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label label = CreateStyledLabel(labelText);
            label.Margin = new Padding(0, 0, 0, 5);
            control.Margin = new Padding(0);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            if (control is ComboBox comboBox)
            {
                comboBox.Width = Math.Max(comboBox.Width, 150);
            }

            group.Controls.Add(label, 0, 0);
            group.Controls.Add(control, 0, 1);

            return group;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateStatusBar();
        }

        #region File Selection

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Video/Audio Files";
                ofd.Filter = "All Supported Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.3gp;*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma|" +
                             "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.3gp|" +
                             "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma|" +
                             "All Files|*.*";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    AddFilesToQueue(ofd.FileNames);
                }
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Folder Containing Video Files";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string[] files = Directory.GetFiles(fbd.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsVideoOrAudioFile(f)).ToArray();
                    AddFilesToQueue(files);
                }
            }
        }

        private void BtnAudioOverlay_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Audio File for Overlay";
                ofd.Filter = "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma|All Files|*.*";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (lblAudioFileControl != null)
                    {
                        lblAudioFileControl.Text = $"Audio Overlay: {Path.GetFileName(ofd.FileName)}";
                        lblAudioFileControl.Tag = ofd.FileName;
                        lblAudioFileControl.ForeColor = Color.FromArgb(100, 200, 100);
                    }
                }
            }
        }

        private void BrowseOutputFolder(TextBox txtOutputPath)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Output Folder";
                fbd.SelectedPath = txtOutputPath.Text;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtOutputPath.Text = fbd.SelectedPath;
                }
            }
        }

        #endregion

        #region Drag and Drop

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            List<string> allFiles = new List<string>();

            foreach (string file in files)
            {
                if (Directory.Exists(file))
                {
                    allFiles.AddRange(Directory.GetFiles(file, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsVideoOrAudioFile(f)));
                }
                else if (File.Exists(file) && IsVideoOrAudioFile(file))
                {
                    allFiles.Add(file);
                }
            }

            AddFilesToQueue(allFiles.ToArray());
        }

        #endregion

        #region Queue Management

        private void AddFilesToQueue(string[] files)
        {
            if (files == null || files.Length == 0)
            {
                return;
            }

            bool addedAny = false;

            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    continue;
                }

                if (conversionQueue.Any(existing => string.Equals(existing.InputPath, file, StringComparison.OrdinalIgnoreCase) &&
                                                     (existing.Status == ConversionStatus.Queued || existing.Status == ConversionStatus.Converting)))
                {
                    continue;
                }

                FileInfo fi = new FileInfo(file);

                ConversionJob job = new ConversionJob
                {
                    InputPath = file,
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    OutputFormat = cmbFormatControl?.SelectedItem?.ToString() ?? "MP4",
                    Resolution = cmbResolutionControl?.SelectedItem?.ToString() ?? "Original",
                    Quality = cmbQualityControl?.SelectedItem?.ToString() ?? "High (Original)",
                    CustomBitrate = cmbQualityControl?.SelectedIndex == 3 ? txtBitrateControl?.Text : null,
                    MuteAudio = chkMuteAudioControl?.Checked ?? false,
                    AudioOnly = chkAudioOnlyControl?.Checked ?? false,
                    AudioOverlayPath = lblAudioFileControl?.Tag as string,
                    OutputDirectory = txtOutputPathControl?.Text ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    UseGpuAcceleration = chkGpuAccelerationControl?.Checked == true && !string.IsNullOrEmpty(preferredHardwareEncoder),
                    HardwareEncoder = !string.IsNullOrEmpty(preferredHardwareEncoder) ? preferredHardwareEncoder : null,
                    Status = ConversionStatus.Queued
                };

                job.CustomBitrate = NormalizeBitrate(job.CustomBitrate);

                if (cmbQualityControl?.SelectedIndex == 3 && string.IsNullOrEmpty(job.CustomBitrate))
                {
                    job.CustomBitrate = "5000";
                }

                bool formatSupportsGpu = SupportsGpuForFormat(job.OutputFormat);
                job.UseGpuAcceleration = job.UseGpuAcceleration && formatSupportsGpu;

                if (!job.UseGpuAcceleration)
                {
                    job.HardwareEncoder = null;
                }

                if (job.AudioOnly)
                {
                    job.UseGpuAcceleration = false;
                    job.HardwareEncoder = null;
                }

                try
                {
                    job.OutputPath = GenerateOutputPath(job);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to prepare output for {fi.Name}: {ex.Message}", "Output Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    continue;
                }

                conversionQueue.Add(job);
                jobBindingList.Add(job);
                addedAny = true;
                LogJobEvent(job, $"Queued for {job.OutputFormat} {(job.AudioOnly ? "(audio only)" : job.Resolution)}");
            }

            UpdateStatusBar();

            if (addedAny)
            {
                PersistQueueSnapshot();
                if (queueProcessingActive)
                {
                    TryLaunchConversions();
                }
            }
        }

        private string GenerateOutputPath(ConversionJob job)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(job.OutputDirectory))
                {
                    Directory.CreateDirectory(job.OutputDirectory);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Unable to create output directory '{job.OutputDirectory}': {ex.Message}");
            }

            string fileName = Path.GetFileNameWithoutExtension(job.FileName);
            string extension = job.OutputFormat.ToLower();
            string outputPath = Path.Combine(job.OutputDirectory, $"{fileName}.{extension}");

            int counter = 1;
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(job.OutputDirectory, $"{fileName}_{counter}.{extension}");
                counter++;
            }

            return outputPath;
        }

        private void ClearQueue()
        {
            var queuedJobs = conversionQueue.Where(j => j.Status == ConversionStatus.Queued).ToList();
            foreach (var job in queuedJobs)
            {
                conversionQueue.Remove(job);
                jobBindingList.Remove(job);
            }
            UpdateStatusBar();
            if (queuedJobs.Count > 0)
            {
                LogJobEvent(null, $"Cleared {queuedJobs.Count} queued job(s) at operator request.");
                PersistQueueSnapshot();
            }
        }

        private void RemoveSelectedJobs(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                var selectedJobs = dgv.SelectedRows.Cast<DataGridViewRow>()
                    .Select(r => r.DataBoundItem as ConversionJob)
                    .Where(j => j != null && j.Status != ConversionStatus.Converting)
                    .ToList();

                foreach (var job in selectedJobs)
                {
                    conversionQueue.Remove(job);
                    jobBindingList.Remove(job);
                }
                UpdateStatusBar();
                if (selectedJobs.Count > 0)
                {
                    LogJobEvent(null, $"Removed {selectedJobs.Count} job(s) from queue.");
                    PersistQueueSnapshot();
                }
            }
        }

        private void CancelSelectedJob(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                var job = dgv.SelectedRows[0].DataBoundItem as ConversionJob;
                if (job != null && job.Status == ConversionStatus.Converting)
                {
                    job.CancellationToken?.Cancel();
                    job.Status = ConversionStatus.Cancelled;
                    job.UpdateDisplay();
                    LogJobEvent(job, "Conversion cancelled by operator.");
                    PersistQueueSnapshot();
                }
            }
        }

        private void OpenOutputFolder(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                var job = dgv.SelectedRows[0].DataBoundItem as ConversionJob;
                if (job != null && File.Exists(job.OutputPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\"");
                }
                else if (job != null && Directory.Exists(job.OutputDirectory))
                {
                    Process.Start("explorer.exe", job.OutputDirectory);
                }
            }
        }

        private bool ValidateQueuedJobs()
        {
            var queuedJobs = conversionQueue.Where(j => j.Status == ConversionStatus.Queued).ToList();
            if (queuedJobs.Count == 0)
            {
                return false;
            }

            var missingSources = queuedJobs.Where(j => !File.Exists(j.InputPath)).ToList();
            foreach (var job in missingSources)
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = "Source file missing";
                job.UpdateDisplay();
                LogJobEvent(job, "Validation failed: source file missing.");
            }

            if (missingSources.Count > 0)
            {
                MessageBox.Show($"Skipped {missingSources.Count} job(s) because the source media was not found.", "Queue Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            PersistQueueSnapshot();
            UpdateStatusBar();

            return conversionQueue.Any(j => j.Status == ConversionStatus.Queued);
        }

        private void TryLaunchConversions()
        {
            if (!queueProcessingActive)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)TryLaunchConversions);
                return;
            }

            foreach (var job in conversionQueue.Where(j => j.Status == ConversionStatus.Queued && !j.RetryPending).ToList())
            {
                if (activeConversions >= maxConcurrentConversions)
                {
                    break;
                }

                StartConversion(job);
            }
        }

        #endregion

        #region Conversion

        private void BtnStartConversion_Click(object sender, EventArgs e)
        {
            if (!conversionQueue.Any(j => j.Status == ConversionStatus.Queued))
            {
                MessageBox.Show("No files in queue to convert!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ValidateQueuedJobs())
            {
                MessageBox.Show("No valid jobs are ready to convert after validation.", "Queue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            queueProcessingActive = true;
            SaveSettings();
            LogJobEvent(null, $"Queue processing started for {conversionQueue.Count(j => j.Status == ConversionStatus.Queued)} job(s).");
            TryLaunchConversions();
        }

        private async void StartConversion(ConversionJob job)
        {
            try
            {
                job.OutputPath = GenerateOutputPath(job);
            }
            catch (Exception ex)
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.UpdateDisplay();
                LogJobEvent(job, $"Output validation failed: {ex.Message}");
                PersistQueueSnapshot();
                TryLaunchConversions();
                return;
            }

            job.RetryPending = false;
            activeConversions++;
            UpdateStatusBar();

            job.Status = ConversionStatus.Converting;
            job.StartTime = DateTime.Now;
            job.CancellationToken = new CancellationTokenSource();
            job.UpdateDisplay();
            LogJobEvent(job, $"Starting conversion to {job.OutputFormat}{(job.UseGpuAcceleration ? $" with {job.HardwareEncoder}" : " (CPU)")}");
            PersistQueueSnapshot();

            try
            {
                await Task.Run(() => ConvertFile(job), job.CancellationToken.Token);

                if (!job.CancellationToken.IsCancellationRequested)
                {
                    job.Status = ConversionStatus.Completed;
                    job.Progress = 100;
                    job.RetryCount = 0;
                    LogJobEvent(job, $"Completed → {job.OutputPath}");
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = ConversionStatus.Cancelled;
                LogJobEvent(job, "Conversion cancelled mid-run.");
            }
            catch (Exception ex)
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = ex.Message;
                LogJobEvent(job, $"Conversion failed: {ex.Message}");
                if (job.RetryCount < MAX_RETRIES)
                {
                    ScheduleRetry(job);
                    return;
                }
                else
                {
                    LogJobEvent(job, $"Retries exhausted after {MAX_RETRIES + 1} attempts.");
                }
            }
            finally
            {
                activeConversions--;
                job.UpdateDisplay();
                UpdateStatusBar();
                TryTriggerAutoShutdown();
                job.CancellationToken?.Dispose();
                job.CancellationToken = null;
                PersistQueueSnapshot();
                TryLaunchConversions();
            }
        }

        private void ScheduleRetry(ConversionJob job)
        {
            job.RetryCount++;
            job.RetryPending = true;
            job.Status = ConversionStatus.Queued;
            job.Progress = 0;
            job.Speed = "0x";
            job.StartTime = null;
            job.TotalDuration = TimeSpan.Zero;
            job.UpdateDisplay();

            int totalAttempts = MAX_RETRIES + 1;
            int nextAttempt = job.RetryCount + 1;
            LogJobEvent(job, $"Retrying in {Math.Pow(2, job.RetryCount):0} second(s) (attempt {nextAttempt} of {totalAttempts}).");
            PersistQueueSnapshot();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, job.RetryCount)));
                BeginInvoke((MethodInvoker)(() =>
                {
                    job.RetryPending = false;
                    if (queueProcessingActive)
                    {
                        TryLaunchConversions();
                    }
                }));
            });
        }

        private void ConvertFile(ConversionJob job)
        {
            if (!ffmpegAvailable && !ResolveFfmpegPath())
            {
                throw new Exception("FFmpeg not found! Please ensure ffmpeg is in the application directory or system PATH.");
            }

            ffmpegAvailable = true;

            string arguments = BuildFFmpegArguments(job);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        ParseFFmpegProgress(job, e.Data);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    if (job.CancellationToken.IsCancellationRequested)
                    {
                        process.Kill();
                        throw new OperationCanceledException();
                    }
                    Thread.Sleep(100);
                }

                if (process.ExitCode != 0 && !job.CancellationToken.IsCancellationRequested)
                {
                    throw new Exception($"FFmpeg conversion failed with exit code {process.ExitCode}");
                }
            }
        }

        private void TryTriggerAutoShutdown()
        {
            if (!autoShutdownEnabled)
            {
                return;
            }

            if (activeConversions > 0 || conversionQueue.Any(j => j.Status == ConversionStatus.Queued || j.Status == ConversionStatus.Converting))
            {
                return;
            }

            if (!conversionQueue.Any(j => j.Status == ConversionStatus.Completed))
            {
                return;
            }

            lock (shutdownLock)
            {
                if (shutdownScheduled)
                {
                    return;
                }
                shutdownScheduled = true;
            }

            BeginInvoke((MethodInvoker)(() =>
            {
                LogJobEvent(null, "Prompting operator for auto-shutdown confirmation.");
                DialogResult result = MessageBox.Show(
                    "All conversions are complete. Do you want to shut down the system in 60 seconds?",
                    "Auto Shutdown",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    ScheduleSystemShutdown();
                    LogJobEvent(null, "Auto-shutdown approved by operator.");
                }
                else
                {
                    lock (shutdownLock)
                    {
                        shutdownScheduled = false;
                        autoShutdownEnabled = false;
                    }
                    if (chkAutoShutdownControl != null)
                    {
                        chkAutoShutdownControl.Checked = false;
                    }
                    LogJobEvent(null, "Auto-shutdown cancelled by operator.");
                }
            }));
        }

        private void ScheduleSystemShutdown()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    MessageBox.Show("Automatic shutdown is only supported on Windows systems.", "Auto Shutdown", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    lock (shutdownLock)
                    {
                        shutdownScheduled = false;
                        autoShutdownEnabled = false;
                    }
                    if (chkAutoShutdownControl != null)
                    {
                        chkAutoShutdownControl.Checked = false;
                    }
                    LogJobEvent(null, "Auto-shutdown aborted: unsupported platform.");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 60 /c \"Hills Video Converter finished all conversions.\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                LogJobEvent(null, "Auto-shutdown scheduled (60 second timer).");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to schedule system shutdown: {ex.Message}", "Auto Shutdown", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LogJobEvent(null, $"Auto-shutdown scheduling failed: {ex.Message}");
                lock (shutdownLock)
                {
                    shutdownScheduled = false;
                }
                if (chkAutoShutdownControl != null)
                {
                    chkAutoShutdownControl.Checked = false;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing &&
                (activeConversions > 0 || conversionQueue.Any(j => j.Status == ConversionStatus.Converting)))
            {
                DialogResult result = MessageBox.Show(
                    "There are conversions still running. Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            bool resumePreference = queueProcessingActive;
            SaveSettings();
            queueProcessingActive = false;
            if (currentSettings != null)
            {
                currentSettings.AutoResumeQueue = resumePreference;
            }
            PersistQueueSnapshot();
            LogJobEvent(null, "Application shutting down.");

            base.OnFormClosing(e);
        }

        private string BuildFFmpegArguments(ConversionJob job)
        {
            StringBuilder args = new StringBuilder();

            // Input file
            args.Append($"-i \"{job.InputPath}\" ");

            // Audio overlay
            if (!string.IsNullOrEmpty(job.AudioOverlayPath) && File.Exists(job.AudioOverlayPath))
            {
                args.Append($"-i \"{job.AudioOverlayPath}\" ");
                args.Append("-filter_complex \"[0:a][1:a]amix=inputs=2:duration=first\" ");
            }

            // Resolution scaling
            if (job.Resolution != "Original")
            {
                string resolution = job.Resolution.Split('(')[0].Trim();
                args.Append($"-vf scale={resolution} ");
            }

            // Quality/Bitrate settings
            if (!job.AudioOnly)
            {
                if (job.UseGpuAcceleration && !string.IsNullOrEmpty(job.HardwareEncoder))
                {
                    args.Append(hardwareAccelerationArgs);
                    args.Append($"-c:v {job.HardwareEncoder} ");
                    args.Append(GetGpuQualityArguments(job));
                }
                else
                {
                    args.Append(GetCpuQualityArguments(job));
                }
            }

            // Audio settings
            if (job.AudioOnly)
            {
                args.Append("-vn -acodec ");
                switch (job.OutputFormat.ToLower())
                {
                    case "mp3":
                        args.Append("libmp3lame "); break;
                    case "aac": args.Append("aac "); break;
                    case "wav": args.Append("pcm_s16le "); break;
                    case "flac": args.Append("flac "); break;
                    case "ogg": args.Append("libvorbis "); break;
                    default: args.Append("copy "); break;
                }
            }
            else if (job.MuteAudio)
            {
                args.Append("-an ");
            }
            else
            {
                args.Append("-c:a aac -b:a 192k ");
            }

            // Output format specific settings
            switch (job.OutputFormat.ToLower())
            {
                case "mp4":
                    args.Append("-f mp4 -movflags +faststart ");
                    break;
                case "avi":
                    args.Append("-f avi ");
                    break;
                case "mkv":
                    args.Append("-f matroska ");
                    break;
                case "webm":
                    args.Append("-c:v libvpx-vp9 -c:a libopus ");
                    break;
            }

            // Progress reporting and overwrite
            args.Append("-progress pipe:1 -y ");

            // Output file
            args.Append($"\"{job.OutputPath}\"");

            return args.ToString();
        }

        private string GetCpuQualityArguments(ConversionJob job)
        {
            if (job.Quality == "Custom Bitrate" && !string.IsNullOrEmpty(job.CustomBitrate))
            {
                return $"-c:v libx264 -preset medium -b:v {job.CustomBitrate}k ";
            }

            return job.Quality switch
            {
                "High (Original)" => "-c:v libx264 -preset slow -crf 18 ",
                "Medium (Balanced)" => "-c:v libx264 -preset medium -crf 23 ",
                "Low (Compressed)" => "-c:v libx264 -preset fast -crf 28 ",
                _ => "-c:v libx264 -preset medium -crf 23 "
            };
        }

        private string GetGpuQualityArguments(ConversionJob job)
        {
            StringBuilder builder = new StringBuilder();
            bool hasCustomBitrate = job.Quality == "Custom Bitrate" && !string.IsNullOrEmpty(job.CustomBitrate);

            if (job.HardwareEncoder?.Contains("nvenc") == true)
            {
                string preset = job.Quality switch
                {
                    "High (Original)" => "p4",
                    "Medium (Balanced)" => "p5",
                    "Low (Compressed)" => "p6",
                    "Custom Bitrate" => "p5",
                    _ => "p5"
                };

                builder.Append($"-preset {preset} -rc vbr ");
                if (hasCustomBitrate)
                {
                    builder.Append($"-b:v {job.CustomBitrate}k ");
                }
                else
                {
                    string cq = job.Quality switch
                    {
                        "High (Original)" => "18",
                        "Medium (Balanced)" => "23",
                        "Low (Compressed)" => "28",
                        _ => "21"
                    };
                    builder.Append($"-cq {cq} ");
                }
            }
            else if (job.HardwareEncoder?.Contains("qsv") == true)
            {
                string preset = job.Quality switch
                {
                    "High (Original)" => "balanced",
                    "Medium (Balanced)" => "balanced",
                    "Low (Compressed)" => "speed",
                    _ => "balanced"
                };
                builder.Append($"-preset {preset} ");
                if (hasCustomBitrate)
                {
                    builder.Append($"-b:v {job.CustomBitrate}k ");
                }
                else
                {
                    string quality = job.Quality switch
                    {
                        "High (Original)" => "18",
                        "Medium (Balanced)" => "23",
                        "Low (Compressed)" => "28",
                        _ => "23"
                    };
                    builder.Append($"-global_quality {quality} ");
                }
            }
            else if (job.HardwareEncoder?.Contains("amf") == true)
            {
                string quality = job.Quality switch
                {
                    "High (Original)" => "quality",
                    "Medium (Balanced)" => "balanced",
                    "Low (Compressed)" => "speed",
                    _ => "balanced"
                };
                builder.Append($"-quality {quality} ");
                if (hasCustomBitrate)
                {
                    builder.Append($"-b:v {job.CustomBitrate}k ");
                }
                else
                {
                    string qp = job.Quality switch
                    {
                        "High (Original)" => "18",
                        "Medium (Balanced)" => "23",
                        "Low (Compressed)" => "28",
                        _ => "23"
                    };
                    builder.Append($"-rc cqp -qp_i {qp} -qp_p {qp} -qp_b {qp} ");
                }
            }
            else if (hasCustomBitrate)
            {
                builder.Append($"-b:v {job.CustomBitrate}k ");
            }

            return builder.ToString();
        }

        private void ParseFFmpegProgress(ConversionJob job, string output)
        {
            try
            {
                // Parse time progress from FFmpeg output
                if (output.Contains("time="))
                {
                    string timeStr = output.Substring(output.IndexOf("time=") + 5).Split(' ')[0];
                    TimeSpan currentTime = ParseFFmpegTime(timeStr);

                    // Get total duration if not already set
                    if (job.TotalDuration == TimeSpan.Zero && output.Contains("Duration:"))
                    {
                        string durationStr = output.Substring(output.IndexOf("Duration:") + 10).Split(',')[0].Trim();
                        job.TotalDuration = ParseFFmpegTime(durationStr);
                    }

                    if (job.TotalDuration > TimeSpan.Zero)
                    {
                        job.Progress = (int)((currentTime.TotalSeconds / job.TotalDuration.TotalSeconds) * 100);
                        job.Progress = Math.Min(100, Math.Max(0, job.Progress));
                    }
                }

                // Parse speed
                if (output.Contains("speed="))
                {
                    string speedStr = output.Substring(output.IndexOf("speed=") + 6).Split('x')[0].Trim();
                    job.Speed = speedStr + "x";
                }

                // Parse bitrate
                if (output.Contains("bitrate="))
                {
                    string bitrateStr = output.Substring(output.IndexOf("bitrate=") + 8).Split(' ')[0].Trim();
                    // Could store this if needed
                }

                // Update duration from output
                if (output.Contains("Duration:") && job.TotalDuration == TimeSpan.Zero)
                {
                    try
                    {
                        string durationStr = output.Substring(output.IndexOf("Duration:") + 10, 11);
                        job.TotalDuration = ParseFFmpegTime(durationStr);
                    }
                    catch { }
                }

                this.Invoke((MethodInvoker)delegate { job.UpdateDisplay(); });
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        private TimeSpan ParseFFmpegTime(string timeStr)
        {
            if (string.IsNullOrWhiteSpace(timeStr))
            {
                return TimeSpan.Zero;
            }

            string[] parts = timeStr.Trim().Split(':');
            if (parts.Length != 3)
            {
                return TimeSpan.Zero;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours))
            {
                return TimeSpan.Zero;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            {
                return TimeSpan.Zero;
            }

            string secondsPart = parts[2].Replace(',', '.');
            if (!double.TryParse(secondsPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                return TimeSpan.Zero;
            }

            int wholeSeconds = (int)Math.Floor(seconds);
            int milliseconds = (int)Math.Round((seconds - wholeSeconds) * 1000);

            return new TimeSpan(hours, minutes, wholeSeconds).Add(TimeSpan.FromMilliseconds(milliseconds));
        }

        #endregion

        #region Helper Methods

        private bool IsVideoOrAudioFile(string file)
        {
            string[] extensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
                                   ".mpg", ".mpeg", ".3gp", ".mp3", ".wav", ".aac", ".flac", ".ogg", ".wma" };
            return extensions.Contains(Path.GetExtension(file).ToLower());
        }

        private void CheckFFmpegAvailability()
        {
            ffmpegAvailable = ResolveFfmpegPath();

            if (!ffmpegAvailable)
            {
                MessageBox.Show(
                    "FFmpeg not found!\n\n" +
                    "This application requires FFmpeg to function.\n\n" +
                    "Please download FFmpeg from https://ffmpeg.org/download.html\n" +
                    "and place ffmpeg.exe in the application directory or add it to your system PATH.",
                    "FFmpeg Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                availableHardwareEncoders.Clear();
                preferredHardwareEncoder = null;
                hardwareAccelerationArgs = string.Empty;
                UpdateGpuToggleAvailability();
                return;
            }

            DetectHardwareEncoders();
            UpdateGpuToggleAvailability();
            ConfigureAudioOnlyMode(chkAudioOnlyControl?.Checked ?? false);
        }

        private bool ResolveFfmpegPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            List<string> candidates = new List<string>
            {
                Path.Combine(baseDirectory, "ffmpeg.exe"),
                Path.Combine(baseDirectory, "ffmpeg"),
                "ffmpeg",
                "ffmpeg.exe"
            };

            foreach (string candidate in candidates.Distinct())
            {
                if (TryRunFfmpegCommand(candidate, "-version", out _))
                {
                    ffmpegPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryRunFfmpegCommand(string fileName, string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    string stdOut = process.StandardOutput.ReadToEnd();
                    string stdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        output = string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut + Environment.NewLine + stdErr;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore resolution errors
            }

            return false;
        }

        private void DetectHardwareEncoders()
        {
            availableHardwareEncoders.Clear();
            preferredHardwareEncoder = null;
            hardwareAccelerationArgs = string.Empty;

            if (!ffmpegAvailable)
            {
                return;
            }

            if (!TryRunFfmpegCommand(ffmpegPath, "-hide_banner -encoders", out string output))
            {
                return;
            }

            string[] preferredOrder = { "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_amf", "hevc_amf" };
            foreach (string encoder in preferredOrder)
            {
                if (output.Contains(encoder))
                {
                    availableHardwareEncoders.Add(encoder);
                }
            }

            preferredHardwareEncoder = availableHardwareEncoders.FirstOrDefault();
            hardwareAccelerationArgs = ResolveHardwareAccelerationArgs(preferredHardwareEncoder);
        }

        private string ResolveHardwareAccelerationArgs(string encoder)
        {
            if (string.IsNullOrEmpty(encoder))
            {
                return string.Empty;
            }

            if (encoder.Contains("nvenc"))
            {
                return "-hwaccel cuda ";
            }

            if (encoder.Contains("qsv"))
            {
                return "-hwaccel qsv ";
            }

            if (encoder.Contains("amf"))
            {
                return "-hwaccel d3d11va ";
            }

            return string.Empty;
        }

        private void UpdateGpuToggleAvailability()
        {
            if (chkGpuAccelerationControl == null)
            {
                return;
            }

            chkGpuAccelerationControl.CheckedChanged -= ChkGpuAccelerationControl_CheckedChanged;

            bool hasHardware = availableHardwareEncoders.Any();
            chkGpuAccelerationControl.Enabled = hasHardware;
            bool desiredState = hasHardware && (preferredGpuToggleState ?? currentSettings?.UseGpu ?? false);
            chkGpuAccelerationControl.Checked = desiredState;
            chkGpuAccelerationControl.ForeColor = hasHardware
                ? Color.FromArgb(140, 220, 255)
                : Color.FromArgb(120, 130, 150);
            if (!hasHardware)
            {
                chkGpuAccelerationControl.Text = "GPU Hyperdrive (Unavailable)";
            }
            else
            {
                chkGpuAccelerationControl.Text = "GPU Hyperdrive";
            }

            chkGpuAccelerationControl.CheckedChanged += ChkGpuAccelerationControl_CheckedChanged;
        }

        private void UpdateStatusBar()
        {
            if (IsDisposed)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                if (!statusBarUpdatePendingHandle)
                {
                    HandleCreated += Form1_HandleCreatedForStatusBar;
                    statusBarUpdatePendingHandle = true;
                }

                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)UpdateStatusBar);
                return;
            }

            Label lblStatus = Controls.Find("lblStatus", true).FirstOrDefault() as Label;
            if (lblStatus != null)
            {
                int queuedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Queued);
                int completedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Completed);
                int failedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Failed);

                lblStatus.Text = $"Ready | Queue: {queuedCount} | Active: {activeConversions}/{maxConcurrentConversions} | " +
                                 $"Completed: {completedCount} | Failed: {failedCount}";
            }
        }

        private void Form1_HandleCreatedForStatusBar(object sender, EventArgs e)
        {
            HandleCreated -= Form1_HandleCreatedForStatusBar;
            statusBarUpdatePendingHandle = false;
            UpdateStatusBar();
        }

        #endregion
    }

    public class AppSettings
    {
        public string SelectedFormat { get; set; } = "MP4";
        public string SelectedResolution { get; set; } = "Original";
        public string SelectedQuality { get; set; } = "High (Original)";
        public string SelectedPreset { get; set; } = "Cinematic HDR";
        public string CustomBitrate { get; set; } = "5000";
        public bool MuteAudio { get; set; }
        public bool AudioOnly { get; set; }
        public bool UseGpu { get; set; }
        public bool AutoShutdown { get; set; }
        public string OutputPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        public int MaxParallelConversions { get; set; } = 3;
        public bool AutoResumeQueue { get; set; }
    }

    public class ConversionJobSnapshot
    {
        public string InputPath { get; set; }
        public string OutputFormat { get; set; }
        public string Resolution { get; set; }
        public string Quality { get; set; }
        public string CustomBitrate { get; set; }
        public bool MuteAudio { get; set; }
        public bool AudioOnly { get; set; }
        public string AudioOverlayPath { get; set; }
        public string OutputDirectory { get; set; }
        public bool UseGpuAcceleration { get; set; }
        public string HardwareEncoder { get; set; }
        public int RetryCount { get; set; }

        public static ConversionJobSnapshot FromJob(ConversionJob job)
        {
            return new ConversionJobSnapshot
            {
                InputPath = job.InputPath,
                OutputFormat = job.OutputFormat,
                Resolution = job.Resolution,
                Quality = job.Quality,
                CustomBitrate = job.CustomBitrate,
                MuteAudio = job.MuteAudio,
                AudioOnly = job.AudioOnly,
                AudioOverlayPath = job.AudioOverlayPath,
                OutputDirectory = job.OutputDirectory,
                UseGpuAcceleration = job.UseGpuAcceleration,
                HardwareEncoder = job.HardwareEncoder,
                RetryCount = job.RetryCount
            };
        }
    }

    #region ConversionJob Class

    public class ConversionJob : INotifyPropertyChanged
    {
        private ConversionStatus _status;
        private int _progress;
        private string _speed = "0x";

        public string InputPath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string OutputFormat { get; set; }
        public string Resolution { get; set; }
        public string Quality { get; set; }
        public string CustomBitrate { get; set; }
        public bool MuteAudio { get; set; }
        public bool AudioOnly { get; set; }
        public string AudioOverlayPath { get; set; }
        public string OutputDirectory { get; set; }
        public string OutputPath { get; set; }
        public bool UseGpuAcceleration { get; set; }
        public string HardwareEncoder { get; set; }
        public DateTime? StartTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
        public string ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public bool RetryPending { get; set; }

        public ConversionStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnPropertyChanged(nameof(Speed));
            }
        }

        // Display Properties
        public string FileSizeFormatted => FormatFileSize(FileSize);

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ConversionStatus.Queued: return "⏳ Queued";
                    case ConversionStatus.Converting: return "🔄 Converting";
                    case ConversionStatus.Completed: return "✅ Completed";
                    case ConversionStatus.Failed: return "❌ Failed";
                    case ConversionStatus.Cancelled: return "⛔ Cancelled";
                    default: return "Unknown";
                }
            }
        }

        public string ProgressText => Status == ConversionStatus.Converting ? $"{Progress}% [{new string('█', Progress / 5)}{new string('░', 20 - Progress / 5)}]" :
                                      Status == ConversionStatus.Completed ? "100% [████████████████████]" :
                                      "-";

        public string ElapsedTime
        {
            get
            {
                if (StartTime.HasValue && Status == ConversionStatus.Converting)
                {
                    var elapsed = DateTime.Now - StartTime.Value;
                    return $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                }
                return "-";
            }
        }

        public string ETA
        {
            get
            {
                if (StartTime.HasValue && Status == ConversionStatus.Converting && Progress > 0)
                {
                    var elapsed = DateTime.Now - StartTime.Value;
                    var totalEstimated = TimeSpan.FromSeconds(elapsed.TotalSeconds / Progress * 100);
                    var remaining = totalEstimated - elapsed;

                    if (remaining.TotalSeconds > 0)
                    {
                        return $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
                    }
                }
                return "-";
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public void UpdateDisplay()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ElapsedTime));
            OnPropertyChanged(nameof(ETA));
            OnPropertyChanged(nameof(Speed));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum ConversionStatus
    {
        Queued,
        Converting,
        Completed,
        Failed,
        Cancelled
    }

    #endregion
}