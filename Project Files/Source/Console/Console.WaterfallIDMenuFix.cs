using System;
using System.Windows.Forms;

namespace Thetis
{
    public partial class Console
    {
        // Console already overrides Form.OnShown in the native source.  Use a one-shot
        // Application.Idle hook instead of declaring a second override in this partial class.
        // This preserves the Waterfall ID menu repair after the main form is created without
        // colliding with the native lifecycle implementation.
        private static readonly bool _sq4kouWaterfallIdMenuHook = InstallWaterfallIdMenuHook();

        private static bool InstallWaterfallIdMenuHook()
        {
            Application.Idle += Sq4kouWaterfallIdMenuIdle;
            return true;
        }

        private static void Sq4kouWaterfallIdMenuIdle(object sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                Console console = form as Console;
                if (console == null || !console.Visible)
                    continue;

                console.EnsureWaterfallIDMenuVisible();
                Application.Idle -= Sq4kouWaterfallIdMenuIdle;
                break;
            }
        }

        private void EnsureWaterfallIDMenuVisible()
        {
            if (menuStrip1 == null) return;

            if (_waterfallIdMenuItem == null)
            {
                _waterfallIdMenuItem = new ToolStripMenuItem("Waterfall ID");
                _waterfallIdMenuItem.Name = "waterfallIdToolStripMenuItem";
            }

            // Route the visible entry to the advanced Waterfall ID UI.
            _waterfallIdMenuItem.Click -= WaterfallIdMenuItem_Click;
            _waterfallIdMenuItem.Click -= WaterfallIdAdvancedMenuItem_Click;
            _waterfallIdMenuItem.Click += WaterfallIdAdvancedMenuItem_Click;
            _waterfallIdMenuItem.ToolTipText = "Transmit text or bitmap as an on-air waterfall image";

            if (_waterfallIdMenuItem.Owner != menuStrip1)
            {
                ToolStripItem existing = menuStrip1.Items["waterfallIdToolStripMenuItem"];
                if (existing != null && !object.ReferenceEquals(existing, _waterfallIdMenuItem))
                    menuStrip1.Items.Remove(existing);

                if (_waterfallIdMenuItem.Owner != null)
                    _waterfallIdMenuItem.Owner.Items.Remove(_waterfallIdMenuItem);

                int insertIndex = menuStrip1.Items.IndexOf(cWXToolStripMenuItem);
                if (insertIndex < 0)
                    insertIndex = 0;
                else
                    insertIndex++;

                menuStrip1.Items.Insert(insertIndex, _waterfallIdMenuItem);
            }

            // Match the native CWX menu appearance so the label is visible in normal state.
            if (cWXToolStripMenuItem != null)
            {
                _waterfallIdMenuItem.ForeColor = cWXToolStripMenuItem.ForeColor;
                _waterfallIdMenuItem.BackColor = cWXToolStripMenuItem.BackColor;
                _waterfallIdMenuItem.Font = cWXToolStripMenuItem.Font;
                _waterfallIdMenuItem.TextAlign = cWXToolStripMenuItem.TextAlign;
                _waterfallIdMenuItem.DisplayStyle = ToolStripItemDisplayStyle.Text;
            }

            _waterfallIdMenuItem.Visible = true;
            _waterfallIdMenuItem.Enabled = true;
        }
    }
}
