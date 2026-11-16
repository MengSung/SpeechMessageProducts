/*
 * 檔案整體說明
 *
 * 目的與定位：提供課程、聚會與個人 QR 圖片待辦的唯一 Hosting 組合邊界。它把 worker
 * 傳入的 detached payload 重新驗證成 LessonQrBundlePayload 或 schema2 的
 * MeetingPersonalQrPayload，再從每筆工作的
 * DI scope 取得教會設定與圖片發布服務，建立單筆 LessonQrBundleHandler 或
 * MeetingPersonalQrHandler；不新增待辦類型或第二個同型別 consumer。
 *
 * 主要功能與流程：OpenAsync 只接受 QrImageGenerate 類型，先解析固定 schema 的
 * lessonId、contentVersion、includePoll，或重驗 schema2 五欄、numeric kind、
 * contact 與穩定來源鍵；再由各設定提供者依正式 churchId 取得前綴並建立 handler。
 * 發布服務必須在所有圖片產生成功後，
 * 以同一個 scope 完成圖片發布與課程 URL 欄位的原子回寫；本檔不自行更新
 * Dataverse，也不把 URL、token 或圖片內容放進跨工作狀態。
 *
 * 使用邊界與維護：factory 為 singleton 時只持有 root scope factory；每筆 lease
 * 是唯一的 scoped 資源 owner。OpenAsync 建構失敗或取消時立即 Dispose scope；
 * worker 在 ExecuteAsync 完成、失敗或取消後 await using lease，確保 scoped SDK、
 * 發布器與其連線不延伸到下一筆工作。設定提供者不得依組織名稱分支，必須依
 * 已授權 churchId 讀取部署設定。
 *
 * 驗證方式：離線測試使用合成 payload、設定提供者及 publisher，驗證工作類型、
 * payload schema、scope 生命週期、取消及 handler 的完整圖片候選契約；測試不連
 * D365、LINE 或檔案系統，不能替代 goodshepherdback 實機驗收。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Outbound;

namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>
/// 依已授權教會正式 ID 提供本次工作的 QR 前綴。實作不得以組織名稱、
/// caller 顯示文字或未驗證設定選擇 profile。
/// </summary>
public interface ILessonQrBundleOptionsProvider
{
    /// <summary>取得本次工作固定且有界的 QR 前綴設定。</summary>
    LessonQrBundleOptions GetOptions(Guid churchId);
}

/// <summary>
/// 課程 QR 圖片發布與原子 URL 回寫的 Host 邊界。
/// </summary>
public interface ILessonQrBundlePublicationService
{
    /// <summary>
    /// 消費完整圖片候選，全部成功後才可提交課程 URL 欄位；失敗不得發布部分成功。
    /// </summary>
    Task PublishAndCommitAsync(Guid churchId, Guid lessonId, int contentVersion,
        System.Collections.Generic.IReadOnlyList<LessonQrRenderedImage> images,
        CancellationToken cancellationToken);
}

/// <summary>依封閉 schema 分派，每筆 QR 待辦都建立專屬 handler scope 的唯一 Hosting factory。</summary>
public sealed class LessonQrBundleHostFactory : IOutboundTaskHandlerFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>只保存 root scope factory，不捕獲 SDK、設定、教會或工作資料。</summary>
    public LessonQrBundleHostFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>
    /// 重新驗證 detached work item，schema1 課程與 schema2 聚會／個人分開解析，
    /// 再建立本工作專屬設定、publisher、renderer 與 handler。成功移交後由
    /// LessonQrBundleHandlerScope 唯一擁有 async scope，建構失敗則當場排空。
    /// </summary>
    public async ValueTask<IOutboundTaskHandlerScope> OpenAsync(
        OutboundWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Type != OutboundWorkType.QrImageGenerate)
            throw new InvalidOperationException("Lesson QR handler type is invalid.");

        if (MeetingPersonalQrPayload.IsSchemaTwo(item.PayloadJson))
        {
            var qrPayload = MeetingPersonalQrPayload.Parse(item.PayloadJson);
            qrPayload.ValidateWorkItem(item);
            var qrScope = _scopeFactory.CreateAsyncScope();
            try
            {
                var publication = qrScope.ServiceProvider.GetRequiredService<MeetingPersonalQrPublicationService>();
                cancellationToken.ThrowIfCancellationRequested();
                return new LessonQrBundleHandlerScope(qrScope,
                    new MeetingPersonalQrHandler(publication, item.ChurchId, qrPayload));
            }
            catch
            {
                await qrScope.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var payload = LessonQrBundlePayload.Parse(item.PayloadJson);
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = scope.ServiceProvider.GetRequiredService<ILessonQrBundleOptionsProvider>();
            var publisher = scope.ServiceProvider.GetRequiredService<ILessonQrBundlePublicationService>();
            var options = provider.GetOptions(item.ChurchId);
            var plan = LessonQrBundlePlan.Create(payload.LessonId, payload.ContentVersion,
                payload.IncludePoll, options);
            var handler = new LessonQrBundleHandler(
                plan,
                (images, token) => publisher.PublishAndCommitAsync(
                    item.ChurchId, payload.LessonId, payload.ContentVersion, images, token),
                new QrImageRenderer());
            cancellationToken.ThrowIfCancellationRequested();
            return new LessonQrBundleHandlerScope(scope, handler);
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>封裝單筆 scoped QR handler，Dispose 後清除 handler 參照並釋放 DI scope。</summary>
internal sealed class LessonQrBundleHandlerScope : IOutboundTaskHandlerScope
{
    private IAsyncDisposable? _scopeOwner;
    private IOutboundTaskHandler? _handler;

    /// <summary>接管 factory 建立的 scope；此 lease 是唯一清理 owner。</summary>
    internal LessonQrBundleHandlerScope(AsyncServiceScope scope, IOutboundTaskHandler handler)
    {
        _scopeOwner = scope;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>取得尚未釋放的 handler；scope 關閉後拒絕再次執行。</summary>
    public IOutboundTaskHandler Handler =>
        Volatile.Read(ref _handler)
        ?? throw new ObjectDisposedException(nameof(LessonQrBundleHandlerScope));

    /// <summary>先解除 handler，再釋放 scope；重複呼叫不重複釋放資源。</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _handler, null);
        var owner = Interlocked.Exchange(ref _scopeOwner, null);
        if (owner is not null)
            await owner.DisposeAsync().ConfigureAwait(false);
    }
}
