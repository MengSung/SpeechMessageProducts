/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 所有上傳及縮圖共用的影像信任邊界。只註冊 JPEG、PNG、GIF， 先辨識尺寸再解碼，禁止用宣告的副檔名或 Content-Type 代替內容驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageInputValidator、ImageInputRejectedException。
 * - 同步檢查並解碼單張影像。輸入由呼叫端擁有，回傳影像的所有權移交呼叫端； 失敗時只有本方法建立的暫存會被釋放，不處置借用的來源串流。
 * - 非同步版本沿用相同解碼器及尺寸檢查；辨識後重設同一請求的串流位置， 不把影像、取消來源或 request 保存到 static。正常 caller 取消原樣往外傳遞。
 * - 獨立設定不能從 Configuration.Default 複製，否則 TIFF 等非允許格式仍會被註冊。 禁用內部像素池，避免診斷或空閒期間持有前一次請求的影像；不修改程序全域設定。
 * - 每次解碼自有 options。輸出既有契約是單張 JPEG，只需第一影格； 限制 GIF／APNG 的影格數，避免低檔案大小但大量影格造成配置放大。
 * 主要操作入口包含 Load、LoadAsync、CreateConfiguration、CreateOptions、ValidateDimensions、BufferInput、BufferInputAsync、
 * UnsupportedFormat；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs、
 * SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageConfiguration = SixLabors.ImageSharp.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;

namespace ChurchReport.Services.Images;

/// <summary>
/// ChurchReport 所有上傳及縮圖共用的影像信任邊界。只註冊 JPEG、PNG、GIF，
/// 先辨識尺寸再解碼，禁止用宣告的副檔名或 Content-Type 代替內容驗證。
/// </summary>
/// <remarks>
/// 設定不對外公開、發布後不再變動，僅包含解碼器與配置策略，不保存請求或影像。
/// 解碼器不保留跨請求的像素池；每次回傳的 Image 由呼叫端 using 擁有。
/// 暫存只限非 seekable 輸入，最多 5 MiB，返回前釋放，不建立檔案、背景工作或共用快取。
/// </remarks>
internal static class ImageInputValidator
{
    private const int MaximumSide = 8192;
    private const long MaximumPixels = 40_000_000;
    private const int MaximumEncodedBytes = 5 * 1024 * 1024;
    private static readonly ImageConfiguration RestrictedConfiguration = CreateConfiguration();

    /// <summary>
    /// 同步檢查並解碼單張影像。輸入由呼叫端擁有，回傳影像的所有權移交呼叫端；
    /// 失敗時只有本方法建立的暫存會被釋放，不處置借用的來源串流。
    /// </summary>
    /// <param name="input">目前請求自有的影像串流，位置必須在影像開頭。</param>
    /// <returns>只包含第一影格的已驗證影像，呼叫端必須 Dispose。</returns>
    /// <exception cref="ImageInputRejectedException">格式、尺寸或內容不符合輸入契約。</exception>
    internal static Image Load(Stream input)
    {
        using var buffer = input.CanSeek ? null : BufferInput(input);
        var source = buffer ?? input;
        var start = source.Position;
        var options = CreateOptions();
        try
        {
            ValidateDimensions(Image.Identify(options, source));
            source.Position = start;
            return Image.Load(options, source);
        }
        catch (UnknownImageFormatException)
        {
            throw ImageInputRejectedException.UnsupportedFormat();
        }
        catch (InvalidImageContentException)
        {
            throw ImageInputRejectedException.CorruptContent();
        }
    }

    /// <summary>
    /// 非同步版本沿用相同解碼器及尺寸檢查；辨識後重設同一請求的串流位置，
    /// 不把影像、取消來源或 request 保存到 static。正常 caller 取消原樣往外傳遞。
    /// </summary>
    /// <param name="input">呼叫端擁有的來源，最長只存續至當次上傳結束。</param>
    /// <param name="cancellationToken">HTTP caller 的取消權杖，不能以解碼例外猜測正常取消。</param>
    /// <returns>由呼叫端確定性釋放的第一影格。</returns>
    internal static async Task<Image> LoadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        using var buffer = input.CanSeek ? null : await BufferInputAsync(input, cancellationToken);
        var source = buffer ?? input;
        var start = source.Position;
        var options = CreateOptions();
        try
        {
            ValidateDimensions(await Image.IdentifyAsync(options, source, cancellationToken));
            source.Position = start;
            return await Image.LoadAsync(options, source, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            throw ImageInputRejectedException.UnsupportedFormat();
        }
        catch (InvalidImageContentException)
        {
            throw ImageInputRejectedException.CorruptContent();
        }
    }

