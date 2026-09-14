using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ImplexityTweaker.Services;

public static class CommandRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await RunProcessAsync(
            "powershell",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string fileName, string arguments)
    {
        return await RunProcessAsync(fileName, arguments, null, CancellationToken.None);
    }

    /// <summary>
    /// Запускает процесс с построчным чтением stdout/stderr и поддержкой отмены.
    /// <paramref name="onOutputLine"/> вызывается для каждой непустой строки вывода
    /// (удобно для парсинга прогресса DISM/SFC и обновления подписи в окне прогресса).
    /// При отмене процесс (и его дерево) принудительно завершается, а метод бросает
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName,
        string arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                output.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                error.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Пользователь отменил операцию — завершаем процесс и всё его дерево.
            TryKill(process);
            throw;
        }

        return (process.ExitCode, output.ToString(), error.ToString());
    }

    /// <summary>Запускает PowerShell-скрипт с построчным выводом и поддержкой отмены.</summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(
        string script,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await RunProcessAsync(
            "powershell",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            onOutputLine,
            cancellationToken);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Процесс уже завершился или недоступен — игнорируем.
        }
    }
}
