using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Управление гибернацией (hiberfil.sys), файлом подкачки (pagefile.sys)
/// и глубокой очисткой хранилища компонентов WinSxS + кэша обновлений SoftwareDistribution.
/// </summary>
public static class SystemStorageTweaksService
{
    private const string PowerRegPath = @"SYSTEM\CurrentControlSet\Control\Power";
    private const string MemoryManagementRegPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    public enum PagefileMode
    {
        SystemManaged = 0,
        Custom = 1,
        Disabled = 2
    }

    public sealed record HibernationStatus(bool Enabled, double SizeGb);

    public sealed record PagefileStatus(PagefileMode Mode, int InitialMb, int MaximumMb, double CurrentFileSizeGb);

    // ===================== Гибернация (hiberfil.sys) =====================

    public static HibernationStatus GetHibernationStatus()
    {
        bool enabled = false;
        double sizeGb = 0;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerRegPath, false);
            if (key?.GetValue("HibernateEnabled") is int v)
                enabled = v != 0;
        }
        catch { }

        try
        {
            var fi = new FileInfo(@"C:\hiberfil.sys");
            if (fi.Exists)
            {
                enabled = true;
                sizeGb = Math.Round(fi.Length / 1024d / 1024d / 1024d, 2);
            }
        }
        catch { }

        return new HibernationStatus(enabled, sizeGb);
    }

    public static async Task<double> SetHibernationAsync(bool enable)
    {
        var before = GetHibernationStatus();
        var arg = enable ? "on" : "off";
        var r = await CommandRunner.RunProcessAsync("powercfg.exe", $"/hibernate {arg}");
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);

        var after = GetHibernationStatus();
        return Math.Max(0, Math.Round(before.SizeGb - after.SizeGb, 2));
    }

    // ===================== Файл подкачки (pagefile.sys) =====================

    public static PagefileStatus GetPagefileStatus()
    {
        var mode = PagefileMode.SystemManaged;
        int initMb = 4096;
        int maxMb = 8192;
        double fileSizeGb = 0;

        try
        {
            var fi = new FileInfo(@"C:\pagefile.sys");
            if (fi.Exists)
                fileSizeGb = Math.Round(fi.Length / 1024d / 1024d / 1024d, 2);
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MemoryManagementRegPath, false);
            var raw = key?.GetValue("PagingFiles") as string[];

            if (raw == null || raw.Length == 0 || raw.All(string.IsNullOrWhiteSpace))
            {
                mode = PagefileMode.Disabled;
            }
            else
            {
                var first = raw.First(s => !string.IsNullOrWhiteSpace(s)).Trim();
                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    int.TryParse(parts[^2], out var parsedMin) &&
                    int.TryParse(parts[^1], out var parsedMax))
                {
                    if (parsedMin == 0 && parsedMax == 0)
                    {
                        mode = PagefileMode.SystemManaged;
                    }
                    else
                    {
                        mode = PagefileMode.Custom;
                        initMb = parsedMin;
                        maxMb = parsedMax;
                    }
                }
                else
                {
                    mode = PagefileMode.SystemManaged;
                }
            }
        }
        catch { }

        return new PagefileStatus(mode, initMb, maxMb, fileSizeGb);
    }

    public static async Task ApplyPagefileAsync(PagefileMode mode, int initialMb, int maximumMb)
    {
        string valueExpr;
        if (mode == PagefileMode.Disabled)
        {
            valueExpr = "@()";
        }
        else if (mode == PagefileMode.SystemManaged)
        {
            valueExpr = "@('?:\\pagefile.sys')";
        }
        else
        {
            int min = Math.Clamp(initialMb, 256, 131072);
            int max = Math.Clamp(Math.Max(min, maximumMb), min, 131072);
            valueExpr = $"@('C:\\pagefile.sys {min} {max}')";
        }

        var script = $@"
$ErrorActionPreference = 'Stop'
Set-ItemProperty -Path 'HKLM:\{MemoryManagementRegPath}' -Name 'PagingFiles' -Type MultiString -Value {valueExpr} -Force
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }

    // ===================== Очистка WinSxS и кэша обновлений =====================

    public static double GetUpdateCacheSizeMb()
    {
        try
        {
            var dir = new DirectoryInfo(@"C:\Windows\SoftwareDistribution\Download");
            if (!dir.Exists)
                return 0;

            long bytes = 0;
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { bytes += file.Length; } catch { }
            }
            return Math.Round(bytes / 1024d / 1024d, 1);
        }
        catch
        {
            return 0;
        }
    }

    public static async Task<double> CleanWinSxSAndUpdateCacheAsync(Action<string>? onLine, CancellationToken ct)
    {
        long freeBefore = GetSystemDriveFreeBytes();

        onLine?.Invoke(LocalizationService.T("Очистка кэша загруженных обновлений (SoftwareDistribution\\Download)…"));
        var cleanWuScript = @"
Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue
Stop-Service -Name bits -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'C:\Windows\SoftwareDistribution\Download\*' -Recurse -Force -ErrorAction SilentlyContinue
";
        await CommandRunner.RunPowerShellAsync(cleanWuScript);

        onLine?.Invoke(LocalizationService.T("Очистка устаревших компонентов хранилища WinSxS (DISM StartComponentCleanup)…"));
        var r = await CommandRunner.RunProcessAsync(
            "dism.exe",
            "/Online /Cleanup-Image /StartComponentCleanup",
            line => onLine?.Invoke(line),
            ct);

        if (r.ExitCode != 0 && !ct.IsCancellationRequested)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);

        long freeAfter = GetSystemDriveFreeBytes();
        double freedMb = freeAfter > freeBefore
            ? Math.Round((freeAfter - freeBefore) / 1024d / 1024d, 1)
            : 0;

        return freedMb;
    }

    private static long GetSystemDriveFreeBytes()
    {
        try
        {
            var drive = new DriveInfo("C");
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
    }
}