    /// <summary>
    /// 獨立設定不能從 Configuration.Default 複製，否則 TIFF 等非允許格式仍會被註冊。
    /// 禁用內部像素池，避免診斷或空閒期間持有前一次請求的影像；不修改程序全域設定。
    /// </summary>
    private static ImageConfiguration CreateConfiguration() => new(
        new JpegConfigurationModule(), new PngConfigurationModule(), new GifConfigurationModule())
    {
        MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = 0 })
    };

    /// <summary>
    /// 每次解碼自有 options。輸出既有契約是單張 JPEG，只需第一影格；
    /// 限制 GIF／APNG 的影格數，避免低檔案大小但大量影格造成配置放大。
    /// </summary>
    private static DecoderOptions CreateOptions() => new() { Configuration = RestrictedConfiguration, MaxFrames = 1 };

    /// <summary>先用 long 計算總像素，避免整數溢位繞過；只讀標頭，不配置完整像素。</summary>
    private static void ValidateDimensions(ImageInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0
            || info.Width > MaximumSide || info.Height > MaximumSide
            || (long)info.Width * info.Height > MaximumPixels)
            throw ImageInputRejectedException.InvalidDimensions();
    }

    /// <summary>非 seekable 來源只建立有界記憶體副本；讀到上限加一即拒絕，不依賴不可信 Length。</summary>
    private static MemoryStream BufferInput(Stream input)
    {
        var buffer = new MemoryStream();
        try
        {
            var block = new byte[8192];
            int read;
            while ((read = input.Read(block, 0, (int)Math.Min(block.Length, MaximumEncodedBytes + 1 - buffer.Length))) > 0)
            {
                buffer.Write(block, 0, read);
                if (buffer.Length > MaximumEncodedBytes) throw ImageInputRejectedException.InvalidSize();
            }
            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>異步複製使用相同上限；成功後將 owner 移交外層 using，任何故障先釋放再傳遞。</summary>
    private static async Task<MemoryStream> BufferInputAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        try
        {
            var block = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(block.AsMemory(0,
                       (int)Math.Min(block.Length, MaximumEncodedBytes + 1 - buffer.Length)), cancellationToken)) > 0)
            {
                buffer.Write(block, 0, read);
                if (buffer.Length > MaximumEncodedBytes) throw ImageInputRejectedException.InvalidSize();
            }
            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }
}

/// <summary>
/// 可預期的影像輸入拒絕；訊息完全由程式控制，不保留原始解析例外、檔案內容或使用者資料。
/// 控制器只捕捉此型別作輸入拒絕，其餘例外仍交給既有共用診斷入口。
/// </summary>
internal sealed class ImageInputRejectedException : Exception
{
    /// <summary>僅接受本型別內部固定訊息，避免任意使用者內容進入 JSON。</summary>
    private ImageInputRejectedException(string message) : base(message) { }
    /// <summary>辨識不到允許格式，包括偽裝成 JPG 的 WebP、TIFF 與其他格式。</summary>
    internal static ImageInputRejectedException UnsupportedFormat() => new(ImageInputText.GetText("Image.Input.UnsupportedFormat"));
    /// <summary>辨識到格式但內容結構損壞，不外送底層解析訊息。</summary>
    internal static ImageInputRejectedException CorruptContent() => new(ImageInputText.GetText("Image.Input.CorruptContent"));
    /// <summary>標頭超過核定尺寸上限時先拒絕，不配置像素。</summary>
    internal static ImageInputRejectedException InvalidDimensions() => new(ImageInputText.GetText("Image.Input.InvalidDimensions"));
    /// <summary>無法 seek 的實際輸入超過既有 5 MiB 上限。</summary>
    internal static ImageInputRejectedException InvalidSize() => new(ImageInputText.GetText("Image.Input.InvalidSize"));
}
