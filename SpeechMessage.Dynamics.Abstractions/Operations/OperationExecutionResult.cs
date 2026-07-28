// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs
// 目的：受控操作的統一結果模型。
//
// 保母教學：
// - 成功時 Data 放投影後的 JSON 友善資料。
// - 失敗時用 ErrorCode，不要把 CRM 原始例外整包丟回產品。
// - 永遠不要在結果裡回傳密碼、token、cookie、connection string。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 受控操作執行結果。
/// </summary>
public sealed class OperationExecutionResult
{
    public required bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 成功時的資料負載。第一版用 object?，後續可改強型別 DTO。
    /// </summary>
    public object? Data { get; init; }

    public static OperationExecutionResult Success(object? data)
        => new()
        {
            Succeeded = true,
            Data = data
        };

    public static OperationExecutionResult Failure(string errorCode, string errorMessage)
        => new()
        {
            Succeeded = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}