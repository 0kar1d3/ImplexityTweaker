using System.IO;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>
/// Снимок состояния «до твиков» и откат к нему.
/// Сохраняется всё, что меняет твикер: ветки реестра, типы запуска служб,
/// состояние задач планировщика, файл hosts, активная схема питания,
/// резервная копия загрузчика (BCD) и настройки приложения.
/// </summary>
public static class TweakBackupService
{
    /// <summary>Сколько последних снимков хранить. Старые удаляются автоматически.</summary>
    public const int MaxSnapshots = 10;

    /// <summary>
    /// Ветки реестра, в которые может писать твикер. Список намеренно широкий:
    /// лишний ключ в снимке лучше, чем не сохранённое изменение.
    /// </summary>
    private static readonly (string Hive, string Path)[] RegistryKeys =
    [
        ("HKCU", @"Control Panel\Accessibility\StickyKeys"),
        ("HKCU", @"Control Panel\Desktop"),
        ("HKCU", @"Software\Microsoft\GameBar"),
        ("HKCU", @"Software\Microsoft\Siuf\Rules"),
        ("HKCU", @"Software\Microsoft\Windows\DWM"),
        ("HKCU", @"Software\Microsoft\Windows\Windows Error Reporting"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\CloudStore"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Privacy"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"),

        ("HKLM", @"SOFTWARE\Policies\Microsoft"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"),

        ("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Control\Power"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\WinSock2\Parameters"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\dmwappushservice"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\SysMain"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\UsoSvc"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\wuauserv"),
    ];

    /// <summary>Службы, тип запуска которых меняет твикер.</summary>
    private static readonly string[] Services =
    [
        "DiagTrack", "dmwappushservice", "SysMain", "UsoSvc", "WaaSMedicSvc", "wuauserv"
    ];

    /// <summary>Задачи планировщика, которые твикер отключает.</summary>
    private static readonly string[] ScheduledTasks =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Device Information\Device"
    ];

    private static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Implexity");

    public static string BackupsFolder => Path.Combine(DataRoot, "backups");

    private static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    private static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    /// <summary>Что удалось сохранить в снимке (0/false — не получилось или нечего было сохранять).</summary>
    public sealed record SnapshotInfo(
        string Name,
        string Folder,
        DateTime Created,
        int KeyCount,
        int ServiceCount,
        int TaskCount,
        bool HasHosts,
        bool HasPower,
        bool HasBcd);

    /// <summary>Итог отката: сколько чего восстановлено.</summary>
    public sealed record RestoreResult(
        int Keys,
        int Services,
        int Tasks,
        bool Hosts,
        bool Power,
        bool Bcd,
        bool Settings);
    /// <summary>Список сохранённых снимков (сначала новые).</summary>
    public static IReadOnlyList<SnapshotInfo> List()
    {
        var result = new List<SnapshotInfo>();

        try
        {
            if (!Directory.Exists(BackupsFolder))
                return result;

            foreach (var dir in Directory.GetDirectories(BackupsFolder))
            {
                var info = new SnapshotInfo(
                    Path.GetFileName(dir), dir, Directory.GetCreationTime(dir), 0, 0, 0, false, false, false);

                try
                {
                    var manifestFile = Path.Combine(dir, "manifest.json");
                    if (File.Exists(manifestFile))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
                        var root = doc.RootElement;

                        var created = root.TryGetProperty("Created", out var createdProp)
                            && createdProp.TryGetDateTime(out var parsed)
                                ? parsed
                                : info.Created;

                        var keys = root.TryGetProperty("Keys", out var keysProp)
                            && keysProp.ValueKind == JsonValueKind.Array
                                ? keysProp.GetArrayLength()
                                : 0;

                        info = new SnapshotInfo(
                            Path.GetFileName(dir),
                            dir,
                            created,
                            keys,
                            ReadInt(root, "Services"),
                            ReadInt(root, "Tasks"),
                            ReadBool(root, "Hosts"),
                            ReadBool(root, "Power"),
                            ReadBool(root, "Bcd"));
                    }
                }
                catch
                {
                    // повреждённый манифест — показываем снимок как есть
                }

                result.Add(info);
            }
        }
        catch
        {
            // нет доступа к папке — вернём пустой список
        }

        result.Sort((a, b) => b.Created.CompareTo(a.Created));
        return result;
    }

    /// <summary>Создаёт снимок текущего состояния. Возвращает имя снимка.</summary>
    public static async Task<string> CreateAsync()
    {
        Directory.CreateDirectory(BackupsFolder);

        var name = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(BackupsFolder, name);
        Directory.CreateDirectory(dir);

        // ===== 1. Ветки реестра =====
        var keys = new List<string>();
        for (var i = 0; i < RegistryKeys.Length; i++)
        {
            var (hive, path) = RegistryKeys[i];
            var file = Path.Combine(dir, $"key{i:D2}.reg");

            try
            {
                var r = await CommandRunner.RunProcessAsync("reg.exe", $"export \"{hive}\\{path}\" \"{file}\" /y");
                if (r.ExitCode == 0 && File.Exists(file))
                    keys.Add($"{hive}\\{path}");
            }
            catch
            {
                // такой ветки может не быть в этой сборке Windows — пропускаем
            }
        }
        // ===== 2. Службы: запоминаем тип запуска =====
        var serviceCount = 0;
        try
        {
            var list = string.Join(",", Services.Select(s => $"'{s}'"));
            var r = await CommandRunner.RunPowerShellAsync(
                $"Get-Service -Name {list} -ErrorAction SilentlyContinue | " +
                "ForEach-Object { Write-Output ($_.Name + ';' + $_.StartType) }");

            var lines = r.Output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains(';'))
                .ToList();

            if (lines.Count > 0)
            {
                File.WriteAllLines(Path.Combine(dir, "services.txt"), lines);
                serviceCount = lines.Count;
            }
        }
        catch
        {
            // не критично
        }

        // ===== 3. Задачи планировщика: запоминаем состояние =====
        var taskCount = 0;
        try
        {
            var pairs = string.Join(", ", ScheduledTasks.Select(task =>
            {
                var idx = task.LastIndexOf('\\');
                var taskPath = task[..(idx + 1)].Replace("'", "''");
                var taskName = task[(idx + 1)..].Replace("'", "''");
                return $"@('{taskPath}','{taskName}')";
            }));

            var r = await CommandRunner.RunPowerShellAsync(
                $"$list = @({pairs}); " +
                "foreach ($item in $list) { $t = Get-ScheduledTask -TaskPath $item[0] -TaskName $item[1] -ErrorAction SilentlyContinue; " +
                "if ($null -ne $t) { Write-Output ($item[0] + $item[1] + ';' + $t.State) } }");

            var lines = r.Output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains(';'))
                .ToList();

            if (lines.Count > 0)
            {
                File.WriteAllLines(Path.Combine(dir, "tasks.txt"), lines);
                taskCount = lines.Count;
            }
        }
        catch
        {
            // не критично
        }

