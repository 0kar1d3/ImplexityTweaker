using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>Данные аккаунта с сайта Implexity.</summary>
public sealed record AccountInfo(
    string Id,
    string? Email,
    string? Username,
    string? AvatarUrl,
    bool IsAdmin,
    string? Prefix,
    string? PrefixColor,
    string? UserNumber,
    string? CreatedAt)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Username) ? (string.IsNullOrWhiteSpace(Email) ? LocalizationService.T("Пользователь") : Email!) : Username!;

    public string AccountNumber =>
        string.IsNullOrWhiteSpace(UserNumber) ? "#--" : "#" + UserNumber;
}

/// <summary>
/// Вход в аккаунт сайта Implexity (Supabase Auth) и хранение сессии.
/// Сессия сохраняется в %LOCALAPPDATA%\Implexity\account.json.
/// </summary>
public static class AccountService
{
    private const string SupabaseUrl = "https://zodhmbsfsbnebelxpxiy.supabase.co";
    private const string SupabaseAnonKey = "sb_publishable_FTtm6ytMv5BMiaWUdHFqDg_smwAsLIw";

    /// <summary>
    /// Адрес сайта Implexity (профили, конфиги, вход через сайт).
    /// </summary>
    public const string SiteUrl = "https://implexity.fun";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static string AccountFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Implexity", "account.json");

    public static AccountInfo? Current { get; private set; }
    public static bool IsLoggedIn => Current != null;

    private static string? _accessToken;
    private static string? _refreshToken;

    /// <summary>Вызывается после входа/выхода (для обновления виджета аккаунта).</summary>
    public static event EventHandler? AccountChanged;

    public static void Initialize()
    {
        LoadSavedSession();
        _ = Task.Run(RefreshSavedSessionAsync);
    }

