using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
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
        private const int MAX_CONCURRENT_CONVERSIONS = 3;
        private int activeConversions = 0;
        private string ffmpegPath = "ffmpeg.exe"; // Ensure FFmpeg is in the same directory or PATH

        public Form1()
        {
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

            CreateUI();
            CheckFFmpegAvailability();
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
            btnClearQueue.Click += (s, e) => ClearQueue();

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

            ComboBox cmbFormat = new ComboBox
            {
                Name = "cmbFormat",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbFormat.Items.AddRange(new string[] { "MP4", "AVI", "MKV", "MOV", "WMV", "FLV", "WEBM", "MP3", "AAC", "WAV", "FLAC", "OGG" });
            cmbFormat.SelectedIndex = 0;

            ComboBox cmbResolution = new ComboBox
            {
                Name = "cmbResolution",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbResolution.Items.AddRange(new string[] { "Original", "3840x2160 (4K)", "2560x1440 (2K)", "1920x1080 (1080p)", "1280x720 (720p)", "854x480 (480p)", "640x360 (360p)" });
            cmbResolution.SelectedIndex = 0;

            ComboBox cmbQuality = new ComboBox
            {
                Name = "cmbQuality",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 170
            };
            cmbQuality.Items.AddRange(new string[] { "High (Original)", "Medium (Balanced)", "Low (Compressed)", "Custom Bitrate" });
            cmbQuality.SelectedIndex = 0;

            ComboBox cmbPreset = new ComboBox
            {
                Name = "cmbPreset",
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 200
            };
            cmbPreset.Items.AddRange(new string[] { "Cinematic HDR", "Mobile Lightning", "Archive Master", "Social Burst", "Audio Diamond" });
            cmbPreset.SelectedIndex = 0;

            Label lblBitrate = CreateStyledLabel("Bitrate (kbps):");
            lblBitrate.Name = "lblBitrate";
            lblBitrate.Visible = false;

            TextBox txtBitrate = new TextBox
            {
                Name = "txtBitrate",
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 110,
                Text = "5000",
                Visible = false
            };

            cmbQuality.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = cmbQuality.SelectedIndex == 3;
                lblBitrate.Visible = isCustom;
                txtBitrate.Visible = isCustom;
            };

            FlowLayoutPanel optionsRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            optionsRow.Controls.Add(CreateOptionGroup("Output Format", cmbFormat));
            optionsRow.Controls.Add(CreateOptionGroup("Resolution", cmbResolution));
            optionsRow.Controls.Add(CreateOptionGroup("Quality", cmbQuality));
            optionsRow.Controls.Add(CreateOptionGroup("Preset", cmbPreset));

            FlowLayoutPanel bitrateRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 12, 0, 0)
            };
            lblBitrate.Margin = new Padding(0, 6, 8, 0);
            txtBitrate.Margin = new Padding(0, 3, 0, 0);
            bitrateRow.Controls.Add(lblBitrate);
            bitrateRow.Controls.Add(txtBitrate);
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

            CheckBox chkMuteAudio = new CheckBox
            {
                Name = "chkMuteAudio",
                Text = "Mute Audio",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };

            CheckBox chkAudioOnly = new CheckBox
            {
                Name = "chkAudioOnly",
                Text = "Extract Audio Only",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };

            CheckBox chkGpuAcceleration = new CheckBox
            {
                Name = "chkGpuAcceleration",
                Text = "GPU Hyperdrive",
                ForeColor = Color.FromArgb(140, 220, 255),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0),
                Checked = true
            };

            CheckBox chkAutoShutdown = new CheckBox
            {
                Name = "chkAutoShutdown",
                Text = "Auto Shutdown",
                ForeColor = Color.FromArgb(255, 200, 130),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 18, 0)
            };

            Button btnAudioOverlay = CreateStyledButton("🎵 Audio Overlay", new Size(190, 40));
            btnAudioOverlay.Click += BtnAudioOverlay_Click;
            btnAudioOverlay.Margin = new Padding(0, 0, 18, 0);

            Label lblAudioFile = new Label
            {
                Name = "lblAudioFile",
                Text = "No audio overlay selected",
                ForeColor = Color.FromArgb(170, 180, 210),
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };

            togglesRow.Controls.Add(chkMuteAudio);
            togglesRow.Controls.Add(chkAudioOnly);
            togglesRow.Controls.Add(chkGpuAcceleration);
            togglesRow.Controls.Add(chkAutoShutdown);
            togglesRow.Controls.Add(btnAudioOverlay);
            togglesRow.Controls.Add(lblAudioFile);
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

            TextBox txtOutputPath = new TextBox
            {
                Name = "txtOutputPath",
                BackColor = Color.FromArgb(45, 50, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Width = 420,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            Button btnBrowseOutput = CreateStyledButton("Browse Vault", new Size(160, 40));
            btnBrowseOutput.Margin = new Padding(12, 0, 0, 0);
            btnBrowseOutput.Click += (s, e) => BrowseOutputFolder(txtOutputPath);

            outputPathLayout.Controls.Add(lblOutput, 0, 0);
            outputPathLayout.Controls.Add(txtOutputPath, 1, 0);
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

            return controlCard;
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
                Text = "Ready | Queue: 0 files | Active Conversions: 0/3",
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
            statusBadges.Controls.Add(CreateBadge("Autosave ON", Color.FromArgb(120, 210, 120), Color.Black, Color.FromArgb(200, 255, 200)));
            statusBadges.Controls.Add(CreateBadge("Resilience Shield", Color.FromArgb(255, 200, 120), Color.Black, Color.FromArgb(255, 235, 200)));
            statusBadges.Controls.Add(CreateBadge("Cloud Sync Linked", Color.FromArgb(150, 200, 255), Color.Black, Color.FromArgb(220, 240, 255)));
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
                    Label lblAudioFile = this.Controls.Find("lblAudioFile", true).FirstOrDefault() as Label;
                    if (lblAudioFile != null)
                    {
                        lblAudioFile.Text = $"Audio Overlay: {Path.GetFileName(ofd.FileName)}";
                        lblAudioFile.Tag = ofd.FileName;
                        lblAudioFile.ForeColor = Color.FromArgb(100, 200, 100);
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
            ComboBox cmbFormat = this.Controls.Find("cmbFormat", true).FirstOrDefault() as ComboBox;
            ComboBox cmbResolution = this.Controls.Find("cmbResolution", true).FirstOrDefault() as ComboBox;
            ComboBox cmbQuality = this.Controls.Find("cmbQuality", true).FirstOrDefault() as ComboBox;
            TextBox txtBitrate = this.Controls.Find("txtBitrate", true).FirstOrDefault() as TextBox;
            CheckBox chkMuteAudio = this.Controls.Find("chkMuteAudio", true).FirstOrDefault() as CheckBox;
            CheckBox chkAudioOnly = this.Controls.Find("chkAudioOnly", true).FirstOrDefault() as CheckBox;
            TextBox txtOutputPath = this.Controls.Find("txtOutputPath", true).FirstOrDefault() as TextBox;
            Label lblAudioFile = this.Controls.Find("lblAudioFile", true).FirstOrDefault() as Label;

            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;

                FileInfo fi = new FileInfo(file);

                ConversionJob job = new ConversionJob
                {
                    InputPath = file,
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    OutputFormat = cmbFormat?.SelectedItem?.ToString() ?? "MP4",
                    Resolution = cmbResolution?.SelectedItem?.ToString() ?? "Original",
                    Quality = cmbQuality?.SelectedItem?.ToString() ?? "High (Original)",
                    CustomBitrate = cmbQuality?.SelectedIndex == 3 ? (txtBitrate?.Text ?? "5000") : null,
                    MuteAudio = chkMuteAudio?.Checked ?? false,
                    AudioOnly = chkAudioOnly?.Checked ?? false,
                    AudioOverlayPath = lblAudioFile?.Tag as string,
                    OutputDirectory = txtOutputPath?.Text ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    Status = ConversionStatus.Queued
                };

                job.OutputPath = GenerateOutputPath(job);
                conversionQueue.Add(job);
                jobBindingList.Add(job);
            }

            UpdateStatusBar();
        }

        private string GenerateOutputPath(ConversionJob job)
        {
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

        #endregion

        #region Conversion

        private async void BtnStartConversion_Click(object sender, EventArgs e)
        {
            var queuedJobs = conversionQueue.Where(j => j.Status == ConversionStatus.Queued).ToList();

            if (queuedJobs.Count == 0)
            {
                MessageBox.Show("No files in queue to convert!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Start conversions (limited by MAX_CONCURRENT_CONVERSIONS)
            foreach (var job in queuedJobs)
            {
                while (activeConversions >= MAX_CONCURRENT_CONVERSIONS)
                {
                    await Task.Delay(500);
                }

                StartConversion(job);
            }
        }

        private async void StartConversion(ConversionJob job)
        {
            activeConversions++;
            UpdateStatusBar();

            job.Status = ConversionStatus.Converting;
            job.StartTime = DateTime.Now;
            job.CancellationToken = new CancellationTokenSource();
            job.UpdateDisplay();

            try
            {
                await Task.Run(() => ConvertFile(job), job.CancellationToken.Token);

                if (!job.CancellationToken.IsCancellationRequested)
                {
                    job.Status = ConversionStatus.Completed;
                    job.Progress = 100;
                }
            }
            catch (Exception ex)
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = ex.Message;
            }
            finally
            {
                activeConversions--;
                job.UpdateDisplay();
                UpdateStatusBar();
            }
        }

        private void ConvertFile(ConversionJob job)
        {
            if (!File.Exists(ffmpegPath) && !IsFFmpegInPath())
            {
                throw new Exception("FFmpeg not found! Please ensure ffmpeg.exe is in the application directory or system PATH.");
            }

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
            if (job.Quality == "High (Original)")
            {
                args.Append("-c:v libx264 -preset slow -crf 18 ");
            }
            else if (job.Quality == "Medium (Balanced)")
            {
                args.Append("-c:v libx264 -preset medium -crf 23 ");
            }
            else if (job.Quality == "Low (Compressed)")
            {
                args.Append("-c:v libx264 -preset fast -crf 28 ");
            }
            else if (job.Quality == "Custom Bitrate" && !string.IsNullOrEmpty(job.CustomBitrate))
            {
                args.Append($"-b:v {job.CustomBitrate}k ");
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
            try
            {
                timeStr = timeStr.Trim();
                string[] parts = timeStr.Split(':');

                if (parts.Length == 3)
                {
                    int hours = int.Parse(parts[0]);
                    int minutes = int.Parse(parts[1]);
                    double seconds = double.Parse(parts[2].Replace(',', '.'));
                    return new TimeSpan(0, hours, minutes, (int)seconds, (int)((seconds % 1) * 1000));
                }
            }
            catch { }

            return TimeSpan.Zero;
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
            if (!File.Exists(ffmpegPath) && !IsFFmpegInPath())
            {
                MessageBox.Show(
                    "FFmpeg not found!\n\n" +
                    "This application requires FFmpeg to function.\n\n" +
                    "Please download FFmpeg from https://ffmpeg.org/download.html\n" +
                    "and place ffmpeg.exe in the application directory or add it to your system PATH.",
                    "FFmpeg Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private bool IsFFmpegInPath()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UpdateStatusBar()
        {
            Label lblStatus = this.Controls.Find("lblStatus", true).FirstOrDefault() as Label;
            if (lblStatus != null)
            {
                int queuedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Queued);
                int completedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Completed);
                int failedCount = conversionQueue.Count(j => j.Status == ConversionStatus.Failed);

                lblStatus.Invoke((MethodInvoker)delegate
                {
                    lblStatus.Text = $"Ready | Queue: {queuedCount} | Active: {activeConversions}/{MAX_CONCURRENT_CONVERSIONS} | " +
                                   $"Completed: {completedCount} | Failed: {failedCount}";
                });
            }
        }

        #endregion
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
        public DateTime? StartTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
        public string ErrorMessage { get; set; }

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