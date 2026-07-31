// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs
// 用途：表達已登錄 Dynamics 作業的受控成功或失敗，並限制成功資料只能是封閉 OperationResponseData。
//
// 安全與生命週期邊界：
// 1. Data 不接受 object、JsonElement、OData 文件或 transport 物件，避免 CRM URL、nextLink、credential、
//    token、session 與 upstream extension data 被 queue、Gateway 或產品意外保存或序列化。
// 2. 結果是純值，不擁有 HttpResponseMessage、stream、CTS、timer 或租用 buffer；connector 必須在建立結果前
//    由 request scope 確定 dispose 這些資源，結果本身不能延長其生命週期。
// 3. 失敗只回傳受控 error code/message，成功只回傳已驗證的 discriminated union，使未知/未支援資料採
//    fail-closed 行為而不是回退為任意 JSON。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 已登錄作業的受控執行結果。成功分支只持有已投影的封閉資料；失敗分支不攜帶上游 body、exception object、
/// endpoint、credential 或使用者工作階段，讓呼叫端可以記錄固定分類而不保留敏感 transport 狀態。
/// </summary>
public sealed class OperationExecutionResult
{
    /// <summary>
    /// 指出 connector/Gateway 是否完成受控作業。false 時 Data 必須保持 null，避免 failure envelope 意外保存
    /// 部分 upstream 資料或未釋放的頁面資源。
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// 受控錯誤分類；不得放入 CRM host、token、authorization header、完整 body 或 exception serialization。
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 可安全回顯的錯誤說明。runtime 的 log/audit owner 仍必須在自己的 scope 內清理任何真實 upstream 例外。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 成功時的唯一產品可見資料形狀。nullable 保留無資料成功的既有語意，但非 null 值一律經過
    /// OperationResponseData 的 branch 驗證與集合複製，不可為 object、JsonElement 或 raw OData payload。
    /// </summary>
    public OperationResponseData? Data { get; init; }

    /// <summary>
    /// 建立成功結果。呼叫端必須先在 request scope 投影並釋放 response/stream/buffer；此方法只保留封閉純值，
    /// 不接管取消、重試、背景工作或外部資源的所有權。
    /// </summary>
    public static OperationExecutionResult Success(OperationResponseData? data)
        => new()
        {
            Succeeded = true,
            Data = data
        };

    /// <summary>
    /// 建立失敗關閉結果。errorCode/errorMessage 應已由 connector 清洗，且不得夾帶部分 Data 或可追蹤的
    /// transport 物件；資源清理仍由失敗發生的 request scope 決定性完成。
    /// </summary>
    public static OperationExecutionResult Failure(string errorCode, string errorMessage)
        => new()
        {
            Succeeded = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}
