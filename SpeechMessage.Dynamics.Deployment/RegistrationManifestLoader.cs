/*
 * 檔案整體說明
 *
 * 目的與定位：讀取受版控的 Plugin 註冊清單。輸入僅限程式固定路徑，錯誤不回顯原始檔案內容， 以免清單誤放機密後被錯誤訊息或日誌擴散。
 *
 * 主要功能與流程：
 * 本檔宣告 RegistrationManifestLoader。
 * - 從 repo 的固定清單位置讀取並驗證可解析的 JSON 結構。
 * 主要操作入口包含 Load。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
