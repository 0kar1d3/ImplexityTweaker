using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>Один замер бенчмарка: подпись (ключ локализации), значение и единица измерения.</summary>
public sealed record BenchmarkMetric(string Title, double Value, string Unit, bool LowerIsBetter = false);

/// <summary>Итог одного прогона бенчмарка (сохраняется в JSON в папке результатов).</summary>
public sealed record BenchmarkResult(
    DateTime Created,
    string CpuName,
    int LogicalCores,
    string OsLabel,
    double CpuSingle,
    double CpuMulti,
    double Memory,
    double DiskWrite,
    double DiskRead,
    double PingAvg,
    double PingJitter,
    double BootSeconds,
    double Index)
{
    /// <summary>Метрики в порядке отображения.</summary>
    public IReadOnlyList<BenchmarkMetric> Metrics =>
    [
        new("Процессор, одно ядро", CpuSingle, "MOps/s"),
        new("Процессор, все ядра", CpuMulti, "MOps/s"),
        new("Память (копирование)", Memory, "ГБ/с"),
        new("Диск, запись (128 МБ)", DiskWrite, "МБ/с"),
        new("Диск, чтение (128 МБ)", DiskRead, "МБ/с"),
        // Removed ping metrics
        new("Загрузка Windows (последняя)", BootSeconds, "с", true),
        new("Индекс Implexity", Index, "ед."),
    ];
}

/// <summary>Прогресс бенчмарка: процент и подпись текущего этапа.</summary>
public sealed record BenchmarkProgress(int Percent, string Label);

/// <summary>
/// Лёгкий встроенный бенчмарк: процессор (одно ядро и все ядра), память, диск,
/// сеть и время последней загрузки Windows. Результаты сохраняются в JSON,
/// чтобы сравнивать свои прогоны между собой.
/// </summary>
public static class BenchmarkService
{
    /// <summary>Опорные значения «среднего» домашнего ПК — по ним считается индекс 1000.</summary>
    private const double RefCpuSingle = 1500;   // MOps/s
    private const double RefMemory = 9;         // ГБ/с
    private const double RefDiskWrite = 150;    // МБ/с
    private const double RefDiskRead = 400;     // МБ/с

    public static string ResultsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Implexity", "benchmarks");

    private static double _sink;

    /// <summary>Запускает полный тест. Вызывать в фоне (не на UI-потоке).</summary>
    public static async Task<BenchmarkResult> RunAsync(IProgress<BenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new BenchmarkProgress(0, "Подготовка…"));

        progress?.Report(new BenchmarkProgress(8, "Процессор: одно ядро…"));
        var cpuSingle = MeasureCpuSingle(ct);

        progress?.Report(new BenchmarkProgress(30, "Процессор: все ядра…"));
        var cpuMulti = MeasureCpuMulti(ct);

        progress?.Report(new BenchmarkProgress(52, "Память…"));
        var memory = MeasureMemory(ct);

        progress?.Report(new BenchmarkProgress(66, "Диск…"));
        var disk = await MeasureDiskAsync(ct);


        progress?.Report(new BenchmarkProgress(88, "Загрузка Windows…"));
        var boot = await MeasureBootTimeAsync();

        progress?.Report(new BenchmarkProgress(99, "Считаем индекс…"));
        var logical = Environment.ProcessorCount;
        var index = ComputeIndex(cpuSingle, cpuMulti, memory, disk.Write, disk.Read, logical);

        var result = new BenchmarkResult(
            DateTime.Now,
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU",
            logical,
            WindowsVersionService.VersionLabel,
            cpuSingle,
            cpuMulti,
            memory,
            disk.Write,
            disk.Read,
            0,
            0,
            boot,
            index);

