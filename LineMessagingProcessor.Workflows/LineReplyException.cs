/*
 * 檔案整體說明
 *
 * 目的與定位：必達 reply-token 流程失敗時拋出的例外。 呼叫端若使用 ReplyOrThrowAsync，代表該回覆不能被靜默吞掉， 因此例外會攜帶完整 LineReplyResult 供產品層記錄與診斷。
 *
 * 主要功能與流程：
 * 本檔宣告 LineReplyException。
 * 資料／選項欄位包含 Result，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows/LineReplyWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 必達 reply-token 流程失敗時拋出的例外。
/// 呼叫端若使用 ReplyOrThrowAsync，代表該回覆不能被靜默吞掉，
/// 因此例外會攜帶完整 <see cref="LineReplyResult"/> 供產品層記錄與診斷。
/// </summary>
public sealed class LineReplyException : Exception
{
    public LineReplyException(LineReplyResult result)
        : base(result?.ErrorMessage ?? "LINE reply failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public LineReplyResult Result { get; }
}
