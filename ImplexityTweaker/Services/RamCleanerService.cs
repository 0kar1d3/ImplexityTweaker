using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace ImplexityTweaker.Services;

/// <summary>
/// Очистка оперативной памяти (аналог Mem Reduct).
/// Сбрасывает рабочие наборы (working sets) всех процессов текущей сессии
/// через Win32 API EmptyWorkingSet. Процессы обрабатываются с небольшой
/// паузой, чтобы не вызывать шквал подкачки страниц и не «подвешивать» систему.
/// Системный кэш (standby list) не трогается — его полная очистка вызывает
/// заметные тормоза из-за повторного чтения файлов с диска.
/// </summary>
public static class RamCleanerService
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public int Cb;
        public nint CommitTotal;
        public nint CommitLimit;
        public nint CommitPeak;
        public nint PhysicalTotal;
        public nint PhysicalAvailable;
        public nint SystemCache;
        public nint KernelTotal;
        public nint KernelPaged;
        public nint KernelNonpaged;
        public nint PageSize;
        public int HandlesCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hProcess);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation pPerformanceInformation, int cb);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    /// <summary>Занимаемая память (как «Используется» в Диспетчере задач): всего − доступно, МБ.</summary>
    private static long UsedMb()
    {
        if (!GetPerformanceInfo(out var pi, Marshal.SizeOf<PerformanceInformation>()))
            return -1;

        long usedPages = (long)pi.PhysicalTotal - (long)pi.PhysicalAvailable;
        return usedPages * (long)pi.PageSize / 1024 / 1024;
    }

    /// <summary>
    /// Принудительно собирает мусор и сжимает рабочий набор самого процесса твикера
    /// (используется при сворачивании в системный трей для минимального потребления ОЗУ).
    /// </summary>
    public static void TrimOwnProcessMemory()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
        }
    }

    /// <summary>Текущий процент загрузки оперативной памяти (0–100).</summary>
    public static int GetMemoryUsagePercent()
    {
        if (!GetPerformanceInfo(out var pi, Marshal.SizeOf<PerformanceInformation>()) || (long)pi.PhysicalTotal <= 0)
            return 0;

        long usedPages = (long)pi.PhysicalTotal - (long)pi.PhysicalAvailable;
        return (int)Math.Clamp(usedPages * 100L / (long)pi.PhysicalTotal, 0L, 100L);
    }

    private static void EnablePrivilege(string privilege)
    {
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
                return;
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid))
                    return;
                var tp = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            // Привилегия не обязательна — без неё пропустим часть процессов.
        }
    }

    /// <summary>
    /// Сбрасывает рабочие наборы всех процессов текущей сессии (EmptyWorkingSet)
    /// и возвращает, сколько освободилось «занятой» памяти в МБ — как её видит
    /// Диспетчер задач (всего − доступно).
    /// </summary>
        public static async Task<long> CleanAsync()
    {
        // Совместимость: вызов без прогресса.
        return await CleanAsync(null);
    }

    /// <summary>
    /// Сбрасывает рабочие наборы всех процессов текущей сессии (EmptyWorkingSet)
    /// и возвращает, сколько освободилось «занятой» памяти в МБ — как её видит
    /// Диспетчер задач (всего − доступно).
    /// </summary>
    /// <param name="progress">Опциональный прогресс: (текущий номер, всего).</param>
    public static async Task<long> CleanAsync(IProgress<(int Current, int Total)>? progress)
    {
        return await Task.Run(() =>
        {
            // SeDebug — доступ к процессам.
            EnablePrivilege("SeDebugPrivilege");

            long usedBefore = UsedMb();

            int currentSession = Process.GetCurrentProcess().SessionId;
            int currentPid = Environment.ProcessId;

            Process[] processes;
            try { processes = Process.GetProcesses(); } catch { return 0L; }

            var targetIds = new List<int>(processes.Length);
            try
            {
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.SessionId == currentSession && p.Id != currentPid)
                            targetIds.Add(p.Id);
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            int total = targetIds.Count;
            progress?.Report((0, total));

            for (int i = 0; i < total; i++)
            {
                try
                {
                    IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, (uint)targetIds[i]);
                    if (handle == IntPtr.Zero)
                        continue;

                    try { EmptyWorkingSet(handle); }
                    finally { CloseHandle(handle); }

                    // Небольшая пауза между процессами: без неё все программы
                    // одновременно начинают подкачивать свои страницы с диска,
                    // и система на секунды «замирает».
                    Thread.Sleep(5);
                }
                catch
                {
                    // Доступ запрещён или процесс завершился — пропускаем.
                }

                progress?.Report((i + 1, total));
            }

            long usedAfter = UsedMb();
            return usedBefore > usedAfter ? usedBefore - usedAfter : 0;
        });
        }
}
