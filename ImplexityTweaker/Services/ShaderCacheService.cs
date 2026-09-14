using System.IO;

namespace ImplexityTweaker.Services;

/// <summary>Результат очистки кэша шейдеров GPU / DirectX.</summary>
public sealed class ShaderCacheCleanResult
{
    /// <summary>Количество удалённых файлов.</summary>
    public int DeletedFiles { get; init; }

    /// <summary>Суммарный объём освобождённого места в мегабайтах.</summary>
    public long FreedMb { get; init; }
}

/// <summary>
/// Очистка кэша шейдеров GPU и DirectX:
/// NVIDIA (DXCache, GLCache), AMD (DxCache) и системный D3DSCache.
/// Заблокированные (используемые) файлы пропускаются.
/// </summary>
public static class ShaderCacheService
{
    public static async Task<ShaderCacheCleanResult> ClearAsync()
    {
        return await ClearAsync(null);
    }

    /// <summary>
    /// Очищает кэш шейдеров GPU и DirectX (NVIDIA DXCache/GLCache, AMD DxCache, D3DSCache).
    /// </summary>
    /// <param name="progress">Опциональный прогресс в процентах 0..100.</param>
    public static async Task<ShaderCacheCleanResult> ClearAsync(IProgress<int>? progress)
    {
        return await Task.Run(() =>
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] cachePaths =
            {
                Path.Combine(localAppData, @"NVIDIA\DXCache"),
                Path.Combine(localAppData, @"NVIDIA\GLCache"),
                Path.Combine(localAppData, @"AMD\DxCache"),
                Path.Combine(localAppData, @"D3DSCache")
            };

            int deletedFiles = 0;
            long freedBytes = 0;
            int total = cachePaths.Length;

            for (int i = 0; i < total; i++)
            {
                var cachePath = cachePaths[i];
                progress?.Report((i + 1) * 100 / Math.Max(1, total));

                if (!Directory.Exists(cachePath))
                    continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(cachePath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long size = new FileInfo(file).Length;
                            File.Delete(file);
                            deletedFiles++;
                            freedBytes += size;
                        }
                        catch
                        {
                            // Файл заблокирован драйвером/процессом — пропускаем.
                        }
                    }
                }
                catch
                {
                    // Нет доступа к папке — пропускаем.
                }

                // Пытаемся удалить опустевшие подпапки (best-effort).
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                                Directory.Delete(dir, false);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            progress?.Report(100);

            return new ShaderCacheCleanResult
            {
                DeletedFiles = deletedFiles,
                FreedMb = freedBytes / 1024 / 1024
            };
        });
    }
}
