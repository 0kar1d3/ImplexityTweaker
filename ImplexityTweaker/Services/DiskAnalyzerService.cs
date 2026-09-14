using System.IO;

namespace ImplexityTweaker.Services;

/// <summary>Анализ занятого места на диске (папки и крупные файлы).</summary>
public static class DiskAnalyzerService
{
    public sealed record Item(string Path, string Name, long Bytes, bool IsFile);

    /// <summary>Считает размеры элементов внутри папки, сортирует по убыванию.</summary>
    public static Task<IReadOnlyList<Item>> AnalyzeAsync(string path, CancellationToken token)
        => Task.Run<IReadOnlyList<Item>>(() => Analyze(path, token), token);

    private static List<Item> Analyze(string path, CancellationToken token)
    {
        var list = new List<Item>();
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            return list;

        try
        {
            foreach (var sub in dir.EnumerateDirectories())
            {
                token.ThrowIfCancellationRequested();

                // Пропускаем ссылки/точки монтирования, иначе можно зациклиться.
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                list.Add(new Item(sub.FullName, sub.Name, DirectorySize(sub, token), false));
            }

            foreach (var file in dir.EnumerateFiles())
            {
                token.ThrowIfCancellationRequested();
                list.Add(new Item(file.FullName, file.Name, file.Length, true));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // нет доступа — вернём то, что успели собрать
        }

        list.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return list;
    }

    private static long DirectorySize(DirectoryInfo dir, CancellationToken token)
    {
        long total = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                token.ThrowIfCancellationRequested();
                try { total += file.Length; } catch { }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                token.ThrowIfCancellationRequested();
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                total += DirectorySize(sub, token);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // пропускаем недоступные элементы
        }
        return total;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = [LocalizationService.T("Б"), LocalizationService.T("КБ"), LocalizationService.T("МБ"), LocalizationService.T("ГБ"), LocalizationService.T("ТБ")];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