        // ===== 4. hosts =====
        var hasHosts = false;
        try
        {
            if (File.Exists(HostsPath))
            {
                File.Copy(HostsPath, Path.Combine(dir, "hosts.bak"), true);
                hasHosts = true;
            }
        }
        catch
        {
            // не критично
        }
        // ===== 5. Активная схема питания + гибернация =====
        var hasPower = false;
        try
        {
            var r = await CommandRunner.RunPowerShellAsync(@"
$active = powercfg /list | Where-Object { $_ -match '\*' } | Select-Object -First 1
if ($active -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') { Write-Output $matches[1] } else { Write-Output '' }
$hib = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name 'HibernateEnabled' -ErrorAction SilentlyContinue).HibernateEnabled
if ($null -eq $hib) { $hib = 1 }
Write-Output ('hib=' + $hib)
");
            var lines = r.Output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var guid = lines.FirstOrDefault(l => !l.StartsWith("hib="));
            var hibernate = lines.FirstOrDefault(l => l.StartsWith("hib="))?[4..] ?? "1";

            if (!string.IsNullOrWhiteSpace(guid))
            {
                var file = Path.Combine(dir, "power.pow");
                await CommandRunner.RunProcessAsync("powercfg.exe", $"/export \"{file}\" {guid}");
                if (File.Exists(file))
                {
                    File.WriteAllLines(Path.Combine(dir, "power.txt"), [guid, "hib=" + hibernate]);
                    hasPower = true;
                }
            }
        }
        catch
        {
            // не критично
        }

        // ===== 6. Загрузчик (BCD) =====
        var hasBcd = false;
        try
        {
            var file = Path.Combine(dir, "bcd.dat");
            var r = await CommandRunner.RunProcessAsync("bcdedit.exe", $"/export \"{file}\"");
            hasBcd = r.ExitCode == 0 && File.Exists(file);
        }
        catch
        {
            // не критично
        }

        // ===== 7. Настройки приложения =====
        try
        {
            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, Path.Combine(dir, "settings.json"), true);
        }
        catch
        {
            // не критично
        }

        var manifest = new Manifest
        {
            Name = name,
            Created = DateTime.Now,
            Keys = keys,
            Services = serviceCount,
            Tasks = taskCount,
            Hosts = hasHosts,
            Power = hasPower,
            Bcd = hasBcd
        };

        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        PruneOldSnapshots();

