// SQ4KOU GPS/PPS bootstrap.
// The original ucInfoBar source is not edited. The ucInfoBar type initializer
// only starts an idle-time search for the main Console StatusStrip.

using System;
using System.Windows.Forms;

namespace Thetis
{
    public partial class ucInfoBar
    {
        static ucInfoBar()
        {
            GpsSyncUiBootstrap.Install();
        }
    }

    internal static class GpsSyncUiBootstrap
    {
        private static bool _installed;
        private static EventHandler _idleHandler;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            _idleHandler = new EventHandler(OnApplicationIdle);
            Application.Idle += _idleHandler;
        }

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (GpsSyncUi.Attach(f))
                    {
                        Application.Idle -= _idleHandler;
                        _idleHandler = null;
                        return;
                    }
                }
            }
            catch
            {
                // Status telemetry must never destabilise Thetis.
            }
        }
    }
}
