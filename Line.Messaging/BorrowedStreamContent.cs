/*
 * 檔案整體說明
 *
 * 目的與定位：上傳期間借用呼叫者串流的 HttpContent；不同於 StreamContent，不取得輸入串流所有權。 每次上傳建立一個，方法完成／故障即 Dispose 並清參照；不緩衝整個檔案，不建立背景工作。
 * 呼叫者必須等上傳完成再修改或 Dispose 輸入，內容不可跨上傳重用。
 *
 * 主要功能與流程：
 * 本檔宣告 BorrowedStreamContent。
 * - 只借入可讀串流，傳輸目的地由 HttpClient 擁有，兩者均不由本類別關閉。
 * - 直接以有界複製緩衝傳輸；等待完整複製，沒有 Task.Run 或遺棄後續工作。
 * - net10 transport 的取消 token 傳到每次讀寫；不以取消後遺棄 CopyToAsync 代替確定性排空。
 * - 可 seek 時提供剩餘長度，否則交由 transport 串流傳送，不為計算長度讀完整內容。
 * 主要操作入口包含 SerializeToStreamAsync、TryComputeLength、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Line.Messaging;

/// <summary>
/// 上傳期間借用呼叫者串流的 HttpContent；不同於 StreamContent，不取得輸入串流所有權。
/// 每次上傳建立一個，方法完成／故障即 Dispose 並清參照；不緩衝整個檔案，不建立背景工作。
/// 呼叫者必須等上傳完成再修改或 Dispose 輸入，內容不可跨上傳重用。
/// </summary>
internal sealed class BorrowedStreamContent : HttpContent
{
    private Stream _stream;
    /// <summary>只借入可讀串流，傳輸目的地由 HttpClient 擁有，兩者均不由本類別關閉。</summary>
    /// <param name="stream">呼叫端維持開啟到傳輸完成的輸入。</param>
    internal BorrowedStreamContent(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>直接以有界複製緩衝傳輸；等待完整複製，沒有 Task.Run 或遺棄後續工作。</summary>
    /// <param name="stream">transport 擁有的目的地。</param><param name="context">底層傳輸內容，不保存。</param><returns>完整複製作業。</returns>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream);

#if !NETFRAMEWORK
    /// <summary>net10 transport 的取消 token 傳到每次讀寫；不以取消後遺棄 CopyToAsync 代替確定性排空。</summary>
    /// <param name="stream">transport 目的地。</param><param name="context">不保存的傳輸內容。</param>
    /// <param name="cancellationToken">由 HttpClient timeout／呼叫取消導出的傳輸界線。</param><returns>實際複製結束的工作。</returns>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken cancellationToken) =>
        (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream, 81920, cancellationToken);
#endif

    /// <summary>可 seek 時提供剩餘長度，否則交由 transport 串流傳送，不為計算長度讀完整內容。</summary>
    /// <param name="length">已知剩餘位元組數或零。</param><returns>是否能不消耗輸入取得長度。</returns>
    protected override bool TryComputeLength(out long length)
    {
        var source = _stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent));
        length = source.CanSeek ? source.Length - source.Position : 0;
        return source.CanSeek;
    }

    /// <summary>清掉借用參照並釋放 HttpContent 自有緩衝；不關閉輸入串流，由原呼叫者 using 負責。</summary>
    /// <param name="disposing">是否為明確處置。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _stream = null;
        base.Dispose(disposing);
    }
}
