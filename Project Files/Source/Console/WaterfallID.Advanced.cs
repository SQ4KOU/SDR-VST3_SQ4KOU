using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Thetis
{
    public partial class Console
    {
        private WaterfallIDAdvancedForm _waterfallIdAdvancedForm;

        private void WaterfallIdAdvancedMenuItem_Click(object sender, EventArgs e)
        {
            if (_waterfallIdAdvancedForm == null || _waterfallIdAdvancedForm.IsDisposed)
                _waterfallIdAdvancedForm = new WaterfallIDAdvancedForm(this);

            if (!_waterfallIdAdvancedForm.Visible)
                _waterfallIdAdvancedForm.Show(this);
            else
            {
                _waterfallIdAdvancedForm.BringToFront();
                _waterfallIdAdvancedForm.Activate();
            }
        }
    }

    internal sealed class WaterfallIDAdvancedForm : Form
    {
        private readonly Console _console;
        private readonly TabControl _sourceTabs;
        private readonly TextBox _text;
        private readonly ComboBox _font;
        private readonly CheckBox _bold;
        private readonly ComboBox _textAlign;

        private Bitmap _sourceImage;
        private string _sourcePath;
        private readonly PictureBox _sourcePreview;
        private readonly Label _sourcePathLabel;
        private readonly ComboBox _fitMode;
        private readonly NumericUpDown _brightness;
        private readonly NumericUpDown _contrast;
        private readonly NumericUpDown _gamma;
        private readonly NumericUpDown _threshold;
        private readonly CheckBox _invert;
        private readonly CheckBox _binary;
        private readonly CheckBox _autoLevels;
        private readonly CheckBox _sharpen;

        private readonly ComboBox _raster;
        private readonly PictureBox _txPreview;
        private readonly Label _modeBand;
        private readonly CheckBox _autoBand;
        private readonly NumericUpDown _lowHz;
        private readonly NumericUpDown _highHz;
        private readonly NumericUpDown _rowMs;
        private readonly NumericUpDown _levelDb;
        private readonly NumericUpDown _preMs;
        private readonly NumericUpDown _postMs;
        private readonly NumericUpDown _repeat;
        private readonly NumericUpDown _pauseMs;
        private readonly Label _status;
        private readonly Button _send;
        private readonly Button _stop;

        private Bitmap _lastProcessed;

        internal WaterfallIDAdvancedForm(Console console)
        {
            _console = console;

            Text = "TX Waterfall ID - SQ4KOU / Thetis-RedPitaya";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(910, 680);
            AllowDrop = true;

            _sourceTabs = new TabControl();
            _sourceTabs.Location = new Point(12, 12);
            _sourceTabs.Size = new Size(430, 244);
            _sourceTabs.SelectedIndexChanged += delegate { RefreshPreview(); };
            Controls.Add(_sourceTabs);

            TabPage textPage = new TabPage("TEXT");
            TabPage imagePage = new TabPage("IMAGE");
            _sourceTabs.TabPages.Add(textPage);
            _sourceTabs.TabPages.Add(imagePage);

            Label textLabel = new Label();
            textLabel.Text = "Text / callsign:";
            textLabel.AutoSize = true;
            textLabel.Location = new Point(12, 16);
            textPage.Controls.Add(textLabel);

            _text = new TextBox();
            _text.Text = "SQ4KOU";
            _text.Multiline = true;
            _text.MaxLength = 64;
            _text.Location = new Point(12, 38);
            _text.Size = new Size(394, 88);
            _text.TextChanged += delegate { RefreshPreview(); };
            textPage.Controls.Add(_text);

            Label fontLabel = new Label();
            fontLabel.Text = "Font:";
            fontLabel.AutoSize = true;
            fontLabel.Location = new Point(12, 145);
            textPage.Controls.Add(fontLabel);

            _font = new ComboBox();
            _font.DropDownStyle = ComboBoxStyle.DropDownList;
            _font.Items.AddRange(new object[] { "Arial", "Segoe UI", "Consolas", "Tahoma", "Times New Roman" });
            _font.SelectedIndex = 0;
            _font.Location = new Point(52, 141);
            _font.Size = new Size(145, 23);
            _font.SelectedIndexChanged += delegate { RefreshPreview(); };
            textPage.Controls.Add(_font);

            _bold = new CheckBox();
            _bold.Text = "Bold";
            _bold.Checked = true;
            _bold.AutoSize = true;
            _bold.Location = new Point(212, 143);
            _bold.CheckedChanged += delegate { RefreshPreview(); };
            textPage.Controls.Add(_bold);

            Label alignLabel = new Label();
            alignLabel.Text = "Align:";
            alignLabel.AutoSize = true;
            alignLabel.Location = new Point(282, 145);
            textPage.Controls.Add(alignLabel);

            _textAlign = new ComboBox();
            _textAlign.DropDownStyle = ComboBoxStyle.DropDownList;
            _textAlign.Items.AddRange(new object[] { "Left", "Center", "Right" });
            _textAlign.SelectedIndex = 1;
            _textAlign.Location = new Point(325, 141);
            _textAlign.Size = new Size(82, 23);
            _textAlign.SelectedIndexChanged += delegate { RefreshPreview(); };
            textPage.Controls.Add(_textAlign);

            Label textInfo = new Label();
            textInfo.Text = "One or two lines are supported. Font size is fitted automatically.";
            textInfo.AutoSize = true;
            textInfo.Location = new Point(12, 184);
            textPage.Controls.Add(textInfo);

            Button loadImage = new Button();
            loadImage.Text = "LOAD IMAGE...";
            loadImage.Location = new Point(12, 12);
            loadImage.Size = new Size(112, 30);
            loadImage.Click += LoadImage_Click;
            imagePage.Controls.Add(loadImage);

            Button pasteImage = new Button();
            pasteImage.Text = "PASTE";
            pasteImage.Location = new Point(132, 12);
            pasteImage.Size = new Size(78, 30);
            pasteImage.Click += PasteImage_Click;
            imagePage.Controls.Add(pasteImage);

            Button clearImage = new Button();
            clearImage.Text = "CLEAR";
            clearImage.Location = new Point(218, 12);
            clearImage.Size = new Size(78, 30);
            clearImage.Click += delegate { SetSourceImage(null, null); };
            imagePage.Controls.Add(clearImage);

            _sourcePathLabel = new Label();
            _sourcePathLabel.Text = "No image loaded";
            _sourcePathLabel.AutoEllipsis = true;
            _sourcePathLabel.Location = new Point(12, 48);
            _sourcePathLabel.Size = new Size(394, 20);
            imagePage.Controls.Add(_sourcePathLabel);

            _sourcePreview = new PictureBox();
            _sourcePreview.BackColor = Color.Black;
            _sourcePreview.BorderStyle = BorderStyle.FixedSingle;
            _sourcePreview.SizeMode = PictureBoxSizeMode.Zoom;
            _sourcePreview.Location = new Point(12, 72);
            _sourcePreview.Size = new Size(394, 130);
            imagePage.Controls.Add(_sourcePreview);

            GroupBox process = new GroupBox();
            process.Text = "IMAGE PROCESSING";
            process.Location = new Point(12, 270);
            process.Size = new Size(430, 250);
            Controls.Add(process);

            Label fitLabel = new Label();
            fitLabel.Text = "Fit:";
            fitLabel.AutoSize = true;
            fitLabel.Location = new Point(14, 30);
            process.Controls.Add(fitLabel);

            _fitMode = new ComboBox();
            _fitMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _fitMode.Items.AddRange(new object[] { "Fit", "Fill", "Stretch" });
            _fitMode.SelectedIndex = 0;
            _fitMode.Location = new Point(70, 26);
            _fitMode.Size = new Size(112, 23);
            _fitMode.SelectedIndexChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_fitMode);

            Label rasterLabel = new Label();
            rasterLabel.Text = "Raster:";
            rasterLabel.AutoSize = true;
            rasterLabel.Location = new Point(216, 30);
            process.Controls.Add(rasterLabel);

            _raster = new ComboBox();
            _raster.DropDownStyle = ComboBoxStyle.DropDownList;
            _raster.Items.AddRange(new object[] { "Fast 120x30", "Standard 150x36", "Detailed 200x48" });
            _raster.SelectedIndex = 1;
            _raster.Location = new Point(267, 26);
            _raster.Size = new Size(145, 23);
            _raster.SelectedIndexChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_raster);

            AddNumericRow(process, "Brightness:", 14, 66, -100, 100, 0, 5, out _brightness);
            AddNumericRow(process, "Contrast:", 216, 66, -100, 100, 0, 5, out _contrast);
            AddNumericRow(process, "Gamma x100:", 14, 103, 20, 300, 100, 5, out _gamma);
            AddNumericRow(process, "Threshold:", 216, 103, 0, 254, 8, 2, out _threshold);

            _invert = new CheckBox();
            _invert.Text = "Invert";
            _invert.AutoSize = true;
            _invert.Location = new Point(17, 145);
            _invert.CheckedChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_invert);

            _binary = new CheckBox();
            _binary.Text = "Binary pixels";
            _binary.AutoSize = true;
            _binary.Location = new Point(100, 145);
            _binary.CheckedChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_binary);

            _autoLevels = new CheckBox();
            _autoLevels.Text = "Auto levels";
            _autoLevels.AutoSize = true;
            _autoLevels.Location = new Point(216, 145);
            _autoLevels.CheckedChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_autoLevels);

            _sharpen = new CheckBox();
            _sharpen.Text = "Sharpen";
            _sharpen.AutoSize = true;
            _sharpen.Location = new Point(318, 145);
            _sharpen.CheckedChanged += delegate { RefreshPreview(); };
            process.Controls.Add(_sharpen);

            _brightness.ValueChanged += delegate { RefreshPreview(); };
            _contrast.ValueChanged += delegate { RefreshPreview(); };
            _gamma.ValueChanged += delegate { RefreshPreview(); };
            _threshold.ValueChanged += delegate { RefreshPreview(); };

            Label processInfo = new Label();
            processInfo.Text = "Image mode supports BMP, PNG, JPG/JPEG, clipboard and drag && drop.\r\nGrayscale weighting is used unless Binary pixels is selected.";
            processInfo.Location = new Point(14, 181);
            processInfo.Size = new Size(398, 48);
            process.Controls.Add(processInfo);

            GroupBox tx = new GroupBox();
            tx.Text = "TX / AUDIO";
            tx.Location = new Point(454, 12);
            tx.Size = new Size(444, 244);
            Controls.Add(tx);

            _modeBand = new Label();
            _modeBand.BorderStyle = BorderStyle.Fixed3D;
            _modeBand.TextAlign = ContentAlignment.MiddleCenter;
            _modeBand.Location = new Point(14, 24);
            _modeBand.Size = new Size(416, 28);
            tx.Controls.Add(_modeBand);

            _autoBand = new CheckBox();
            _autoBand.Text = "AUTO band from TX filter";
            _autoBand.Checked = true;
            _autoBand.AutoSize = true;
            _autoBand.Location = new Point(14, 66);
            _autoBand.CheckedChanged += AutoBand_CheckedChanged;
            tx.Controls.Add(_autoBand);

            Label lowLabel = new Label();
            lowLabel.Text = "Low Hz:";
            lowLabel.AutoSize = true;
            lowLabel.Location = new Point(224, 68);
            tx.Controls.Add(lowLabel);

            _lowHz = new NumericUpDown();
            _lowHz.Minimum = 50;
            _lowHz.Maximum = 9000;
            _lowHz.Value = 150;
            _lowHz.Increment = 10;
            _lowHz.Location = new Point(276, 64);
            _lowHz.Size = new Size(66, 23);
            tx.Controls.Add(_lowHz);

            Label highLabel = new Label();
            highLabel.Text = "High:";
            highLabel.AutoSize = true;
            highLabel.Location = new Point(350, 68);
            tx.Controls.Add(highLabel);

            _highHz = new NumericUpDown();
            _highHz.Minimum = 300;
            _highHz.Maximum = 10000;
            _highHz.Value = 2550;
            _highHz.Increment = 10;
            _highHz.Location = new Point(389, 64);
            _highHz.Size = new Size(48, 23);
            tx.Controls.Add(_highHz);

            AddNumericRow(tx, "Row time ms:", 14, 104, 60, 500, 180, 10, out _rowMs);
            AddNumericRow(tx, "TX level dB:", 224, 104, -30, 0, -6, 1, out _levelDb);
            AddNumericRow(tx, "Pre-delay ms:", 14, 141, 0, 1000, 50, 10, out _preMs);
            AddNumericRow(tx, "Post-delay ms:", 224, 141, 0, 1000, 50, 10, out _postMs);
            AddNumericRow(tx, "Repeat:", 14, 178, 1, 5, 1, 1, out _repeat);
            AddNumericRow(tx, "Pause ms:", 224, 178, 0, 5000, 1000, 100, out _pauseMs);

            _lowHz.ValueChanged += delegate { RefreshPreview(); };
            _highHz.ValueChanged += delegate { RefreshPreview(); };
            _rowMs.ValueChanged += delegate { RefreshPreview(); };
            _preMs.ValueChanged += delegate { RefreshPreview(); };
            _postMs.ValueChanged += delegate { RefreshPreview(); };
            _repeat.ValueChanged += delegate { RefreshPreview(); };
            _pauseMs.ValueChanged += delegate { RefreshPreview(); };

            GroupBox previewBox = new GroupBox();
            previewBox.Text = "TX RASTER PREVIEW";
            previewBox.Location = new Point(454, 270);
            previewBox.Size = new Size(444, 250);
            Controls.Add(previewBox);

            _txPreview = new PictureBox();
            _txPreview.BackColor = Color.Black;
            _txPreview.BorderStyle = BorderStyle.FixedSingle;
            _txPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _txPreview.Location = new Point(14, 25);
            _txPreview.Size = new Size(416, 176);
            previewBox.Controls.Add(_txPreview);

            Label previewInfo = new Label();
            previewInfo.Text = "Preview shows the actual frequency orientation. LSB/DIGL is mirrored automatically.";
            previewInfo.Location = new Point(14, 208);
            previewInfo.Size = new Size(416, 32);
            previewBox.Controls.Add(previewInfo);

            Button previewButton = new Button();
            previewButton.Text = "PREVIEW";
            previewButton.Location = new Point(12, 536);
            previewButton.Size = new Size(100, 34);
            previewButton.Click += delegate { RefreshPreview(); };
            Controls.Add(previewButton);

            Button export = new Button();
            export.Text = "EXPORT WAV";
            export.Location = new Point(120, 536);
            export.Size = new Size(110, 34);
            export.Click += Export_Click;
            Controls.Add(export);

            Button savePreset = new Button();
            savePreset.Text = "SAVE PRESET";
            savePreset.Location = new Point(238, 536);
            savePreset.Size = new Size(110, 34);
            savePreset.Click += SavePreset_Click;
            Controls.Add(savePreset);

            Button loadPreset = new Button();
            loadPreset.Text = "LOAD PRESET";
            loadPreset.Location = new Point(356, 536);
            loadPreset.Size = new Size(110, 34);
            loadPreset.Click += LoadPreset_Click;
            Controls.Add(loadPreset);

            _send = new Button();
            _send.Text = "SEND ID";
            _send.Location = new Point(510, 536);
            _send.Size = new Size(112, 34);
            _send.Click += Send_Click;
            Controls.Add(_send);

            _stop = new Button();
            _stop.Text = "STOP";
            _stop.Enabled = false;
            _stop.Location = new Point(630, 536);
            _stop.Size = new Size(92, 34);
            _stop.Click += Stop_Click;
            Controls.Add(_stop);

            Button close = new Button();
            close.Text = "CLOSE";
            close.Location = new Point(806, 536);
            close.Size = new Size(92, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            _status = new Label();
            _status.BorderStyle = BorderStyle.Fixed3D;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Location = new Point(12, 586);
            _status.Size = new Size(886, 70);
            Controls.Add(_status);

            AssignPersistenceNames();
            _console.WaterfallIDStateChanged += Console_WaterfallIDStateChanged;
            FormClosing += WaterfallIDAdvancedForm_FormClosing;
            DragEnter += WaterfallIDAdvancedForm_DragEnter;
            DragDrop += WaterfallIDAdvancedForm_DragDrop;
            Shown += delegate
            {
                if (_autoBand.Checked) UpdateAutoBand();
                RefreshPreview();
            };

            AutoBand_CheckedChanged(null, EventArgs.Empty);
            RestorePersistentState();
        }

        private const string PersistenceTable = "WaterfallIDAdvancedForm";

        private void AssignPersistenceNames()
        {
            _sourceTabs.Name = "tabsWaterfallIDSource";
            _text.Name = "txtWaterfallIDText";
            _font.Name = "comboWaterfallIDFont";
            _bold.Name = "chkWaterfallIDBold";
            _textAlign.Name = "comboWaterfallIDTextAlign";
            _fitMode.Name = "comboWaterfallIDFit";
            _raster.Name = "comboWaterfallIDRaster";
            _brightness.Name = "udWaterfallIDBrightness";
            _contrast.Name = "udWaterfallIDContrast";
            _gamma.Name = "udWaterfallIDGamma";
            _threshold.Name = "udWaterfallIDThreshold";
            _invert.Name = "chkWaterfallIDInvert";
            _binary.Name = "chkWaterfallIDBinary";
            _autoLevels.Name = "chkWaterfallIDAutoLevels";
            _sharpen.Name = "chkWaterfallIDSharpen";
            _autoBand.Name = "chkWaterfallIDAutoBand";
            _lowHz.Name = "udWaterfallIDLowHz";
            _highHz.Name = "udWaterfallIDHighHz";
            _rowMs.Name = "udWaterfallIDRowMs";
            _levelDb.Name = "udWaterfallIDLevelDb";
            _preMs.Name = "udWaterfallIDPreMs";
            _postMs.Name = "udWaterfallIDPostMs";
            _repeat.Name = "udWaterfallIDRepeat";
            _pauseMs.Name = "udWaterfallIDPauseMs";
        }

        private void RestorePersistentState()
        {
            Common.RestoreForm(this, PersistenceTable, false);

            Dictionary<string, string> vars = DB.GetVarsDictionary(PersistenceTable);
            string sourcePath;
            if (vars.TryGetValue("WaterfallIDSourcePath", out sourcePath) &&
                !string.IsNullOrWhiteSpace(sourcePath) &&
                !string.Equals(sourcePath, "Clipboard", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(sourcePath))
            {
                try
                {
                    using (Image loaded = Image.FromFile(sourcePath))
                    {
                        SetSourceImage(new Bitmap(loaded), sourcePath);
                    }
                }
                catch
                {
                    // A stale or unreadable external image must not prevent the form from opening.
                }
            }

            if (_autoBand.Checked) UpdateAutoBand();
            RefreshPreview();
        }

        private void SavePersistentState()
        {
            Common.SaveForm(this, PersistenceTable);

            string sourcePath = _sourcePath;
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.Equals(sourcePath, "Clipboard", StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = string.Empty;
            }

            DB.SaveVars(PersistenceTable, new List<string>
            {
                "WaterfallIDSourcePath/" + sourcePath
            });
        }

        private static void AddNumericRow(Control parent, string labelText, int x, int y, int min, int max, int value, int increment, out NumericUpDown control)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Location = new Point(x, y + 4);
            parent.Controls.Add(label);

            control = new NumericUpDown();
            control.Minimum = min;
            control.Maximum = max;
            control.Value = value;
            control.Increment = increment;
            control.Location = new Point(x + 98, y);
            control.Size = new Size(74, 23);
            parent.Controls.Add(control);
        }

        private bool ImageMode
        {
            get { return _sourceTabs.SelectedIndex == 1; }
        }

        private void GetRasterSize(out int width, out int height)
        {
            if (_raster.SelectedIndex == 0)
            {
                width = 120;
                height = 30;
            }
            else if (_raster.SelectedIndex == 2)
            {
                width = 200;
                height = 48;
            }
            else
            {
                width = 150;
                height = 36;
            }
        }

        private void GetAudioBand(out int low, out int high)
        {
            if (_autoBand.Checked)
            {
                _console.GetWaterfallIDAudioBand(out low, out high);
                SetNumericSafe(_lowHz, low);
                SetNumericSafe(_highHz, high);
            }
            else
            {
                low = (int)_lowHz.Value;
                high = (int)_highHz.Value;
            }
        }

        private static void SetNumericSafe(NumericUpDown control, decimal value)
        {
            if (value < control.Minimum) value = control.Minimum;
            if (value > control.Maximum) value = control.Maximum;
            if (control.Value != value) control.Value = value;
        }

        private void AutoBand_CheckedChanged(object sender, EventArgs e)
        {
            _lowHz.Enabled = !_autoBand.Checked;
            _highHz.Enabled = !_autoBand.Checked;
            if (_autoBand.Checked) UpdateAutoBand();
            RefreshPreview();
        }

        private void UpdateAutoBand()
        {
            int low;
            int high;
            _console.GetWaterfallIDAudioBand(out low, out high);
            SetNumericSafe(_lowHz, low);
            SetNumericSafe(_highHz, high);
        }

        private Bitmap BuildProcessedRaster()
        {
            int width;
            int height;
            GetRasterSize(out width, out height);

            if (!ImageMode)
            {
                return WaterfallIDAdvancedGenerator.RenderText(
                    _text.Text,
                    width,
                    height,
                    _font.SelectedItem == null ? "Arial" : _font.SelectedItem.ToString(),
                    _bold.Checked,
                    _textAlign.SelectedIndex);
            }

            if (_sourceImage == null)
                throw new InvalidOperationException("Load or paste an image first.");

            return WaterfallIDAdvancedGenerator.ProcessImage(
                _sourceImage,
                width,
                height,
                _fitMode.SelectedIndex,
                (int)_brightness.Value,
                (int)_contrast.Value,
                (double)_gamma.Value / 100.0,
                (int)_threshold.Value,
                _invert.Checked,
                _binary.Checked,
                _autoLevels.Checked,
                _sharpen.Checked);
        }

        private void RefreshPreview()
        {
            if (_txPreview == null || IsDisposed) return;

            try
            {
                int low;
                int high;
                GetAudioBand(out low, out high);

                Bitmap processed = BuildProcessedRaster();
                Bitmap display = _console.WaterfallIDIsLowerSideband
                    ? WaterfallIDAdvancedGenerator.Mirror(processed)
                    : new Bitmap(processed);

                Bitmap oldProcessed = _lastProcessed;
                _lastProcessed = processed;
                if (oldProcessed != null) oldProcessed.Dispose();

                Image old = _txPreview.Image;
                _txPreview.Image = display;
                if (old != null) old.Dispose();

                int width;
                int height;
                GetRasterSize(out width, out height);
                double seconds = WaterfallIDAdvancedGenerator.EstimateDuration(
                    height,
                    (int)_rowMs.Value,
                    (int)_preMs.Value,
                    (int)_postMs.Value,
                    (int)_repeat.Value,
                    (int)_pauseMs.Value);

                DSPMode mode = _console.WaterfallIDCurrentTXMode;
                _modeBand.Text = mode + "  " + low + "-" + high + " Hz  " + width + "x" + height +
                    (_console.WaterfallIDIsLowerSideband ? "  MIRRORED" : "  NORMAL");

                _status.Text = "READY  |  " + (ImageMode ? "IMAGE" : "TEXT") +
                    "  |  estimated TX " + seconds.ToString("0.0", CultureInfo.InvariantCulture) +
                    " s  |  repeat " + ((int)_repeat.Value).ToString(CultureInfo.InvariantCulture) +
                    "  |  output normalized to 0.68 FS; playback gain " + _levelDb.Value + " dB";
            }
            catch (Exception ex)
            {
                _status.Text = "PREVIEW: " + ex.Message;
            }
        }

        private void LoadImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Load Waterfall ID image";
                dlg.Filter = "Images (*.bmp;*.png;*.jpg;*.jpeg)|*.bmp;*.png;*.jpg;*.jpeg|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                LoadImageFile(dlg.FileName);
            }
        }

        private void LoadImageFile(string fileName)
        {
            try
            {
                using (Image loaded = Image.FromFile(fileName))
                {
                    SetSourceImage(new Bitmap(loaded), fileName);
                }
                _sourceTabs.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                _status.Text = "IMAGE LOAD FAILED: " + ex.Message;
            }
        }

        private void PasteImage_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsImage())
                {
                    _status.Text = "Clipboard does not contain an image.";
                    return;
                }

                using (Image img = Clipboard.GetImage())
                {
                    SetSourceImage(img == null ? null : new Bitmap(img), "Clipboard");
                }
                _sourceTabs.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                _status.Text = "PASTE FAILED: " + ex.Message;
            }
        }

        private void SetSourceImage(Bitmap image, string source)
        {
            Bitmap old = _sourceImage;
            _sourceImage = image;
            _sourcePath = source;
            if (old != null) old.Dispose();

            Image oldPreview = _sourcePreview.Image;
            _sourcePreview.Image = image == null ? null : new Bitmap(image);
            if (oldPreview != null) oldPreview.Dispose();

            _sourcePathLabel.Text = image == null ? "No image loaded" : (source ?? "Image");
            RefreshPreview();
        }

        private void WaterfallIDAdvancedForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void WaterfallIDAdvancedForm_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                LoadImageFile(files[0]);
            }
            catch (Exception ex)
            {
                _status.Text = "DROP FAILED: " + ex.Message;
            }
        }

        private string CreateWave(string fileName)
        {
            int low;
            int high;
            GetAudioBand(out low, out high);
            if (high <= low + 200)
                throw new InvalidOperationException("TX audio passband is too narrow.");

            using (Bitmap raster = BuildProcessedRaster())
            {
                double seconds = WaterfallIDAdvancedGenerator.GenerateWaveFile(
                    raster,
                    fileName,
                    low,
                    high,
                    (int)_rowMs.Value,
                    _console.WaterfallIDIsLowerSideband,
                    (int)_preMs.Value,
                    (int)_postMs.Value,
                    (int)_repeat.Value,
                    (int)_pauseMs.Value);

                return seconds.ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        private void Send_Click(object sender, EventArgs e)
        {
            if (!_console.WaterfallIDModeSupported)
            {
                _status.Text = "TX BLOCKED: select USB, LSB, DIGU or DIGL.";
                return;
            }

            string wav = Path.Combine(Path.GetTempPath(), "Thetis-WaterfallID-" + Guid.NewGuid().ToString("N") + ".wav");
            _send.Enabled = false;
            _status.Text = "GENERATING Waterfall ID...";
            Application.DoEvents();

            try
            {
                string seconds = CreateWave(wav);
                string error;
                if (!_console.StartWaterfallID(wav, (double)_levelDb.Value, out error))
                {
                    try { if (File.Exists(wav)) File.Delete(wav); } catch { }
                    _status.Text = "TX BLOCKED: " + error;
                    _send.Enabled = true;
                    _stop.Enabled = false;
                    return;
                }

                _status.Text = "TX ACTIVE  |  " + seconds + " s  |  " +
                    (_console.WaterfallIDIsLowerSideband ? "LSB/DIGL mirrored" : "normal orientation");
                _stop.Enabled = true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(wav)) File.Delete(wav); } catch { }
                _status.Text = "GENERATOR ERROR: " + ex.Message;
                _send.Enabled = true;
                _stop.Enabled = false;
            }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Export Waterfall ID WAV";
                dlg.Filter = "WAV file (*.wav)|*.wav";
                dlg.FileName = "Waterfall-ID.wav";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string seconds = CreateWave(dlg.FileName);
                    _status.Text = "WAV EXPORTED  |  " + seconds + " s  |  " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    _status.Text = "EXPORT FAILED: " + ex.Message;
                }
            }
        }

        private void Stop_Click(object sender, EventArgs e)
        {
            string error;
            if (!_console.StopWaterfallID(out error) && !string.IsNullOrWhiteSpace(error))
                _status.Text = "STOP ERROR: " + error;
        }

        private void Console_WaterfallIDStateChanged(bool active, string status)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)delegate { Console_WaterfallIDStateChanged(active, status); }); }
                catch { }
                return;
            }

            _send.Enabled = !active;
            _stop.Enabled = active;
            _status.Text = active ? "TX ACTIVE  |  " + status : status;
            if (!active) RefreshPreview();
        }

        private void SavePreset_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Save Waterfall ID preset";
                dlg.Filter = "Waterfall ID preset (*.wfid)|*.wfid|Text file (*.txt)|*.txt";
                dlg.FileName = "Waterfall-ID.wfid";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    using (StreamWriter w = new StreamWriter(dlg.FileName, false))
                    {
                        w.WriteLine("version=1");
                        w.WriteLine("mode=" + (ImageMode ? "image" : "text"));
                        w.WriteLine("text=" + EscapePreset(_text.Text));
                        w.WriteLine("font=" + EscapePreset(_font.Text));
                        w.WriteLine("bold=" + _bold.Checked);
                        w.WriteLine("align=" + _textAlign.SelectedIndex);
                        w.WriteLine("image=" + EscapePreset(_sourcePath ?? string.Empty));
                        w.WriteLine("fit=" + _fitMode.SelectedIndex);
                        w.WriteLine("raster=" + _raster.SelectedIndex);
                        w.WriteLine("brightness=" + _brightness.Value);
                        w.WriteLine("contrast=" + _contrast.Value);
                        w.WriteLine("gamma=" + _gamma.Value);
                        w.WriteLine("threshold=" + _threshold.Value);
                        w.WriteLine("invert=" + _invert.Checked);
                        w.WriteLine("binary=" + _binary.Checked);
                        w.WriteLine("autolevels=" + _autoLevels.Checked);
                        w.WriteLine("sharpen=" + _sharpen.Checked);
                        w.WriteLine("autoband=" + _autoBand.Checked);
                        w.WriteLine("low=" + _lowHz.Value);
                        w.WriteLine("high=" + _highHz.Value);
                        w.WriteLine("rowms=" + _rowMs.Value);
                        w.WriteLine("leveldb=" + _levelDb.Value);
                        w.WriteLine("prems=" + _preMs.Value);
                        w.WriteLine("postms=" + _postMs.Value);
                        w.WriteLine("repeat=" + _repeat.Value);
                        w.WriteLine("pausems=" + _pauseMs.Value);
                    }
                    _status.Text = "PRESET SAVED: " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    _status.Text = "PRESET SAVE FAILED: " + ex.Message;
                }
            }
        }

        private void LoadPreset_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Load Waterfall ID preset";
                dlg.Filter = "Waterfall ID preset (*.wfid;*.txt)|*.wfid;*.txt|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string imagePath = null;
                    string mode = "text";
                    string[] lines = File.ReadAllLines(dlg.FileName);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int eq = lines[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string key = lines[i].Substring(0, eq).Trim().ToLowerInvariant();
                        string value = UnescapePreset(lines[i].Substring(eq + 1));

                        if (key == "mode") mode = value;
                        else if (key == "text") _text.Text = value;
                        else if (key == "font") SelectComboText(_font, value);
                        else if (key == "bold") _bold.Checked = ParseBool(value, _bold.Checked);
                        else if (key == "align") SetComboIndex(_textAlign, value);
                        else if (key == "image") imagePath = value;
                        else if (key == "fit") SetComboIndex(_fitMode, value);
                        else if (key == "raster") SetComboIndex(_raster, value);
                        else if (key == "brightness") SetNumericText(_brightness, value);
                        else if (key == "contrast") SetNumericText(_contrast, value);
                        else if (key == "gamma") SetNumericText(_gamma, value);
                        else if (key == "threshold") SetNumericText(_threshold, value);
                        else if (key == "invert") _invert.Checked = ParseBool(value, _invert.Checked);
                        else if (key == "binary") _binary.Checked = ParseBool(value, _binary.Checked);
                        else if (key == "autolevels") _autoLevels.Checked = ParseBool(value, _autoLevels.Checked);
                        else if (key == "sharpen") _sharpen.Checked = ParseBool(value, _sharpen.Checked);
                        else if (key == "autoband") _autoBand.Checked = ParseBool(value, _autoBand.Checked);
                        else if (key == "low") SetNumericText(_lowHz, value);
                        else if (key == "high") SetNumericText(_highHz, value);
                        else if (key == "rowms") SetNumericText(_rowMs, value);
                        else if (key == "leveldb") SetNumericText(_levelDb, value);
                        else if (key == "prems") SetNumericText(_preMs, value);
                        else if (key == "postms") SetNumericText(_postMs, value);
                        else if (key == "repeat") SetNumericText(_repeat, value);
                        else if (key == "pausems") SetNumericText(_pauseMs, value);
                    }

                    if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath) && !string.Equals(imagePath, "Clipboard", StringComparison.OrdinalIgnoreCase))
                        LoadImageFile(imagePath);

                    _sourceTabs.SelectedIndex = string.Equals(mode, "image", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    RefreshPreview();
                    _status.Text = "PRESET LOADED: " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    _status.Text = "PRESET LOAD FAILED: " + ex.Message;
                }
            }
        }

        private static string EscapePreset(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string UnescapePreset(string value)
        {
            if (value == null) return string.Empty;
            string result = value.Replace("\\n", "\n").Replace("\\r", "\r");
            return result.Replace("\\\\", "\\");
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static void SelectComboText(ComboBox combo, string value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private static void SetComboIndex(ComboBox combo, string value)
        {
            int i;
            if (int.TryParse(value, out i) && i >= 0 && i < combo.Items.Count)
                combo.SelectedIndex = i;
        }

        private static void SetNumericText(NumericUpDown control, string value)
        {
            decimal parsed;
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) &&
                !decimal.TryParse(value, out parsed)) return;
            SetNumericSafe(control, parsed);
        }

        private void WaterfallIDAdvancedForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SavePersistentState();

            if (_console.WaterfallIDIsActive)
            {
                string ignored;
                _console.StopWaterfallID(out ignored);
            }

            _console.WaterfallIDStateChanged -= Console_WaterfallIDStateChanged;

            if (_sourceImage != null) _sourceImage.Dispose();
            _sourceImage = null;
            if (_lastProcessed != null) _lastProcessed.Dispose();
            _lastProcessed = null;

            Image p = _sourcePreview.Image;
            _sourcePreview.Image = null;
            if (p != null) p.Dispose();
            p = _txPreview.Image;
            _txPreview.Image = null;
            if (p != null) p.Dispose();
        }
    }

    internal static class WaterfallIDAdvancedGenerator
    {
        private const int SampleRate = 48000;
        private const double TargetPeak = 0.68;

        internal static Bitmap RenderText(string text, int width, int height, string fontName, bool bold, int alignment)
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

                StringFormat format = new StringFormat();
                format.Alignment = alignment == 0 ? StringAlignment.Near : (alignment == 2 ? StringAlignment.Far : StringAlignment.Center);
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.None;

                FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
                float fontSize = Math.Min(32f, height - 2f);
                Font font = null;
                RectangleF bounds = new RectangleF(2f, 1f, width - 4f, height - 2f);

                try
                {
                    while (fontSize >= 6f)
                    {
                        if (font != null) font.Dispose();
                        font = new Font(string.IsNullOrWhiteSpace(fontName) ? "Arial" : fontName, fontSize, style, GraphicsUnit.Pixel);
                        SizeF measured = g.MeasureString(value, font, new SizeF(bounds.Width, 1000f), format);
                        if (measured.Width <= bounds.Width + 1f && measured.Height <= bounds.Height + 1f) break;
                        fontSize -= 1f;
                    }

                    if (font == null)
                        font = new Font("Arial", 6f, style, GraphicsUnit.Pixel);

                    g.DrawString(value, font, Brushes.White, bounds, format);
                }
                finally
                {
                    if (font != null) font.Dispose();
                    format.Dispose();
                }
            }

            return bitmap;
        }

        internal static Bitmap ProcessImage(Image source, int width, int height, int fitMode, int brightness, int contrast, double gamma, int threshold, bool invert, bool binary, bool autoLevels, bool sharpen)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (width < 8 || height < 8) throw new ArgumentException("Raster is too small.");

            Bitmap scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle dest = CalculateDestination(source.Width, source.Height, width, height, fitMode);
                g.DrawImage(source, dest, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            }

            double[,] values = new double[width, height];
            double min = 1.0;
            double max = 0.0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = scaled.GetPixel(x, y);
                    double v = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                    values[x, y] = v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
            scaled.Dispose();

            if (autoLevels && max > min + 0.001)
            {
                double span = max - min;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        values[x, y] = (values[x, y] - min) / span;
            }

            double brightnessOffset = brightness / 100.0;
            double contrastFactor = (100.0 + contrast) / 100.0;
            contrastFactor *= contrastFactor;
            if (gamma < 0.05) gamma = 0.05;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double v = values[x, y];
                    v = ((v - 0.5) * contrastFactor) + 0.5 + brightnessOffset;
                    v = Clamp01(v);
                    v = Math.Pow(v, 1.0 / gamma);
                    if (invert) v = 1.0 - v;
                    values[x, y] = Clamp01(v);
                }
            }

            if (sharpen)
                values = Sharpen(values, width, height);

            double t = threshold / 255.0;
            Bitmap output = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double v = values[x, y];
                    if (binary)
                        v = v >= t ? 1.0 : 0.0;
                    else if (t > 0.0)
                        v = v <= t ? 0.0 : Clamp01((v - t) / (1.0 - t));

                    int b = (int)Math.Round(Clamp01(v) * 255.0);
                    output.SetPixel(x, y, Color.FromArgb(b, b, b));
                }
            }

            return output;
        }

        private static Rectangle CalculateDestination(int sourceWidth, int sourceHeight, int width, int height, int fitMode)
        {
            if (fitMode == 2 || sourceWidth <= 0 || sourceHeight <= 0)
                return new Rectangle(0, 0, width, height);

            double sx = width / (double)sourceWidth;
            double sy = height / (double)sourceHeight;
            double scale = fitMode == 1 ? Math.Max(sx, sy) : Math.Min(sx, sy);
            int w = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            return new Rectangle((width - w) / 2, (height - h) / 2, w, h);
        }

        private static double[,] Sharpen(double[,] input, int width, int height)
        {
            double[,] output = new double[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        output[x, y] = input[x, y];
                        continue;
                    }

                    double v = 5.0 * input[x, y]
                        - input[x - 1, y]
                        - input[x + 1, y]
                        - input[x, y - 1]
                        - input[x, y + 1];
                    output[x, y] = Clamp01(v);
                }
            }
            return output;
        }

        internal static Bitmap Mirror(Bitmap source)
        {
            Bitmap copy = new Bitmap(source);
            copy.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return copy;
        }

        internal static double EstimateDuration(int height, int rowDurationMs, int preMs, int postMs, int repeat, int pauseMs)
        {
            const double fadeMs = 5.0;
            return (preMs + postMs + repeat * (height * rowDurationMs + fadeMs) + Math.Max(0, repeat - 1) * pauseMs) / 1000.0;
        }

        internal static double GenerateWaveFile(Bitmap raster, string fileName, int lowHz, int highHz, int rowDurationMs, bool mirrorForLowerSideband, int preMs, int postMs, int repeat, int pauseMs)
        {
            if (raster == null) throw new ArgumentNullException("raster");
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Output WAV path is empty.");
            if (lowHz < 50) lowHz = 50;
            if (highHz > 10000) highHz = 10000;
            if (highHz <= lowHz + 200) throw new ArgumentException("TX audio passband is too narrow for Waterfall ID.");
            if (rowDurationMs < 60) rowDurationMs = 60;
            if (rowDurationMs > 500) rowDurationMs = 500;
            if (preMs < 0) preMs = 0;
            if (postMs < 0) postMs = 0;
            if (repeat < 1) repeat = 1;
            if (repeat > 5) repeat = 5;
            if (pauseMs < 0) pauseMs = 0;

            int width = raster.Width;
            int height = raster.Height;
            if (width < 2 || height < 2) throw new ArgumentException("Waterfall raster is too small.");

            double[,] pixels = new double[width, height];
            bool any = false;
            for (int row = 0; row < height; row++)
            {
                int sourceY = height - 1 - row;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = mirrorForLowerSideband ? (width - 1 - x) : x;
                    Color c = raster.GetPixel(sourceX, sourceY);
                    double v = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                    if (v < 0.003) v = 0.0;
                    pixels[x, row] = v;
                    if (v > 0.0) any = true;
                }
            }
            if (!any) throw new InvalidOperationException("Waterfall raster contains no active pixels.");

            int rowSamples = (int)Math.Round(SampleRate * rowDurationMs / 1000.0);
            int fadeSamples = Math.Max(1, SampleRate / 200);
            int preSamples = (int)Math.Round(SampleRate * preMs / 1000.0);
            int postSamples = (int)Math.Round(SampleRate * postMs / 1000.0);
            int pauseSamples = (int)Math.Round(SampleRate * pauseMs / 1000.0);
            long totalLong = preSamples + postSamples + (long)repeat * (height * (long)rowSamples + fadeSamples) + (long)Math.Max(0, repeat - 1) * pauseSamples;
            if (totalLong > int.MaxValue) throw new InvalidOperationException("Generated Waterfall ID is too long.");
            int totalSamples = (int)totalLong;
            double[] audio = new double[totalSamples];

            double[] sinPhase = new double[width];
            double[] cosPhase = new double[width];
            double[] sinInc = new double[width];
            double[] cosInc = new double[width];
            for (int x = 0; x < width; x++)
            {
                double fraction = x / (double)(width - 1);
                double frequency = lowHz + (highHz - lowHz) * fraction;
                double delta = 2.0 * Math.PI * frequency / SampleRate;
                sinPhase[x] = 0.0;
                cosPhase[x] = 1.0;
                sinInc[x] = Math.Sin(delta);
                cosInc[x] = Math.Cos(delta);
            }

            int outIndex = preSamples;
            double[] previousAmplitude = new double[width];

            for (int r = 0; r < repeat; r++)
            {
                for (int x = 0; x < width; x++) previousAmplitude[x] = 0.0;

                for (int row = 0; row < height; row++)
                {
                    for (int s = 0; s < rowSamples; s++)
                    {
                        double transition = s >= fadeSamples ? 1.0 : s / (double)fadeSamples;
                        double sample = 0.0;
                        for (int x = 0; x < width; x++)
                        {
                            double target = pixels[x, row];
                            double amplitude = previousAmplitude[x] + (target - previousAmplitude[x]) * transition;
                            if (amplitude > 0.0) sample += amplitude * sinPhase[x];
                            AdvanceOscillator(x, sinPhase, cosPhase, sinInc, cosInc);
                        }
                        audio[outIndex++] = sample;
                    }
                    for (int x = 0; x < width; x++) previousAmplitude[x] = pixels[x, row];
                }

                for (int s = 0; s < fadeSamples; s++)
                {
                    double transition = 1.0 - s / (double)fadeSamples;
                    double sample = 0.0;
                    for (int x = 0; x < width; x++)
                    {
                        double amplitude = previousAmplitude[x] * transition;
                        if (amplitude > 0.0) sample += amplitude * sinPhase[x];
                        AdvanceOscillator(x, sinPhase, cosPhase, sinInc, cosInc);
                    }
                    audio[outIndex++] = sample;
                }

                if (r < repeat - 1)
                    outIndex += pauseSamples;
            }

            double peak = 0.0;
            for (int i = 0; i < audio.Length; i++)
            {
                double a = Math.Abs(audio[i]);
                if (a > peak) peak = a;
            }
            if (peak < 1e-12) throw new InvalidOperationException("Waterfall ID audio contains no signal.");
            double scale = TargetPeak / peak;

            string dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

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
                    double v = audio[i] * scale;
                    if (v > 0.999969) v = 0.999969;
                    if (v < -1.0) v = -1.0;
                    short pcm = (short)Math.Round(v * 32767.0);
                    writer.Write(pcm);
                    writer.Write(pcm);
                }
            }

            return audio.Length / (double)SampleRate;
        }

        private static void AdvanceOscillator(int x, double[] sinPhase, double[] cosPhase, double[] sinInc, double[] cosInc)
        {
            double oldSin = sinPhase[x];
            double oldCos = cosPhase[x];
            sinPhase[x] = oldSin * cosInc[x] + oldCos * sinInc[x];
            cosPhase[x] = oldCos * cosInc[x] - oldSin * sinInc[x];
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
