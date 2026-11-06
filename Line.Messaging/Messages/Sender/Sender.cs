/*
 * 檔案整體說明
 *
 * 目的與定位：單則訊息的發送者品牌呈現，不代表 channel 身分或授權；只含不可變顯示資料，不保存憑證。
 *
 * 主要功能與流程：
 * 本檔宣告 Sender。
 * - 取得顯示名稱；在建構時驗證一至二十個 UTF-16 單元，避免進入傳輸階段才失敗。
 * - 取得絕對 HTTPS 圖示網址；SDK 不下載圖片，亦不持有網路資源。
 * - 建立可供訊息獨立引用的品牌資訊；呼叫端不得以此顯示資料取代伺服器端 channel 授權。
 * 資料／選項欄位包含 Sender、Name、IconUrl，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging/Messages/TextMessage.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging;

/// <summary>單則訊息的發送者品牌呈現，不代表 channel 身分或授權；只含不可變顯示資料，不保存憑證。</summary>
public sealed class Sender
{
    /// <summary>取得顯示名稱；在建構時驗證一至二十個 UTF-16 單元，避免進入傳輸階段才失敗。</summary>
    public string Name { get; }
    /// <summary>取得絕對 HTTPS 圖示網址；SDK 不下載圖片，亦不持有網路資源。</summary>
    public string IconUrl { get; }
    /// <summary>建立可供訊息獨立引用的品牌資訊；呼叫端不得以此顯示資料取代伺服器端 channel 授權。</summary>
    /// <param name="name">一至二十字的顯示名稱。</param><param name="iconUrl">絕對 HTTPS 圖示網址。</param>
    public Sender(string name, string iconUrl)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 20)
            throw new ArgumentException("Sender name must contain 1 to 20 UTF-16 code units.", nameof(name));
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Sender icon must be an absolute HTTPS URL.", nameof(iconUrl));
        Name = name;
        IconUrl = iconUrl;
    }
}
