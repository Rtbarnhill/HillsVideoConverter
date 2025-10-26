using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
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
            this.DragEnter += Form1_DragEnter;
            this.DragDrop += Form1_DragDrop;
            this.BackColor = Color.FromArgb(30, 30, 30);

            jobBindingList = new BindingList<ConversionJob>();

            CreateUI();
            CheckFFmpegAvailability();
        }

        private void CreateUI()
        {
            // Main Container Panel
            Panel mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(30, 30, 30)
            };
            this.Controls.Add(mainPanel);

            // Title Label
            Label titleLabel = new Label
            {
                Text = "🎬 Hills Video Converter Pro",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 150, 255),
                AutoSize = true,
                Location = new Point(20, 10)
            };
            mainPanel.Controls.Add(titleLabel);

            // Subtitle
            Label subtitleLabel = new Label
            {
                Text = "Advanced Multi-Format Video Conversion Engine | Supports Files Over 25GB",
                Font = new Font("Segoe UI", 10, FontStyle.Italic),
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = true,
                Location = new Point(25, 50)
            };
            mainPanel.Controls.Add(subtitleLabel);

            // Top Control Panel
            Panel controlPanel = new Panel
            {
                Location = new Point(20, 85),
                Size = new Size(1340, 180),
                BackColor = Color.FromArgb(45, 45, 48),
                BorderStyle = BorderStyle.FixedSingle
            };
            mainPanel.Controls.Add(controlPanel);

            // Add Input Files Button
            Button btnAddFiles = CreateStyledButton("📁 Add Files", new Point(15, 15), new Size(180, 45));
            btnAddFiles.Click += BtnAddFiles_Click;
            controlPanel.Controls.Add(btnAddFiles);

            // Add Folder Button
            Button btnAddFolder = CreateStyledButton("📂 Add Folder", new Point(205, 15), new Size(180, 45));
            btnAddFolder.Click += BtnAddFolder_Click;
            controlPanel.Controls.Add(btnAddFolder);

            // Clear Queue Button
            Button btnClearQueue = CreateStyledButton("🗑️ Clear Queue", new Point(395, 15), new Size(180, 45));
            btnClearQueue.Click += (s, e) => ClearQueue();
            controlPanel.Controls.Add(btnClearQueue);

            // Drag and Drop Label
            Label lblDragDrop = new Label
            {
                Text = "⬇️ DRAG & DROP FILES HERE",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 200, 100),
                Location = new Point(600, 20),
                Size = new Size(400, 35),
                TextAlign = ContentAlignment.MiddleCenter
            };
            controlPanel.Controls.Add(lblDragDrop);

            // Output Format
            Label lblFormat = CreateStyledLabel("Output Format:", new Point(15, 75));
            controlPanel.Controls.Add(lblFormat);

            ComboBox cmbFormat = new ComboBox
            {
                Name = "cmbFormat",
                Location = new Point(150, 72),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            cmbFormat.Items.AddRange(new string[] { "MP4", "AVI", "MKV", "MOV", "WMV", "FLV", "WEBM", "MP3", "AAC", "WAV", "FLAC", "OGG" });
            cmbFormat.SelectedIndex = 0;
            controlPanel.Controls.Add(cmbFormat);

            // Resolution
            Label lblResolution = CreateStyledLabel("Resolution:", new Point(320, 75));
            controlPanel.Controls.Add(lblResolution);

            ComboBox cmbResolution = new ComboBox
            {
                Name = "cmbResolution",
                Location = new Point(420, 72),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            cmbResolution.Items.AddRange(new string[] { "Original", "3840x2160 (4K)", "2560x1440 (2K)", "1920x1080 (1080p)", "1280x720 (720p)", "854x480 (480p)", "640x360 (360p)" });
            cmbResolution.SelectedIndex = 0;
            controlPanel.Controls.Add(cmbResolution);

            // Quality/Bitrate
            Label lblQuality = CreateStyledLabel("Quality:", new Point(590, 75));
            controlPanel.Controls.Add(lblQuality);

            ComboBox cmbQuality = new ComboBox
            {
                Name = "cmbQuality",
                Location = new Point(670, 72),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            cmbQuality.Items.AddRange(new string[] { "High (Original)", "Medium (Balanced)", "Low (Compressed)", "Custom Bitrate" });
            cmbQuality.SelectedIndex = 0;
            controlPanel.Controls.Add(cmbQuality);

            // Custom Bitrate
            Label lblBitrate = CreateStyledLabel("Bitrate (kbps):", new Point(840, 75));
            lblBitrate.Name = "lblBitrate";
            lblBitrate.Visible = false;
            controlPanel.Controls.Add(lblBitrate);

            TextBox txtBitrate = new TextBox
            {
                Name = "txtBitrate",
                Location = new Point(960, 72),
                Size = new Size(100, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                Text = "5000",
                Visible = false
            };
            controlPanel.Controls.Add(txtBitrate);

            cmbQuality.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = cmbQuality.SelectedIndex == 3;
                lblBitrate.Visible = isCustom;
                txtBitrate.Visible = isCustom;
            };

            // Audio Options
            Label lblAudio = CreateStyledLabel("Audio:", new Point(15, 115));
            controlPanel.Controls.Add(lblAudio);

            CheckBox chkMuteAudio = new CheckBox
            {
                Name = "chkMuteAudio",
                Text = "Mute Audio",
                Location = new Point(150, 115),
                Size = new Size(120, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
            controlPanel.Controls.Add(chkMuteAudio);

            CheckBox chkAudioOnly = new CheckBox
            {
                Name = "chkAudioOnly",
                Text = "Audio Only (Extract)",
                Location = new Point(280, 115),
                Size = new Size(160, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
            controlPanel.Controls.Add(chkAudioOnly);

            // Audio Overlay
            Button btnAudioOverlay = CreateStyledButton("🎵 Add Audio Overlay", new Point(460, 110), new Size(160, 30));
            btnAudioOverlay.Click += BtnAudioOverlay_Click;
            controlPanel.Controls.Add(btnAudioOverlay);

            Label lblAudioFile = new Label
            {
                Name = "lblAudioFile",
                Text = "No audio overlay selected",
                Location = new Point(630, 115),
                Size = new Size(400, 25),
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 8, FontStyle.Italic)
            };
            controlPanel.Controls.Add(lblAudioFile);

            // Output Destination
            Label lblOutput = CreateStyledLabel("Output Folder:", new Point(15, 150));
            controlPanel.Controls.Add(lblOutput);

            TextBox txtOutputPath = new TextBox
            {
                Name = "txtOutputPath",
                Location = new Point(150, 147),
                Size = new Size(800, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            controlPanel.Controls.Add(txtOutputPath);

            Button btnBrowseOutput = CreateStyledButton("Browse...", new Point(960, 145), new Size(100, 30));
            btnBrowseOutput.Click += (s, e) => BrowseOutputFolder(txtOutputPath);
            controlPanel.Controls.Add(btnBrowseOutput);

            // Start Conversion Button
            Button btnStartConversion = CreateStyledButton("🚀 START CONVERSION", new Point(1080, 110), new Size(240, 50));
            btnStartConversion.BackColor = Color.FromArgb(0, 180, 0);
            btnStartConversion.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            btnStartConversion.Click += BtnStartConversion_Click;
            controlPanel.Controls.Add(btnStartConversion);

            // Job Queue DataGridView
            DataGridView dgvJobs = new DataGridView
            {
                Name = "dgvJobs",
                Location = new Point(20, 275),
                Size = new Size(1340, 520),
                BackgroundColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(70, 70, 70),
                BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                AllowDrop = true
            };

            dgvJobs.DragEnter += Form1_DragEnter;
            dgvJobs.DragDrop += Form1_DragDrop;

            dgvJobs.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(60, 60, 60);
            dgvJobs.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvJobs.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvJobs.ColumnHeadersHeight = 35;

            dgvJobs.DefaultCellStyle.BackColor = Color.FromArgb(45, 45, 48);
            dgvJobs.DefaultCellStyle.ForeColor = Color.White;
            dgvJobs.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            dgvJobs.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvJobs.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            dgvJobs.RowTemplate.Height = 30;

            // Define Columns
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FileName", HeaderText = "File Name", Width = 250 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FileSizeFormatted", HeaderText = "Size", Width = 100 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OutputFormat", HeaderText = "Format", Width = 80 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Resolution", HeaderText = "Resolution", Width = 120 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "StatusText", HeaderText = "Status", Width = 120 });

            DataGridViewTextBoxColumn progressCol = new DataGridViewTextBoxColumn
            {
                DataPropertyName = "ProgressText",
                HeaderText = "Progress",
                Width = 150
            };
            dgvJobs.Columns.Add(progressCol);

            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ElapsedTime", HeaderText = "Time", Width = 90 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Speed", Width = 100 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ETA", HeaderText = "ETA", Width = 90 });
            dgvJobs.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OutputPath", HeaderText = "Output Path", Width = 240 });

            dgvJobs.DataSource = jobBindingList;
            mainPanel.Controls.Add(dgvJobs);

            // Status Bar
            Panel statusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = Color.FromArgb(60, 60, 60)
            };
            mainPanel.Controls.Add(statusBar);

            Label lblStatus = new Label
            {
                Name = "lblStatus",
                Text = "Ready | Queue: 0 files | Active Conversions: 0/3",
                Location = new Point(10, 8),
                Size = new Size(800, 20),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
            statusBar.Controls.Add(lblStatus);

            // Context Menu for Jobs
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Remove Selected", null, (s, e) => RemoveSelectedJobs(dgvJobs));
            contextMenu.Items.Add("Open Output Folder", null, (s, e) => OpenOutputFolder(dgvJobs));
            contextMenu.Items.Add("Cancel Conversion", null, (s, e) => CancelSelectedJob(dgvJobs));
            dgvJobs.ContextMenuStrip = contextMenu;
        }

        private Button CreateStyledButton(string text, Point location, Size size)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private Label CreateStyledLabel(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(130, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
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