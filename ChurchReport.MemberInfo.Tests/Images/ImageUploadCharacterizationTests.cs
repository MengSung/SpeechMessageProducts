/*
 * 檔案整體說明
 *
 * 目的與定位：升級前先執行既有三個正常化 sink，涵蓋四條上傳路徑與另外三個縮圖方法。 樣本全部為記憶體合成的四色圖，期待尺寸、方向、像素及大小範圍由人工推導， 不以編碼後的位元組或 ImageSharp 
 * 自己的轉向結果當作黃金答案，且完全不連 CRM。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageUploadCharacterizationTests、ImageFixture、ImageToolUtilityProvider、ImageServiceProxy、ImageContextProxy、
 * ImageUploadAttempt。
 * - 四條上傳路徑各測三種允許格式及 EXIF；偽裝 WebP 改由安全拒絕案例保護；GIF 不承載 EXIF，另保護其不旋轉行為。
 * - 鎖住既有 JPEG90、AutoOrient 與會友補白邊規則；新版本容許編碼位元組不同， 但九宮格中心的四色必須在容差內一致，EXIF 不得造成二次旋轉。
 * - 三個影像讀取縮圖方法也受套件升級影響；保留既有裁切、等比縮放與 JPEG 品質效果。
 * - 三個既有縮圖方法遇到無法解碼的內容，都回傳原始陣列且不向頁面拋錯； 此測試鎖住回退契約，後續安全限制拒絕其他格式時仍必須維持相同行為。
 * 測試案例入口包含 Upload_AllowedFormats_MatchGoldenAfterUpgrade、Thumbnail_AllowedFormats_PreserveDimensionsPixelsAndSize、
 * Thumbnail_CorruptContent_ReturnsOriginalWithoutThrowing、Thumbnail_SmallImageAndExif_KeepExistingBehavior、
 * AssertColors、Thumbnail、CreateSample、NormalizeThroughExistingPath；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Controllers;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Images;

/// <summary>
/// 升級前先執行既有三個正常化 sink，涵蓋四條上傳路徑與另外三個縮圖方法。
/// 樣本全部為記憶體合成的四色圖，期待尺寸、方向、像素及大小範圍由人工推導，
/// 不以編碼後的位元組或 ImageSharp 自己的轉向結果當作黃金答案，且完全不連 CRM。
/// </summary>
public sealed class ImageUploadCharacterizationTests
{
    /// <summary>四條上傳路徑各測三種允許格式及 EXIF；偽裝 WebP 改由安全拒絕案例保護；GIF 不承載 EXIF，另保護其不旋轉行為。</summary>
    public static IEnumerable<object[]> UploadCases =>
        from route in new[] { "member", "personal", "new-attached", "new-direct" }
        from format in new[] { "jpeg", "png", "gif" }
        from orientation in new[] { false, true }
        select new object[] { route, format, orientation };

    /// <summary>
    /// 鎖住既有 JPEG90、AutoOrient 與會友補白邊規則；新版本容許編碼位元組不同，
    /// 但九宮格中心的四色必須在容差內一致，EXIF 不得造成二次旋轉。
    /// </summary>
    /// <param name="route">四條上傳路徑所使用的既有 sink。</param>
    /// <param name="format">真實圖檔格式，不以附檔名決定解碼器。</param>
    /// <param name="orientation">是否寫入旋轉 90 度的 EXIF。</param>
    [Theory]
    [MemberData(nameof(UploadCases))]
    public async Task Upload_AllowedFormats_MatchGoldenAfterUpgrade(string route, string format, bool orientation)
    {
        var bytes = ImageFixture.CreateSample(format, orientation);
        var result = await ImageFixture.NormalizeThroughExistingPath(route, bytes);
        Assert.InRange(result.Length, 500, 15000);
        using var decoded = Image.Load<Rgb24>(result);
        Assert.Equal("JPEG", decoded.Metadata.DecodedImageFormat!.Name);
        var rotated = orientation && format != "gif";
        Assert.Equal(route == "member" ? 96 : rotated ? 64 : 96, decoded.Width);
        Assert.Equal(route == "member" ? 96 : rotated ? 96 : 64, decoded.Height);
        AssertColors(decoded, rotated);
        if (decoded.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var tag) == true)
        {
            Assert.NotEqual((ushort)6, tag.Value);
        }
    }

    /// <summary>三個影像讀取縮圖方法也受套件升級影響；保留既有裁切、等比縮放與 JPEG 品質效果。</summary>
    /// <param name="route">縮圖種類。</param>
    /// <param name="format">合成來源格式。</param>
    [Theory]
    [InlineData("member-crop", "jpeg")][InlineData("member-crop", "png")][InlineData("member-crop", "gif")]
    [InlineData("member-fit", "jpeg")][InlineData("member-fit", "png")][InlineData("member-fit", "gif")]
    [InlineData("personal-crop", "jpeg")][InlineData("personal-crop", "png")][InlineData("personal-crop", "gif")]
    public void Thumbnail_AllowedFormats_PreserveDimensionsPixelsAndSize(string route, string format)
    {
        var result = ImageFixture.Thumbnail(route, ImageFixture.CreateSample(format), 80);
        Assert.InRange(result.Length, 500, 15000);
        using var decoded = Image.Load<Rgb24>(result);
        Assert.Equal("JPEG", decoded.Metadata.DecodedImageFormat!.Name);
        Assert.Equal(80, decoded.Width);
        Assert.Equal(route == "member-fit" ? 53 : 80, decoded.Height);
        AssertColors(decoded, rotated: false);
    }

    /// <summary>
    /// 三個既有縮圖方法遇到無法解碼的內容，都回傳原始陣列且不向頁面拋錯；
    /// 此測試鎖住回退契約，後續安全限制拒絕其他格式時仍必須維持相同行為。
    /// </summary>
    /// <param name="route">三個真實縮圖入口之一。</param>
    [Theory]
    [InlineData("member-crop")]
    [InlineData("member-fit")]
    [InlineData("personal-crop")]
    public void Thumbnail_CorruptContent_ReturnsOriginalWithoutThrowing(string route)
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Same(original, ImageFixture.Thumbnail(route, original, 80));
    }

    /// <summary>
    /// 原圖尺寸已小於縮圖目標時不重新編碼，保留原始內容；
    /// 帶 EXIF 的大圖則維持原有不自動旋轉的縮圖行為，避免升級誤改畫面方向。
    /// </summary>
    /// <param name="route">三個真實縮圖入口之一。</param>
    [Theory]
    [InlineData("member-crop")]
    [InlineData("member-fit")]
    [InlineData("personal-crop")]
    public void Thumbnail_SmallImageAndExif_KeepExistingBehavior(string route)
    {
        var original = ImageFixture.CreateSample("jpeg", orientation: true);
        Assert.Same(original, ImageFixture.Thumbnail(route, original, 100));
        using var decoded = Image.Load<Rgb24>(ImageFixture.Thumbnail(route, original, 80));
        Assert.Equal(80, decoded.Width);
        Assert.Equal(route == "member-fit" ? 53 : 80, decoded.Height);
        AssertColors(decoded, rotated: false);
    }

    /// <summary>手算四個象限中心顏色；JPEG 有損壓縮使用每通道 35 容差，不鏡像待測編碼演算法。</summary>
    private static void AssertColors(Image<Rgb24> image, bool rotated)
    {
        var red = new Rgb24(240, 20, 20); var green = new Rgb24(20, 240, 20);
        var blue = new Rgb24(20, 20, 240); var yellow = new Rgb24(240, 240, 20);
        var expected = rotated ? new[] { blue, red, yellow, green } : new[] { red, green, blue, yellow };
        for (var index = 0; index < 4; index++)
        {
            var actual = image[(index % 2 == 0 ? 1 : 3) * image.Width / 4, (index < 2 ? 1 : 3) * image.Height / 4];
            Assert.InRange(Math.Abs(actual.R - expected[index].R), 0, 35);
            Assert.InRange(Math.Abs(actual.G - expected[index].G), 0, 35);
            Assert.InRange(Math.Abs(actual.B - expected[index].B), 0, 35);
        }
    }
}

/// <summary>只建立合成圖片及記憶體控制器依賴，所有 stream、DI scope 與 controller 都只屬一次測試呼叫。</summary>
internal static class ImageFixture
{
    /// <summary>
    /// 呼叫實際縮圖方法；測試不另外實作縮放或例外回退，不會以替身掩蓋解碼結果。
    /// 呼叫僅使用記憶體圖片，所有解碼與串流由產品方法在返回前釋放。
    /// </summary>
    internal static byte[] Thumbnail(string route, byte[] bytes, int size)
    {
        var type = route == "personal-crop" ? typeof(PersonalController) : typeof(MemberInfoController);
        var method = type.GetMethod(route == "member-fit" ? "CreateFitThumbnail" : "CreateThumbnailIfNeeded",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (byte[])method.Invoke(null, new object[] { bytes, size })!;
    }

    /// <summary>產生 96×64 四色測試圖；沒有檔名、照片、個資或外部輸入，所有影像與輸出串流立即釋放。</summary>
    internal static byte[] CreateSample(string format, bool orientation = false)
    {
        using var image = new Image<Rgb24>(96, 64);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            image[x, y] = y < 32 ? (x < 48 ? new Rgb24(240, 20, 20) : new Rgb24(20, 240, 20))
                : (x < 48 ? new Rgb24(20, 20, 240) : new Rgb24(240, 240, 20));
        if (orientation && format != "gif")
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        }
        IImageEncoder encoder = format switch
        {
            "jpeg" => new JpegEncoder { Quality = 95 }, "png" => new PngEncoder(),
            "gif" => new GifEncoder(), "bmp" => new BmpEncoder(), "tiff" => new TiffEncoder(), "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
            _ => throw new ArgumentException("Unknown synthetic format.")
        };
        using var output = new MemoryStream(); image.Save(output, encoder); return output.ToArray();
    }

    /// <summary>
    /// 使用真實 controller 的影像處理；MemberInfo 直接測純 sink，新人附圖測共用 sink，
    /// 另外兩路執行公開 action。以假的服務接收 entityimage，不執行登入、SDK 連線或新人建立。
    /// </summary>
    internal static async Task<byte[]> NormalizeThroughExistingPath(string route, byte[] bytes)
    {
        var attempt = await AttemptUpload(route, bytes);
        Assert.True(attempt.Success, attempt.Message ?? attempt.Error?.GetType().Name);
        return Assert.IsType<byte[]>(attempt.Bytes);
    }

    /// <summary>
    /// 同時觀察實際 action／純正常化 sink 的拒絕結果與 SDK 寫入，不讓測試替身自行判斷圖片是否合法。
    /// 全部依賴由此呼叫擁有並確定釋放；可注入 SDK 真正失敗以檢驗最終 catch 的通知責任。
    /// </summary>
    internal static async Task<ImageUploadAttempt> AttemptUpload(string route, byte[] bytes,
        Exception? updateFailure = null, IFormFile? suppliedFile = null)
    {
        using var input = new MemoryStream(bytes);
        // 刻意讓每種實際格式都宣告為 JPG，驗證信任邊界必須檢查內容而非只相信檔名與 Content-Type。
        var file = suppliedFile ?? new FormFile(input, 0, bytes.Length, "imageFile", "synthetic.jpg")
            { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
        if (route == "member")
        {
            try
            {
                var normalized = (byte[])typeof(MemberInfoController).GetMethod("NormalizeUploadedImage", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { file })!;
                return new(true, normalized, null, null);
            }
            catch (TargetInvocationException exception)
            {
                return new(false, null, exception.InnerException?.Message, exception.InnerException);
            }
        }
        var service = DispatchProxy.Create<IOrganizationService, ImageServiceProxy>();
        var serviceState = (ImageServiceProxy)(object)service;
        serviceState.UpdateFailure = updateFailure;
        using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        using var tracer = new NullToolUtilityTracer();
        using var utility = new ToolUtilityClass(service, tracer, new ConfigurationBuilder().Build());
        var context = DispatchProxy.Create<IInMemoryDataContext, ImageContextProxy>();
        var controllerType = route == "personal" ? typeof(PersonalController) : typeof(NewPersonController);
        using var controller = (BaseChurchController)RuntimeHelpers.GetUninitializedObject(controllerType);
        typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, context);
        typeof(BaseChurchController).GetField("_toolUtilityProvider", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, new ImageToolUtilityProvider(utility));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } };
        try
        {
            JsonResult? result = null;
            if (route == "personal")
                result = Assert.IsType<JsonResult>(await ((PersonalController)controller).UploadContactImage(file));
            else if (route == "new-direct")
                result = Assert.IsType<JsonResult>(await ((NewPersonController)controller).UploadNewPersonImage(file));
            else
                await (Task)typeof(NewPersonController).GetMethod("TryUploadNewPersonImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(controller, new object[] { file })!;
            var success = result == null || (bool)result.Value!.GetType().GetProperty("success")!.GetValue(result.Value)!;
            var message = (string?)result?.Value?.GetType().GetProperty("message")?.GetValue(result.Value);
            return new(success, serviceState.Updated?.GetAttributeValue<byte[]>("entityimage"), message, null);
        }
        catch (Exception exception)
        {
            var error = exception is TargetInvocationException reflected ? reflected.InnerException! : exception;
            return new(false, serviceState.Updated?.GetAttributeValue<byte[]>("entityimage"), error.Message, error);
        }
    }

}

/// <summary>
/// 借用單次測試建立的真實工具；工具的 owner 是 NormalizeThroughExistingPath，
/// 本轉接不保存 static、Session 或憑證，也不建立額外工具或外部連線。
/// </summary>
internal sealed class ImageToolUtilityProvider(ToolUtilityClass utility) : IToolUtilityProvider
{
    /// <summary>提供當次控制器所需的工具，生命週期由外層 using 確定性釋放。</summary>
    public ToolUtilityClass GetToolUtility() => utility;
}

/// <summary>此測試服務只接受照片 Update 與目前 contact Retrieve，任何其他 SDK 存取立即失敗，避免假綠燈。</summary>
public class ImageServiceProxy : DispatchProxy
{
    /// <summary>只屬當次測試的照片更新快照，沒有 static 或真實 credential。</summary>
    public Entity? Updated { get; private set; }
    /// <summary>只在當次 SDK Update 注入故障，不保存至任何共用狀態。</summary>
    public Exception? UpdateFailure { get; set; }
    /// <summary>攔截 SDK 邊界而保留真實影像處理；不建立任何網路資源。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "Update")
        {
            if (UpdateFailure != null) throw UpdateFailure;
            Updated = (Entity)args![0]!; return null;
        }
        if (targetMethod.Name == "Retrieve") return new Entity("contact", (Guid)args![1]!);
        throw new NotSupportedException("Unexpected synthetic service call.");
    }
}

/// <summary>只提供兩個影像流程需要的 request 自有模型，不執行真正模型的外部初始化。</summary>
public class ImageContextProxy : DispatchProxy
{
    private readonly PersonalInfomationModel personal = new() { m_LoginContact = new Entity("contact", Guid.NewGuid()) };
    private readonly NewPersonModel newcomer = (NewPersonModel)RuntimeHelpers.GetUninitializedObject(typeof(NewPersonModel));
    /// <summary>建立帶假資料庫 ID 的新人模型；不呼叫會初始化其他產品狀態的建構式。</summary>
    public ImageContextProxy() => newcomer.m_NewContact = new NewContact { PresentRecordId = Guid.NewGuid().ToString() };
    /// <summary>禁止意外存取其他 Session／業務依賴，只有已核定的兩項模型能通過。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
    {
        "get_NewPersonModel" => newcomer, "get_PersonalInfomationModel" => personal,
        _ => throw new NotSupportedException("Unexpected synthetic context call.")
    };
}

/// <summary>實際產品路徑的觀察結果；沒有 Session、SDK client 或 stream 參考，僅存本案例的輸出。</summary>
internal sealed record ImageUploadAttempt(bool Success, byte[]? Bytes, string? Message, Exception? Error);
