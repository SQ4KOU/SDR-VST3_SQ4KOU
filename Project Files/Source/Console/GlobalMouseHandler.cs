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

        private static readonly HashSet<string> _nativeMovablePanelNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "panelBandHF",
                "panelBandVHF",
                "panelBandGEN",
                "panelMode",
                "panelFilter",
                "panelDisplay2",
                "panelOptions",
                "panelSoundControls",
                "panelVFO",
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
                "panelAndromedaMisc"
            };

        private void InitializeNativePanelShiftDrag()
        {
            if (_nativePanelShiftDragFilter != null) return;

            _nativePanelShiftDragFilter = new NativePanelShiftDragFilter(this);
            Application.AddMessageFilter(_nativePanelShiftDragFilter);
        }

        private const string NativePanelLocationKeyPrefix = "NativePanelLocation.";

        private void AppendNativePanelLocations(List<string> state)
        {
            if (state == null) return;

            foreach (string name in _nativeMovablePanelNames)
            {
                Control panel = Controls.Cast<Control>()
                    .FirstOrDefault(c => c.Parent == this && c.Name == name);

                if (panel == null) continue;

                state.Add(NativePanelLocationKeyPrefix + name + "/" +
                    panel.Left.ToString() + "|" + panel.Top.ToString());
            }
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
                if (!_nativeMovablePanelNames.Contains(name))
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

                int maxX = Math.Max(0, ClientSize.Width - panel.Width);
                int maxY = Math.Max(0, ClientSize.Height - panel.Height);

                Point restored = new Point(
                    Math.Max(0, Math.Min(maxX, kvp.Value.X)),
                    Math.Max(0, Math.Min(maxY, kvp.Value.Y)));

                if (panel.Location != restored)
                    panel.Location = restored;
            }
        }


        private Control ResolveNativeMovablePanel(IntPtr hwnd)
        {
            Control c = hwnd != IntPtr.Zero ? Control.FromHandle(hwnd) : null;

            while (c != null && c != this)
            {
                if (c.Parent == this)
                {
                    if (c is PanelTS && _nativeMovablePanelNames.Contains(c.Name))
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
            if (direct is PanelTS && _nativeMovablePanelNames.Contains(direct.Name))
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

            int maxX = Math.Max(0, ClientSize.Width - _nativePanelDragPrimary.Width);
            int maxY = Math.Max(0, ClientSize.Height - _nativePanelDragPrimary.Height);

            newX = Math.Max(0, Math.Min(maxX, newX));
            newY = Math.Max(0, Math.Min(maxY, newY));

            int appliedDx = newX - primaryOrigin.X;
            int appliedDy = newY - primaryOrigin.Y;

            foreach (KeyValuePair<Control, Point> kvp in _nativePanelDragOrigins)
            {
                Point origin = kvp.Value;
                Point next = new Point(origin.X + appliedDx, origin.Y + appliedDy);
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
