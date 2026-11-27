/*
 * 檔案整體說明
 * 目的與定位：F8輸入格式與產品白名單的內部共同判斷，供授權、稽核及SDK執行邊界使用。
 * 主要功能與流程：只接受核定的三種產品與有界ASCII開發者識別碼，不解析身分、不查詢、不以字串格式當成授權。
 * 使用邊界與維護：沒有可變共用狀態、秘密、快取或資源；所有計算在單次呼叫完成，拒絕輸入時不得把本文複製進例外或告警。變更產品或字元政策須同步驗證例與schema欄位上限，不能放寬正式環境連線邊界。
 */
namespace SpeechMessage.Access;

/// <summary>與資料授權分離的純格式守門，格式合法不代表任何操作獲准。</summary>
internal static class AccessValidation
{
    /// <summary>只允許本階段三種產品，拒絕舊版及未知名稱。</summary>
    internal static bool ValidProduct(string? value) => value is "GoodShepherd2" or "GoodButler" or "GoodGuild";

    /// <summary>固定識別碼限英數與底線、連字號、點及冒號，避免秘密或任意個資成為diagnostic operation。</summary>
    internal static bool ValidIdentifier(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
}
