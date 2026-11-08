/*
 * 檔案整體說明
 *
 * 目的與定位：P2-08唯一的net10 ImageSharp圖面合成器，接受已取字的純值格子與固定LINE尺寸，
 * 不查D365、不選教會／語系，也不建立HTTP或通知器；字型由Host提供且必須涵蓋已啟用語系。
 * 主要功能與流程：先驗尺寸與有界格子，再於獨立無像素池的Configuration產生白底、格線與文字，
 * 使用ImageSharp.Drawing排版，不載入任意使用者圖片。成功回傳從0開始的PNG stream。
 * 資源擁有權：單次最多2500×1686像素、20格、每格256字；Image由本方法using擁有，
 * PNG成功後移交handler，失敗先Dispose再拋原例外。取消在格子邊界及PNG寫入傳遞，不留背景task。
 * 固定Font只含字型資料，不保存教會、Session或credential；不改全域Configuration，
 * 不建立字型檔或永久圖檔。文字不能塞出格子，量測無法容納時拒絕，不以截字掩蓋。
 * 驗證：真ImageSharp像素與尺寸測試、取消與allocation baseline；測試字型使用repo既有第三方資產，
 * 不下載或搬入OS字型，離線PNG不宣稱LINE顯示或實機成功。
 */
using Line.Messaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;

namespace LineMessagingProcessor.RichMenus;

/// <summary>固定字型政策的操作無狀態renderer；Font借入且不含教會或使用者資料。</summary>
public sealed class RichMenuImageRenderer : IRichMenuImageRenderer
{
    private readonly Font _font;

    /// <summary>注入Host核定字型；可與其他純renderer共用不可變字型資料，不共用像素。</summary>
    public RichMenuImageRenderer(Font font)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        if (font.Size is < 1 or > 128) throw new ArgumentException("Font size is not bounded.", nameof(font));
        ProfileKey = "p208-grid-v1:" + font.Family.Name + ":" +
            font.Size.ToString(CultureInfo.InvariantCulture) + ":" + font.IsBold + ":" + font.IsItalic;
    }

    /// <summary>對應字型及固定圖面演算法版本，變更後不得沿用舊fingerprint。</summary>
    public string ProfileKey { get; }

    /// <summary>產生一張有界PNG；成功後stream的唯一owner是呼叫端。</summary>
    public async Task<Stream> RenderAsync(ImagemapSize size, IReadOnlyList<RichMenuImageLabel> labels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(labels);
        if (size.Width != 2500 || size.Height is not (843 or 1686) || labels.Count is < 1 or > 20)
            throw new ArgumentException("Rich menu canvas or labels are invalid.");
        var candidate = labels.ToArray();
        foreach (var label in candidate)
            if (label is null || label.X < 0 || label.Y < 0 || label.Width < 40 || label.Height < 40 ||
                (long)label.X + label.Width > size.Width || (long)label.Y + label.Height > size.Height ||
                string.IsNullOrWhiteSpace(label.Text) || label.Text.Length > 256)
                throw new ArgumentException("Rich menu image label is invalid.");

        var config = new Configuration(new PngConfigurationModule())
        {
            MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = 0 })
        };
        using var image = new Image<Rgba32>(config, size.Width, size.Height, Color.White);
        foreach (var label in candidate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new RichTextOptions(_font) { WrappingLength = label.Width - 32 };
            var measured = TextMeasurer.MeasureBounds(label.Text, options);
            if (measured.Width > label.Width - 32 || measured.Height > label.Height - 32)
                throw new ArgumentException("Rich menu label cannot fit without truncation.");
            options.Origin = new PointF(label.X + 16 - measured.X,
                label.Y + (label.Height - measured.Height) / 2 - measured.Y);
            image.Mutate(context => context
                .Fill(Color.LightGray, new RectangularPolygon(label.X, label.Y, label.Width, label.Height))
                .Fill(Color.White, new RectangularPolygon(label.X + 2, label.Y + 2, label.Width - 4, label.Height - 4))
                .DrawText(options, label.Text, Color.Black));
        }
        var output = new MemoryStream();
        try
        {
            await image.SaveAsPngAsync(output, cancellationToken).ConfigureAwait(false);
            if (output.Length > 1024 * 1024)
                throw new InvalidOperationException("Rich menu PNG exceeds one MiB.");
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
