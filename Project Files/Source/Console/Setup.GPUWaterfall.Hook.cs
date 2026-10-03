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

                // Restore palette text before ApplyLoadedGPUWaterfallSettings().
                // Otherwise ApplyLoaded... fires the legacy palette handler first
                // and can overwrite a saved 256-entry choice before it is restored.
                RestoreGPUWaterfallPaletteSelections();
                ApplyLoadedGPUWaterfallSettings();
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
            if (initializing || _gpuWaterfallUiLoading) return;
            Display.InvalidateNativeWaterfallPalette();

            try
            {
                string rx1 = comboColorPalette?.Text ?? "";
                string rx2 = comboRX2ColorPalette?.Text ?? "";
                string tx = comboColorPalette_tx?.Text ?? "";

                // Ordinary Setup persistence is authoritative for palette choice.
                // The palette combos now contain the 256-entry items before
                // getOptions(), so these exact strings can be restored at startup.
                Dictionary<string,string> options =
                    DB.GetVarsDictionary("Options") ?? new Dictionary<string,string>();
                options["comboColorPalette"] = rx1;
                options["comboRX2ColorPalette"] = rx2;
                options["comboColorPalette_tx"] = tx;
                DB.SaveVarsDictionary("Options", ref options, true);

                // Keep the earlier GPU-waterfall store synchronized so existing
                // databases remain compatible with the previous implementation.
                Dictionary<string,string> ui =
                    DB.GetVarsDictionary("GPUWaterfallUI") ?? new Dictionary<string,string>();
                ui["__RX1PaletteText"] = rx1;
                ui["__RX2PaletteText"] = rx2;
                ui["__TXPaletteText"] = tx;
                DB.SaveVarsDictionary("GPUWaterfallUI", ref ui, true);

                // Saves the enum values too and flushes all pending DB changes.
                Display.PersistNativeGPUWaterfallSettings();

                GPUWaterfallLogger.Log("PALETTE",
                    "Persisted RX1=" + rx1 + " RX2=" + rx2 + " TX=" + tx);
            }
            catch (Exception ex)
            {
                GPUWaterfallLogger.Log("PALETTE-SAVE-FAIL", ex.Message);
            }
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
                Dictionary<string,string> options =
                    DB.GetVarsDictionary("Options") ?? new Dictionary<string,string>();
                Dictionary<string,string> legacy =
                    DB.GetVarsDictionary("GPUWaterfallUI") ?? new Dictionary<string,string>();

                void RestoreCombo(ComboBox combo, string optionKey, string legacyKey)
                {
                    if (combo == null) return;

                    options.TryGetValue(optionKey, out string optionText);
                    legacy.TryGetValue(legacyKey, out string legacyText);

                    bool Is256(string value) =>
                        !string.IsNullOrEmpty(value) &&
                        value.EndsWith(" 256", StringComparison.OrdinalIgnoreCase);

                    // Migration rule for databases written by the previous build:
                    // if the normal Options restore fell back to legacy "Enhanced"
                    // but GPUWaterfallUI still contains "Enhanced 256", preserve the
                    // explicit 256-entry choice. Once restored it is written back to
                    // Options by the normal palette handler.
                    string text = Is256(optionText) ? optionText :
                                  Is256(legacyText) ? legacyText :
                                  !string.IsNullOrEmpty(optionText) ? optionText :
                                  legacyText;
                    if (string.IsNullOrEmpty(text)) return;

                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        if (string.Equals(combo.Items[i]?.ToString(), text,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            combo.SelectedIndex = i;
                            return;
                        }
                    }
                }

                RestoreCombo(comboColorPalette, "comboColorPalette", "__RX1PaletteText");
                RestoreCombo(comboRX2ColorPalette, "comboRX2ColorPalette", "__RX2PaletteText");
                RestoreCombo(comboColorPalette_tx, "comboColorPalette_tx", "__TXPaletteText");

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
