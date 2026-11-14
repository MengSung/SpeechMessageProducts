/*
 * 檔案整體說明
 *
 * 目的與定位：Shadow 通知的操作區域 sink，只收集會友 GUID、受限範本代號、Retry
 * Key、訊息數與 SHA-256，不建立 LINE client、不解密、不保存訊息原文或 LINE ID。
 * 主要功能與流程：Record 完整驗證後累計總數，最多保留 128 個不可變摘要；超量停止
 * 保存並標記 Overflow，writer 依總數宣告無法完整比對。Records 回傳 detached 陣列；
 * 範本最長 120 個 ASCII 識別字元，雜湊只接受 64 個小寫十六進位字元，不使用 regex。
 * 使用邊界與維護：實例只屬單次 Execute 並由外殼循序使用；無可變 static、
 * 背景工作、訂閱或外部連線。Clear／Dispose 確定排空，Dispose 後拒絕新記錄；Core
 * 呼叫端不轉移任何機密或可釋放資源。離線測試驗證格式、容量及排空，不等同 LINE
 * 實機傳送成功。例外訊息固定，不回顯輸入；writer 可在失敗收尾安全讀取已驗證摘要。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>不可變推播比對資料，不含 LINE ID、憑證或訊息內容。</summary>
internal sealed record ShadowPushRecord(Guid ContactId, string TemplateKey, Guid RetryKey, int MessageCount, string ContentSha256);

/// <summary>單次 Execute 專屬、有容量上限及確定清理責任的推播收集器。</summary>
internal sealed class ShadowPushRecorder : SpeechMessageProducts.GoodShepherd.Core.Notifications.IShadowPushSink, IDisposable
{
    private readonly List<ShadowPushRecord> records = new();
    private bool disposed;
    /// <summary>包含超過容量的推播意圖總數，僅有效輸入會增加。</summary>
    public long TotalCount { get; private set; }
    /// <summary>保留容量不足的明確訊號，不能將缺少記錄當成完整結果。</summary>
    public bool Overflow => TotalCount > records.Count;
    /// <summary>全部參數通過驗證後記錄安全摘要；格式失敗不增加計數，不回顯輸入。</summary>
    public void Record(Guid contactId, string templateKey, Guid retryKey, int messageCount, string contentSha256)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ShadowPushRecorder));
        if (contactId == Guid.Empty) throw new ArgumentException("Contact identity is required.", nameof(contactId));
        if (retryKey == Guid.Empty) throw new ArgumentException("Retry identity is required.", nameof(retryKey));
        if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > 120 ||
            templateKey.Any(c => !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '_' or ':' or '-')) ||
            RecordingOrganizationService.ContainsLineId(templateKey)) throw new ArgumentException("Template key is invalid.", nameof(templateKey));
        if (messageCount < 0 || messageCount > 20) throw new ArgumentOutOfRangeException(nameof(messageCount));
        if (contentSha256 is null || contentSha256.Length != 64 || contentSha256.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new ArgumentException("Content hash is invalid.", nameof(contentSha256));
        TotalCount = checked(TotalCount + 1);
        if (records.Count < 128) records.Add(new ShadowPushRecord(contactId, templateKey, retryKey, messageCount, contentSha256));
    }
    /// <summary>回傳 detached 陣列且元素不可變，不將內部清單交給規則。</summary>
    public IReadOnlyList<ShadowPushRecord> Records => new ReadOnlyCollection<ShadowPushRecord>(records.ToArray());
    /// <summary>由單次執行外殼 owner 清除全部摘要，不關閉或擁有 Core 資源。</summary>
    public void Clear() { records.Clear(); TotalCount = 0; }
    /// <summary>確定排空並停止使用，可重複呼叫而無外部行為。</summary>
    public void Dispose() { Clear(); disposed = true; }
}
