namespace LineMessagingProcessor.Workflows;

/// <summary>
/// RichMenu 必達流程失敗時使用的標準例外。
/// 呼叫端可從 Result 取得 provider 回覆、驗證錯誤或原始例外，不需要解析字串。
/// </summary>
public sealed class LineRichMenuException : Exception
{
    public LineRichMenuException(LineRichMenuResult result)
        : base(result?.ErrorMessage ?? "LINE RichMenu workflow failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public LineRichMenuResult Result { get; }
}
