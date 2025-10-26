using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace VideoConverter
{
    public partial class Form1 : Form
    {
        private readonly List<ConversionJob> conversionQueue = new();
        private readonly BindingList<ConversionJob> jobBindingList = new();
        private const int MAX_CONCURRENT_CONVERSIONS = 3;
        private int activeConversions = 0;

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

            CreateUI();
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
            cmbFormat.Items.AddRange(new string[] { "MP4", "WMV", "AVI", "WEBM", "MP3", "M4A", "WMA", "WAV" });
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

        private void Form1_Load(object? sender, EventArgs e)
        {
            UpdateStatusBar();
        }

        #region File Selection

        private void BtnAddFiles_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Video/Audio Files";
                ofd.Filter = "All Supported Files|*.mp4;*.wmv;*.avi;*.webm;*.mp3;*.m4a;*.wma;*.wav|" +
                             "Video Files|*.mp4;*.wmv;*.avi;*.webm|" +
                             "Audio Files|*.mp3;*.m4a;*.wma;*.wav|" +
                             "All Files|*.*";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    AddFilesToQueue(ofd.FileNames);
                }
            }
        }

        private void BtnAddFolder_Click(object? sender, EventArgs e)
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

        private void Form1_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void Form1_DragDrop(object? sender, DragEventArgs e)
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

            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;

                FileInfo fi = new FileInfo(file);
                string selectedFormat = cmbFormat?.SelectedItem?.ToString() ?? "MP4";
                bool isAudioFormat = new[] { "MP3", "M4A", "WMA", "WAV" }.Contains(selectedFormat.ToUpperInvariant());

                ConversionJob job = new ConversionJob
                {
                    InputPath = file,
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    OutputFormat = selectedFormat,
                    Resolution = cmbResolution?.SelectedItem?.ToString() ?? "Original",
                    Quality = cmbQuality?.SelectedItem?.ToString() ?? "High (Original)",
                    CustomBitrate = cmbQuality?.SelectedIndex == 3 ? (txtBitrate?.Text ?? "5000") : null,
                    MuteAudio = !isAudioFormat && (chkMuteAudio?.Checked ?? false),
                    AudioOnly = (chkAudioOnly?.Checked ?? false) || isAudioFormat,
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

        private async void BtnStartConversion_Click(object? sender, EventArgs e)
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
            job.ErrorMessage = null;
            job.TotalDuration = TimeSpan.Zero;
            job.UpdateDisplay();

            try
            {
                await ConvertFileAsync(job);

                if (job.CancellationToken?.IsCancellationRequested == true)
                {
                    job.Status = ConversionStatus.Cancelled;
                }
                else
                {
                    job.Status = ConversionStatus.Completed;
                    job.Progress = 100;
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = ConversionStatus.Cancelled;
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


        private async Task ConvertFileAsync(ConversionJob job)
        {
            if (job.CancellationToken is null)
            {
                throw new InvalidOperationException("Cancellation token source was not initialised.");
            }

            Directory.CreateDirectory(job.OutputDirectory);

            UpdateJobSafely(job, () =>
            {
                job.Progress = 0;
                job.Speed = "0 MB/s";
            });

            MediaEncodingProfile profile = CreateEncodingProfile(job);

            if (job.MuteAudio && profile.Audio != null && !job.AudioOnly)
            {
                profile.Audio = null;
            }

            StorageFile inputFile = await StorageFile.GetFileFromPathAsync(job.InputPath);
            StorageFolder outputFolder = await StorageFolder.GetFolderFromPathAsync(job.OutputDirectory);
            StorageFile outputFile = await outputFolder.CreateFileAsync(Path.GetFileName(job.OutputPath), CreationCollisionOption.ReplaceExisting);

            UpdateJobSafely(job, () => job.OutputPath = outputFile.Path);

            await PopulateJobDurationAsync(job, inputFile);

            var transcoder = new MediaTranscoder
            {
                HardwareAccelerationEnabled = true,
                AlwaysReencode = true
            };

            var prepareResult = await transcoder.PrepareFileTranscodeAsync(inputFile, outputFile, profile);

            if (!prepareResult.CanTranscode)
            {
                throw new InvalidOperationException($"Unable to transcode file. Reason: {prepareResult.FailureReason}");
            }

            var stopwatch = Stopwatch.StartNew();
            var transcodeOperation = prepareResult.TranscodeAsync();

            transcodeOperation.Progress = new AsyncActionProgressHandler<double>((_, progress) =>
            {
                int progressValue = (int)Math.Clamp(Math.Round(progress), 0, 100);
                string speed = FormatSpeed(job.FileSize, stopwatch.Elapsed, progressValue);

                UpdateJobSafely(job, () =>
                {
                    job.Progress = progressValue;
                    job.Speed = speed;
                });
            });

            using (job.CancellationToken.Token.Register(() => transcodeOperation.Cancel()))
            {
                await transcodeOperation.AsTask(job.CancellationToken.Token);
            }

            stopwatch.Stop();

            long outputSize = 0;
            if (File.Exists(outputFile.Path))
            {
                outputSize = new FileInfo(outputFile.Path).Length;
            }

            double totalSeconds = stopwatch.Elapsed.TotalSeconds;
            string finalSpeed = totalSeconds > 0
                ? $"{((outputSize > 0 ? outputSize : job.FileSize) / totalSeconds) / (1024d * 1024d):0.##} MB/s"
                : "0 MB/s";

            UpdateJobSafely(job, () =>
            {
                job.Progress = 100;
                job.Speed = finalSpeed;
            });
        }

        private bool IsVideoOrAudioFile(string file)
        {
            string[] extensions = { ".mp4", ".wmv", ".avi", ".webm", ".mp3", ".m4a", ".wma", ".wav" };
            return extensions.Contains(Path.GetExtension(file).ToLower());
        }

        private void UpdateJobSafely(ConversionJob job, Action updateAction)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    updateAction();
                    job.UpdateDisplay();
                }));
            }
            else
            {
                updateAction();
                job.UpdateDisplay();
            }
        }

        private async Task PopulateJobDurationAsync(ConversionJob job, StorageFile file)
        {
            try
            {
                if (!job.AudioOnly)
                {
                    VideoProperties videoProps = await file.Properties.GetVideoPropertiesAsync();
                    if (videoProps.Duration > TimeSpan.Zero)
                    {
                        job.TotalDuration = videoProps.Duration;
                        return;
                    }
                }

                MusicProperties audioProps = await file.Properties.GetMusicPropertiesAsync();
                if (audioProps.Duration > TimeSpan.Zero)
                {
                    job.TotalDuration = audioProps.Duration;
                }
            }
            catch
            {
                job.TotalDuration = TimeSpan.Zero;
            }
        }

        private MediaEncodingProfile CreateEncodingProfile(ConversionJob job)
        {
            string format = job.OutputFormat.ToUpperInvariant();

            if (job.AudioOnly)
            {
                AudioEncodingQuality audioQuality = MapAudioQuality(job.Quality);
                MediaEncodingProfile audioProfile = format switch
                {
                    "MP3" => MediaEncodingProfile.CreateMp3(audioQuality),
                    "M4A" => MediaEncodingProfile.CreateM4a(audioQuality),
                    "WMA" => MediaEncodingProfile.CreateWma(audioQuality),
                    "WAV" => MediaEncodingProfile.CreateWav(audioQuality),
                    _ => MediaEncodingProfile.CreateMp3(audioQuality)
                };

                ApplyAudioBitrate(job, audioProfile);
                return audioProfile;
            }
            else
            {
                VideoEncodingQuality videoQuality = VideoEncodingQuality.Auto;

                MediaEncodingProfile videoProfile = format switch
                {
                    "MP4" => MediaEncodingProfile.CreateMp4(videoQuality),
                    "WMV" => MediaEncodingProfile.CreateWmv(videoQuality),
                    "AVI" => MediaEncodingProfile.CreateAvi(videoQuality),
                    "WEBM" => MediaEncodingProfile.CreateWebM(videoQuality),
                    _ => MediaEncodingProfile.CreateMp4(videoQuality)
                };

                ApplyResolution(job, videoProfile);
                ApplyVideoBitrate(job, videoProfile);
                ApplyAudioBitrate(job, videoProfile);

                return videoProfile;
            }
        }

        private void ApplyResolution(ConversionJob job, MediaEncodingProfile profile)
        {
            if (profile.Video is null)
            {
                return;
            }

            (uint width, uint height)? resolution = TryParseResolution(job.Resolution);
            if (resolution.HasValue)
            {
                profile.Video.Width = resolution.Value.width;
                profile.Video.Height = resolution.Value.height;
            }
        }

        private void ApplyVideoBitrate(ConversionJob job, MediaEncodingProfile profile)
        {
            if (profile.Video is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(job.CustomBitrate) && uint.TryParse(job.CustomBitrate, out uint customKbps))
            {
                profile.Video.Bitrate = customKbps * 1000;
                return;
            }

            uint? bitrate = job.Quality switch
            {
                string q when q.Contains("Low", StringComparison.OrdinalIgnoreCase) => 1_500_000u,
                string q when q.Contains("Medium", StringComparison.OrdinalIgnoreCase) => 4_000_000u,
                _ => null
            };

            if (bitrate.HasValue)
            {
                profile.Video.Bitrate = bitrate.Value;
            }
        }

        private void ApplyAudioBitrate(ConversionJob job, MediaEncodingProfile profile)
        {
            if (profile.Audio is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(job.CustomBitrate) && uint.TryParse(job.CustomBitrate, out uint customKbps))
            {
                profile.Audio.Bitrate = customKbps * 1000;
                return;
            }

            uint? bitrate = job.Quality switch
            {
                string q when q.Contains("Low", StringComparison.OrdinalIgnoreCase) => 128_000u,
                string q when q.Contains("Medium", StringComparison.OrdinalIgnoreCase) => 192_000u,
                _ => 256_000u
            };

            profile.Audio.Bitrate = bitrate.Value;
        }

        private AudioEncodingQuality MapAudioQuality(string? qualityText)
        {
            if (qualityText is null)
            {
                return AudioEncodingQuality.High;
            }

            if (qualityText.Contains("Low", StringComparison.OrdinalIgnoreCase))
            {
                return AudioEncodingQuality.Low;
            }

            if (qualityText.Contains("Medium", StringComparison.OrdinalIgnoreCase))
            {
                return AudioEncodingQuality.Medium;
            }

            return AudioEncodingQuality.High;
        }

        private (uint width, uint height)? TryParseResolution(string? resolutionText)
        {
            if (string.IsNullOrWhiteSpace(resolutionText) || resolutionText.Equals("Original", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string candidate = resolutionText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            string[] parts = candidate.Split('x');

            if (parts.Length == 2 && uint.TryParse(parts[0], out uint width) && uint.TryParse(parts[1], out uint height))
            {
                return (width, height);
            }

            return null;
        }

        private string FormatSpeed(long fileSize, TimeSpan elapsed, int progressPercentage)
        {
            if (elapsed.TotalSeconds <= 0 || fileSize <= 0 || progressPercentage <= 0)
            {
                return "0 MB/s";
            }

            double processedBytes = fileSize * (progressPercentage / 100d);
            double bytesPerSecond = processedBytes / elapsed.TotalSeconds;
            return $"{bytesPerSecond / (1024d * 1024d):0.##} MB/s";
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
        private string _speed = "0 MB/s";

        public string InputPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string OutputFormat { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public string? CustomBitrate { get; set; }
        public bool MuteAudio { get; set; }
        public bool AudioOnly { get; set; }
        public string OutputDirectory { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public DateTime? StartTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public CancellationTokenSource? CancellationToken { get; set; }
        public string? ErrorMessage { get; set; }

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

        public event PropertyChangedEventHandler? PropertyChanged;

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