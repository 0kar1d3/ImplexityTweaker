using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Управление режимом прерываний MSI (Message Signaled-Based Interrupts) и приоритетом прерываний
/// (Interrupt Affinity Policy) для видеокарт и сетевых адаптеров PCI (аналог MSI_util_v3).
/// </summary>
public static class MsiModeService
{
    public enum InterruptPriority
    {
        Default = 0,
        Low = 1,
        Normal = 2,
        High = 3
    }

    public sealed class MsiDeviceInfo
    {
        public required string PnpDeviceId { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; } // "GPU" | "Network" | "USB"
        public bool MsiSupported { get; set; }
        public InterruptPriority Priority { get; set; }
        public ulong AffinityMask { get; set; }
        public bool HasInterruptManagementKey { get; init; }

        public string RegistryPath =>
            $@"HKLM\SYSTEM\CurrentControlSet\Enum\{PnpDeviceId}\Device Parameters\Interrupt Management";
    }

    public sealed record AffinityOption(ulong Mask, string Label)
    {
        public override string ToString() => Label;
    }

    public static List<AffinityOption> GetAffinityPresets()
    {
        int cores = Math.Clamp(Environment.ProcessorCount, 1, 64);
        ulong allMask = cores >= 64 ? ulong.MaxValue : (1UL << cores) - 1;

        var list = new List<AffinityOption>
        {
            new(0UL, LocalizationService.T("Авто (любое ядро)"))
        };

        if (cores >= 3)
            list.Add(new(allMask & ~1UL, LocalizationService.T("Кроме CPU 0")));

        int step = cores >= 8 ? 2 : 1;
        for (int c = step; c < cores && list.Count < 8; c += step)
            list.Add(new(1UL << c, $"CPU {c}"));

        return list;
    }

    /// <summary>Загружает список PCI-устройств (GPU, сетевых адаптеров и USB-контроллеров) с текущими параметрами MSI.</summary>
    public static Task<List<MsiDeviceInfo>> GetDevicesAsync()
    {
        return Task.Run(() =>
        {
            var result = new List<MsiDeviceInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectPciDevices("SELECT Name, PNPDeviceID FROM Win32_VideoController", "GPU", seen, result);
            CollectPciDevices("SELECT Name, PNPDeviceID FROM Win32_NetworkAdapter WHERE PNPDeviceID LIKE 'PCI%'", "Network", seen, result);

            return result;
        });
    }

    private static void CollectPciDevices(string wql, string category, HashSet<string> seen, List<MsiDeviceInfo> result)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var pnp = obj["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                    var name = obj["Name"]?.ToString()?.Trim() ?? "";
                    if (pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && seen.Add(pnp))
                    {
                        result.Add(ReadDeviceFromRegistry(pnp, string.IsNullOrWhiteSpace(name) ? pnp : name, category));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MsiMode", $"WMI query failed for {category}: {ex.Message}");
        }
    }

    private static MsiDeviceInfo ReadDeviceFromRegistry(string pnpDeviceId, string name, string category)
    {
        var baseSubKey = $@"SYSTEM\CurrentControlSet\Enum\{pnpDeviceId}\Device Parameters\Interrupt Management";
        bool msiEnabled = false;
        var priority = InterruptPriority.Default;
        ulong affinityMask = 0;
        bool hasKey = false;

        try
        {
            using var imKey = Registry.LocalMachine.OpenSubKey(baseSubKey, false);
            if (imKey != null)
            {
                hasKey = true;
                using var msiKey = imKey.OpenSubKey("MessageSignaledInterruptProperties", false);
                if (msiKey?.GetValue("MSISupported") is int msiVal)
                    msiEnabled = msiVal == 1;

                using var affKey = imKey.OpenSubKey("Affinity Policy", false);
                if (affKey != null)
                {
                    if (affKey.GetValue("DevicePriority") is int prioVal && prioVal is >= 0 and <= 3)
                        priority = (InterruptPriority)prioVal;

                    if (affKey.GetValue("DevicePolicy") is int devPolicy && devPolicy == 4 &&
                        affKey.GetValue("AssignmentSetOverride") is byte[] raw && raw.Length > 0)
                    {
                        for (int i = 0; i < Math.Min(raw.Length, 8); i++)
                            affinityMask |= (ulong)raw[i] << (i * 8);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MsiMode", $"Registry read failed for {pnpDeviceId}: {ex.Message}");
        }

        return new MsiDeviceInfo
        {
            PnpDeviceId = pnpDeviceId,
            Name = name,
            Category = category,
            MsiSupported = msiEnabled,
            Priority = priority,
            AffinityMask = affinityMask,
            HasInterruptManagementKey = hasKey
        };
    }

    /// <summary>Применяет режим MSI, приоритет и маску ядра прерываний для указанного PCI-устройства.</summary>
    public static Task ApplyDeviceSettingsAsync(string pnpDeviceId, bool msiEnabled, InterruptPriority priority, ulong affinityMask = 0)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId) || !pnpDeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Некорректный идентификатор PCI-устройства.");

        return Task.Run(() =>
        {
            string msiSubKey = $@"SYSTEM\CurrentControlSet\Enum\{pnpDeviceId}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
            string affSubKey = $@"SYSTEM\CurrentControlSet\Enum\{pnpDeviceId}\Device Parameters\Interrupt Management\Affinity Policy";

            using (var msiKey = Registry.LocalMachine.OpenSubKey(msiSubKey, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(msiSubKey, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ветку HKLM\\{msiSubKey}."))
            {
                msiKey.SetValue("MSISupported", msiEnabled ? 1 : 0, RegistryValueKind.DWord);
            }

            if (priority == InterruptPriority.Default && affinityMask == 0)
            {
                using var affKey = Registry.LocalMachine.OpenSubKey(affSubKey, writable: true);
                affKey?.DeleteValue("DevicePriority", throwOnMissingValue: false);
                affKey?.DeleteValue("DevicePolicy", throwOnMissingValue: false);
                affKey?.DeleteValue("AssignmentSetOverride", throwOnMissingValue: false);
                return;
            }

            using (var affKey = Registry.LocalMachine.OpenSubKey(affSubKey, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(affSubKey, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ветку HKLM\\{affSubKey}."))
            {
                if (priority == InterruptPriority.Default)
                    affKey.DeleteValue("DevicePriority", throwOnMissingValue: false);
                else
                    affKey.SetValue("DevicePriority", (int)priority, RegistryValueKind.DWord);

                if (affinityMask == 0)
                {
                    affKey.DeleteValue("DevicePolicy", throwOnMissingValue: false);
                    affKey.DeleteValue("AssignmentSetOverride", throwOnMissingValue: false);
                }
                else
                {
                    affKey.SetValue("DevicePolicy", 4, RegistryValueKind.DWord);
                    affKey.SetValue("AssignmentSetOverride", BitConverter.GetBytes(affinityMask), RegistryValueKind.Binary);
                }
            }
        });
    }

    public static string PriorityLabel(InterruptPriority priority) => priority switch
    {
        InterruptPriority.Low => LocalizationService.T("Низкий"),
        InterruptPriority.Normal => LocalizationService.T("Обычный"),
        InterruptPriority.High => LocalizationService.T("Высокий"),
        _ => LocalizationService.T("По умолчанию")
    };
}
