// Generic GPS/PPS telemetry for Thetis.
// Radio-instance independent: no fixed IP, MAC, serial number or radio model.
// UDP source is associated dynamically with the radio currently selected in Thetis.
// Read-only status extension: does not modify ChannelMaster, DSP, PureSignal or WideBand.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace Thetis
{
    internal sealed class GpsSyncSnapshot
    {
        public byte Sequence;
        public byte State;
        public byte Flags;
        public byte CorrectionMask;
        public byte AcquisitionCount;
        public uint PpsSequence;
        public uint PpsCountRaw;
        public uint ClockUsedHz;
        public int ClockErrorHz;
        public int ClockErrorPpb;
        public ushort PpsAgeMs;
        public byte DiagFlags;
        public DateTime ReceivedUtc;
        public string SourceIp;

        public GpsSyncSnapshot Clone()
        {
            return (GpsSyncSnapshot)MemberwiseClone();
        }
    }

    internal static class GpsSyncTelemetry
    {
        private const int Port = 12007;
        private const int PacketLength = 32;
        private const int OfflineMs = 2500;
        private const int PurgeAfterMs = 60000;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, GpsSyncSnapshot> LastBySource =
            new Dictionary<string, GpsSyncSnapshot>(StringComparer.OrdinalIgnoreCase);
        private static bool _started;
        // SQ4KOU Stage 3: UI thread caches the currently selected radio address here.
        // TCI worker threads can then select the correct UDP/12007 source without
        // touching WinForms controls cross-thread. Null means AUTO/newest source.
        private static string _preferredSourceIp;

        public static void SetPreferredSourceIp(string sourceIp)
        {
            lock (Sync)
            {
                _preferredSourceIp = String.IsNullOrWhiteSpace(sourceIp) ? null : sourceIp.Trim();
            }
        }

        public static GpsSyncSnapshot GetPreferredSnapshot(out bool online)
        {
            string preferred;
            lock (Sync)
            {
                preferred = _preferredSourceIp;
            }
            return GetSnapshot(preferred, out online);
        }

        public static void Start()
        {
            lock (Sync)
            {
                if (_started) return;
                _started = true;
            }

            Thread t = new Thread(ReceiveThread);
            t.IsBackground = true;
            t.Name = "GPS UDP telemetry";
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        // preferredSourceIp is resolved at runtime from the radio selected in Thetis.
        // No configured/fixed radio address is required.  If no selection can be
        // resolved yet, the newest valid telemetry source is used temporarily.
        public static GpsSyncSnapshot GetSnapshot(string preferredSourceIp, out bool online)
        {
            lock (Sync)
            {
                GpsSyncSnapshot selected = null;

                if (!String.IsNullOrWhiteSpace(preferredSourceIp))
                {
                    LastBySource.TryGetValue(preferredSourceIp, out selected);
                }
                else
                {
                    foreach (GpsSyncSnapshot candidate in LastBySource.Values)
                    {
                        if (candidate == null) continue;
                        if (selected == null || candidate.ReceivedUtc > selected.ReceivedUtc)
                            selected = candidate;
                    }
                }

                if (selected == null)
                {
                    online = false;
                    return null;
                }

                GpsSyncSnapshot s = selected.Clone();
                online = (DateTime.UtcNow - s.ReceivedUtc).TotalMilliseconds <= OfflineMs;
                return s;
            }
        }

        private static void ReceiveThread()
        {
            for (;;)
            {
                UdpClient udp = null;
                try
                {
                    udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                    udp.Client.ReceiveTimeout = 500;

                    for (;;)
                    {
                        try
                        {
                            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                            byte[] p = udp.Receive(ref remote);
                            if (remote == null || remote.Address == null) continue;

                            GpsSyncSnapshot s;
                            if (!TryDecode(p, out s)) continue;

                            s.ReceivedUtc = DateTime.UtcNow;
                            s.SourceIp = remote.Address.ToString();

                            lock (Sync)
                            {
                                LastBySource[s.SourceIp] = s;
                                PurgeOldSourcesLocked(s.ReceivedUtc);
                            }
                        }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode == SocketError.TimedOut ||
                                ex.SocketErrorCode == SocketError.WouldBlock)
                                continue;
                            throw;
                        }
                    }
                }
                catch
                {
                    Thread.Sleep(1000);
                }
                finally
                {
                    if (udp != null) udp.Close();
                }
            }
        }

        private static void PurgeOldSourcesLocked(DateTime nowUtc)
        {
            if (LastBySource.Count <= 8) return;

            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, GpsSyncSnapshot> kv in LastBySource)
            {
                if (kv.Value == null ||
                    (nowUtc - kv.Value.ReceivedUtc).TotalMilliseconds > PurgeAfterMs)
                    remove.Add(kv.Key);
            }

            foreach (string key in remove)
                LastBySource.Remove(key);
        }

        private static bool TryDecode(byte[] p, out GpsSyncSnapshot s)
        {
            s = null;
            if (p == null || p.Length != PacketLength) return false;
            if (p[0] != 0x07 || p[2] != 0x5A || p[3] != 0x02) return false;
            if (Crc8(p, 31) != p[31]) return false;

            GpsSyncSnapshot n = new GpsSyncSnapshot();
            n.Sequence = p[1];
            n.State = p[4];
            n.Flags = p[5];
            n.CorrectionMask = p[6];
            n.AcquisitionCount = p[7];
            n.PpsSequence = ReadU32(p, 8);
            n.PpsCountRaw = ReadU32(p, 12);
            n.ClockUsedHz = ReadU32(p, 16);
            n.ClockErrorHz = ReadI32(p, 20);
            n.ClockErrorPpb = ReadI32(p, 24);
            n.PpsAgeMs = (ushort)(p[28] | (p[29] << 8));
            n.DiagFlags = p[30];
            s = n;
            return true;
        }

        private static uint ReadU32(byte[] p, int o)
        {
            return (uint)(p[o] |
                          (p[o + 1] << 8) |
                          (p[o + 2] << 16) |
                          (p[o + 3] << 24));
        }

        private static int ReadI32(byte[] p, int o)
        {
            return unchecked((int)ReadU32(p, o));
        }

        private static byte Crc8(byte[] data, int count)
        {
            byte crc = 0;
            int i, b;
            for (i = 0; i < count; i++)
            {
                crc ^= data[i];
                for (b = 0; b < 8; b++)
                    crc = (byte)(((crc & 0x80) != 0) ? ((crc << 1) ^ 0x07) : (crc << 1));
            }
            return crc;
        }
    }

    internal static class GpsSyncUi
    {
        private const string ItemName = "tslGpsSync";

        // High-saturation status colours for strong visibility on the status bar.
        private static readonly Color GpsOffline = Color.FromArgb(190, 190, 190);
        private static readonly Color GpsLocked = Color.FromArgb(0, 255, 0);
        private static readonly Color GpsAcquire = Color.FromArgb(255, 255, 0);
        private static readonly Color GpsHoldover = Color.FromArgb(255, 128, 0);
        private static readonly Color GpsFault = Color.FromArgb(255, 32, 32);

        private static ToolStripStatusLabel _gps;
        private static StatusStrip _strip;
        private static System.Windows.Forms.Timer _timer;

        public static bool Attach(Control root)
        {
            if (root == null || root.IsDisposed) return false;
            if (_gps != null && !_gps.IsDisposed) return true;

            StatusStrip strip = FindBestStatusStrip(root);
            if (strip == null) return false;

            // SQ4KOU_GPS_BEFORE_UTC: keep GPS immediately to the left of UTC time.
            ToolStripItem utcAnchor = FindUtcTimeItem(strip);

            ToolStripItem existing = strip.Items[ItemName];
            if (existing != null)
            {
                _gps = existing as ToolStripStatusLabel;
                if (_gps != null) return true;
                strip.Items.Remove(existing);
                existing.Dispose();
            }

            ToolStripStatusLabel gps = new ToolStripStatusLabel();
            gps.Name = ItemName;
            gps.Text = "GPS";
            gps.AutoSize = true;
            gps.ForeColor = GpsOffline;
            gps.Font = new Font(strip.Font, FontStyle.Bold);
            gps.Margin = new Padding(8, 0, 0, 0);
            gps.ToolTipText = "GPS: brak telemetrii z aktualnie wybranego radia";
            gps.IsLink = false;

            int utcIndex = (utcAnchor != null) ? strip.Items.IndexOf(utcAnchor) : -1;
            if (utcIndex >= 0) strip.Items.Insert(utcIndex, gps);
            else strip.Items.Add(gps);
            strip.ShowItemToolTips = true;

            _gps = gps;
            _strip = strip;
            GpsSyncTelemetry.Start();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            gps.Click += OnGpsClick;
            strip.Disposed += OnStripDisposed;

            OnTimerTick(null, EventArgs.Empty);
            return true;
        }

        private static StatusStrip FindBestStatusStrip(Control root)
        {
            StatusStrip fallback = null;
            return FindBestStatusStripRecursive(root, ref fallback) ?? fallback;
        }

        private static StatusStrip FindBestStatusStripRecursive(Control root, ref StatusStrip fallback)
        {
            StatusStrip s = root as StatusStrip;
            if (s != null)
            {
                if (fallback == null) fallback = s;
                if (FindUtcTimeItem(s) != null) return s;
            }

            foreach (Control c in root.Controls)
            {
                StatusStrip found = FindBestStatusStripRecursive(c, ref fallback);
                if (found != null && FindUtcTimeItem(found) != null) return found;
            }

            return null;
        }

        private static ToolStripItem FindUtcTimeItem(StatusStrip strip)
        {
            if (strip == null) return null;

            foreach (ToolStripItem item in strip.Items)
            {
                if (item == null) continue;

                string text = (item.Text ?? String.Empty).Trim();
                string name = (item.Name ?? String.Empty).Trim();

                if (name.Equals("toolStripStatusLabel_UTCTime", StringComparison.OrdinalIgnoreCase))
                    return item;

                if (name.IndexOf("utc", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0)
                    return item;

                if (text.EndsWith(" utc", StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        // Executed by a WinForms timer, therefore querying the current Setup/radio
        // selection happens on the UI thread and does not cross-thread-access controls.
        private static string GetSelectedRadioIp()
        {
            try
            {
                Console c = Console.getConsole();
                if (c == null || c.IsSetupFormNull) return null;
                if (c.SetupForm == null || c.SetupForm.SelectedRadioList == null) return null;

                var ri = c.SetupForm.SelectedRadioList.SelectedRadioDetails;
                if (ri == null || ri.IpAddress == null) return null;
                return ri.IpAddress.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void OnTimerTick(object sender, EventArgs e)
        {
            if (_gps == null || _gps.IsDisposed) return;

            bool online;
            string selectedRadioIp = GetSelectedRadioIp();
            GpsSyncTelemetry.SetPreferredSourceIp(selectedRadioIp);
            GpsSyncSnapshot s = GpsSyncTelemetry.GetSnapshot(selectedRadioIp, out online);
            UpdateItem(_gps, s, online);
            _gps.ToolTipText = BuildTooltip(s, online);
        }

        private static void OnGpsClick(object sender, EventArgs e)
        {
            bool online;
            string selectedRadioIp = GetSelectedRadioIp();
            GpsSyncTelemetry.SetPreferredSourceIp(selectedRadioIp);
            GpsSyncSnapshot s = GpsSyncTelemetry.GetSnapshot(selectedRadioIp, out online);
            IWin32Window owner = (_strip != null) ? _strip.FindForm() : null;
            MessageBox.Show(owner, BuildDetails(s, online, selectedRadioIp),
                "GPS / PPS - radio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OnStripDisposed(object sender, EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            if (_gps != null)
            {
                try { _gps.Click -= OnGpsClick; } catch { }
                _gps = null;
            }

            _strip = null;
        }

        private static void UpdateItem(ToolStripStatusLabel gps, GpsSyncSnapshot s, bool online)
        {
            if (!online || s == null)
            {
                gps.ForeColor = GpsOffline;
                return;
            }

            switch (s.State)
            {
                case 3: // LOCKED
                    gps.ForeColor = ((s.Flags & 0x40) != 0) ? GpsLocked : GpsAcquire;
                    break;
                case 2: // ACQUIRE
                    gps.ForeColor = GpsAcquire;
                    break;
                case 4: // HOLDOVER
                    gps.ForeColor = GpsHoldover;
                    break;
                default:
                    gps.ForeColor = GpsFault;
                    break;
            }
        }

        private static string StateName(byte state)
        {
            switch (state)
            {
                case 1: return "NO PPS";
                case 2: return "ACQUIRE";
                case 3: return "LOCKED";
                case 4: return "HOLDOVER";
                case 5: return "INVALID";
                case 6: return "FAULT";
                default: return "OFF";
            }
        }

        private static string YesNo(bool v)
        {
            return v ? "YES" : "NO";
        }

        private static string BuildTooltip(GpsSyncSnapshot s, bool online)
        {
            if (!online || s == null) return "GPS: brak świeżej telemetrii z aktualnie wybranego radia";

            double ppm = s.ClockErrorPpb / 1000.0;
            return String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "GPS {0} | {1} Hz | {2:+0.000;-0.000;0.000} ppm | PPS age {3} ms | {4}",
                StateName(s.State), s.ClockUsedHz, ppm, s.PpsAgeMs, s.SourceIp);
        }

        private static string BuildDetails(GpsSyncSnapshot s, bool online, string selectedRadioIp)
        {
            if (!online || s == null)
            {
                string selected = String.IsNullOrWhiteSpace(selectedRadioIp) ? "AUTO" : selectedRadioIp;
                return "GPS telemetry: OFFLINE\r\n\r\n" +
                       "Brak poprawnego, świeżego pakietu UDP/12007 z aktualnie wybranego radia.\r\n" +
                       "Wybrane radio: " + selected + ".\r\n\r\n" +
                       "Odbiornik nie zawiera stałego IP, MAC ani identyfikatora urządzenia.";
            }

            double ppm = s.ClockErrorPpb / 1000.0;
            bool seen = (s.Flags & 0x02) != 0;
            bool valid = (s.Flags & 0x04) != 0;
            bool recent = (s.Flags & 0x08) != 0;
            bool locked = (s.Flags & 0x10) != 0;
            bool hold = (s.Flags & 0x20) != 0;
            bool active = (s.Flags & 0x40) != 0;

            return String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "STATE            : {0}\r\n" +
                "PPS SEEN         : {1}\r\n" +
                "PPS VALID        : {2}\r\n" +
                "PPS RECENT       : {3}\r\n" +
                "LOCK             : {4}\r\n" +
                "HOLDOVER         : {5}\r\n" +
                "DISCIPLINE       : {6}\r\n" +
                "CLOCK USED       : {7} Hz\r\n" +
                "CLOCK ERROR      : {8:+0;-0;0} Hz\r\n" +
                "ERROR            : {9:+0.000;-0.000;0.000} ppm\r\n" +
                "PPS AGE          : {10} ms\r\n" +
                "PPS SEQ          : {11}\r\n" +
                "PPS COUNT RAW    : {12}\r\n" +
                "ACQ COUNT        : {13}\r\n" +
                "CORRECTION MASK  : 0x{14:X2}\r\n" +
                "FLAGS            : 0x{15:X2}\r\n" +
                "DIAG             : 0x{16:X2}\r\n" +
                "PACKET SEQ       : {17}\r\n" +
                "SOURCE           : {18}:12007",
                StateName(s.State), YesNo(seen), YesNo(valid), YesNo(recent), YesNo(locked),
                YesNo(hold), active ? "ACTIVE" : "INACTIVE", s.ClockUsedHz, s.ClockErrorHz,
                ppm, s.PpsAgeMs, s.PpsSequence, s.PpsCountRaw, s.AcquisitionCount,
                s.CorrectionMask, s.Flags, s.DiagFlags, s.Sequence, s.SourceIp);
        }
    }
}
