/*
 * 檔案整體說明
 *
 * 目的與定位：表示P2-03 Channel Token封包或key未通過驗證的固定失敗。
 * 與ChannelTokenProtector合作，讓上層依型別處理拒絕，不靠provider訊息判斷，
 * 也不把明文、密文、key、輸入長度或原始例外傳給日誌／告警。
 *
 * 主要功能與流程：建構時只設定固定英文訊息，沒有接收輸入或inner的介面。
 * 此型別不執行查詢、授權、通知、加密或清理；Core把失敗交回真正的Host／Plugin
 * 邊界依共用例外政策處理，不能在此加入產品或notifier反向相依。
 *
 * 維護與驗證：本檔沒有可變共享狀態或disposable owner，例外由當次caller持有。
 * 測試須確認所有拒絕路徑同型別、同訊息、Data為空且InnerException為null；
 * 不能擴增會回顯輸入的建構式來區分padding、MAC或key錯誤。
 */
using System.Security.Cryptography;

namespace LineMessagingProcessor.Core.Security;

/// <summary>
/// 固定的Channel Token保護失敗；不提供自訂訊息或inner入口，避免把provider細節、
/// 明密文或key帶入上層例外通知。所有格式與驗證拒絕皆使用本型別。
/// </summary>
public sealed class ChannelTokenProtectionException : CryptographicException
{
    /// <summary>建立沒有inner與輸入內容的固定失敗；呼叫端只依型別分類，不能回傳敏感診斷。</summary>
    public ChannelTokenProtectionException() : base("Channel token protection failed.")
    {
    }
}
