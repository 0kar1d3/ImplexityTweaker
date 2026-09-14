using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Threading;

namespace ImplexityTweaker.Services;

/// <summary>
/// Фоновый наблюдатель твиков процессов: поднимает приоритет выбранному процессу
/// и/или ограничивает его физическими ядрами сразу после запуска.
/// </summary>
public static class ProcessPriorityWatcher
{
    private static DispatcherTimer? _timer;
    private static readonly HashSet<string> Applied = [];
    private static IntPtr? _physicalMask;

    public static bool IsRunning => _timer != null;

    /// <summary>Перезапускает наблюдение с учётом текущих настроек.</summary>
    public static void Refresh()
    {
        Stop();

        var tweaks = ProcessTweaksStore.Load();
        if (tweaks.AffinityEnabled || tweaks.AutoPriorityEnabled)
            Start();
    }

    public static void Start()
    {
        if (_timer != null)
            return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        Applied.Clear();
    }

    private static void Tick()
    {
        try
        {
            if (Applied.Count > 512)
                Applied.Clear();

            var tweaks = ProcessTweaksStore.Load();

            if (tweaks.AutoPriorityEnabled)
                ApplyTo(tweaks.AutoPriorityProcess, true, false);

            if (tweaks.AffinityEnabled)
                ApplyTo(tweaks.AffinityProcess, false, true);
        }
        catch
        {
            // наблюдатель не должен ломать приложение
        }
    }

    private static void ApplyTo(string exeName, bool raisePriority, bool physicalCoresOnly)
    {
        var name = Path.GetFileNameWithoutExtension(exeName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                if (!Applied.Add(ProcessKey(process)))
                    continue;

                try
                {
                    if (raisePriority)
                        process.PriorityClass = ProcessPriorityClass.High;

                    if (physicalCoresOnly)
                    {
                        var mask = CachedPhysicalCoreMask();
                        if (mask != IntPtr.Zero)
                            process.ProcessorAffinity = mask;
                    }
                }
                catch
                {
                    // процесс завершился или недостаточно прав
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>
    /// Ключ «PID + время запуска»: PID переиспользуются, поэтому одного PID мало —
    /// иначе новый процесс со старым PID остался бы без твика.
    /// </summary>
    private static string ProcessKey(Process process)
    {
        try
        {
            return $"{process.Id}:{process.StartTime.Ticks}";
        }
        catch
        {
            return process.Id.ToString();
        }
    }

    /// <summary>Маска физических ядер считается один раз за запуск (запрос к WMI небыстрый).</summary>
    private static IntPtr CachedPhysicalCoreMask()
        => _physicalMask ??= PhysicalCoreMask();

    /// <summary>Маска «только физические ядра» (при SMT оставляем по одному потоку на ядро).</summary>
    private static IntPtr PhysicalCoreMask()
    {
        try
        {
            var logical = Environment.ProcessorCount;
            if (logical <= 0 || logical > 64)
                return IntPtr.Zero;

            long mask = 0;
            if (IsSmtEnabled(logical))
            {
                for (var i = 0; i < logical; i += 2)
                    mask |= 1L << i;
            }
            else
            {
                for (var i = 0; i < logical; i++)
                    mask |= 1L << i;
            }

            return new IntPtr(mask);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static bool IsSmtEnabled(int logical)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            foreach (ManagementObject cpu in searcher.Get())
            {
                if (cpu["NumberOfCores"] is { } cores &&
                    int.TryParse(cores.ToString(), out var count) && count > 0)
                {
                    return logical > count;
                }
            }
        }
        catch
        {
            // WMI недоступен — считаем, что SMT есть, если потоков больше четырёх
        }

        return logical > 4;
    }

}
