using System;
using System.Collections.Generic;

namespace Demo3D.Bench
{
    /// <summary>Pure interpretation of machine state: power plans, NVIDIA throttle reasons, SMBIOS.</summary>
    public static class MachineFacts
    {
        public static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid UltimatePerformance = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        /// <summary>The row's name for a power plan: high, ultimate, balanced, saver, or its friendly name.</summary>
        public static string PowerPlanName(Guid scheme, string friendly)
        {
            if (scheme == HighPerformance) return "high";
            if (scheme == UltimatePerformance) return "ultimate";
            if (scheme == Balanced) return "balanced";
            if (scheme == PowerSaver) return "saver";
            return string.IsNullOrEmpty(friendly) ? scheme.ToString() : friendly;
        }

        /// <summary>
        /// Only High performance and Ultimate Performance may run a sweep. A custom plan copied from
        /// High performance has its own GUID, so its friendly name decides.
        /// </summary>
        public static bool PowerPlanAllowed(Guid scheme, string friendly) =>
            scheme == HighPerformance || scheme == UltimatePerformance
            || (friendly != null && (friendly.IndexOf("High performance", StringComparison.OrdinalIgnoreCase) >= 0
                                     || friendly.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0));

        // NVML's clocks event (throttle) reasons.
        public const ulong GpuIdle = 0x1;
        public const ulong ApplicationsClocksSetting = 0x2;
        public const ulong SwPowerCap = 0x4;
        public const ulong HwSlowdown = 0x8;
        public const ulong SyncBoost = 0x10;
        public const ulong SwThermalSlowdown = 0x20;
        public const ulong HwThermalSlowdown = 0x40;
        public const ulong HwPowerBrakeSlowdown = 0x80;
        public const ulong DisplayClockSetting = 0x100;

        /// <summary>Reasons that mark a run noisy: any thermal or hardware slowdown. A power cap under
        /// full load is normal for a desktop card and is only recorded.</summary>
        public const ulong NoiseReasons = HwSlowdown | SwThermalSlowdown | HwThermalSlowdown | HwPowerBrakeSlowdown;

        /// <summary>Thermal reasons, which also refuse a sweep's start.</summary>
        public const ulong ThermalReasons = SwThermalSlowdown | HwThermalSlowdown;

        public static string ThrottleNames(ulong bits)
        {
            var names = new List<string>();
            if ((bits & SwPowerCap) != 0) names.Add("power_cap");
            if ((bits & HwSlowdown) != 0) names.Add("hw_slowdown");
            if ((bits & SwThermalSlowdown) != 0) names.Add("sw_thermal");
            if ((bits & HwThermalSlowdown) != 0) names.Add("hw_thermal");
            if ((bits & HwPowerBrakeSlowdown) != 0) names.Add("power_brake");
            if ((bits & ApplicationsClocksSetting) != 0) names.Add("app_clocks");
            if ((bits & SyncBoost) != 0) names.Add("sync_boost");
            if ((bits & DisplayClockSetting) != 0) names.Add("display_clock");
            return names.Count == 0 ? "none" : string.Join("|", names);
        }

        /// <summary>
        /// The configured memory speed (MT/s, which Windows reports as MHz) of the first populated
        /// memory device in a raw SMBIOS table as GetSystemFirmwareTable('RSMB') returns it: an
        /// 8-byte header, then the structures. Falls back to the device's rated speed.
        /// </summary>
        public static int? MemoryMHz(byte[] raw)
        {
            if (raw == null || raw.Length < 8) return null;
            int len = BitConverter.ToInt32(raw, 4);
            int end = Math.Min(raw.Length, 8 + len);
            int i = 8;
            while (i + 4 <= end)
            {
                int type = raw[i], slen = raw[i + 1];
                if (slen < 4 || i + slen > end) break;
                if (type == 17 && slen > 0x16)
                {
                    int size = BitConverter.ToUInt16(raw, i + 0x0C);
                    int speed = BitConverter.ToUInt16(raw, i + 0x15);
                    int configured = slen >= 0x22 ? BitConverter.ToUInt16(raw, i + 0x20) : 0;
                    if (size != 0) // 0: no module in this slot
                    {
                        int mhz = configured != 0 && configured != 0xFFFF ? configured : speed;
                        if (mhz != 0 && mhz != 0xFFFF) return mhz;
                    }
                }
                if (type == 127) break;
                // Skip the formatted area, then the string set (ends with two zero bytes).
                int j = i + slen;
                while (j + 1 < end && !(raw[j] == 0 && raw[j + 1] == 0)) j++;
                i = j + 2;
            }
            return null;
        }
    }
}
