// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
// 目的：本機 local-dev 暫存 ADFS access/refresh token（產品與 WebApi 都可共用）。
//
// 保姆級教學：
// 1. jesus ADFS 不支援 password grant，只支援 authorization_code / refresh_token。
// 2. 本機先瀏覽器授權一次，把 refresh_token 存檔；之後自動 refresh。
// 3. 不是 per-user CRM session pool。
// 4. 檔案必須 gitignore。
// ============================================================================

using System.Text.Json;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 本機 ADFS token 檔案內容。
/// </summary>
public sealed class LocalDevAdfsTokenRecord
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public string? AuthorityUri { get; set; }
    public string? ResourceUri { get; set; }
    public string? ClientId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// 以 JSON 檔讀寫本機 ADFS token。
/// </summary>
public static class LocalDevAdfsTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly object Gate = new();

    public static bool TryLoad(string? path, out LocalDevAdfsTokenRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            lock (Gate)
            {
                var json = File.ReadAllText(path);
                record = JsonSerializer.Deserialize<LocalDevAdfsTokenRecord>(json, JsonOptions);
                return record is not null &&
                       (!string.IsNullOrWhiteSpace(record.AccessToken) ||
                        !string.IsNullOrWhiteSpace(record.RefreshToken));
            }
        }
        catch
        {
            record = null;
            return false;
        }
    }

    public static void Save(string path, LocalDevAdfsTokenRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(record);

        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(record, JsonOptions);
        lock (Gate)
        {
            File.WriteAllText(path, json);
        }
    }
}
