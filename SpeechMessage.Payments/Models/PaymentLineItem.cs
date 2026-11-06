/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款的商品明細。 MyPay 等 provider 需要 items 陣列；宿主產品未提供時 adapter 會補一筆預設明細。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentLineItem。
 * 資料／選項欄位包含 PaymentLineItem、Name、Quantity、UnitPrice、Currency，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Models/PaymentCreateRequest.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的商品明細。
/// MyPay 等 provider 需要 items 陣列；宿主產品未提供時 adapter 會補一筆預設明細。
/// </summary>
public sealed record PaymentLineItem
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
}
