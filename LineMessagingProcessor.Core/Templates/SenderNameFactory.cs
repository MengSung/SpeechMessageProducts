/*
 * 檔案整體說明
 *
 * 目的與定位：共用sender名稱政策，由發送教會的授權context及名稱取字，不讓訊息
 * 呼叫者任意覆寫品牌；同Channel的不同教會仍各自產生純值。
 * 主要功能與流程：以Line.Sender.Template格式化churchName，超20個UTF16碼元時
 * 改取同scope的Brand.ShortName；仍超限或空白即拒絕，不能截斷或悄悄不帶sender。
 * 使用邊界與維護：只借用catalog，無I/O、cache、queue、timer或可變共享欄位；
 * context、名稱與輸出最多存活於本次通知，不記錄教會名。文化僅供取字，Host先
 * 驗組織／產品／教會授權；錯誤向最終主機共用告警入口傳播，不在此自行通知LINE。
 */
using System;
using SpeechMessage.Localization;

namespace LineMessagingProcessor.Core.Templates;

/// <summary>無狀態sender名稱決策，沿用字典五層與完整scope驗證。</summary>
public sealed class SenderNameFactory
{
    private readonly ITextCatalog catalog;
    /// <summary>借用字典owner，不保存每次教會設定或更改其生命週期。</summary>
    public SenderNameFactory(ITextCatalog catalog) =>
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    /// <summary>發送端以伺服器來源教會產生sender；長度失敗必須在HTTP前停止。</summary>
    public string Create(TextContext context, string churchName)
    {
#if NETFRAMEWORK
        if (context is null) throw new ArgumentNullException(nameof(context));
#else
        ArgumentNullException.ThrowIfNull(context);
#endif
        if (string.IsNullOrWhiteSpace(churchName) || churchName.Length > 160)
            throw new ArgumentException("A bounded source church name is required.", nameof(churchName));
        var sender = catalog.GetText(context, new TextKey("Line.Sender.Template"), new TextArg("churchName", churchName));
        if (sender is not null && sender.Length > 20)
            sender = catalog.GetText(context, new TextKey("Brand.ShortName"));
        if (string.IsNullOrWhiteSpace(sender) || sender!.Length > 20)
            throw new InvalidOperationException("The server sender name exceeds its allowed size.");
        return sender!;
    }
}
