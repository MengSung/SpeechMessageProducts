/*
 * 檔案整體說明
 *
 * 目的與定位：此檔真正由 SpeechMessage.Hosting 組件編譯，保留 ToolUtilityNameSpace.Diagnostics 供舊來源相容；ToolUtility 僅以型別轉送指向本唯一實作。
 * 例外目的地的不可變啟動快照；兩個開關可獨立組合，預設保持既有雙開行為。 僅保存布林值，不保存組態、request 或秘密；修改 appsettings 後須重啟 Host。
 *
 * 主要功能與流程：
 * 本檔宣告 ExceptionOutputOptions。
 * - 是否寫入 Exception.log；停用時連通知故障狀態也不得建立檔案。
 * - 是否允許 LINE 入列；停用時不啟動 consumer 或建立通知 client。
 * - 由部署組合根建立固定策略；雙開保序、單開僅執行所選項目、全關無輸出。
 * - 只讀受信任部署的 ExceptionNotifications 區段；缺省為 true，無效布林拒絕啟動， 避免拼錯設定造成靜默關閉。回傳值不訂閱 reload，也不延長組態 provider 的生命。
 * 主要操作入口包含 FromConfiguration。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Hosting/Diagnostics/ExceptionDiagnostics.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Extensions.Configuration;

namespace ToolUtilityNameSpace.Diagnostics;

/// <summary>
/// 例外目的地的不可變啟動快照；兩個開關可獨立組合，預設保持既有雙開行為。
/// 僅保存布林值，不保存組態、request 或秘密；修改 appsettings 後須重啟 Host。
/// </summary>
public sealed class ExceptionOutputOptions
{
    /// <summary>是否寫入 Exception.log；停用時連通知故障狀態也不得建立檔案。</summary>
    public bool WriteExceptionLog { get; }

    /// <summary>是否允許 LINE 入列；停用時不啟動 consumer 或建立通知 client。</summary>
    public bool SendLine { get; }

    /// <summary>由部署組合根建立固定策略；雙開保序、單開僅執行所選項目、全關無輸出。</summary>
    public ExceptionOutputOptions(bool writeExceptionLog = true, bool sendLine = true)
    {
        WriteExceptionLog = writeExceptionLog;
        SendLine = sendLine;
    }

    /// <summary>
    /// 只讀受信任部署的 ExceptionNotifications 區段；缺省為 true，無效布林拒絕啟動，
    /// 避免拼錯設定造成靜默關閉。回傳值不訂閱 reload，也不延長組態 provider 的生命。
    /// </summary>
    public static ExceptionOutputOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ExceptionOutputOptions(
            configuration.GetValue("ExceptionNotifications:WriteExceptionLog", true),
            configuration.GetValue("ExceptionNotifications:SendLine", true));
    }
}
