/*
 * 檔案整體說明
 *
 * 目的與定位：表示商店 profile 使用的金流環境。 provider 實作會依據此值與 profile endpoint 判斷要呼叫測試站或正式站。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentEnvironment。
 * - 測試或沙盒環境；通常使用測試商店代號、測試金鑰與 sandbox API endpoint。
 * - 正式環境；必須搭配正式商店代號、正式金鑰與 production API endpoint。
 * 資料／選項欄位包含 PaymentEnvironment，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs、
 * SpeechMessage.Payments/Configuration/PaymentMerchantProfile.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 表示商店 profile 使用的金流環境。
/// provider 實作會依據此值與 profile endpoint 判斷要呼叫測試站或正式站。
/// </summary>
public enum PaymentEnvironment
{
    /// <summary>
    /// 測試或沙盒環境；通常使用測試商店代號、測試金鑰與 sandbox API endpoint。
    /// </summary>
    Sandbox = 0,

    /// <summary>
    /// 正式環境；必須搭配正式商店代號、正式金鑰與 production API endpoint。
    /// </summary>
    Production = 1
}
