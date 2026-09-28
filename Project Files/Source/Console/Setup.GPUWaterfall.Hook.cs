using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Thetis
{
    public partial class Setup
    {
        private bool _gpuWaterfallUiBuilt;
        private bool _gpuWaterfallUiLoading;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_gpuWaterfallUiBuilt)
            {
                _gpuWaterfallUiBuilt = true;
                Display.EnsureNativeGPUWaterfallSettingsLoaded();
                InitGPUWaterfallSetupUI();
                LoadGPUWaterfallUIState();
                ApplyLoadedGPUWaterfallSettings();
                RestoreGPUWaterfallPaletteSelections();
                WireGPUWaterfallPersistence(_tpWaterfall);

                if (comboColorPalette != null) comboColorPalette.SelectedIndexChanged += GPUWaterfallControlChanged;
                if (comboRX2ColorPalette != null) comboRX2ColorPalette.SelectedIndexChanged += GPUWaterfallControlChanged;
                if (comboColorPalette_tx != null) comboColorPalette_tx.SelectedIndexChanged += GPUWaterfallControlChanged;
            }
        }

        private IEnumerable<Control> EnumerateGPUWaterfallControls(Control root)
        {
            if (root == null) yield break;
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control cc in EnumerateGPUWaterfallControls(c)) yield return cc;
            }
        }

        private void WireGPUWaterfallPersistence(Control root)
        {
            foreach (Control c in EnumerateGPUWaterfallControls(root))
            {
                if (c is CheckBox cb) cb.CheckedChanged += GPUWaterfallControlChanged;
                else if (c is ComboBox co) co.SelectedIndexChanged += GPUWaterfallControlChanged;
                else if (c is NumericUpDown nu) nu.ValueChanged += GPUWaterfallControlChanged;
                else if (c is TrackBar tb) tb.ValueChanged += GPUWaterfallControlChanged;
            }
        }

        private void GPUWaterfallControlChanged(object sender, EventArgs e)
        {
            if (!_gpuWaterfallUiLoading) SaveGPUWaterfallUIState();
        }

        private void PersistWaterfallPaletteSettings()
        {
            SaveGPUWaterfallUIState();
        }

        private void SaveGPUWaterfallUIState()
        {
            if (_gpuWaterfallUiLoading || _tpWaterfall == null) return;
            try
            {
                Dictionary<string,string> d = DB.GetVarsDictionary("GPUWaterfallUI") ?? new Dictionary<string,string>();
                foreach (Control c in EnumerateGPUWaterfallControls(_tpWaterfall))
                {
                    if (string.IsNullOrEmpty(c.Name)) continue;
                    if (c is CheckBox cb) d[c.Name] = cb.Checked.ToString();
                    else if (c is ComboBox co) d[c.Name] = co.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                    else if (c is NumericUpDown nu) d[c.Name] = nu.Value.ToString(CultureInfo.InvariantCulture);
                    else if (c is TrackBar tb) d[c.Name] = tb.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (comboColorPalette != null) d["__RX1PaletteText"] = comboColorPalette.Text;
                if (comboRX2ColorPalette != null) d["__RX2PaletteText"] = comboRX2ColorPalette.Text;
                if (comboColorPalette_tx != null) d["__TXPaletteText"] = comboColorPalette_tx.Text;

                DB.SaveVarsDictionary("GPUWaterfallUI", ref d, true);
                DB.WriteDB();
                Display.PersistNativeGPUWaterfallSettings();
            }
            catch { }
        }

        private void RestoreGPUWaterfallPaletteSelections()
        {
            _gpuWaterfallUiLoading = true;
            try
            {
                Dictionary<string,string> d = DB.GetVarsDictionary("GPUWaterfallUI") ?? new Dictionary<string,string>();

                void RestoreCombo(ComboBox combo, string key)
                {
                    if (combo == null || !d.TryGetValue(key, out string text) || string.IsNullOrEmpty(text)) return;
                    int index = combo.FindStringExact(text);
                    if (index >= 0) combo.SelectedIndex = index;
                }

                RestoreCombo(comboColorPalette, "__RX1PaletteText");
                RestoreCombo(comboRX2ColorPalette, "__RX2PaletteText");
                RestoreCombo(comboColorPalette_tx, "__TXPaletteText");

                EventArgs e = EventArgs.Empty;
                comboColorPalette_SelectedIndexChanged(this, e);
                comboRX2ColorPalette_SelectedIndexChanged(this, e);
                comboColorPalette_tx_SelectedIndexChanged(this, e);
            }
            catch { }
            finally { _gpuWaterfallUiLoading = false; }
        }

        private void LoadGPUWaterfallUIState()
        {
            if (_tpWaterfall == null) return;
            _gpuWaterfallUiLoading = true;
            try
            {
                Dictionary<string,string> d = DB.GetVarsDictionary("GPUWaterfallUI");
                foreach (Control c in EnumerateGPUWaterfallControls(_tpWaterfall))
                {
                    if (string.IsNullOrEmpty(c.Name) || !d.TryGetValue(c.Name, out string v)) continue;
                    if (c is CheckBox cb && bool.TryParse(v, out bool b)) cb.Checked = b;
                    else if (c is ComboBox co && int.TryParse(v, out int i) && i >= 0 && i < co.Items.Count) co.SelectedIndex = i;
                    else if (c is NumericUpDown nu && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal m))
                        nu.Value = Math.Max(nu.Minimum, Math.Min(nu.Maximum, m));
                    else if (c is TrackBar tb && int.TryParse(v, out int t))
                        tb.Value = Math.Max(tb.Minimum, Math.Min(tb.Maximum, t));
                }
            }
            catch { }
            finally { _gpuWaterfallUiLoading = false; }
        }
    }
}
