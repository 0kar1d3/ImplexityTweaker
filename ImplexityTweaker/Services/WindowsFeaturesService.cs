namespace ImplexityTweaker.Services;

/// <summary>
/// Работа с компонентами Windows (Features) через DISM и PowerShell.
/// Все операции требуют прав администратора.
/// </summary>
public static class WindowsFeaturesService
{
    /// <summary>
    /// Проверяет, включён ли компонент (включая состояние "ожидает включения").
    /// При ошибке или недоступности WMI/DISM возвращает false.
    /// </summary>
    public static async Task<bool> IsFeatureEnabledAsync(string featureName)
    {
        try
        {
            var r = await CommandRunner.RunPowerShellAsync(
                $"(Get-WindowsOptionalFeature -Online -FeatureName '{featureName}' -ErrorAction SilentlyContinue).State");

            if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output))
                return false;

            var state = r.Output.Trim();
            return state.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                || state.Equals("EnablePending", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Включает или отключает компонент через DISM.
    /// При ошибке выбрасывает исключение с выводом DISM.
    /// <paramref name="cancellationToken"/> позволяет прервать операцию: процесс DISM
    /// (вместе с дочерними) принудительно завершается.
    /// </summary>
    public static async Task SetFeatureEnabledAsync(string featureName, bool enable, CancellationToken cancellationToken = default)
    {
        string arguments = enable
            ? $"/online /enable-feature /featurename:{featureName} /all /norestart"
            : $"/online /disable-feature /featurename:{featureName} /norestart";

        var r = await CommandRunner.RunProcessAsync("dism.exe", arguments, null, cancellationToken);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }
}
