// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuFingerprint
// 主要成員：BuildName、Create、ShortVersion
// 引用命名空間：System.Security.Cryptography、System.Text、Line.Messaging、Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
