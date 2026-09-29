using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace Thetis
{
    public partial class Console
    {
        private const string WATERFALL_ID_PLAY_ID = "sq4kou-waterfall-id";
        private ToolStripMenuItem _waterfallIdMenuItem;
        private WaterfallIDForm _waterfallIdForm;
        private bool _waterfallIdActive;
        private string _waterfallIdTempFile;
        private Action<bool, string, string, bool> _waterfallIdPlayingChangedHandler;

        private bool _waterfallIdStateSaved;
        private bool _waterfallIdSavedTXEQ;
        private bool _waterfallIdSavedCPDR;
        private bool _waterfallIdSavedCFC;
        private bool _waterfallIdSavedPhase;
        private bool _waterfallIdSavedLeveler;
        private bool _waterfallIdSavedMoxOnPlayback;

        internal event Action<bool, string> WaterfallIDStateChanged;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            InstallWaterfallIDMenu();
        }

        private void InstallWaterfallIDMenu()
        {
            if (_waterfallIdMenuItem != null) return;

            MenuStrip strip = MainMenuStrip;
            if (strip == null)
                strip = FindFirstMenuStrip(this);
            if (strip == null) return;

            ToolStripMenuItem item = new ToolStripMenuItem("Waterfall ID...");
            item.Name = "waterfallIdToolStripMenuItem";
            item.ToolTipText = "Transmit text as an on-air waterfall image";
            item.Click += WaterfallIdMenuItem_Click;

            ToolStripMenuItem tools = null;
            for (int i = 0; i < strip.Items.Count; i++)
            {
                ToolStripMenuItem candidate = strip.Items[i] as ToolStripMenuItem;
                if (candidate == null) continue;
                string text = (candidate.Text ?? string.Empty).Replace("&", string.Empty).Trim();
                if (string.Equals(text, "Tools", StringComparison.OrdinalIgnoreCase))
                {
                    tools = candidate;
                    break;
                }
            }

            if (tools != null)
            {
                tools.DropDownItems.Add(new ToolStripSeparator());
                tools.DropDownItems.Add(item);
            }
            else
            {
                strip.Items.Add(item);
            }

            _waterfallIdMenuItem = item;
        }

        private static MenuStrip FindFirstMenuStrip(Control root)
        {
            if (root == null) return null;
            MenuStrip direct = root as MenuStrip;
            if (direct != null) return direct;

            for (int i = 0; i < root.Controls.Count; i++)
            {
                MenuStrip found = FindFirstMenuStrip(root.Controls[i]);
                if (found != null) return found;
            }
            return null;
        }

        private void WaterfallIdMenuItem_Click(object sender, EventArgs e)
        {
            if (_waterfallIdForm == null || _waterfallIdForm.IsDisposed)
                _waterfallIdForm = new WaterfallIDForm(this);

            if (!_waterfallIdForm.Visible)
                _waterfallIdForm.Show(this);
            else
            {
                _waterfallIdForm.BringToFront();
                _waterfallIdForm.Activate();
            }
        }

        internal DSPMode WaterfallIDCurrentTXMode
        {
            get
            {
                if (RX2Enabled && VFOBTX) return RX2DSPMode;
                return RX1DSPMode;
            }
        }

        internal bool WaterfallIDIsLowerSideband
        {
            get
            {
                DSPMode mode = WaterfallIDCurrentTXMode;
                return mode == DSPMode.LSB || mode == DSPMode.DIGL;
            }
        }

        internal bool WaterfallIDModeSupported
        {
            get
            {
                DSPMode mode = WaterfallIDCurrentTXMode;
                return mode == DSPMode.USB ||
                       mode == DSPMode.LSB ||
                       mode == DSPMode.DIGU ||
                       mode == DSPMode.DIGL;
            }
        }

        internal bool WaterfallIDIsActive
        {
            get { return _waterfallIdActive; }
        }

        internal void GetWaterfallIDAudioBand(out int lowHz, out int highHz)
        {
            int low = 0;
            int high = 0;

            try
            {
                low = Math.Abs(SetupForm.TXFilterLow);
                high = Math.Abs(SetupForm.TXFilterHigh);
            }
            catch
            {
                low = 0;
                high = 0;
            }

            if (low > high)
            {
                int t = low;
                low = high;
                high = t;
            }

            const int guardHz = 100;
            low += guardHz;
            high -= guardHz;

            low = Math.Max(80, low);
            high = Math.Min(6000, high);

            if (high - low < 800)
            {
                low = 150;
                high = 2550;
            }

            lowHz = low;
            highHz = high;
        }

        internal bool StartWaterfallID(string wavPath, double gainDb, out string error)
        {
            error = null;

            if (_waterfallIdActive)
            {
                error = "Waterfall ID is already transmitting.";
                return false;
            }

            if (!WaterfallIDModeSupported)
            {
                error = "Waterfall ID supports USB, LSB, DIGU and DIGL only.";
                return false;
            }

            if (ARP == null)
            {
                error = "Thetis audio playback engine is not available.";
                return false;
            }

            if (ARP.IsBusy)
            {
                error = "Thetis audio record/playback engine is busy.";
                return false;
            }

            try
            {
                if (cmaster.TCIServer != null && cmaster.TCIServer.UsesActiveTCITxAudio())
                {
                    error = "TCI TX audio is active. Stop the TCI TX audio stream before Waterfall ID.";
                    return false;
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            {
                error = "Waterfall ID WAV file is missing.";
                return false;
            }

            SaveWaterfallIDState();

            try
            {
                TXEQ = false;
                CPDR = false;
                CFCEnabled = false;
                PhaseRotEnabled = false;
                LevelerEnabled = false;

                ARP.MoxOnPlayback = true;

                _waterfallIdPlayingChangedHandler = delegate(bool playing, string playId, string filename, bool viaWdsp)
                {
                    if (!playing && _waterfallIdActive)
                        FinishWaterfallID("ID complete");
                };
                ARP.PlayingChanged += _waterfallIdPlayingChangedHandler;

                _waterfallIdTempFile = wavPath;

                string playbackError;
                bool ok = ARP.PlayFileViaWDSP(
                    WATERFALL_ID_PLAY_ID,
                    wavPath,
                    0,
                    out playbackError,
                    gainDb,
                    true);

                if (!ok)
                {
                    error = string.IsNullOrWhiteSpace(playbackError) ? "Unable to start Waterfall ID playback." : playbackError;
                    DetachWaterfallIDPlaybackHandler();
                    RestoreWaterfallIDState();
                    DeleteWaterfallIDTempFile();
                    return false;
                }

                _waterfallIdActive = true;
                RaiseWaterfallIDStateChanged(true, "Transmitting Waterfall ID");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                DetachWaterfallIDPlaybackHandler();
                try
                {
                    string ignored;
                    ARP.StopPlayback(out ignored);
                }
                catch { }
                RestoreWaterfallIDState();
                DeleteWaterfallIDTempFile();
                _waterfallIdActive = false;
                return false;
            }
        }

        internal bool StopWaterfallID(out string error)
        {
            error = null;
            if (!_waterfallIdActive) return true;

            try
            {
                bool ok = ARP.StopPlayback(out error);
                if (_waterfallIdActive)
                    FinishWaterfallID("ID stopped");
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                FinishWaterfallID("ID stopped");
                return false;
            }
        }

        private void SaveWaterfallIDState()
        {
            _waterfallIdSavedTXEQ = TXEQ;
            _waterfallIdSavedCPDR = CPDR;
            _waterfallIdSavedCFC = CFCEnabled;
            _waterfallIdSavedPhase = PhaseRotEnabled;
            _waterfallIdSavedLeveler = LevelerEnabled;
            _waterfallIdSavedMoxOnPlayback = ARP.MoxOnPlayback;
            _waterfallIdStateSaved = true;
        }

        private void RestoreWaterfallIDState()
        {
            if (!_waterfallIdStateSaved) return;

            try { TXEQ = _waterfallIdSavedTXEQ; } catch { }
            try { CPDR = _waterfallIdSavedCPDR; } catch { }
            try { CFCEnabled = _waterfallIdSavedCFC; } catch { }
            try { PhaseRotEnabled = _waterfallIdSavedPhase; } catch { }
            try { LevelerEnabled = _waterfallIdSavedLeveler; } catch { }
            try { ARP.MoxOnPlayback = _waterfallIdSavedMoxOnPlayback; } catch { }

            _waterfallIdStateSaved = false;
        }

        private void DetachWaterfallIDPlaybackHandler()
        {
            if (_waterfallIdPlayingChangedHandler == null || ARP == null) return;
            try { ARP.PlayingChanged -= _waterfallIdPlayingChangedHandler; } catch { }
            _waterfallIdPlayingChangedHandler = null;
        }

        private void FinishWaterfallID(string status)
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)delegate { FinishWaterfallID(status); });
                }
                catch { }
                return;
            }

            DetachWaterfallIDPlaybackHandler();
            RestoreWaterfallIDState();
            DeleteWaterfallIDTempFile();
            _waterfallIdActive = false;
            RaiseWaterfallIDStateChanged(false, status);
        }

        private void DeleteWaterfallIDTempFile()
        {
            string file = _waterfallIdTempFile;
            _waterfallIdTempFile = null;
            if (string.IsNullOrWhiteSpace(file)) return;

            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch { }
        }

        private void RaiseWaterfallIDStateChanged(bool active, string status)
        {
            Action<bool, string> h = WaterfallIDStateChanged;
            if (h == null) return;

            Delegate[] list = h.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<bool, string>)list[i])(active, status); }
                catch { }
            }
        }
    }

    internal sealed class WaterfallIDForm : Form
    {
        private readonly Console _console;
        private readonly TextBox _text;
        private readonly PictureBox _preview;
        private readonly NumericUpDown _rowMs;
        private readonly NumericUpDown _levelDb;
        private readonly Label _modeBand;
        private readonly Label _status;
        private readonly Button _send;
        private readonly Button _stop;

        internal WaterfallIDForm(Console console)
        {
            _console = console;

            Text = "TX Waterfall ID";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 360);

            Label textLabel = new Label();
            textLabel.Text = "Text / callsign:";
            textLabel.AutoSize = true;
            textLabel.Location = new Point(12, 15);
            Controls.Add(textLabel);

            _text = new TextBox();
            _text.Text = "SQ4KOU";
            _text.MaxLength = 24;
            _text.Location = new Point(110, 11);
            _text.Size = new Size(230, 23);
            _text.TextChanged += delegate { RefreshPreview(); };
            Controls.Add(_text);

            _modeBand = new Label();
            _modeBand.AutoSize = false;
            _modeBand.TextAlign = ContentAlignment.MiddleRight;
            _modeBand.Location = new Point(350, 11);
            _modeBand.Size = new Size(175, 23);
            Controls.Add(_modeBand);

            _preview = new PictureBox();
            _preview.BackColor = Color.Black;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.Location = new Point(12, 46);
            _preview.Size = new Size(513, 150);
            Controls.Add(_preview);

            Label speedLabel = new Label();
            speedLabel.Text = "Row time:";
            speedLabel.AutoSize = true;
            speedLabel.Location = new Point(12, 214);
            Controls.Add(speedLabel);

            _rowMs = new NumericUpDown();
            _rowMs.Minimum = 60;
            _rowMs.Maximum = 500;
            _rowMs.Increment = 10;
            _rowMs.Value = 180;
            _rowMs.Location = new Point(82, 210);
            _rowMs.Size = new Size(72, 23);
            Controls.Add(_rowMs);

            Label msLabel = new Label();
            msLabel.Text = "ms";
            msLabel.AutoSize = true;
            msLabel.Location = new Point(159, 214);
            Controls.Add(msLabel);

            Label levelLabel = new Label();
            levelLabel.Text = "TX level:";
            levelLabel.AutoSize = true;
            levelLabel.Location = new Point(220, 214);
            Controls.Add(levelLabel);

            _levelDb = new NumericUpDown();
            _levelDb.Minimum = -30;
            _levelDb.Maximum = 0;
            _levelDb.Increment = 1;
            _levelDb.Value = -6;
            _levelDb.Location = new Point(280, 210);
            _levelDb.Size = new Size(65, 23);
            Controls.Add(_levelDb);

            Label dbLabel = new Label();
            dbLabel.Text = "dB";
            dbLabel.AutoSize = true;
            dbLabel.Location = new Point(350, 214);
            Controls.Add(dbLabel);

            Button previewButton = new Button();
            previewButton.Text = "PREVIEW";
            previewButton.Location = new Point(12, 252);
            previewButton.Size = new Size(105, 33);
            previewButton.Click += delegate { RefreshPreview(); };
            Controls.Add(previewButton);

            _send = new Button();
            _send.Text = "SEND ID";
            _send.Location = new Point(126, 252);
            _send.Size = new Size(110, 33);
            _send.Click += Send_Click;
            Controls.Add(_send);

            _stop = new Button();
            _stop.Text = "STOP";
            _stop.Location = new Point(245, 252);
            _stop.Size = new Size(90, 33);
            _stop.Enabled = false;
            _stop.Click += Stop_Click;
            Controls.Add(_stop);

            Button close = new Button();
            close.Text = "CLOSE";
            close.Location = new Point(435, 252);
            close.Size = new Size(90, 33);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            _status = new Label();
            _status.AutoSize = false;
            _status.BorderStyle = BorderStyle.Fixed3D;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Location = new Point(12, 302);
            _status.Size = new Size(513, 34);
            Controls.Add(_status);

            _console.WaterfallIDStateChanged += Console_WaterfallIDStateChanged;
            FormClosing += WaterfallIDForm_FormClosing;
            Shown += delegate { RefreshPreview(); };
        }

        private void RefreshPreview()
        {
            try
            {
                int low;
                int high;
                _console.GetWaterfallIDAudioBand(out low, out high);
                DSPMode mode = _console.WaterfallIDCurrentTXMode;
                _modeBand.Text = mode.ToString() + "  " + low + "-" + high + " Hz";

                Bitmap next = WaterfallIDGenerator.RenderTextBitmap(_text.Text, 150, 36);
                Image old = _preview.Image;
                _preview.Image = next;
                if (old != null) old.Dispose();

                decimal durationMs = _rowMs.Value * 36m + 100m;
                _status.Text = "Ready — estimated TX time " + (durationMs / 1000m).ToString("0.0") + " s";
            }
            catch (Exception ex)
            {
                _status.Text = "Preview error: " + ex.Message;
            }
        }

        private void Send_Click(object sender, EventArgs e)
        {
            string text = (_text.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                _status.Text = "Enter text to transmit.";
                return;
            }

            if (!_console.WaterfallIDModeSupported)
            {
                _status.Text = "Unsupported TX mode. Select USB, LSB, DIGU or DIGL.";
                return;
            }

            _send.Enabled = false;
            _status.Text = "Generating Waterfall ID...";
            Application.DoEvents();

            string wav = Path.Combine(
                Path.GetTempPath(),
                "Thetis-WaterfallID-" + Guid.NewGuid().ToString("N") + ".wav");

            try
            {
                int low;
                int high;
                _console.GetWaterfallIDAudioBand(out low, out high);

                double seconds = WaterfallIDGenerator.GenerateWaveFile(
                    text,
                    wav,
                    low,
                    high,
                    (int)_rowMs.Value,
                    _console.WaterfallIDIsLowerSideband);

                string error;
                bool ok = _console.StartWaterfallID(wav, (double)_levelDb.Value, out error);
                if (!ok)
                {
                    try { if (File.Exists(wav)) File.Delete(wav); } catch { }
                    _status.Text = "Not transmitted: " + error;
                    _send.Enabled = true;
                    _stop.Enabled = false;
                    return;
                }

                _status.Text = "Transmitting — " + seconds.ToString("0.0") + " s";
                _stop.Enabled = true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(wav)) File.Delete(wav); } catch { }
                _status.Text = "Generation failed: " + ex.Message;
                _send.Enabled = true;
                _stop.Enabled = false;
            }
        }

        private void Stop_Click(object sender, EventArgs e)
        {
            string error;
            if (!_console.StopWaterfallID(out error) && !string.IsNullOrWhiteSpace(error))
                _status.Text = "Stop error: " + error;
        }

        private void Console_WaterfallIDStateChanged(bool active, string status)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)delegate { Console_WaterfallIDStateChanged(active, status); });
                }
                catch { }
                return;
            }

            _send.Enabled = !active;
            _stop.Enabled = active;
            _status.Text = status;
            if (!active) RefreshPreview();
        }

        private void WaterfallIDForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_console.WaterfallIDIsActive)
            {
                string ignored;
                _console.StopWaterfallID(out ignored);
            }

            _console.WaterfallIDStateChanged -= Console_WaterfallIDStateChanged;

            Image img = _preview.Image;
            _preview.Image = null;
            if (img != null) img.Dispose();
        }
    }

    internal static class WaterfallIDGenerator
    {
        private const int SampleRate = 48000;
        private const int Width = 150;
        private const int Height = 36;
        private const double TargetPeak = 0.68;

        internal static Bitmap RenderTextBitmap(string text, int width, int height)
        {
            if (width < 8) width = 8;
            if (height < 8) height = 8;

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                string value = (text ?? string.Empty).Trim();
                if (value.Length == 0) value = "SQ4KOU";

                float fontSize = Math.Min(30f, height - 4f);
                Font font = null;
                SizeF measured = SizeF.Empty;

                while (fontSize >= 8f)
                {
                    if (font != null) font.Dispose();
                    font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    measured = g.MeasureString(value, font);
                    if (measured.Width <= width - 4 && measured.Height <= height - 2) break;
                    fontSize -= 1f;
                }

                if (font == null)
                    font = new Font("Arial", 8f, FontStyle.Bold, GraphicsUnit.Pixel);

                using (font)
                {
                    measured = g.MeasureString(value, font);
                    float x = Math.Max(0f, (width - measured.Width) * 0.5f);
                    float y = Math.Max(0f, (height - measured.Height) * 0.5f);
                    g.DrawString(value, font, Brushes.White, x, y);
                }
            }

            return bitmap;
        }

        internal static double GenerateWaveFile(
            string text,
            string fileName,
            int lowHz,
            int highHz,
            int rowDurationMs,
            bool mirrorForLowerSideband)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Output WAV path is empty.");

            if (lowHz < 50) lowHz = 50;
            if (highHz > 10000) highHz = 10000;
            if (highHz <= lowHz + 200)
                throw new ArgumentException("TX audio passband is too narrow for Waterfall ID.");

            if (rowDurationMs < 60) rowDurationMs = 60;
            if (rowDurationMs > 500) rowDurationMs = 500;

            double[,] pixels = new double[Width, Height];
            using (Bitmap bitmap = RenderTextBitmap(text, Width, Height))
            {
                bool any = false;

                for (int row = 0; row < Height; row++)
                {
                    int sourceY = Height - 1 - row;

                    for (int x = 0; x < Width; x++)
                    {
                        int sourceX = mirrorForLowerSideband ? (Width - 1 - x) : x;
                        Color c = bitmap.GetPixel(sourceX, sourceY);
                        double value =
                            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

                        if (value < 0.035) value = 0.0;
                        pixels[x, row] = value;
                        if (value > 0.0) any = true;
                    }
                }

                if (!any)
                    throw new InvalidOperationException("Rendered Waterfall ID is empty.");
            }

            int rowSamples = (int)Math.Round(SampleRate * rowDurationMs / 1000.0);
            int padSamples = SampleRate / 20;
            int fadeSamples = Math.Max(1, SampleRate / 200);
            int bodySamples = rowSamples * Height;
            int totalSamples = padSamples + bodySamples + padSamples;

            double[] audio = new double[totalSamples];

            double[] sinPhase = new double[Width];
            double[] cosPhase = new double[Width];
            double[] sinInc = new double[Width];
            double[] cosInc = new double[Width];

            for (int x = 0; x < Width; x++)
            {
                double fraction = Width == 1 ? 0.0 : x / (double)(Width - 1);
                double frequency = lowHz + (highHz - lowHz) * fraction;
                double delta = 2.0 * Math.PI * frequency / SampleRate;

                sinPhase[x] = 0.0;
                cosPhase[x] = 1.0;
                sinInc[x] = Math.Sin(delta);
                cosInc[x] = Math.Cos(delta);
            }

            double[] previousAmplitude = new double[Width];
            int outIndex = padSamples;

            for (int row = 0; row < Height; row++)
            {
                for (int s = 0; s < rowSamples; s++)
                {
                    double transition = s >= fadeSamples ? 1.0 : s / (double)fadeSamples;
                    double sample = 0.0;

                    for (int x = 0; x < Width; x++)
                    {
                        double target = pixels[x, row];
                        double amplitude =
                            previousAmplitude[x] + (target - previousAmplitude[x]) * transition;

                        if (amplitude > 0.0)
                            sample += amplitude * sinPhase[x];

                        double oldSin = sinPhase[x];
                        double oldCos = cosPhase[x];
                        sinPhase[x] = oldSin * cosInc[x] + oldCos * sinInc[x];
                        cosPhase[x] = oldCos * cosInc[x] - oldSin * sinInc[x];
                    }

                    audio[outIndex++] = sample;
                }

                for (int x = 0; x < Width; x++)
                    previousAmplitude[x] = pixels[x, row];
            }

            int tailFade = Math.Min(fadeSamples, padSamples);
            for (int s = 0; s < tailFade; s++)
            {
                double transition = 1.0 - s / (double)tailFade;
                double sample = 0.0;

                for (int x = 0; x < Width; x++)
                {
                    double amplitude = previousAmplitude[x] * transition;
                    if (amplitude > 0.0)
                        sample += amplitude * sinPhase[x];

                    double oldSin = sinPhase[x];
                    double oldCos = cosPhase[x];
                    sinPhase[x] = oldSin * cosInc[x] + oldCos * sinInc[x];
                    cosPhase[x] = oldCos * cosInc[x] - oldSin * sinInc[x];
                }

                if (outIndex < audio.Length)
                    audio[outIndex++] = sample;
            }

            double peak = 0.0;
            for (int i = 0; i < audio.Length; i++)
            {
                double a = Math.Abs(audio[i]);
                if (a > peak) peak = a;
            }

            if (peak < 1e-12)
                throw new InvalidOperationException("Waterfall ID audio contains no signal.");

            double scale = TargetPeak / peak;

            string dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (BinaryWriter writer = new BinaryWriter(File.Open(fileName, FileMode.Create, FileAccess.Write, FileShare.Read)))
            {
                const short channels = 2;
                const short bitsPerSample = 16;
                const short blockAlign = channels * (bitsPerSample / 8);
                int byteRate = SampleRate * blockAlign;
                int dataBytes = audio.Length * blockAlign;

                writer.Write(0x46464952);
                writer.Write(36 + dataBytes);
                writer.Write(0x45564157);
                writer.Write(0x20746D66);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(SampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(0x61746164);
                writer.Write(dataBytes);

                for (int i = 0; i < audio.Length; i++)
                {
                    double value = audio[i] * scale;
                    if (value > 0.999969) value = 0.999969;
                    if (value < -1.0) value = -1.0;

                    short pcm = (short)Math.Round(value * 32767.0);
                    writer.Write(pcm);
                    writer.Write(pcm);
                }
            }

            return audio.Length / (double)SampleRate;
        }
    }
}