        progress?.Report(new BenchmarkProgress(100, "Готово"));
        return result;
    }

    /// <summary>
    /// Относительный индекс: 1000 — средний домашний ПК (опорные значения выше).
    /// Нужен для сравнения своих прогонов, а не для сравнения с другими сайтами.
    /// </summary>
    private static double ComputeIndex(double cpuSingle, double cpuMulti, double memory,
        double diskWrite, double diskRead, int logicalCores)
    {
        var refMulti = RefCpuSingle * Math.Max(1, logicalCores) * 0.85;

        // Отсутствующий замер (<= 0, например «нет данных» из-за занятого или полного диска)
        // считаем нейтральным, а не провальным: иначе один неснятый замер роняет индекс.
        double Higher(double value, double reference) => value <= 0 || reference <= 0 ? 1 : Clamp(value / reference);

        var score =
            0.28 * Higher(cpuSingle, RefCpuSingle) +
            0.27 * Higher(cpuMulti, refMulti) +
            0.21 * Higher(memory, RefMemory) +
            0.12 * Higher(diskWrite, RefDiskWrite) +
            0.12 * Higher(diskRead, RefDiskRead);

        return Math.Round(1000 * score, 0);
    }

    private static double Clamp(double value, double min = 0.2, double max = 3) =>
        value < min ? min : value > max ? max : value;

    private static double MeasureCpuSingle(CancellationToken ct)
    {
        const int batch = 20000;
        var sw = Stopwatch.StartNew();
        double sink = 0;
        long ops = 0;

        while (sw.ElapsedMilliseconds < 1200)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < batch; i++)
            {
                sink += Math.Sqrt(i + 1.0000001);
                sink *= 1.0000000001;
            }
            ops += batch;
        }

        sw.Stop();
        _sink = sink;
        return Math.Round(ops / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1_000_000.0, 1);
    }

    private static double MeasureCpuMulti(CancellationToken ct)
    {
        const int batch = 20000;
        var sw = Stopwatch.StartNew();
        var workers = Math.Max(1, Environment.ProcessorCount);
        long total = 0;
        double sink = 0;

        Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, _ =>
        {
            double local = 0;
            long ops = 0;
            while (sw.ElapsedMilliseconds < 1200)
            {
                for (var i = 0; i < batch; i++)
                {
                    local += Math.Sqrt(i + 1.0000001);
                    local *= 1.0000000001;
                }
                ops += batch;
            }
            Interlocked.Add(ref total, ops);
            Interlocked.Exchange(ref sink, local);
        });

        sw.Stop();
        _sink = sink;
        return Math.Round(total / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1_000_000.0, 1);
    }

    private static double MeasureMemory(CancellationToken ct)
    {
        const int size = 16 * 1024 * 1024;   // 16 млн long = 128 МБ
        var a = new long[size];
        var b = new long[size];
        for (var i = 0; i < size; i++)
            a[i] = i;

        var sw = Stopwatch.StartNew();
        long moved = 0;
        while (sw.ElapsedMilliseconds < 800)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < size; i++)
            {
                b[i] = a[i] + 1;
                moved += 16;   // 8 байт чтения + 8 байт записи
            }
        }
        sw.Stop();
        _sink = b[0];

        return Math.Round(moved / Math.Max(0.001, sw.Elapsed.TotalSeconds) / (1024.0 * 1024 * 1024), 2);
    }

    private static async Task<(double Write, double Read)> MeasureDiskAsync(CancellationToken ct)
    {
        const int chunk = 1024 * 1024;    // 1 МБ за операцию
        const int chunks = 128;           // всего 128 МБ
        var path = Path.Combine(Path.GetTempPath(), "implexity-bench.tmp");

        var block = new byte[chunk];
        new Random(12345).NextBytes(block);

        double write = 0;
        double read = 0;

        try
        {
            var sw = Stopwatch.StartNew();
            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, chunk, FileOptions.SequentialScan))
            {
                for (var i = 0; i < chunks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await fs.WriteAsync(block.AsMemory(0, chunk), ct);
                }
                await fs.FlushAsync(ct);
            }
            sw.Stop();
            write = Math.Round(chunks / Math.Max(0.001, sw.Elapsed.TotalSeconds), 1);   // МБ/с

            var buffer = new byte[chunk];
            sw.Restart();
            long readBytes = 0;
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunk, FileOptions.SequentialScan))
            {
                int n;
                while ((n = await fs.ReadAsync(buffer.AsMemory(0, chunk), ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    readBytes += n;
                }
            }
            sw.Stop();
            _sink = buffer[0];
            read = Math.Round(readBytes / 1024.0 / 1024.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds), 1);   // МБ/с
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Диск может быть занят или недоступен — оставляем нули (в отчёте будет «нет данных»).
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Временный файл удалит система.
            }
        }

        return (write, read);
    }

    /// <summary>Время последней загрузки Windows в секундах (0 — данных нет).</summary>
    private static async Task<double> MeasureBootTimeAsync()
    {
        const string script = @"
$e = Get-WinEvent -MaxEvents 1 -FilterHashtable @{ LogName = 'Microsoft-Windows-Diagnostics-Performance/Operational'; Id = 100 } -ErrorAction SilentlyContinue
if ($e) { [math]::Round([double]$e.Properties[3].Value / 1000, 1) }
";
        try
        {
            var r = await CommandRunner.RunPowerShellAsync(script);
            var text = (r.Output ?? string.Empty).Trim();
            if (r.ExitCode == 0 && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return Math.Round(seconds, 1);
        }
        catch
        {
            // Журнал производительности может быть отключён — это не ошибка теста.
        }

        return 0;
    }

    private static JsonSerializerOptions JsonOptions => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Сохраняет результат в папку результатов и возвращает путь к файлу.</summary>
    public static string Save(BenchmarkResult result)
    {
        Directory.CreateDirectory(ResultsFolder);
        var path = Path.Combine(ResultsFolder, $"benchmark-{result.Created:yyyy-MM-dd-HH-mm-ss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
        return path;
    }

    /// <summary>История прогонов: сначала новые.</summary>
    public static IReadOnlyList<BenchmarkResult> LoadHistory(int max = 20)
    {
        var list = new List<BenchmarkResult>();

        try
        {
            if (!Directory.Exists(ResultsFolder))
                return list;

            foreach (var file in Directory.GetFiles(ResultsFolder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(max))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<BenchmarkResult>(File.ReadAllText(file), JsonOptions);
                    if (result != null)
                        list.Add(result);
                }
                catch
                {
                    // Битый файл пропускаем.
                }
            }
        }
        catch
        {
            // Папки ещё нет.
        }

        return list.OrderByDescending(r => r.Created).ToList();
    }

    /// <summary>Открывает папку с результатами в проводнике.</summary>
    public static void OpenResultsFolder()
    {
        try
        {
            Directory.CreateDirectory(ResultsFolder);
            Process.Start(new ProcessStartInfo { FileName = ResultsFolder, UseShellExecute = true });
        }
        catch
        {
            // Проводник может быть недоступен — молча игнорируем.
        }
    }
}
