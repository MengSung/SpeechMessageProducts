/*
 * 檔案整體說明
 *
 * 目的與定位：表示 LINE 沒有回傳指定 alias id 的 RichMenu alias。 專用例外讓 provisioning 程式可以分辨「可建立的 missing alias」與「應回報為同步錯誤的其他
 * provider failure」。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuAliasNotFoundException。
 * - 建立指定 LINE RichMenu alias id 不存在的例外。
 * - 取得不存在的 LINE RichMenu alias id。
 * 資料／選項欄位包含 RichMenuAliasId，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs、
 * LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 表示 LINE 沒有回傳指定 alias id 的 RichMenu alias。
/// 專用例外讓 provisioning 程式可以分辨「可建立的 missing alias」與「應回報為同步錯誤的其他 provider failure」。
/// </summary>
public sealed class LineRichMenuAliasNotFoundException : Exception
{
    /// <summary>
    /// 建立指定 LINE RichMenu alias id 不存在的例外。
    /// </summary>
    /// <param name="richMenuAliasId">向 LINE 查詢的 alias id。</param>
    public LineRichMenuAliasNotFoundException(string richMenuAliasId)
        : base($"RichMenu alias '{richMenuAliasId}' was not found.")
    {
        RichMenuAliasId = richMenuAliasId;
    }

    /// <summary>
    /// 取得不存在的 LINE RichMenu alias id。
    /// </summary>
    public string RichMenuAliasId { get; }
}