        return name;
    }
    /// <summary>Возвращает состояние к снимку: реестр, службы, задачи, hosts, питание, BCD, настройки.</summary>
    public static async Task<RestoreResult> RestoreAsync(string name)
    {
        var dir = Path.Combine(BackupsFolder, name);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(LocalizationService.T("Снимок не найден: ") + name);

        // ===== Реестр =====
        var keys = 0;
        foreach (var file in Directory.GetFiles(dir, "key*.reg").OrderBy(f => f))
        {
            try
            {
                var r = await CommandRunner.RunProcessAsync("reg.exe", $"import \"{file}\"");
                if (r.ExitCode == 0)
                    keys++;
            }
            catch
            {
                // один ключ не восстановился — продолжаем
            }
        }

        // ===== hosts =====
        var hosts = false;
        try
        {
            var copy = Path.Combine(dir, "hosts.bak");
            if (File.Exists(copy))
            {
                File.Copy(copy, HostsPath, true);
                hosts = true;
            }
        }
        catch
        {
            // не критично
        }

        // ===== Службы =====
        var services = 0;
        var servicesFile = Path.Combine(dir, "services.txt");
        if (File.Exists(servicesFile))
        {
            foreach (var raw in File.ReadAllLines(servicesFile))
            {
                var parts = raw.Split(';');
                if (parts.Length < 2)
                    continue;

                var mode = parts[1].Trim().ToLowerInvariant() switch
                {
                    "automatic" => "auto",
                    "manual" => "demand",
                    "disabled" => "disabled",
                    "boot" => "boot",
                    "system" => "system",
                    _ => null
                };

                if (mode is null)
                    continue;

                try
                {
                    var r = await CommandRunner.RunProcessAsync("sc.exe", $"config {parts[0].Trim()} start= {mode}");
                    if (r.ExitCode == 0)
                        services++;
                }
                catch
                {
                    // не критично
                }
            }
        }

        // ===== Задачи планировщика =====
        var tasks = 0;
        var tasksFile = Path.Combine(dir, "tasks.txt");
        if (File.Exists(tasksFile))
        {
            foreach (var raw in File.ReadAllLines(tasksFile))
            {
                var idx = raw.LastIndexOf(';');
                if (idx <= 0)
                    continue;

                var taskName = raw[..idx].Trim();
                var disabled = raw[(idx + 1)..].Trim().Equals("Disabled", StringComparison.OrdinalIgnoreCase);

                try
                {
                    var r = await CommandRunner.RunProcessAsync("schtasks.exe",
                        $"/Change /TN \"{taskName}\" {(disabled ? "/Disable" : "/Enable")}");
                    if (r.ExitCode == 0)
                        tasks++;
                }
                catch
                {
                    // не критично
                }
            }
        }
        // ===== Схема питания =====
        var power = false;
        try
        {
            var file = Path.Combine(dir, "power.pow");
            var info = Path.Combine(dir, "power.txt");
            if (File.Exists(file) && File.Exists(info))
            {
                var lines = File.ReadAllLines(info);
                var guid = lines.FirstOrDefault()?.Trim();

                if (!string.IsNullOrWhiteSpace(guid))
                {
                    await CommandRunner.RunProcessAsync("powercfg.exe", $"/import \"{file}\"");
                    var r = await CommandRunner.RunProcessAsync("powercfg.exe", $"/setactive {guid}");
                    power = r.ExitCode == 0;
                }

                var hibernate = lines.FirstOrDefault(l => l.StartsWith("hib="))?[4..];
                if (hibernate == "1")
                    await CommandRunner.RunProcessAsync("powercfg.exe", "/h on");
                else if (hibernate == "0")
                    await CommandRunner.RunProcessAsync("powercfg.exe", "/h off");
            }
        }
        catch
        {
            // не критично
        }

        // ===== Загрузчик =====
        var bcd = false;
        try
        {
            var file = Path.Combine(dir, "bcd.dat");
            if (File.Exists(file))
            {
                var r = await CommandRunner.RunProcessAsync("bcdedit.exe", $"/import \"{file}\"");
                bcd = r.ExitCode == 0;
            }
        }
        catch
        {
            // не критично
        }

        // ===== Настройки приложения =====
        var settings = false;
        try
        {
            var copy = Path.Combine(dir, "settings.json");
            if (File.Exists(copy))
            {
                File.Copy(copy, SettingsPath, true);
                settings = true;
            }
        }
        catch
        {
            // не критично
        }

        return new RestoreResult(keys, services, tasks, hosts, power, bcd, settings);
    }

    /// <summary>Удаляет снимок с диска.</summary>
    public static void Delete(string name)
    {
        var dir = Path.Combine(BackupsFolder, name);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }

    /// <summary>Оставляет только последние <see cref="MaxSnapshots"/> снимков.</summary>
    private static void PruneOldSnapshots()
    {
        try
        {
            var old = Directory.GetDirectories(BackupsFolder)
                .OrderByDescending(d => Path.GetFileName(d))
                .Skip(MaxSnapshots);

            foreach (var dir in old)
                Directory.Delete(dir, true);
        }
        catch
        {
            // не критично: места на диске хватает
        }
    }

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True;

    private sealed class Manifest
    {
        public string Name { get; set; } = string.Empty;
        public DateTime Created { get; set; }
        public List<string> Keys { get; set; } = [];
        public int Services { get; set; }
        public int Tasks { get; set; }
        public bool Hosts { get; set; }
        public bool Power { get; set; }
        public bool Bcd { get; set; }
    }
}
