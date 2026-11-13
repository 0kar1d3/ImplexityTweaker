using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImplexityTweaker.Services;

/// <summary>
/// Замер реального FPS, времени кадра (Frametime) и 1% Low FPS активного окна игры
/// напрямую через события ETW Windows (DXGI / D3D9 / DxgKrnl) без инъекции DLL.
/// Требует прав администратора (приложение уже запускается с requireAdministrator).
/// </summary>
public sealed class FpsEtwTracker : IDisposable
{
    private const string SessionName = "ImplexityTweakerOverlayTrace";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const ulong INVALID_TRACE_HANDLE = ulong.MaxValue;
    private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNODE_HEADER
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACE_LOGFILE_HEADER
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public IntPtr TimeZone; // TIME_ZONE_INFORMATION is 172 bytes inline below; keep manual layout below
    }

    // Точная Win32-структура EVENT_RECORD
    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_DESCRIPTOR
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_HEADER
    {
        public ushort Size;
        public ushort HeaderType;
        public ushort Flags;
        public ushort EventProperty;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public EVENT_DESCRIPTOR EventDescriptor;
        public ulong KernelTime; // union ProcessorTime (8 bytes)
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ETW_BUFFER_CONTEXT
    {
        public byte ProcessorNumber;
        public byte Alignment;
        public ushort LoggerId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_RECORD
    {
        public EVENT_HEADER EventHeader;
        public ETW_BUFFER_CONTEXT BufferContext;
        public ushort ExtendedDataCount;
        public ushort UserDataLength;
        public IntPtr ExtendedData;
        public IntPtr UserData;
        public IntPtr UserContext;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EventRecordCallback(ref EVENT_RECORD eventRecord);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint BufferCallback(IntPtr logfile);

    // Используем сырой буфер для EVENT_TRACE_LOGFILEW, чтобы не ошибиться с размером TIME_ZONE_INFORMATION (172 байта)
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(
        ulong traceHandle,
        ref Guid providerId,
        uint controlCode,
        byte level,
        ulong matchAnyKeyword,
        ulong matchAllKeyword,
        uint timeout,
        IntPtr enableParameters);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll")]
    private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong traceHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public sealed record FrameMetrics(double Fps, double FrametimeMs, double Low1PercentFps, string ProcessName, double[]? FramerateHistory = null);

    private enum StreamKind : byte
    {
        Dxgi = 1,
        D3d9 = 2,
        DxgKrnl = 3
    }

    private readonly record struct StreamKey(StreamKind Kind, ulong SwapChainOrThread);

    private sealed class PresentStream
    {
        public readonly long[] Timestamps = new long[512];
        public int Count;
        public int Head;
        public long LastTick;

        public void Push(long qpcTick)
        {
            Timestamps[Head] = qpcTick;
            Head = (Head + 1) % Timestamps.Length;
            if (Count < Timestamps.Length)
                Count++;
            if (qpcTick > LastTick)
                LastTick = qpcTick;
        }
    }

    private const uint DXGI_PRESENT_TEST = 0x00000001;

    private readonly object _sync = new();
    private readonly Dictionary<StreamKey, PresentStream> _streams = new();
    private long _lastDxgiOrD3d9Tick;

    private volatile uint _targetPid;
    private string _targetProcessName = string.Empty;
    private uint _selfPid = (uint)Environment.ProcessId;

    private ulong _sessionHandle;
    private ulong _traceHandle = INVALID_TRACE_HANDLE;
    private Thread? _traceThread;
    private volatile bool _running;
    private EventRecordCallback? _callbackKeepAlive;
    private IntPtr _logfileBuffer;

    public bool Start()
    {
        if (_running)
            return true;

        try
        {
            StopExistingSession();

            int propSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>() + (SessionName.Length + 2) * 2;
            IntPtr propPtr = Marshal.AllocHGlobal(propSize);
            try
            {
                ZeroMemory(propPtr, propSize);
                var props = new EVENT_TRACE_PROPERTIES
                {
                    Wnode = new WNODE_HEADER
                    {
                        BufferSize = (uint)propSize,
                        Guid = Guid.NewGuid(),
                        ClientContext = 1, // QPC timestamps
                        Flags = WNODE_FLAG_TRACED_GUID
                    },
                    BufferSize = 64,
                    MinimumBuffers = 2,
                    MaximumBuffers = 16,
                    LogFileMode = EVENT_TRACE_REAL_TIME_MODE,
                    FlushTimer = 1,
                    LoggerNameOffset = (uint)Marshal.SizeOf<EVENT_TRACE_PROPERTIES>()
                };
                Marshal.StructureToPtr(props, propPtr, false);

                uint status = StartTraceW(out _sessionHandle, SessionName, propPtr);
                if (status != 0)
                    return false;
            }
            finally
            {
                Marshal.FreeHGlobal(propPtr);
            }

            // Подключаем провайдеры: DXGI (Events+Analytic), D3D9, DxgKrnl (Present)
            Guid g1 = DxgiProvider;
            EnableTraceEx2(_sessionHandle, ref g1, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 5, 0x8000000000000002UL, 0, 0, IntPtr.Zero);

            Guid g2 = D3d9Provider;
            EnableTraceEx2(_sessionHandle, ref g2, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 5, 0xFFFFFFFFFFFFFFFFUL, 0, 0, IntPtr.Zero);

            Guid g3 = DxgKrnlProvider;
            EnableTraceEx2(_sessionHandle, ref g3, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4, 0x0000000008000000UL, 0, 0, IntPtr.Zero);

            return StartTraceLoop();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("FpsEtw", $"Failed to start ETW session: {ex.Message}");
            Dispose();
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EVENT_TRACE_HEADER
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE
    {
        public EVENT_TRACE_HEADER Header;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TIME_ZONE_INFORMATION
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string StandardName;
        public SYSTEMTIME StandardDate;
        public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DaylightName;
        public SYSTEMTIME DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACE_LOGFILE_HEADER_FULL
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public TIME_ZONE_INFORMATION TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EVENT_TRACE_LOGFILEW
    {
        public IntPtr LogFileName;
        public IntPtr LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EVENT_TRACE CurrentEvent;
        public TRACE_LOGFILE_HEADER_FULL LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public IntPtr EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    private bool StartTraceLoop()
    {
        _callbackKeepAlive = OnEventRecord;
        var logFile = new EVENT_TRACE_LOGFILEW
        {
            LoggerName = Marshal.StringToHGlobalUni(SessionName),
            ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD,
            EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callbackKeepAlive)
        };

        int sz = Marshal.SizeOf<EVENT_TRACE_LOGFILEW>();
        _logfileBuffer = Marshal.AllocHGlobal(sz);
        Marshal.StructureToPtr(logFile, _logfileBuffer, false);

        _traceHandle = OpenTraceW(_logfileBuffer);
        Marshal.FreeHGlobal(logFile.LoggerName);

        if (_traceHandle == INVALID_TRACE_HANDLE || _traceHandle == 0)
            return false;

        _running = true;
        _traceThread = new Thread(() =>
        {
            try
            {
                ProcessTrace([_traceHandle], 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "ImplexityOverlayEtw",
            Priority = ThreadPriority.BelowNormal
        };
        _traceThread.Start();
        return true;
    }

    private void OnEventRecord(ref EVENT_RECORD record)
    {
        if (!_running)
            return;

        uint pid = record.EventHeader.ProcessId;
        if (pid == 0 || pid != _targetPid)
            return;

        var provider = record.EventHeader.ProviderId;
        ushort id = record.EventHeader.EventDescriptor.Id;

        // Используем точный аппаратный QPC-штамп ядра (ClientContext = 1),
        // а не время доставки пакета из ETW-буфера (Stopwatch.GetTimestamp).
        long eventTick = record.EventHeader.TimeStamp;
        if (eventTick <= 0)
            eventTick = Stopwatch.GetTimestamp();

        StreamKind kind;
        ulong swapChain = 0;
        bool is32Bit = (record.EventHeader.Flags & 0x0020) != 0;
        int ptrSize = is32Bit ? 4 : 8;

        if (provider == DxgiProvider && id == 42)
        {
            kind = StreamKind.Dxgi;
            if (record.UserData != IntPtr.Zero && record.UserDataLength >= ptrSize)
            {
                swapChain = is32Bit
                    ? (uint)Marshal.ReadInt32(record.UserData)
                    : (ulong)Marshal.ReadInt64(record.UserData);
                if (record.UserDataLength >= ptrSize + 4)
                {
                    uint flags = (uint)Marshal.ReadInt32(record.UserData, ptrSize);
                    if ((flags & DXGI_PRESENT_TEST) != 0)
                        return;
                }
            }
            _lastDxgiOrD3d9Tick = eventTick;
        }
        else if (provider == D3d9Provider && id == 1)
        {
            kind = StreamKind.D3d9;
            if (record.UserData != IntPtr.Zero && record.UserDataLength >= ptrSize)
            {
                swapChain = is32Bit
                    ? (uint)Marshal.ReadInt32(record.UserData)
                    : (ulong)Marshal.ReadInt64(record.UserData);
            }
            _lastDxgiOrD3d9Tick = eventTick;
        }
        else if (provider == DxgKrnlProvider && id == 184)
        {
            // DxgKrnl Present — только как резерв для OpenGL / Vulkan без DXGI и D3D9
            if (eventTick - _lastDxgiOrD3d9Tick <= Stopwatch.Frequency * 2)
                return;
            kind = StreamKind.DxgKrnl;
        }
        else
        {
            return;
        }

        ulong keyId = swapChain != 0 ? swapChain : record.EventHeader.ThreadId;
        var key = new StreamKey(kind, keyId);

        lock (_sync)
        {
            if (!_streams.TryGetValue(key, out var stream))
            {
                if (_streams.Count >= 16)
                    PruneStaleStreams(eventTick);
                if (_streams.Count >= 16)
                    return;

                stream = new PresentStream();
                _streams[key] = stream;
            }
            stream.Push(eventTick);
        }
    }

    private void PruneStaleStreams(long nowTick)
    {
        long limit = Stopwatch.Frequency * 3;
        List<StreamKey>? toRemove = null;
        foreach (var kv in _streams)
        {
            if (nowTick - kv.Value.LastTick > limit)
            {
                toRemove ??= new List<StreamKey>();
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove != null)
        {
            foreach (var k in toRemove)
                _streams.Remove(k);
        }
    }

    /// <summary>Обновляет целевой процесс по текущему активному окну и возвращает реальные FPS / Frametime / 1% Low.</summary>
    public FrameMetrics Sample()
    {
        UpdateForegroundTarget();

        long nowTick = Stopwatch.GetTimestamp();
        long staleLimit = Stopwatch.Frequency * 2;
        long oneSecTicks = Stopwatch.Frequency;
        long twoSecTicks = Stopwatch.Frequency * 2;
        double freqMs = 1000.0 / Stopwatch.Frequency;

        long[]? copied = null;
        int copiedCount = 0;

        lock (_sync)
        {
            PresentStream? bestStream = null;
            int bestScore = -1;
            bool hasPrimaryApi = false;

            foreach (var kv in _streams)
            {
                if (nowTick - kv.Value.LastTick <= staleLimit &&
                    kv.Value.Count >= 2 &&
                    kv.Key.Kind is StreamKind.Dxgi or StreamKind.D3d9)
                {
                    hasPrimaryApi = true;
                    break;
                }
            }

            foreach (var kv in _streams)
            {
                var st = kv.Value;
                if (st.Count < 2 || nowTick - st.LastTick > staleLimit)
                    continue;
                if (hasPrimaryApi && kv.Key.Kind == StreamKind.DxgKrnl)
                    continue;

                // Выбираем главный игровой SwapChain по числу кадров за последнюю секунду
                long cutoff = st.LastTick - oneSecTicks;
                int recentFrames = 0;
                for (int i = 0; i < st.Count; i++)
                {
                    if (st.Timestamps[i] >= cutoff)
                        recentFrames++;
                }

                if (recentFrames > bestScore)
                {
                    bestScore = recentFrames;
                    bestStream = st;
                }
            }

            if (bestStream != null)
            {
                copiedCount = bestStream.Count;
                copied = new long[copiedCount];
                int start = copiedCount == bestStream.Timestamps.Length ? bestStream.Head : 0;
                for (int i = 0; i < copiedCount; i++)
                    copied[i] = bestStream.Timestamps[(start + i) % bestStream.Timestamps.Length];
            }
        }

        if (copied == null || copiedCount < 2)
            return new FrameMetrics(0, 0, 0, _targetProcessName);

        // Сортируем QPC-метки: события с разных ядер CPU могут сбрасываться из ETW-буферов с микро-разбросом
        Array.Sort(copied, 0, copiedCount);

        long latestTick = copied[copiedCount - 1];
        long historyCutoff = latestTick - twoSecTicks;
        int start2s = 0;
        while (start2s < copiedCount - 2 && copied[start2s] < historyCutoff)
            start2s++;

        // Фильтруем дублирующиеся мгновенные штампы (<0.2 мс), если драйвер прислал дубликат
        double minValidTickDelta = 0.2 / freqMs;
        var validTicks = new List<long>(copiedCount - start2s);
        validTicks.Add(copied[start2s]);
        for (int i = start2s + 1; i < copiedCount; i++)
        {
            if (copied[i] - validTicks[^1] >= minValidTickDelta)
                validTicks.Add(copied[i]);
        }

        if (validTicks.Count < 2)
            return new FrameMetrics(0, 0, 0, _targetProcessName);

        int intervalCount = validTicks.Count - 1;
        var intervalsMs = new double[intervalCount];
        for (int i = 0; i < intervalCount; i++)
            intervalsMs[i] = (validTicks[i + 1] - validTicks[i]) * freqMs;

        // Средний FPS и Frametime по точному окну последней 1.0 секунды: (N - 1) / spanSec
        long cutoff1s = validTicks[^1] - oneSecTicks;
        int firstIdx1s = validTicks.Count - 2;
        while (firstIdx1s > 0 && validTicks[firstIdx1s] > cutoff1s)
            firstIdx1s--;

        int framesIn1s = validTicks.Count - 1 - firstIdx1s;
        double span1sMs = (validTicks[^1] - validTicks[firstIdx1s]) * freqMs;
        double avgMs = framesIn1s > 0 && span1sMs > 0.1 ? span1sMs / framesIn1s : intervalsMs[^1];
        double fps = avgMs > 0.05 ? 1000.0 / avgMs : 0;

        // Хронологический график фреймрейта (до 44 сглаженных точек за последние ~2 секунды)
        const int targetPoints = 44;
        int pointCount = Math.Min(intervalCount, targetPoints);
        var fpsHistory = new double[pointCount];
        for (int p = 0; p < pointCount; p++)
        {
            int from = (int)Math.Floor(p * (double)intervalCount / pointCount);
            int to = (int)Math.Floor((p + 1) * (double)intervalCount / pointCount);
            if (to <= from) to = from + 1;

            double bSum = 0;
            for (int j = from; j < to && j < intervalCount; j++)
                bSum += intervalsMs[j];

            double bAvg = bSum / Math.Max(1, to - from);
            fpsHistory[p] = bAvg > 0.05 ? Math.Round(1000.0 / bAvg, 1) : 0;
        }

        // 1% Low (верхний 99-й процентиль времени кадра за ~2 сек)
        Array.Sort(intervalsMs);
        int p99Index = Math.Clamp((int)Math.Ceiling(intervalCount * 0.99) - 1, 0, intervalCount - 1);
        double p99Ms = intervalsMs[p99Index];
        double low1Fps = p99Ms > 0.05 ? 1000.0 / p99Ms : fps;

        return new FrameMetrics(
            Math.Round(fps, 0),
            Math.Round(avgMs, 1),
            Math.Round(low1Fps, 0),
            _targetProcessName,
            fpsHistory);
    }

    private void UpdateForegroundTarget()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == _selfPid || pid == _targetPid)
                return;

            string procName = string.Empty;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                procName = p.ProcessName + ".exe";
            }
            catch
            {
                return;
            }

            // Игнорируем системную оболочку проводника, чтобы при сворачивании не терять данные
            if (string.Equals(procName, "ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "SearchHost.exe", StringComparison.OrdinalIgnoreCase))
                return;

            lock (_sync)
            {
                _targetPid = pid;
                _targetProcessName = procName;
                _streams.Clear();
                _lastDxgiOrD3d9Tick = 0;
            }
        }
        catch
        {
        }
    }

    private static void StopExistingSession()
    {
        int propSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>() + (SessionName.Length + 64) * 2;
        IntPtr propPtr = Marshal.AllocHGlobal(propSize);
        try
        {
            ZeroMemory(propPtr, propSize);
            var props = new EVENT_TRACE_PROPERTIES
            {
                Wnode = new WNODE_HEADER { BufferSize = (uint)propSize, Flags = WNODE_FLAG_TRACED_GUID },
                LoggerNameOffset = (uint)Marshal.SizeOf<EVENT_TRACE_PROPERTIES>()
            };
            Marshal.StructureToPtr(props, propPtr, false);
            ControlTraceW(0, SessionName, propPtr, EVENT_TRACE_CONTROL_STOP);
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(propPtr);
        }
    }

    private static void ZeroMemory(IntPtr ptr, int bytes)
    {
        byte[] zeros = new byte[bytes];
        Marshal.Copy(zeros, 0, ptr, bytes);
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            if (_traceHandle != INVALID_TRACE_HANDLE && _traceHandle != 0)
            {
                CloseTrace(_traceHandle);
                _traceHandle = INVALID_TRACE_HANDLE;
            }
        }
        catch { }

        StopExistingSession();
        _sessionHandle = 0;

        try { _traceThread?.Join(500); } catch { }
        _traceThread = null;

        if (_logfileBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_logfileBuffer);
            _logfileBuffer = IntPtr.Zero;
        }
    }
}
