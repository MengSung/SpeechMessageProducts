/*
 * 檔案整體說明
 *
 * 目的與定位：圖片信任邊界的固定顯示文字。只複製生成資料的繁中欄位至凍結字典， 保留現有網站語系，不改變執行緒文化，也不保存請求、教會或使用者的資料。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageInputText。
 * - 已建立新人後僅提示照片未儲存；原因必須來自固定的輸入拒絕例外。
 * 主要操作入口包含 GetText、AttachmentRejected。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs、
 * SpeechMessageProducts.ChurchReport/Services/Images/ImageInputValidator.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Frozen;

namespace ChurchReport.Services.Images;

/// <summary>
/// 圖片信任邊界的固定顯示文字。只複製生成資料的繁中欄位至凍結字典，
/// 保留現有網站語系，不改變執行緒文化，也不保存請求、教會或使用者的資料。
/// </summary>
/// <remarks>
/// 容量只由編譯時資源決定；不把執行期拒絕原因加入字典。所有公開入口僅供同組件
/// 使用固定資源鍵，缺鍵直接失敗，避免回傳空訊息而讓守門出現假綠燈。
/// </remarks>
internal static class ImageInputText
{
    /// <summary>完成固定資料複製後才發布，不公開生成器的可變陣列。</summary>
    private static readonly FrozenDictionary<string, string> Text =
        ImageInputTextData.Rows.ToFrozenDictionary(row => row[0]!, row => row[1]!, StringComparer.Ordinal);

    /// <summary>取回不可變文字；鍵只來自程式，不接受使用者輸入。</summary>
    /// <param name="key">本組件固定的資源鍵。</param>
    /// <returns>既有繁中介面使用的完整文字。</returns>
    internal static string GetText(string key) => Text[key];

    /// <summary>已建立新人後僅提示照片未儲存；原因必須來自固定的輸入拒絕例外。</summary>
    /// <param name="rejection">不含使用者內容或原始解析訊息的固定拒絕。</param>
    /// <returns>只屬當次回應的說明，永不加入靜態集合。</returns>
    internal static string AttachmentRejected(ImageInputRejectedException rejection) =>
        GetText("Image.Upload.AttachmentRejected").Replace("{reason}", rejection.Message, StringComparison.Ordinal);
}
