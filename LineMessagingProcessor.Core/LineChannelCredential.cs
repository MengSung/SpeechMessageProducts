/*
 * 檔案整體說明
 *
 * 目的與定位：單次送訊由呼叫端提供的不可變憑證值；不自行解析教會或保存到共用客戶端。 呼叫端決定最長生命週期，字串無法保證清零，因此禁止快取、序列化或記錄此值。
 *
 * 主要功能與流程：
 * 本檔宣告 LineChannelCredential。
 * - 驗證標頭所需的非空 ASCII token；錯誤只提供固定原因，絕不回顯輸入。
 * - 只供當次組合 Authorization 使用，不向結果、全域標頭或診斷傳遞。
 * 主要操作入口包含 ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Core/ILinePushClient.cs、LineMessagingProcessor.Core/LinePushClient.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 單次送訊由呼叫端提供的不可變憑證值；不自行解析教會或保存到共用客戶端。
/// 呼叫端決定最長生命週期，字串無法保證清零，因此禁止快取、序列化或記錄此值。
/// </summary>
public sealed class LineChannelCredential
{
    /// <summary>驗證標頭所需的非空 ASCII token；錯誤只提供固定原因，絕不回顯輸入。</summary>
    /// <param name="token">由受信任邊界選定的頻道 token，不能由收件者指定。</param>
    public LineChannelCredential(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("A nonempty header-safe token is required.", nameof(token));
        foreach (var character in token)
            if (character <= 32 || character >= 127)
                throw new ArgumentException("The token must contain only visible ASCII characters.", nameof(token));
        AccessToken = token;
    }

    /// <summary>只供當次組合 Authorization 使用，不向結果、全域標頭或診斷傳遞。</summary>
    internal string AccessToken { get; }

    /// <summary>提供固定遮罩文字，使一般插值診斷不會輸出憑證內容。</summary>
    /// <returns>固定的 LineChannelCredential(***)。</returns>
    public override string ToString() => "LineChannelCredential(***)";
}