    /// <summary>Фоновое обновление профиля при запуске (подтягивает актуальные ник/префикс/аватар).</summary>
    private static async Task RefreshSavedSessionAsync()
    {
        // Локальные снимки: фоновый поток не должен читать поля, которые UI-поток
        // может изменить вызовами LoginAsync/Logout (гонка без lock).
        var token = _accessToken;
        var account = Current;
        if (string.IsNullOrEmpty(token) || account == null)
            return;

        try
        {
            var (profile, status) = await FetchProfileWithStatusAsync(token, account.Id);
            if (status == ProfileFetchStatus.Ok && profile != null)
            {
                // Обновляем только если за время запроса не сменился пользователь (гонка с LoginAsync).
                if (Current?.Id == account.Id)
                {
                    Current = profile;
                    SaveSession();
                    AccountChanged?.Invoke(null, EventArgs.Empty);
                }
                return;
            }

            // Токен доступа недействителен — пробуем обновить пару токенов.
            if (status == ProfileFetchStatus.Unauthorized)
            {
                var (refreshed, unauthorized) = await TryRefreshTokenAsync();

                if (!refreshed)
                {
                    // Только если refresh-токен окончательно мёртв (401) — выходим из аккаунта.
                    // При сетевом сбое (refreshed=false, unauthorized=false) сессию НЕ стираем.
                    if (unauthorized && Current?.Id == account.Id)
                        Logout();
                    return;
                }

                var refreshedToken = _accessToken;
                if (string.IsNullOrEmpty(refreshedToken))
                    return;

                // Второй запрос профиля идёт по СНАПШОТУ account.Id, а не по Current.Id:
                // иначе при смене пользователя обновили бы чужой профиль и разлогинили свежий вход.
                var (profile2, status2) = await FetchProfileWithStatusAsync(refreshedToken, account.Id);
                if (status2 == ProfileFetchStatus.Ok && profile2 != null)
                {
                    if (Current?.Id == account.Id)
                    {
                        Current = profile2;
                        SaveSession();
                        AccountChanged?.Invoke(null, EventArgs.Empty);
                    }
                }
                else if (status2 == ProfileFetchStatus.Unauthorized)
                {
                    if (Current?.Id == account.Id)
                        Logout();
                }
                // status2 == Error | NotFound после успешного refresh — временный сбой: сессию сохраняем.
            }
            // status == Error | NotFound: сеть/5xx/профиль удалён — сессию НЕ стираем,
            // чтобы разовый сбой сети не уничтожал сохранённый вход.
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Обновляет пару токенов. Возвращает:
    /// Ok = токены обновлены; Unauthorized = refresh-токен окончательно недействителен (нужен повторный вход);
    /// оба false = временный сбой сети/сервера (сессию удалять нельзя).
    /// </summary>
    private static async Task<(bool Ok, bool Unauthorized)> TryRefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            return (false, true);

        try
        {
            var payload = JsonSerializer.Serialize(new { refresh_token = _refreshToken });
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"{SupabaseUrl}/auth/v1/token?grant_type=refresh_token")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", SupabaseAnonKey);

            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, true); // refresh-токен мёртв — нужен новый вход
            if (!resp.IsSuccessStatusCode)
                return (false, false); // 4xx/5xx, сеть — временно

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            _refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            return (true, false);
        }
        catch (HttpRequestException)
        {
            return (false, false); // сеть — временно
        }
        catch (OperationCanceledException)
        {
            return (false, false); // таймаут — временно
        }
        catch (Exception)
        {
            return (false, false);
        }
    }

    /// <summary>Вход по почте и паролю аккаунта сайта.</summary>
    public static async Task<(bool Ok, string? Error)> LoginAsync(string email, string password)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { email, password });
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"{SupabaseUrl}/auth/v1/token?grant_type=password")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", SupabaseAnonKey);

            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                // 5xx/сеть — сервер недоступен; 4xx — разбираем тело ошибки.
                if ((int)resp.StatusCode >= 500)
                    return (false, "Сервер недоступен. Попробуйте позже.");
                return (false, ExtractAuthError(body, (int)resp.StatusCode));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            _refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var userId = root.GetProperty("user").GetProperty("id").GetString() ?? "";

            var profile = await FetchProfileAsync(_accessToken!, userId);
            if (profile == null)
                return (false, "Не удалось получить профиль аккаунта.");

            Current = profile;
            SaveSession();
            AccountChanged?.Invoke(null, EventArgs.Empty);
            return (true, null);

        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static void Logout()
    {
        lock (SessionLock)
        {
            _sessionVersion++;
            Current = null;
            _accessToken = null;
            _refreshToken = null;
            try { if (File.Exists(AccountFilePath)) File.Delete(AccountFilePath); } catch { }
        }
        AccountChanged?.Invoke(null, EventArgs.Empty);
    }


    private enum ProfileFetchStatus { Ok, NotFound, Unauthorized, Error }

    /// <summary>
    /// Загрузка профиля с разграничением статусов:
    /// Unauthorized — токен недействителен; Error — сеть/5xx/таймаут; NotFound — профиль удалён.
    /// </summary>
    private static async Task<(AccountInfo? Profile, ProfileFetchStatus Status)> FetchProfileWithStatusAsync(string token, string userId)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, ProfileFetchStatus.Unauthorized);

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"{SupabaseUrl}/rest/v1/profiles?select=*&id=eq.{Uri.EscapeDataString(userId)}");
            req.Headers.Add("apikey", SupabaseAnonKey);
            req.Headers.Add("Authorization", "Bearer " + token);

            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (null, ProfileFetchStatus.Unauthorized);
            if (!resp.IsSuccessStatusCode)
                return (null, ProfileFetchStatus.Error);

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (null, ProfileFetchStatus.NotFound);

            var p = doc.RootElement[0];
            var profile = new AccountInfo(
                p.TryGetProperty("id", out var id) ? id.GetString() ?? userId : userId,
                p.TryGetProperty("email", out var em) ? em.GetString() : null,
                p.TryGetProperty("username", out var un) ? un.GetString() : null,
                p.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
                p.TryGetProperty("is_admin", out var ia) && ia.ValueKind == JsonValueKind.True,
                p.TryGetProperty("prefix", out var pf) ? pf.GetString() : null,
                p.TryGetProperty("prefix_color", out var pc) ? pc.GetString() : null,
                p.TryGetProperty("user_number", out var num) ? JsonNumberToString(num) : null,
                p.TryGetProperty("created_at", out var ca) ? ca.GetString() : null);
            return (profile, ProfileFetchStatus.Ok);
        }
        catch (HttpRequestException)
        {
            return (null, ProfileFetchStatus.Error);
        }
        catch (OperationCanceledException)
        {
            return (null, ProfileFetchStatus.Error);
        }
        catch (Exception)
        {
            return (null, ProfileFetchStatus.Error);
        }
    }

    /// <summary>Загрузка профиля; null при любой ошибке (для простых вызовов входа).</summary>
    private static async Task<AccountInfo?> FetchProfileAsync(string token, string userId)
    {
        var (profile, _) = await FetchProfileWithStatusAsync(token, userId);
        return profile;
    }

    private static string? JsonNumberToString(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            _ => null
        };

    /// <summary>Защищает session-файл и поля сессии от гонок между фоновым refresh и UI-потоком.</summary>
    private static readonly object SessionLock = new();
    private static int _sessionVersion;

    private static void SaveSession()
    {
        try
        {
            lock (SessionLock)
            {
                var version = _sessionVersion;

                var dir = Path.GetDirectoryName(AccountFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var data = new
                {
                    access_token = _accessToken,
                    refresh_token = _refreshToken,
                    profile = Current
                };

                // Атомарная запись: temp-файл → переименование (не остаётся битого JSON при сбое).
                var tmp = AccountFilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, AccountFilePath, overwrite: true);

                // Если за время записи произошёл Logout — повторно удаляем файл,
                // иначе после выхода из аккаунта он бы «воскрес» из этого потока.
                if (_sessionVersion != version)
                {
                    try { if (File.Exists(AccountFilePath)) File.Delete(AccountFilePath); } catch { }
                }
            }
        }
        catch { /* ignore */ }
    }

    private static void LoadSavedSession()
    {
        try
        {
            if (!File.Exists(AccountFilePath))
                return;

            using var doc = JsonDocument.Parse(File.ReadAllText(AccountFilePath));
            var root = doc.RootElement;
            _accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            _refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            if (root.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.Object)
                Current = JsonSerializer.Deserialize<AccountInfo>(pr.GetRawText());

            // Токены без профиля — «мусорная» сессия: не даём ей жить и порождать 401-запросы.
            if (Current == null)
            {
                _accessToken = null;
                _refreshToken = null;
            }
        }
        catch { /* ignore */ }
    }

    private static string? ExtractAuthError(string body, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error_code", out var ec))
            {
                var code = ec.GetString();
                if (code == "invalid_credentials")
                    return "Неверный email или пароль.";
                if (code == "email_not_confirmed")
                    return "Подтвердите email на сайте перед входом.";
            }
            if (doc.RootElement.TryGetProperty("msg", out var m)) return m.GetString();
            if (doc.RootElement.TryGetProperty("error_description", out var d)) return d.GetString();
            if (doc.RootElement.TryGetProperty("message", out var msg)) return msg.GetString();
        }
        catch { /* ignore */ }
        return statusCode == 401 ? "Неверный email или пароль." : "Ошибка авторизации.";
    }

    /// <summary>Цвет префикса (как в палитре сайта Tailwind).</summary>
    public static string GetPrefixColorHex(string? colorName)
{
    return (colorName ?? "").ToLowerInvariant() switch
    {
        "amber" => "#FBBF24",
        "red" => "#F87171",
        "purple" => "#C084FC",
        "sky" => "#38BDF8",
        "emerald" => "#34D399",
        "pink" => "#F472B6",
        "neon" => "#4D9DFF",
        "white" => "#FFFFFF",
        "rainbow" => "#FF4D4D", // Красивый акцентный цвет или обработка градиента
        _ => "#FFFFFF"
    };
}

    /// <summary>Цвет 500-й тональности (для фона и рамки бейджа префикса, как на сайте).</summary>
    public static string GetPrefixBadgeColorHex(string? colorName)
    {
        return (colorName ?? "").ToLowerInvariant() switch
        {
            "amber" => "#FBBF24",
        "red" => "#F87171",
        "purple" => "#C084FC",
        "sky" => "#38BDF8",
        "emerald" => "#34D399",
        "pink" => "#F472B6",
        "neon" => "#4D9DFF",
        "white" => "#FFFFFF",
        "rainbow" => "#FF4D4D", // Красивый акцентный цвет или обработка градиента
        _ => "#FFFFFF"
        };
    }

    /// <summary>URL профиля пользователя на сайте.</summary>
    public static string GetProfileUrl(AccountInfo account)
        => $"{SiteUrl}/profile.html?id={Uri.EscapeDataString(account.Id)}";

    /// <summary>URL страницы входа через сайт.</summary>
    public static string GetAuthPageUrl(string code)
        => $"{SiteUrl}/auth.html?code={Uri.EscapeDataString(code)}";

    /// <summary>URL страницы конфигов на сайте.</summary>
    public static string ConfigsPageUrl => $"{SiteUrl}/configs.html";

    // ===================== Вход через сайт (device auth) =====================

    private const string DeviceAuthTable = "device_auth";

    /// <summary>Создаёт заявку на вход через сайт и возвращает одноразовый код.</summary>
    public static async Task<(bool Ok, string? Code, string? Error)> BeginDeviceLoginAsync()
    {
        try
        {
            var code = GenerateDeviceCode();
            await CleanupStaleDeviceLoginsAsync();

            var payload = JsonSerializer.Serialize(new { code, status = "pending" });
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/{DeviceAuthTable}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", SupabaseAnonKey);
            req.Headers.Add("Authorization", "Bearer " + SupabaseAnonKey);
            req.Headers.Add("Prefer", "return=minimal");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (false, null, ExtractRestError(await resp.Content.ReadAsStringAsync()) ?? $"HTTP {(int)resp.StatusCode}");

            return (true, code, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Опрашивает заявку: возвращает токены, когда на сайте завершён вход.</summary>
    public static async Task<(string? AccessToken, string? RefreshToken, string? UserId)?> PollDeviceLoginAsync(string code)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{SupabaseUrl}/rest/v1/{DeviceAuthTable}?select=access_token,refresh_token,user_id&code=eq.{Uri.EscapeDataString(code)}");
            req.Headers.Add("apikey", SupabaseAnonKey);
            req.Headers.Add("Authorization", "Bearer " + SupabaseAnonKey);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var row = doc.RootElement[0];
            var access = row.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            var refresh = row.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var userId = row.TryGetProperty("user_id", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(userId))
                return null;

            return (access, refresh, userId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Завершает вход через сайт: сохраняет сессию и подтягивает профиль.</summary>
    public static async Task<(bool Ok, string? Error)> CompleteDeviceLoginAsync(string access, string? refresh, string userId)
    {
        try
        {
            _accessToken = access;
            _refreshToken = refresh;

            var profile = await FetchProfileAsync(access, userId);
            if (profile == null)
            {
                _accessToken = null;
                _refreshToken = null;
                return (false, "Не удалось получить профиль аккаунта.");
            }

            Current = profile;
            SaveSession();
            AccountChanged?.Invoke(null, EventArgs.Empty);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Удаляет использованную заявку входа через сайт.</summary>
    public static async Task DeleteDeviceLoginAsync(string code)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete,
                $"{SupabaseUrl}/rest/v1/{DeviceAuthTable}?code=eq.{Uri.EscapeDataString(code)}");
            req.Headers.Add("apikey", SupabaseAnonKey);
            req.Headers.Add("Authorization", "Bearer " + SupabaseAnonKey);
            using var resp = await Http.SendAsync(req);
        }
        catch
        {
            /* ignore */
        }
    }

    private static async Task CleanupStaleDeviceLoginsAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10).ToString("O");
            using var req = new HttpRequestMessage(HttpMethod.Delete,
                $"{SupabaseUrl}/rest/v1/{DeviceAuthTable}?created_at=lt.{Uri.EscapeDataString(cutoff)}");
            req.Headers.Add("apikey", SupabaseAnonKey);
            req.Headers.Add("Authorization", "Bearer " + SupabaseAnonKey);
            using var resp = await Http.SendAsync(req);
        }
        catch
        {
            /* ignore */
        }
    }

    private static string GenerateDeviceCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    /// <summary>
    /// Выполняет авторизованный HTTP-запрос; при 401 один раз обновляет токен и повторяет.
    /// Возвращает (успех, статус-код последнего ответа).
    /// </summary>
    private static async Task<(bool Ok, int Status)> SendAuthenticatedAsync(Func<string, HttpRequestMessage> buildRequest)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = _accessToken;
            if (string.IsNullOrEmpty(token))
                return (false, 401);

            using var req = buildRequest(token);
            using var resp = await Http.SendAsync(req);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0)
            {
                var (refreshed, unauthorized) = await TryRefreshTokenAsync();
                if (refreshed)
                    continue; // повторяем запрос с новым токеном
                return (false, unauthorized ? 401 : (int)resp.StatusCode);
            }

            return (resp.IsSuccessStatusCode, (int)resp.StatusCode);
        }

        return (false, 401);
    }

    /// <summary>Публикует локальный конфиг .implexity на сайт (Storage + таблица configs).</summary>
    /// <param name="configPath">Путь к .implexity-файлу.</param>
    /// <param name="isPrivate">Приватный конфиг (виден только автору и админам сайта).</param>
    public static async Task<(bool Ok, string? Error)> PublishConfigAsync(string configPath, bool isPrivate)
    {
        if (string.IsNullOrEmpty(_accessToken) || Current == null)
            return (false, "Войдите в аккаунт, чтобы опубликовать конфиг.");

        try
        {
            var bytes = File.ReadAllBytes(configPath);
            var fileName = Path.GetFileName(configPath);
            var userId = Current.Id; // локальный снимок — Current может измениться из другого потока
            var filePath = $"{userId}/{Uri.EscapeDataString(fileName)}";

            // 1. Загрузка файла в Storage (бакет configs) с повторной попыткой при 401.
            var (uploadOk, uploadStatus) = await SendAuthenticatedAsync(token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/storage/v1/object/configs/{filePath}")
                {
                    Content = new ByteArrayContent(bytes)
                };
                req.Headers.Add("apikey", SupabaseAnonKey);
                req.Headers.Add("Authorization", "Bearer " + token);
                req.Headers.Add("x-upsert", "true");
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return req;
            });

            if (!uploadOk)
            {
                if (uploadStatus == 401)
                    return (false, "Сессия истекла. Войдите в аккаунт заново.");
                return (false, LocalizationService.TF("Не удалось загрузить файл на сайт (HTTP {0}). Проверьте, что бакет «configs» создан в Supabase.", uploadStatus));
            }

            // 2. Публикация метаданных в таблицу configs (settings_summary как jsonb-объект).
            var fileUrl = $"{SupabaseUrl}/storage/v1/object/public/configs/{filePath}";

            JsonElement summary = default;
            var hasSummary = false;
            using (var doc = JsonDocument.Parse(bytes))
            {
                if (doc.RootElement.TryGetProperty("Settings", out var s) && s.ValueKind == JsonValueKind.Object)
                {
                    summary = s.Clone();
                    hasSummary = true;
                }
            }

            var rowDict = new Dictionary<string, object?>
            {
                ["title"] = Path.GetFileNameWithoutExtension(fileName),
                ["description"] = null,
                ["tweaker_version"] = "1.5.0",
                ["file_url"] = fileUrl,
                ["file_name"] = fileName,
                ["format"] = AppSettings.ConfigFormatMarker,
                ["config_version"] = AppSettings.ConfigVersion,
                ["settings_summary"] = hasSummary ? summary : null,
                ["is_private"] = isPrivate,
                ["author_id"] = userId
            };
            var row = JsonSerializer.Serialize(rowDict);

            var (insertOk, insertStatus) = await SendAuthenticatedAsync(token =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/configs")
                {
                    Content = new StringContent(row, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("apikey", SupabaseAnonKey);
                req.Headers.Add("Authorization", "Bearer " + token);
                req.Headers.Add("Prefer", "return=minimal");
                return req;
            });

            if (!insertOk)
            {
                if (insertStatus == 401)
                    return (false, "Сессия истекла. Войдите в аккаунт заново.");
                return (false, LocalizationService.TF("Не удалось опубликовать конфиг (HTTP {0}).", insertStatus));
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? ExtractRestError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString();
        }
        catch
        {
            /* ignore */
        }
        return null;
    }
}
