/*
 * 檔案整體說明
 *
 * 目的與定位：僅容許一名收件者與最多五則 text 的操作快照，不能表示群播或廣播。 建構時複製並驗證全部訊息再發布，來源集合後續修改不得影響已建立請求。
 *
 * 主要功能與流程：
 * 本檔宣告 LinePushRequest。
 * - 在任何 HTTP 操作之前完成單人 ID、訊息界限與事件重試鍵驗證。
 * - 伺服器已驗證的單一收件 ID，僅用於當次 HTTP 內容，不可當成授權依據。
 * - 呼叫專屬且不可變的完整訊息集合，不把外部可變清單直接發布。
 * - 由業務事件衍生的重試鍵；Core 不重新產生或在失敗時替換。
 * 主要操作入口包含 ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Core/ILinePushClient.cs、LineMessagingProcessor.Core/LinePushClient.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 僅容許一名收件者與最多五則 text 的操作快照，不能表示群播或廣播。
/// 建構時複製並驗證全部訊息再發布，來源集合後續修改不得影響已建立請求。
/// </summary>
public sealed class LinePushRequest
{
    /// <summary>在任何 HTTP 操作之前完成單人 ID、訊息界限與事件重試鍵驗證。</summary>
    /// <param name="userId">U 加三十二個小寫十六進位字元，須完整符合、不能附加換行。</param>
    /// <param name="messages">一到五則非空文字，每則最多五千個 UTF-16 碼元。</param>
    /// <param name="retryKey">同一業務事件重試必須沿用的非空識別碼。</param>
    public LinePushRequest(string userId, IReadOnlyList<string> messages, Guid retryKey)
    {
        if (userId is null || userId.Length != 33 || userId[0] != 'U')
            throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
        for (var index = 1; index < userId.Length; index++)
        {
            var character = userId[index];
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
        }
#if NETFRAMEWORK
        if (messages is null) throw new ArgumentNullException(nameof(messages));
#else
        ArgumentNullException.ThrowIfNull(messages);
#endif
        var count = messages.Count;
        if (count is < 1 or > 5)
            throw new ArgumentException("One through five text messages are required.", nameof(messages));
        if (retryKey == Guid.Empty)
            throw new ArgumentException("A nonempty event retry key is required.", nameof(retryKey));

        // 先完成有界候選再發布；不得保存 caller 清單，或逐步暴露尚未驗證完的內容。
        var snapshot = new string[count];
        for (var index = 0; index < count; index++)
        {
            var text = messages[index];
            if (string.IsNullOrEmpty(text) || text.Length > 5000)
                throw new ArgumentException("Each text must contain one through 5000 UTF-16 code units.", nameof(messages));
            snapshot[index] = text;
        }
        UserId = userId;
        RetryKey = retryKey;
        Messages = Array.AsReadOnly(snapshot);
    }

    /// <summary>伺服器已驗證的單一收件 ID，僅用於當次 HTTP 內容，不可當成授權依據。</summary>
    public string UserId { get; }

    /// <summary>呼叫專屬且不可變的完整訊息集合，不把外部可變清單直接發布。</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>由業務事件衍生的重試鍵；Core 不重新產生或在失敗時替換。</summary>
    public Guid RetryKey { get; }

    /// <summary>診斷只能顯示型別及遮罩，不帶 userId 或訊息內容。</summary>
    /// <returns>固定遮罩。</returns>
    public override string ToString() => "LinePushRequest(***)";
}
