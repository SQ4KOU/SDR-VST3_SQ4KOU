/*  GlobalMouseHandler.cs

This file is part of a program that implements a Software-Defined Radio.

This code/file can be found on GitHub : https://github.com/ramdor/Thetis

Copyright (C) 2000-2025 Original authors
Copyright (C) 2020-2026 Richard Samphire MW0LGE

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

The author can be reached by email at

mw0lge@grange-lane.co.uk
*/
//
//============================================================================================//
// Dual-Licensing Statement (Applies Only to Author's Contributions, Richard Samphire MW0LGE) //
// ------------------------------------------------------------------------------------------ //
// For any code originally written by Richard Samphire MW0LGE, or for any modifications       //
// made by him, the copyright holder for those portions (Richard Samphire) reserves the       //
// right to use, license, and distribute such code under different terms, including           //
// closed-source and proprietary licences, in addition to the GNU General Public License      //
// granted above. Nothing in this statement restricts any rights granted to recipients under  //
// the GNU GPL. Code contributed by others (not Richard Samphire) remains licensed under      //
// its original terms and is not affected by this dual-licensing statement in any way.        //
// Richard Samphire can be reached by email at :  mw0lge@grange-lane.co.uk                    //
//============================================================================================//

using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Thetis
{
    public delegate void MouseMovedEvent();

    public class GlobalMouseHandler : IMessageFilter
    {
        private const int WM_MOUSEMOVE = 0x0200;
        //private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        public event MouseMovedEvent MouseUp; 
        public event MouseEventHandler MouseMove;   //MW0LGE_21e

        //public event MouseMovedEvent MouseDown;

        #region IMessageFilter Members

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_MOUSEMOVE)
            {
                MouseMove?.Invoke(null, new MouseEventArgs(MouseButtons.None,0, Control.MousePosition.X, Control.MousePosition.Y, 0));
            }
            //if (m.Msg == WM_LBUTTONDOWN)
            //{
            //    if (MouseUp != null)
            //    {
            //        MouseDown();
            //    }
            //}

            if (m.Msg == WM_LBUTTONUP)
            {
                if (MouseUp != null)
                {
                    MouseUp();
                }
            }

            // Always allow message to continue to the next filter control
            return false;
        }

        #endregion
    }


    // SQ4KOU native Console panel move support.
    // These are the actual built-in WinForms PanelTS blocks from console.Designer.cs
    // (BAND, MODE, FILTER, VFO, DSP, RX2 blocks, etc.), not Meters/Gadgets.
    public partial class Console
    {
        private NativePanelShiftDragFilter _nativePanelShiftDragFilter;
        private Control _nativePanelDragPrimary;
        private Dictionary<Control, Point> _nativePanelDragOrigins;
        private Point _nativePanelDragMouseStart;
        private Point? _nativeModeSpecificSharedLocation;
        private bool _nativeApplyingModeSpecificLocation;
        private bool _nativeModeSpecificRestorePending;
        // Authoritative positions for panels that have been restored from State or
        // explicitly moved by the operator. Once a position enters this table it is
        // "welded": native layout code may resize/show/hide the control, but may not
        // change its X/Y. Only Shift+LMB drag is allowed to update the locked point.
        private readonly Dictionary<string, Point> _nativeLockedPanelLocations =
            new Dictionary<string, Point>(StringComparer.Ordinal);
        // Authorization is per-control, not global. A LocationChanged callback can
        // synchronously trigger layout of other controls; those other controls must
        // remain protected even while one explicit restore is in progress.
        private readonly HashSet<Control> _nativeAuthorizedLocationWrites =
            new HashSet<Control>();

        private static readonly string[] _nativeModeSpecificPanelNames =
        {
            "panelModeSpecificCW",
            "panelModeSpecificPhone",
            "panelModeSpecificDigital",
            "panelModeSpecificFM"
        };

        private static readonly HashSet<string> _nativeMovablePanelNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                // Top-level PanelTS blocks
                "panelBandHF",
                "panelBandVHF",
                "panelBandGEN",
                "panelMode",
                "panelFilter",
                "panelDisplay",
                "panelDisplay2",
                "panelMeterLabels",
                "panelOptions",
                "panelSoundControls",
                "panelVFO",
                "panelVFOALabels",
                "panelVFOBLabels",
                "panelVFOLabels",
                "panelDSP",
                "panelMultiRX",
                "panelPower",
                "panelModeSpecificCW",
                "panelModeSpecificPhone",
                "panelModeSpecificDigital",
                "panelModeSpecificFM",
                "panelRX2Mixer",
                "panelRX2DSP",
                "panelRX2Display",
                "panelRX2Mode",
                "panelRX2Filter",
                "panelRX2Power",
                "panelRX2RF",
                "panelButtonBar",
                "panelAndromedaMisc",

                // RX1 squelch is not contained in a PanelTS in the native
                // designer. It is three separate top-level controls that
                // visually form one movable block.
                "chkSquelch",
                "ptbSquelch",
                "picSquelch",

                // Top-level GroupBoxTS blocks. These were previously excluded
                // because the drag resolver accepted PanelTS only.
                "grpMultimeter",
                "grpRX2Meter",
                "grpDisplaySplit",
                "grpVFOBetween",
                "grpVFOA",
                "grpVFOB",
                "grpMultimeterMenus"
            };

        private void InitializeNativePanelShiftDrag()
        {
            if (_nativePanelShiftDragFilter != null) return;

            _nativePanelShiftDragFilter = new NativePanelShiftDragFilter(this);
            Application.AddMessageFilter(_nativePanelShiftDragFilter);
            InitializeNativeModeSpecificLocationGuard();
            InitializeNativeWeldedLocationGuard();
        }

        private void InitializeNativeWeldedLocationGuard()
        {
            foreach (string name in _nativeMovablePanelNames)
            {
                Control panel = Controls.Find(name, true).FirstOrDefault();
                if (panel == null) continue;

                panel.LocationChanged -= NativeWeldedPanel_LocationChanged;
                panel.LocationChanged += NativeWeldedPanel_LocationChanged;
            }
        }

        private bool IsNativePanelInActiveShiftDrag(Control panel)
        {
            return panel != null &&
                   _nativePanelDragPrimary != null &&
                   _nativePanelDragOrigins != null &&
                   _nativePanelDragOrigins.ContainsKey(panel);
        }

        private void SetNativePanelLocationAuthorized(Control panel, Point location, bool updateLock)
        {
            if (panel == null) return;

            _nativeAuthorizedLocationWrites.Add(panel);
            try
            {
                if (panel.Location != location)
                    panel.Location = location;
            }
            finally
            {
                _nativeAuthorizedLocationWrites.Remove(panel);
            }

            if (updateLock)
                _nativeLockedPanelLocations[panel.Name] = location;
        }

        private void NativeWeldedPanel_LocationChanged(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            Control panel = sender as Control;
            if (panel == null || !_nativeMovablePanelNames.Contains(panel.Name))
                return;

            if (_nativeAuthorizedLocationWrites.Contains(panel))
                return;

            // Shift+LMB is the one and only path allowed to alter a locked position.
            if (IsNativePanelInActiveShiftDrag(panel))
                return;

            if (!_nativeLockedPanelLocations.TryGetValue(panel.Name, out Point locked))
                return;

            if (panel.Location == locked)
                return;

            // Restore synchronously. Deferred BeginInvoke restoration allowed a native
            // layout pass to win temporarily and was the source of visible/random jumps.
            SetNativePanelLocationAuthorized(panel, locked, false);
        }

        private void InitializeNativeModeSpecificLocationGuard()
        {
            Control seed = null;

            foreach (string name in _nativeModeSpecificPanelNames)
            {
                Control panel = Controls.Find(name, true).FirstOrDefault();
                if (panel == null) continue;

                if (seed == null) seed = panel;
                panel.VisibleChanged -= NativeModeSpecificPanel_VisibleChanged;
                panel.VisibleChanged += NativeModeSpecificPanel_VisibleChanged;
                panel.LocationChanged -= NativeModeSpecificPanel_LocationChanged;
                panel.LocationChanged += NativeModeSpecificPanel_LocationChanged;
            }

            if (seed != null && !_nativeModeSpecificSharedLocation.HasValue)
                _nativeModeSpecificSharedLocation = seed.Location;
        }

        private static bool IsNativeModeSpecificPanelName(string name)
        {
            return Array.IndexOf(_nativeModeSpecificPanelNames, name) >= 0;
        }

        private void QueueNativeModeSpecificRestore()
        {
            if (_nativeModeSpecificRestorePending || IsDisposed || Disposing)
                return;

            _nativeModeSpecificRestorePending = true;
            BeginInvoke((MethodInvoker)delegate
            {
                _nativeModeSpecificRestorePending = false;
                if (!IsDisposed && !Disposing && _nativePanelDragPrimary == null)
                    ApplyNativeModeSpecificSharedLocation();
            });
        }

        private void NativeModeSpecificPanel_VisibleChanged(object sender, EventArgs e)
        {
            if (_nativeApplyingModeSpecificLocation || !_nativeModeSpecificSharedLocation.HasValue)
                return;

            Control panel = sender as Control;
            if (panel == null || !panel.Visible || IsDisposed || Disposing)
                return;

            // Native mode switching can emit several Visible/Location events in one
            // BAND/Mode transition. Coalesce them to a single deferred restore so the
            // UI queue cannot accumulate redundant layout work during rapid band changes.
            QueueNativeModeSpecificRestore();
        }

        private void NativeModeSpecificPanel_LocationChanged(object sender, EventArgs e)
        {
            if (_nativeApplyingModeSpecificLocation || !_nativeModeSpecificSharedLocation.HasValue)
                return;

            if (_nativePanelDragPrimary != null &&
                IsNativeModeSpecificPanelName(_nativePanelDragPrimary.Name))
                return;

            Control panel = sender as Control;
            if (panel == null || IsDisposed || Disposing)
                return;

            if (_nativeAuthorizedLocationWrites.Contains(panel))
                return;

            Point shared = _nativeModeSpecificSharedLocation.Value;
            if (panel.Location == shared)
                return;

            QueueNativeModeSpecificRestore();
        }

        private void ApplyNativeModeSpecificSharedLocation()
        {
            if (!_nativeModeSpecificSharedLocation.HasValue) return;

            List<Control> panels = new List<Control>();
            foreach (string name in _nativeModeSpecificPanelNames)
            {
                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == name);
                if (panel != null) panels.Add(panel);
            }
            if (panels.Count == 0) return;

            Point requested = _nativeModeSpecificSharedLocation.Value;
            int maxX = panels.Min(p => Math.Max(0, ClientSize.Width - p.Width));
            int maxY = panels.Min(p => Math.Max(0, ClientSize.Height - p.Height));
            Point target = new Point(
                Math.Max(0, Math.Min(maxX, requested.X)),
                Math.Max(0, Math.Min(maxY, requested.Y)));

            _nativeModeSpecificSharedLocation = target;
            _nativeApplyingModeSpecificLocation = true;
            try
            {
                foreach (Control panel in panels)
                {
                    if (panel.Location != target)
                        panel.Location = target;
                }

                // Only the native mode-selection logic decides which one is visible.
                // Keep that currently visible panel on top after any delayed layout/
                // location restore. Without this, another top-level Console panel can
                // later cover panelModeSpecificPhone/CW/Digital/FM even though its
                // Visible property is still true.
                Control visiblePanel = panels.FirstOrDefault(p => p.Visible);
                if (visiblePanel != null)
                    visiblePanel.BringToFront();
            }
            finally
            {
                _nativeApplyingModeSpecificLocation = false;
            }
        }

        private const string NativePanelLocationKeyPrefix = "NativePanelLocation.";

        private static bool PersistNativePanelLocation(string name)
        {
            // RX2 panels are positioned by the native RX2 layout logic. Persisting and
            // restoring their coordinates while RX2 is OFF can pull those panels back
            // onto the Console even though RX2 itself was never enabled.
            return _nativeMovablePanelNames.Contains(name) &&
                   !name.StartsWith("panelRX2", StringComparison.Ordinal) &&
                   !name.StartsWith("grpRX2", StringComparison.Ordinal);
        }

        private void AppendNativePanelLocations(List<string> state)
        {
            if (state == null) return;

            foreach (string name in _nativeMovablePanelNames)
            {
                if (!PersistNativePanelLocation(name)) continue;

                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == name);

                if (panel == null) continue;

                Point location;
                if (_nativeLockedPanelLocations.TryGetValue(name, out Point locked))
                    location = locked;
                else if (IsNativeModeSpecificPanelName(name) && _nativeModeSpecificSharedLocation.HasValue)
                    location = _nativeModeSpecificSharedLocation.Value;
                else
                    location = panel.Location;

                state.Add(NativePanelLocationKeyPrefix + name + "/" +
                    location.X.ToString() + "|" + location.Y.ToString());
            }
        }

        private Dictionary<string, Point> CapturePersistedNativePanelLocations()
        {
            Dictionary<string, Point> captured = new Dictionary<string, Point>(StringComparer.Ordinal);

            foreach (string name in _nativeMovablePanelNames)
            {
                if (!PersistNativePanelLocation(name)) continue;

                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == name);
                if (panel != null)
                {
                    if (_nativeLockedPanelLocations.TryGetValue(name, out Point locked))
                        captured[name] = locked;
                    else
                        captured[name] = panel.Location;
                }
            }

            return captured;
        }

        private void RestoreCapturedNativePanelLocations(Dictionary<string, Point> captured)
        {
            if (captured == null || captured.Count == 0) return;

            foreach (KeyValuePair<string, Point> kvp in captured)
            {
                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == kvp.Key);
                if (panel == null) continue;

                // Snapshot restore is an authorized programmatic write, but it does
                // not create a new manual lock. Existing locks remain authoritative.
                SetNativePanelLocationAuthorized(panel, kvp.Value, false);
            }

            // RX2/layout transitions can alter top-level Z-order without changing
            // the mode-specific panel's Visible flag. Re-assert the shared position
            // and foreground ownership after the restore pass.
            ApplyNativeModeSpecificSharedLocation();
        }

        private void RestoreNativePanelLocationsFromState()
        {
            Dictionary<string, Point> saved = new Dictionary<string, Point>(StringComparer.Ordinal);

            foreach (string entry in DB.GetVars("State"))
            {
                int slash = entry.IndexOf('/');
                if (slash <= 0 || slash >= entry.Length - 1) continue;

                string key = entry.Substring(0, slash);
                if (!key.StartsWith(NativePanelLocationKeyPrefix, StringComparison.Ordinal))
                    continue;

                string name = key.Substring(NativePanelLocationKeyPrefix.Length);
                if (!PersistNativePanelLocation(name))
                    continue;

                string[] xy = entry.Substring(slash + 1).Split('|');
                if (xy.Length != 2) continue;

                if (Int32.TryParse(xy[0], out int x) &&
                    Int32.TryParse(xy[1], out int y))
                {
                    saved[name] = new Point(x, y);
                }
            }

            foreach (KeyValuePair<string, Point> kvp in saved)
            {
                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == kvp.Key);

                if (panel == null) continue;

                // Saved State is an operator position. Restore it exactly and weld it.
                // No automatic clamping/re-layout may silently alter the X/Y later.
                SetNativePanelLocationAuthorized(panel, kvp.Value, true);
            }

            Point shared;
            if (saved.TryGetValue("panelModeSpecificPhone", out shared))
            {
                _nativeModeSpecificSharedLocation = shared;
                ApplyNativeModeSpecificSharedLocation();
            }
            else
            {
                foreach (string name in _nativeModeSpecificPanelNames)
                {
                    if (saved.TryGetValue(name, out shared))
                    {
                        _nativeModeSpecificSharedLocation = shared;
                        ApplyNativeModeSpecificSharedLocation();
                        break;
                    }
                }
            }
        }


        private static bool IsNativeMovableControl(Control control)
        {
            if (control == null) return false;
            if (!_nativeMovablePanelNames.Contains(control.Name)) return false;

            return control is PanelTS || control is GroupBoxTS ||
                   control.Name == "chkSquelch" ||
                   control.Name == "ptbSquelch" ||
                   control.Name == "picSquelch";
        }

        private Control ResolveNativeMovablePanel(IntPtr hwnd)
        {
            Control c = hwnd != IntPtr.Zero ? Control.FromHandle(hwnd) : null;

            while (c != null && c != this)
            {
                if (c.Parent == this)
                {
                    if (IsNativeMovableControl(c))
                        return c;

                    break;
                }

                c = c.Parent;
            }

            // Fallback for native child HWNDs that are not represented directly
            // by Control.FromHandle(): resolve the top-level Console child under
            // the current pointer.
            Point client = PointToClient(Control.MousePosition);
            Control direct = GetChildAtPoint(client, GetChildAtPointSkip.Invisible);
            if (IsNativeMovableControl(direct))
                return direct;

            return null;
        }

        private IEnumerable<Control> GetNativePanelDragGroup(Control primary)
        {
            string[] names;

            switch (primary.Name)
            {
                case "panelBandHF":
                case "panelBandVHF":
                case "panelBandGEN":
                    names = new[] { "panelBandHF", "panelBandVHF", "panelBandGEN" };
                    break;

                case "panelModeSpecificCW":
                case "panelModeSpecificPhone":
                case "panelModeSpecificDigital":
                case "panelModeSpecificFM":
                    names = new[]
                    {
                        "panelModeSpecificCW",
                        "panelModeSpecificPhone",
                        "panelModeSpecificDigital",
                        "panelModeSpecificFM"
                    };
                    break;

                case "chkSquelch":
                case "ptbSquelch":
                case "picSquelch":
                    names = new[]
                    {
                        "chkSquelch",
                        "ptbSquelch",
                        "picSquelch"
                    };
                    break;

                default:
                    names = new[] { primary.Name };
                    break;
            }

            foreach (string name in names)
            {
                Control found = Controls.Cast<Control>().FirstOrDefault(x => x.Name == name);
                if (found != null && found.Parent == this)
                    yield return found;
            }
        }

        private bool BeginNativePanelShiftDrag(IntPtr hwnd)
        {
            bool shiftDown = (Control.ModifierKeys & Keys.Shift) == Keys.Shift || Common.ShiftKeyDown;
            if (!shiftDown) return false;

            Control primary = ResolveNativeMovablePanel(hwnd);
            if (primary == null) return false;

            _nativePanelDragPrimary = primary;
            _nativePanelDragMouseStart = Control.MousePosition;
            _nativePanelDragOrigins = new Dictionary<Control, Point>();

            foreach (Control c in GetNativePanelDragGroup(primary))
                _nativePanelDragOrigins[c] = c.Location;

            primary.BringToFront();
            Cursor.Current = Cursors.SizeAll;
            return true;
        }

        private bool ContinueNativePanelShiftDrag()
        {
            if (_nativePanelDragPrimary == null || _nativePanelDragOrigins == null)
                return false;

            bool shiftDown = (Control.ModifierKeys & Keys.Shift) == Keys.Shift || Common.ShiftKeyDown;
            bool leftDown = (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;

            if (!shiftDown || !leftDown)
            {
                EndNativePanelShiftDrag();
                return true;
            }

            Point mouse = Control.MousePosition;
            int dx = mouse.X - _nativePanelDragMouseStart.X;
            int dy = mouse.Y - _nativePanelDragMouseStart.Y;

            Point primaryOrigin = _nativePanelDragOrigins[_nativePanelDragPrimary];
            int newX = primaryOrigin.X + dx;
            int newY = primaryOrigin.Y + dy;
            bool modeSpecificGroup = IsNativeModeSpecificPanelName(_nativePanelDragPrimary.Name);

            int maxX = modeSpecificGroup
                ? _nativePanelDragOrigins.Keys.Min(p => Math.Max(0, ClientSize.Width - p.Width))
                : Math.Max(0, ClientSize.Width - _nativePanelDragPrimary.Width);
            int maxY = modeSpecificGroup
                ? _nativePanelDragOrigins.Keys.Min(p => Math.Max(0, ClientSize.Height - p.Height))
                : Math.Max(0, ClientSize.Height - _nativePanelDragPrimary.Height);

            newX = Math.Max(0, Math.Min(maxX, newX));
            newY = Math.Max(0, Math.Min(maxY, newY));

            int appliedDx = newX - primaryOrigin.X;
            int appliedDy = newY - primaryOrigin.Y;

            foreach (KeyValuePair<Control, Point> kvp in _nativePanelDragOrigins)
            {
                Point next = modeSpecificGroup
                    ? new Point(newX, newY)
                    : new Point(kvp.Value.X + appliedDx, kvp.Value.Y + appliedDy);

                if (kvp.Key.Location != next)
                    kvp.Key.Location = next;
            }

            Cursor.Current = Cursors.SizeAll;
            return true;
        }

        private bool EndNativePanelShiftDrag()
        {
            if (_nativePanelDragPrimary == null)
                return false;

            // Commit the new operator-selected position for the complete drag group.
            // From this point every native layout write to Location/Left/Top/Bounds is
            // rejected by NativeWeldedPanel_LocationChanged.
            foreach (Control panel in _nativePanelDragOrigins.Keys)
                _nativeLockedPanelLocations[panel.Name] = panel.Location;

            if (IsNativeModeSpecificPanelName(_nativePanelDragPrimary.Name))
            {
                _nativeModeSpecificSharedLocation = _nativePanelDragPrimary.Location;
                ApplyNativeModeSpecificSharedLocation();
            }

            _nativePanelDragPrimary = null;
            _nativePanelDragOrigins = null;
            Cursor.Current = Cursors.Default;
            return true;
        }

        private sealed class NativePanelShiftDragFilter : IMessageFilter
        {
            private const int WM_MOUSEMOVE = 0x0200;
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_LBUTTONUP = 0x0202;

            private readonly Console _owner;

            public NativePanelShiftDragFilter(Console owner)
            {
                _owner = owner;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (_owner == null || _owner.IsDisposed || _owner.Disposing)
                    return false;

                switch (m.Msg)
                {
                    case WM_LBUTTONDOWN:
                        // Consume Shift+LMB only when it starts a drag on one of
                        // the native movable Console panels. Without Shift the
                        // original control receives the click unchanged (LOCKED).
                        return _owner.BeginNativePanelShiftDrag(m.HWnd);

                    case WM_MOUSEMOVE:
                        return _owner.ContinueNativePanelShiftDrag();

                    case WM_LBUTTONUP:
                        return _owner.EndNativePanelShiftDrag();
                }

                return false;
            }
        }
    }

}
