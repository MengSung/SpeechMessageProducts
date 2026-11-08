/*
 * 檔案整體說明
 *
 * 目的與定位：P2-09b的net10 QR圖片待辦入口，實作Hosting既有IOutboundTaskHandler；
 * 四種內容只委派GoodShepherd.Core的P2-09a編碼器，不複製字串規則或接受任意原始QR內容。
 * 主要功能與流程：Host先完成組織／教會／資料列授權，再傳入不可變ID與欄位；
 * 處理器先驗內容預算，再以ZXing.Net及ImageSharp轉為PNG，完整await下游消費後釋放stream。
 * 資源與隔離：handler只屬於單筆工作scope，renderer及publish委派由scope擁有且僅借用；
 * 不保存Session、token、credential、快取、timer或背景Task。內容不寫log或例外訊息，
 * 內容UTF8最多2048bytes、畫布512×512像素、PNG最多512KiB；圖片由renderer using擁有，
 * 交出的PNG由handler唯一Dispose。
 * 取消於產圖邊界及PNG編碼傳遞；publish必須遵守token及Host設定的有限I/O逾時，
 * 不用WaitAsync遺棄消費中的stream。失敗原樣交既有Worker共用告警，不另建LINE通知器。
 * 驗證方式：離線真PNG經ZXing解回既有格式，超長於產圖前拒絕，成功／故障／取消及A/B
 * 並行後核對stream與ImageSharp配置歸零；不把語法驗證當成授權或實機驗收。
 */
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace LineMessagingProcessor.RichMenus.QrImages;

/// <summary>選擇P2-09a既有資料列編碼；值本身不建立資料存取權。</summary>
public enum QrImageContentKind
{
    /// <summary>課程、課次與簽到代碼。</summary>
    Lesson,
    /// <summary>聚會、屬性與簽到代碼。</summary>
    Meeting,
    /// <summary>個人資料列與既有顯示尾欄。</summary>
    Personal,
    /// <summary>小組週報與簽到代碼。</summary>
    SmallGroup
}

/// <summary>
/// 單筆已授權工作的不可變內容參數。RecordId必須來自正式資料列，First／Second
/// 只是codec欄位而非使用者或教會身分；只保留至工作scope結束，不存入共用快取。
/// </summary>
/// <param name="Kind">固定的四類格式。</param>
/// <param name="RecordId">由Host另行授權的正式資料列ID。</param>
/// <param name="First">課次、聚會屬性、個人尾欄或小組簽到代碼。</param>
/// <param name="Second">課程或聚會的簽到代碼。</param>
/// <param name="Separator">聚會舊格式的分隔符，非聚會時不使用。</param>
public sealed record QrImageGenerateRequest(QrImageContentKind Kind, Guid RecordId,
    string First = "", string Second = "",
    QrCodePayloadSeparator Separator = QrCodePayloadSeparator.Underscore)
{
    /// <summary>在配置圖片前以唯一codec產生內容，超長或非法欄位只丟固定錯誤。</summary>
    public string Encode()
    {
        // 先限制單欄，再讓codec組合；不得先為無界caller輸入配置巨型合併字串。
        if (First is null || Second is null || First.Length > 2048 || Second.Length > 2048)
            throw new ArgumentException("QR fields exceed their bounded contract.");
        var content = Kind switch
        {
            QrImageContentKind.Lesson => QrCodeContentCodec.EncodeLesson(RecordId, First, Second),
            QrImageContentKind.Meeting => QrCodeContentCodec.EncodeMeeting(RecordId, First, Second, Separator),
            QrImageContentKind.Personal => QrCodeContentCodec.EncodePersonal(RecordId, First),
            QrImageContentKind.SmallGroup => QrCodeContentCodec.EncodeSmallGroup(RecordId, First),
            _ => throw new ArgumentException("QR content kind is invalid.")
        };
        QrImageRenderer.ValidateContent(content);
        return content;
    }
}

/// <summary>操作無狀態的PNG邊界；成功stream移交handler，失敗自行清理所有已配置資源。</summary>
public interface IQrImageRenderer
{
    /// <summary>只在本次呼叫持有有界內容，完整傳遞取消，不啟動遺棄的背景工作。</summary>
    Task<Stream> RenderAsync(string content, CancellationToken cancellationToken);
}

/// <summary>唯一ZXing.Net矩陣至ImageSharp PNG的net10實作，不建立或修改全域影像設定。</summary>
public sealed class QrImageRenderer : IQrImageRenderer
{
    /// <summary>PNG輸出的固定上限，不依caller輸入決定持有容量。</summary>
    internal const int MaximumPngBytes = 512 * 1024;

    /// <summary>產生固定有界畫布的PNG；成功後呼叫端必須Dispose，失敗不留下stream或像素。</summary>
    public async Task<Stream> RenderAsync(string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateContent(content);
        var matrix = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, 512, 512,
            new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.CHARACTER_SET] = "UTF-8",
                [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
                [EncodeHintType.MARGIN] = 4
            });
        var configuration = new Configuration(new PngConfigurationModule())
        {
            MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = 0 })
        };
        using var image = new Image<L8>(configuration, 512, 512);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < 512; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 512; x++)
                    row[x] = new L8(matrix[x, y] ? (byte)0 : (byte)255);
            }
        });
        var output = new MemoryStream();
        try
        {
            await image.SaveAsPngAsync(output, cancellationToken).ConfigureAwait(false);
            if (output.Length > MaximumPngBytes)
                throw new InvalidOperationException("QR PNG exceeds its bounded contract.");
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 保守限制QR-M的UTF8位元組預算；codec的2048字元上限不是圖片容量。
    /// 嚴格拒絕孤立surrogate，不允許encoder悄悄替換成不同內容；錯誤不含原輸入。
    /// </summary>
    internal static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 2048)
            throw new ArgumentException("QR content exceeds its bounded contract.");
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(content) > 2048)
                throw new ArgumentException("QR content exceeds its byte budget.");
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("QR content is not valid Unicode.");
        }
    }
}

/// <summary>每筆scope專用的QR待辦處理器，PNG只有一個owner且真正失敗原樣交Worker。</summary>
public sealed class QrImageGenerateHandler : IOutboundTaskHandler
{
    private readonly QrImageGenerateRequest _request;
    private readonly Func<Stream, CancellationToken, Task> _publish;
    private readonly IQrImageRenderer _renderer;

    /// <summary>借用已授權的不可變請求、單筆publish委派與無狀態renderer，不承接其Dispose責任。</summary>
    public QrImageGenerateHandler(QrImageGenerateRequest request,
        Func<Stream, CancellationToken, Task> publish, IQrImageRenderer? renderer = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _renderer = renderer ?? new QrImageRenderer();
    }

    /// <summary>先編碼及驗證再產圖，完整await消費後釋放PNG，取消或故障也不得跳過清理。</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = _request.Encode();
        await using var png = await _renderer.RenderAsync(content, cancellationToken).ConfigureAwait(false);
        if (png is null || !png.CanRead || !png.CanSeek ||
            png.Length is < 1 or > QrImageRenderer.MaximumPngBytes)
            throw new InvalidOperationException("QR renderer returned an invalid PNG stream.");
        cancellationToken.ThrowIfCancellationRequested();
        png.Position = 0;
        await _publish(png, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
