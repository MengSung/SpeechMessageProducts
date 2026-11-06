/*
 * 檔案整體說明
 *
 * 目的與定位：依 RichMenu 版面與 PNG 內容產生穩定 fingerprint。 Provisioning workflow 會把 fingerprint 短碼附加到 LINE RichMenu 名稱，
 * 讓同步流程能判斷遠端選單是否已是最新版本。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuFingerprint。
 * - 依 catalog definition 與 PNG 內容產生 LINE RichMenu 的可版本化名稱。
 * - 將預先計算好的 fingerprint 轉成可放入 LINE RichMenu name 的版本化名稱。
 * - 以 RichMenu 版面 JSON 與 PNG 圖片內容建立 SHA-256 fingerprint。
 * - 取得 fingerprint 前段短碼，供 LINE RichMenu 名稱維持可讀且不超過長度限制。
 * 主要操作入口包含 BuildName、Create、ShortVersion。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Security.Cryptography;
using System.Text;
using Line.Messaging;
using Newtonsoft.Json;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 依 RichMenu 版面與 PNG 內容產生穩定 fingerprint。
/// Provisioning workflow 會把 fingerprint 短碼附加到 LINE RichMenu 名稱，讓同步流程能判斷遠端選單是否已是最新版本。
/// </summary>
public static class LineRichMenuFingerprint
{
    /// <summary>
    /// 依 catalog definition 與 PNG 內容產生 LINE RichMenu 的可版本化名稱。
    /// </summary>
    /// <param name="definition">包含 menu key 與 RichMenu 版面的 catalog definition。</param>
    /// <param name="pngBytes">即將上傳到 LINE 的 PNG bytes。</param>
    public static string BuildName(LineRichMenuDefinition definition, byte[] pngBytes)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (pngBytes == null)
        {
            throw new ArgumentNullException(nameof(pngBytes));
        }

        var fingerprint = Create(definition.RichMenu, pngBytes);
        return BuildName(definition, fingerprint);
    }

    /// <summary>
    /// 將預先計算好的 fingerprint 轉成可放入 LINE RichMenu name 的版本化名稱。
    /// </summary>
    /// <param name="definition">提供穩定 menu key 的 catalog definition。</param>
    /// <param name="fingerprint">完整 SHA-256 fingerprint 字串。</param>
    public static string BuildName(LineRichMenuDefinition definition, string fingerprint)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var baseName = string.IsNullOrWhiteSpace(definition.RichMenu.Name)
            ? definition.MenuKey
            : definition.RichMenu.Name.Trim();
        var suffix = " v" + ShortVersion(fingerprint);
        var maxBaseLength = Math.Max(1, 300 - suffix.Length);
        if (baseName.Length > maxBaseLength)
        {
            baseName = baseName[..maxBaseLength];
        }

        return baseName + suffix;
    }

    /// <summary>
    /// 以 RichMenu 版面 JSON 與 PNG 圖片內容建立 SHA-256 fingerprint。
    /// </summary>
    /// <param name="richMenu">要佈建的 LINE RichMenu 版面。</param>
    /// <param name="pngBytes">與該版面配套的 PNG bytes。</param>
    public static string Create(RichMenu richMenu, byte[] pngBytes)
    {
        if (richMenu == null)
        {
            throw new ArgumentNullException(nameof(richMenu));
        }

        if (pngBytes == null)
        {
            throw new ArgumentNullException(nameof(pngBytes));
        }

        var stablePayload = new
        {
            richMenu.Size,
            richMenu.Selected,
            richMenu.ChatBarText,
            richMenu.Areas
        };

        var json = JsonConvert.SerializeObject(stablePayload);
        using var sha256 = SHA256.Create();
        var richMenuBytes = Encoding.UTF8.GetBytes(json);
        var merged = new byte[richMenuBytes.Length + pngBytes.Length];
        Buffer.BlockCopy(richMenuBytes, 0, merged, 0, richMenuBytes.Length);
        Buffer.BlockCopy(pngBytes, 0, merged, richMenuBytes.Length, pngBytes.Length);
        return Convert.ToHexString(sha256.ComputeHash(merged)).ToLowerInvariant();
    }

    /// <summary>
    /// 取得 fingerprint 前段短碼，供 LINE RichMenu 名稱維持可讀且不超過長度限制。
    /// </summary>
    /// <param name="fingerprint">完整 fingerprint；必須至少 12 個字元。</param>
    public static string ShortVersion(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return "unknown";
        }

        var normalized = fingerprint.Trim();
        return normalized.Length <= 12 ? normalized : normalized[..12];
    }

}
