using System;
using System.IO;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 讀取受版控的 Plugin 註冊清單。輸入僅限程式固定路徑，錯誤不回顯原始檔案內容，
/// 以免清單誤放機密後被錯誤訊息或日誌擴散。
/// </summary>
internal static class RegistrationManifestLoader
{
    private const long MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    /// <summary>從 repo 的固定清單位置讀取並驗證可解析的 JSON 結構。</summary>
    /// <param name="repositoryRoot">已由 CLI 決定的 repo 根目錄。</param>
    /// <returns>只含固定註冊資料與秘密佔位的清單。</returns>
    /// <exception cref="InvalidDataException">清單缺失、過大或格式無效。</exception>
    public static RegistrationManifest Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var path = Path.Combine(repositoryRoot, "d365", "registration", "plugins.registration.json");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("Plugin 註冊清單不存在、為空或超過允許大小。");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = JsonSerializer.Deserialize<RegistrationManifest>(stream, JsonOptions);
            return manifest ?? throw new InvalidDataException("Plugin 註冊清單格式無效。");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Plugin 註冊清單格式無效。");
        }
    }
}
