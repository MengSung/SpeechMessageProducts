ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p0-stage0-resume-07

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第7批：套件補修與 P0-12A 圖片防護

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：1c3c6fe7e066983e7346aded36fe30209c6341241c53bb6d1bf45f4610ba35c9
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：ImageSharp 3.2.0、Xml/Pkcs 10.0.14、net48 NuGetAuditMode=all 與 Json 6.0.11；不得抑制警告；六解碼位置同一限定 JPEG/PNG/GIF 設定，先 Identify 再尺寸檢查與完整 Load，最大邊8192、總像素40M；拒絕與真正故障分流、舊縮圖回退、EXIF/JPEG行為、串流／Image確定釋放與跨請求隔離。

```diff
diff --git a/.trellis/spec/backend/image-input-contract.md b/.trellis/spec/backend/image-input-contract.md
new file mode 100644
index 00000000..017d3105
--- /dev/null
+++ b/.trellis/spec/backend/image-input-contract.md
@@ -0,0 +1,66 @@
+# ChurchReport Image Input Contract
+
+## Scope and entry points
+
+P0-12A uses Services/Images/ImageInputValidator for all six image decode sites:
+MemberInfo upload normalization, Personal upload, the shared NewPerson upload,
+MemberInfo crop/fit thumbnails, and Personal crop thumbnails. The NewPerson
+normalizer serves both SaveNewPerson attachments and UploadNewPersonImage.
+Do not introduce a separate default Image.Load path in controllers.
+
+## Content and size validation
+
+Keep the existing declaration whitelist (.jpg/.jpeg/.png/.gif and JPEG/PNG/GIF
+content types), the existing upload size limit of 5 MiB, and authorization flow.
+Declaration checks do not establish the actual format. A private Configuration
+registers only JPEG, PNG and GIF modules; never clone Configuration.Default,
+which would keep other formats registered. WebP, TIFF, BMP and other formats
+masquerading as JPG are rejected before full decoding.
+
+Identify dimensions with the same restricted options before Load. Reject a side
+above 8192 or a long width-times-height above 40,000,000; equality is allowed.
+Decode only the first frame because the existing output is a single JPEG.
+Preserve orientation, square padding, crop/fit and JPEG quality from each
+existing path. Golden tests compare decoded dimensions and pixels, not encoder
+bytes. Every fixture is synthetic and intentionally declares JPG/JPEG even when
+the actual format differs.
+
+## Rejections, publication and ownership
+
+ImageInputRejectedException carries only fixed application messages, without
+raw decoder exception messages or input references. Unsupported/corrupt content
+and dimension violations are expected refusals and do not notify administrators.
+No upload may fall back to storing the original unvalidated bytes.
+
+Thumbnails deliberately keep their preexisting fallback: return the same original
+array when input is refused, without throwing into the page. Genuine unexpected
+faults still pass the original Exception to the existing shared diagnostics entry;
+JSON uses fixed messages. A photo refusal after a newcomer has already been
+created must not claim the newcomer creation failed or prompt a duplicate create.
+
+The caller owns its source stream and the returned Image; using scopes dispose
+both, plus output streams, before publishing a new JPEG byte array. The helper
+borrows the source and owns only its temporary buffer. Non-seekable input is
+buffered at most 5 MiB plus one byte before rejection; the buffer is disposed on
+success and failure. Options are operation-local. Configuration is private,
+unchanging and contains no user data; its pixel pool is disabled so no previous
+request image is retained by that decoder. No timers, background tasks or mutable
+user-specific statics are introduced.
+
+## Verification
+
+Retain allowed-format golden cases and all three thumbnail fallback contracts.
+Cover disguised WebP/TIFF/BMP, corrupt input, size limits before pixel decoding,
+exact dimension limits, bounded non-seekable input, first-frame GIF behavior,
+compiled calls to the shared helper, fixed error output and the actual shared
+exception diagnostics owner with an in-memory sender. Concurrent mixed inputs
+must stay isolated, streams must drain to zero and repeated real decodes must
+not add ImageSharp allocations abandoned to finalizers. No D365/LINE access.
+
+## Fixed interface text
+
+New rejection and failure text lives in ImageInputText.resx. I18nCheck generates
+ImageInputText.g.cs; verify checks both this file and SharedText.g.cs. The local
+facade copies only the fixed Traditional Chinese column into a frozen dictionary,
+with no runtime user-specific cache or culture mutation. Do not increase the CJK
+baseline or ignore the validator to accept new display literals.

diff --git a/ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs b/ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs
new file mode 100644
index 00000000..8174e0b1
--- /dev/null
+++ b/ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs
@@ -0,0 +1,290 @@
+using System.Reflection;
+using System.Runtime.CompilerServices;
+using ChurchReport.Controllers;
+using ChurchReport.Models;
+using ChurchReport.Models.CrmTransmitModule;
+using Microsoft.AspNetCore.Http;
+using Microsoft.AspNetCore.Mvc;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Xrm.Sdk;
+using SixLabors.ImageSharp;
+using SixLabors.ImageSharp.Formats;
+using SixLabors.ImageSharp.Formats.Gif;
+using SixLabors.ImageSharp.Formats.Bmp;
+using SixLabors.ImageSharp.Formats.Tiff;
+using SixLabors.ImageSharp.Formats.Jpeg;
+using SixLabors.ImageSharp.Formats.Png;
+using SixLabors.ImageSharp.Formats.Webp;
+using SixLabors.ImageSharp.Metadata.Profiles.Exif;
+using SixLabors.ImageSharp.PixelFormats;
+using ToolUtilityNameSpace;
+using ToolUtilityNameSpace.DependencyInjection;
+using ToolUtilityNameSpace.Diagnostics;
+using Xunit;
+
+namespace ChurchReport.MemberInfo.Tests.Images;
+
+/// <summary>
+/// 升級前先執行既有三個正常化 sink，涵蓋四條上傳路徑與另外三個縮圖方法。
+/// 樣本全部為記憶體合成的四色圖，期待尺寸、方向、像素及大小範圍由人工推導，
+/// 不以編碼後的位元組或 ImageSharp 自己的轉向結果當作黃金答案，且完全不連 CRM。
+/// </summary>
+public sealed class ImageUploadCharacterizationTests
+{
+    /// <summary>四條上傳路徑各測三種允許格式及 EXIF；偽裝 WebP 改由安全拒絕案例保護；GIF 不承載 EXIF，另保護其不旋轉行為。</summary>
+    public static IEnumerable<object[]> UploadCases =>
+        from route in new[] { "member", "personal", "new-attached", "new-direct" }
+        from format in new[] { "jpeg", "png", "gif" }
+        from orientation in new[] { false, true }
+        select new object[] { route, format, orientation };
+
+    /// <summary>
+    /// 鎖住既有 JPEG90、AutoOrient 與會友補白邊規則；新版本容許編碼位元組不同，
+    /// 但九宮格中心的四色必須在容差內一致，EXIF 不得造成二次旋轉。
+    /// </summary>
+    /// <param name="route">四條上傳路徑所使用的既有 sink。</param>
+    /// <param name="format">真實圖檔格式，不以附檔名決定解碼器。</param>
+    /// <param name="orientation">是否寫入旋轉 90 度的 EXIF。</param>
+    [Theory]
+    [MemberData(nameof(UploadCases))]
+    public async Task Upload_AllowedFormats_MatchGoldenAfterUpgrade(string route, string format, bool orientation)
+    {
+        var bytes = ImageFixture.CreateSample(format, orientation);
+        var result = await ImageFixture.NormalizeThroughExistingPath(route, bytes);
+        Assert.InRange(result.Length, 500, 15000);
+        using var decoded = Image.Load<Rgb24>(result);
+        Assert.Equal("JPEG", decoded.Metadata.DecodedImageFormat!.Name);
+        var rotated = orientation && format != "gif";
+        Assert.Equal(route == "member" ? 96 : rotated ? 64 : 96, decoded.Width);
+        Assert.Equal(route == "member" ? 96 : rotated ? 96 : 64, decoded.Height);
+        AssertColors(decoded, rotated);
+        if (decoded.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var tag) == true)
+        {
+            Assert.NotEqual((ushort)6, tag.Value);
+        }
+    }
+
+    /// <summary>三個影像讀取縮圖方法也受套件升級影響；保留既有裁切、等比縮放與 JPEG 品質效果。</summary>
+    /// <param name="route">縮圖種類。</param>
+    /// <param name="format">合成來源格式。</param>
+    [Theory]
+    [InlineData("member-crop", "jpeg")][InlineData("member-crop", "png")][InlineData("member-crop", "gif")]
+    [InlineData("member-fit", "jpeg")][InlineData("member-fit", "png")][InlineData("member-fit", "gif")]
+    [InlineData("personal-crop", "jpeg")][InlineData("personal-crop", "png")][InlineData("personal-crop", "gif")]
+    public void Thumbnail_AllowedFormats_PreserveDimensionsPixelsAndSize(string route, string format)
+    {
+        var result = ImageFixture.Thumbnail(route, ImageFixture.CreateSample(format), 80);
+        Assert.InRange(result.Length, 500, 15000);
+        using var decoded = Image.Load<Rgb24>(result);
+        Assert.Equal("JPEG", decoded.Metadata.DecodedImageFormat!.Name);
+        Assert.Equal(80, decoded.Width);
+        Assert.Equal(route == "member-fit" ? 53 : 80, decoded.Height);
+        AssertColors(decoded, rotated: false);
+    }
+
+    /// <summary>
+    /// 三個既有縮圖方法遇到無法解碼的內容，都回傳原始陣列且不向頁面拋錯；
+    /// 此測試鎖住回退契約，後續安全限制拒絕其他格式時仍必須維持相同行為。
+    /// </summary>
+    /// <param name="route">三個真實縮圖入口之一。</param>
+    [Theory]
+    [InlineData("member-crop")]
+    [InlineData("member-fit")]
+    [InlineData("personal-crop")]
+    public void Thumbnail_CorruptContent_ReturnsOriginalWithoutThrowing(string route)
+    {
+        var original = new byte[] { 1, 2, 3, 4, 5 };
+        Assert.Same(original, ImageFixture.Thumbnail(route, original, 80));
+    }
+
+    /// <summary>
+    /// 原圖尺寸已小於縮圖目標時不重新編碼，保留原始內容；
+    /// 帶 EXIF 的大圖則維持原有不自動旋轉的縮圖行為，避免升級誤改畫面方向。
+    /// </summary>
+    /// <param name="route">三個真實縮圖入口之一。</param>
+    [Theory]
+    [InlineData("member-crop")]
+    [InlineData("member-fit")]
+    [InlineData("personal-crop")]
+    public void Thumbnail_SmallImageAndExif_KeepExistingBehavior(string route)
+    {
+        var original = ImageFixture.CreateSample("jpeg", orientation: true);
+        Assert.Same(original, ImageFixture.Thumbnail(route, original, 100));
+        using var decoded = Image.Load<Rgb24>(ImageFixture.Thumbnail(route, original, 80));
+        Assert.Equal(80, decoded.Width);
+        Assert.Equal(route == "member-fit" ? 53 : 80, decoded.Height);
+        AssertColors(decoded, rotated: false);
+    }
+
+    /// <summary>手算四個象限中心顏色；JPEG 有損壓縮使用每通道 35 容差，不鏡像待測編碼演算法。</summary>
+    private static void AssertColors(Image<Rgb24> image, bool rotated)
+    {
+        var red = new Rgb24(240, 20, 20); var green = new Rgb24(20, 240, 20);
+        var blue = new Rgb24(20, 20, 240); var yellow = new Rgb24(240, 240, 20);
+        var expected = rotated ? new[] { blue, red, yellow, green } : new[] { red, green, blue, yellow };
+        for (var index = 0; index < 4; index++)
+        {
+            var actual = image[(index % 2 == 0 ? 1 : 3) * image.Width / 4, (index < 2 ? 1 : 3) * image.Height / 4];
+            Assert.InRange(Math.Abs(actual.R - expected[index].R), 0, 35);
+            Assert.InRange(Math.Abs(actual.G - expected[index].G), 0, 35);
+            Assert.InRange(Math.Abs(actual.B - expected[index].B), 0, 35);
+        }
+    }
+}
+
+/// <summary>只建立合成圖片及記憶體控制器依賴，所有 stream、DI scope 與 controller 都只屬一次測試呼叫。</summary>
+internal static class ImageFixture
+{
+    /// <summary>
+    /// 呼叫實際縮圖方法；測試不另外實作縮放或例外回退，不會以替身掩蓋解碼結果。
+    /// 呼叫僅使用記憶體圖片，所有解碼與串流由產品方法在返回前釋放。
+    /// </summary>
+    internal static byte[] Thumbnail(string route, byte[] bytes, int size)
+    {
+        var type = route == "personal-crop" ? typeof(PersonalController) : typeof(MemberInfoController);
+        var method = type.GetMethod(route == "member-fit" ? "CreateFitThumbnail" : "CreateThumbnailIfNeeded",
+            BindingFlags.NonPublic | BindingFlags.Static)!;
+        return (byte[])method.Invoke(null, new object[] { bytes, size })!;
+    }
+
+    /// <summary>產生 96×64 四色測試圖；沒有檔名、照片、個資或外部輸入，所有影像與輸出串流立即釋放。</summary>
+    internal static byte[] CreateSample(string format, bool orientation = false)
+    {
+        using var image = new Image<Rgb24>(96, 64);
+        for (var y = 0; y < image.Height; y++)
+        for (var x = 0; x < image.Width; x++)
+            image[x, y] = y < 32 ? (x < 48 ? new Rgb24(240, 20, 20) : new Rgb24(20, 240, 20))
+                : (x < 48 ? new Rgb24(20, 20, 240) : new Rgb24(240, 240, 20));
+        if (orientation && format != "gif")
+        {
+            image.Metadata.ExifProfile = new ExifProfile();
+            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
+        }
+        IImageEncoder encoder = format switch
+        {
+            "jpeg" => new JpegEncoder { Quality = 95 }, "png" => new PngEncoder(),
+            "gif" => new GifEncoder(), "bmp" => new BmpEncoder(), "tiff" => new TiffEncoder(), "webp" => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
+            _ => throw new ArgumentException("Unknown synthetic format.")
+        };
+        using var output = new MemoryStream(); image.Save(output, encoder); return output.ToArray();
+    }
+
+    /// <summary>
+    /// 使用真實 controller 的影像處理；MemberInfo 直接測純 sink，新人附圖測共用 sink，
+    /// 另外兩路執行公開 action。以假的服務接收 entityimage，不執行登入、SDK 連線或新人建立。
+    /// </summary>
+    internal static async Task<byte[]> NormalizeThroughExistingPath(string route, byte[] bytes)
+    {
+        var attempt = await AttemptUpload(route, bytes);
+        Assert.True(attempt.Success, attempt.Message ?? attempt.Error?.GetType().Name);
+        return Assert.IsType<byte[]>(attempt.Bytes);
+    }
+
+    /// <summary>
+    /// 同時觀察實際 action／純正常化 sink 的拒絕結果與 SDK 寫入，不讓測試替身自行判斷圖片是否合法。
+    /// 全部依賴由此呼叫擁有並確定釋放；可注入 SDK 真正失敗以檢驗最終 catch 的通知責任。
+    /// </summary>
+    internal static async Task<ImageUploadAttempt> AttemptUpload(string route, byte[] bytes,
+        Exception? updateFailure = null, IFormFile? suppliedFile = null)
+    {
+        using var input = new MemoryStream(bytes);
+        // 刻意讓每種實際格式都宣告為 JPG，驗證信任邊界必須檢查內容而非只相信檔名與 Content-Type。
+        var file = suppliedFile ?? new FormFile(input, 0, bytes.Length, "imageFile", "synthetic.jpg")
+            { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
+        if (route == "member")
+        {
+            try
+            {
+                var normalized = (byte[])typeof(MemberInfoController).GetMethod("NormalizeUploadedImage", BindingFlags.NonPublic | BindingFlags.Static)!
+                    .Invoke(null, new object[] { file })!;
+                return new(true, normalized, null, null);
+            }
+            catch (TargetInvocationException exception)
+            {
+                return new(false, null, exception.InnerException?.Message, exception.InnerException);
+            }
+        }
+        var service = DispatchProxy.Create<IOrganizationService, ImageServiceProxy>();
+        var serviceState = (ImageServiceProxy)(object)service;
+        serviceState.UpdateFailure = updateFailure;
+        using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
+        using var tracer = new NullToolUtilityTracer();
+        using var utility = new ToolUtilityClass(service, tracer, new ConfigurationBuilder().Build());
+        var context = DispatchProxy.Create<IInMemoryDataContext, ImageContextProxy>();
+        var controllerType = route == "personal" ? typeof(PersonalController) : typeof(NewPersonController);
+        using var controller = (BaseChurchController)RuntimeHelpers.GetUninitializedObject(controllerType);
+        typeof(BaseChurchController).GetField("InMemoryContext", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, context);
+        typeof(BaseChurchController).GetField("_toolUtilityProvider", BindingFlags.NonPublic | BindingFlags.Instance)!
+            .SetValue(controller, new ImageToolUtilityProvider(utility));
+        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } };
+        try
+        {
+            JsonResult? result = null;
+            if (route == "personal")
+                result = Assert.IsType<JsonResult>(await ((PersonalController)controller).UploadContactImage(file));
+            else if (route == "new-direct")
+                result = Assert.IsType<JsonResult>(await ((NewPersonController)controller).UploadNewPersonImage(file));
+            else
+                await (Task)typeof(NewPersonController).GetMethod("TryUploadNewPersonImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
+                    .Invoke(controller, new object[] { file })!;
+            var success = result == null || (bool)result.Value!.GetType().GetProperty("success")!.GetValue(result.Value)!;
+            var message = (string?)result?.Value?.GetType().GetProperty("message")?.GetValue(result.Value);
+            return new(success, serviceState.Updated?.GetAttributeValue<byte[]>("entityimage"), message, null);
+        }
+        catch (Exception exception)
+        {
+            var error = exception is TargetInvocationException reflected ? reflected.InnerException! : exception;
+            return new(false, serviceState.Updated?.GetAttributeValue<byte[]>("entityimage"), error.Message, error);
+        }
+    }
+
+}
+
+/// <summary>
+/// 借用單次測試建立的真實工具；工具的 owner 是 NormalizeThroughExistingPath，
+/// 本轉接不保存 static、Session 或憑證，也不建立額外工具或外部連線。
+/// </summary>
+internal sealed class ImageToolUtilityProvider(ToolUtilityClass utility) : IToolUtilityProvider
+{
+    /// <summary>提供當次控制器所需的工具，生命週期由外層 using 確定性釋放。</summary>
+    public ToolUtilityClass GetToolUtility() => utility;
+}
+
+/// <summary>此測試服務只接受照片 Update 與目前 contact Retrieve，任何其他 SDK 存取立即失敗，避免假綠燈。</summary>
+public class ImageServiceProxy : DispatchProxy
+{
+    /// <summary>只屬當次測試的照片更新快照，沒有 static 或真實 credential。</summary>
+    public Entity? Updated { get; private set; }
+    /// <summary>只在當次 SDK Update 注入故障，不保存至任何共用狀態。</summary>
+    public Exception? UpdateFailure { get; set; }
+    /// <summary>攔截 SDK 邊界而保留真實影像處理；不建立任何網路資源。</summary>
+    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
+    {
+        if (targetMethod!.Name == "Update")
+        {
+            if (UpdateFailure != null) throw UpdateFailure;
+            Updated = (Entity)args![0]!; return null;
+        }
+        if (targetMethod.Name == "Retrieve") return new Entity("contact", (Guid)args![1]!);
+        throw new NotSupportedException("Unexpected synthetic service call.");
+    }
+}
+
+/// <summary>只提供兩個影像流程需要的 request 自有模型，不執行真正模型的外部初始化。</summary>
+public class ImageContextProxy : DispatchProxy
+{
+    private readonly PersonalInfomationModel personal = new() { m_LoginContact = new Entity("contact", Guid.NewGuid()) };
+    private readonly NewPersonModel newcomer = (NewPersonModel)RuntimeHelpers.GetUninitializedObject(typeof(NewPersonModel));
+    /// <summary>建立帶假資料庫 ID 的新人模型；不呼叫會初始化其他產品狀態的建構式。</summary>
+    public ImageContextProxy() => newcomer.m_NewContact = new NewContact { PresentRecordId = Guid.NewGuid().ToString() };
+    /// <summary>禁止意外存取其他 Session／業務依賴，只有已核定的兩項模型能通過。</summary>
+    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
+    {
+        "get_NewPersonModel" => newcomer, "get_PersonalInfomationModel" => personal,
+        _ => throw new NotSupportedException("Unexpected synthetic context call.")
+    };
+}
+
+/// <summary>實際產品路徑的觀察結果；沒有 Session、SDK client 或 stream 參考，僅存本案例的輸出。</summary>
+internal sealed record ImageUploadAttempt(bool Success, byte[]? Bytes, string? Message, Exception? Error);

diff --git a/ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs b/ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs
new file mode 100644
index 00000000..ce1dfa82
--- /dev/null
+++ b/ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs
@@ -0,0 +1,398 @@
+using System.Buffers.Binary;
+using System.Reflection;
+using System.Reflection.Emit;
+using System.Runtime.CompilerServices;
+using ChurchReport.Controllers;
+using ChurchReport.Services.Images;
+using Microsoft.AspNetCore.Http;
+using SixLabors.ImageSharp;
+using SixLabors.ImageSharp.Diagnostics;
+using SixLabors.ImageSharp.Formats.Gif;
+using SixLabors.ImageSharp.PixelFormats;
+using ToolUtilityNameSpace.Diagnostics;
+using Xunit;
+
+namespace ChurchReport.MemberInfo.Tests.Images;
+
+/// <summary>
+/// 以真正控制器影像路徑驗證內容白名單、解碼前尺寸拒絕與釋放責任。
+/// 僅以記憶體假 SDK 隔離網路；診斷 owner 只寫測試暫存目錄，不建立任何 LINE sender。
+/// </summary>
+[Collection("ExceptionReporting")]
+public sealed class ImageUploadSafetyTests
+{
+    /// <summary>四條呼叫路徑都要拒絕偽裝 JPG 的 TIFF，不能把未驗證內容存進 CRM。</summary>
+    [Theory]
+    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
+    public async Task Upload_TiffFile_RejectedBeforeDecode(string route) =>
+        await AssertRejected(route, ImageFixture.CreateSample("tiff"), "格式");
+
+    /// <summary>現有宣告白名單保持不變，實際 BMP 不能因宣告 JPG 而繞過內容辨識。</summary>
+    [Theory]
+    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
+    public async Task Upload_UnsupportedFormat_Rejected(string route) =>
+        await AssertRejected(route, ImageFixture.CreateSample("bmp"), "格式");
+
+    /// <summary>WebP 曾被通用解碼器接受；本核定刻意改為拒絕，不能再保存原始檔案。</summary>
+    [Theory]
+    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
+    public async Task Upload_DisguisedWebpAsJpg_Rejected(string route) =>
+        await AssertRejected(route, ImageFixture.CreateSample("webp"), "格式");
+
+    /// <summary>
+    /// PNG 只有合法標頭與結尾，沒有像素資料；回應必須先報尺寸而非嘗試像素解碼。
+    /// 兩個單邊邊界與總面積邊界各自獨立，避免只檢查其中一項而產生假綠燈。
+    /// </summary>
+    [Theory]
+    [InlineData("member", 8193, 1)][InlineData("personal", 1, 8193)]
+    [InlineData("new-attached", 8000, 5001)][InlineData("new-direct", 8000, 5001)]
+    public async Task Upload_OversizedDimensions_RejectedBeforeFullDecode(string route, int width, int height) =>
+        await AssertRejected(route, PngHeader(width, height), "尺寸");
+
+    /// <summary>不是影像的內容必須走固定輸入拒絕，不寫入資料、不把原始內容或解析錯誤外送。</summary>
+    [Theory]
+    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
+    public async Task Upload_CorruptFile_ReturnsExpectedRejection(string route) =>
+        await AssertRejected(route, new byte[] { 1, 2, 3, 4, 5 }, null);
+
+    /// <summary>
+    /// 允許格式的合法標頭但缺少像素資料，必須以損壞圖檔拒絕；
+    /// 上限本身仍允許，不能誤把等於 8192 或等於 40,000,000 判成尺寸超限。
+    /// </summary>
+    [Theory]
+    [InlineData(8192, 1)][InlineData(1, 8192)][InlineData(8000, 5000)]
+    public void Upload_ExactDimensionLimits_AreNotRejectedAsOversized(int width, int height)
+    {
+        using var input = new MemoryStream(PngHeader(width, height));
+        var rejection = Assert.Throws<ImageInputRejectedException>(() =>
+        {
+            using var image = ImageInputValidator.Load(input);
+        });
+        Assert.Contains("損壞", rejection.Message);
+        Assert.DoesNotContain("尺寸", rejection.Message);
+        Assert.True(input.CanRead);
+    }
+
+    /// <summary>
+    /// 非 seekable 串流不能因 Length 不可用而無界暫存。同步與非同步都只讀 5 MiB 加一，
+    /// 拒絕後釋放內部副本但不釋放借用來源；來源最後仍由本案例 using 擁有。
+    /// </summary>
+    [Theory]
+    [InlineData(false)][InlineData(true)]
+    public async Task Upload_NonSeekableOversizedStream_IsBoundedAndBorrowed(bool asynchronous)
+    {
+        using var input = new CountedNonSeekableStream(new byte[6 * 1024 * 1024]);
+        ImageInputRejectedException rejection;
+        if (asynchronous)
+            rejection = await Assert.ThrowsAsync<ImageInputRejectedException>(async () =>
+            {
+                using var image = await ImageInputValidator.LoadAsync(input);
+            });
+        else
+            rejection = Assert.Throws<ImageInputRejectedException>(() =>
+            {
+                using var image = ImageInputValidator.Load(input);
+            });
+        Assert.Contains("5MB", rejection.Message);
+        Assert.Equal(5 * 1024 * 1024 + 1, input.ReadBytes);
+        Assert.True(input.CanRead);
+    }
+
+    /// <summary>動態 GIF 輸出仍以第一影格為準，禁止多影格造成配置放大或改用第二人的影像。</summary>
+    [Fact]
+    public async Task Upload_AnimatedGif_UsesFirstFrameForEveryRoute()
+    {
+        using var source = new Image<Rgb24>(96, 64, new Rgb24(240, 20, 20));
+        using var second = new Image<Rgb24>(96, 64, new Rgb24(20, 20, 240));
+        source.Frames.AddFrame(second.Frames.RootFrame);
+        using var output = new MemoryStream();
+        source.Save(output, new GifEncoder());
+        foreach (var route in new[] { "member", "personal", "new-attached", "new-direct" })
+        {
+            var result = await ImageFixture.NormalizeThroughExistingPath(route, output.ToArray());
+            using var decoded = Image.Load<Rgb24>(result);
+            var pixel = decoded[decoded.Width / 2, decoded.Height / 2];
+            Assert.InRange(pixel.R, 220, 255);
+            Assert.InRange(pixel.B, 0, 45);
+        }
+    }
+
+    /// <summary>
+    /// 重複跑實際六個解碼位置及拒絕分支，所有輸入串流立即歸零；
+    /// 強制排空後不得新增 ImageSharp 未 Dispose 的配置。事件只計數、不保存配置堆疊，
+    /// finally 解除 static 訂閱，避免測試自身延長圖檔或案例生命週期。
+    /// </summary>
+    [Fact]
+    public async Task Upload_Soak_DrainsImageResourcesWithoutFinalizerLeaks()
+    {
+        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
+        var baseline = MemoryDiagnostics.TotalUndisposedAllocationCount;
+        var leaks = 0;
+        void OnLeak(string _) => Interlocked.Increment(ref leaks);
+        MemoryDiagnostics.UndisposedAllocation += OnLeak;
+        try
+        {
+            var bytes = ImageFixture.CreateSample("png");
+            for (var round = 0; round < 25; round++)
+            {
+                foreach (var route in new[] { "member", "personal", "new-attached", "new-direct" })
+                {
+                    var file = new TrackedImageFile(bytes);
+                    var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
+                    Assert.True(attempt.Success);
+                    Assert.NotNull(attempt.Bytes);
+                    Assert.Equal(0, file.ActiveStreams);
+                }
+                foreach (var route in new[] { "member-crop", "member-fit", "personal-crop" })
+                    Assert.NotEmpty(ImageFixture.Thumbnail(route, bytes, 80));
+            }
+            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
+            Assert.Equal(0, Volatile.Read(ref leaks));
+            Assert.Equal(baseline, MemoryDiagnostics.TotalUndisposedAllocationCount);
+        }
+        finally
+        {
+            MemoryDiagnostics.UndisposedAllocation -= OnLeak;
+        }
+    }
+
+    /// <summary>資料庫既有圖片無法以允許解碼器處理時，三個縮圖維持回原始陣列且不拋錯。</summary>
+    [Theory]
+    [InlineData("member-crop", "webp")][InlineData("member-fit", "tiff")][InlineData("personal-crop", "bmp")]
+    public void Thumbnail_DisallowedFormat_KeepsExistingFallback(string route, string format)
+    {
+        var original = ImageFixture.CreateSample(format);
+        Assert.Same(original, ImageFixture.Thumbnail(route, original, 80));
+    }
+
+    /// <summary>
+    /// 檢查編譯後真正呼叫，而非 grep 原始碼；六個解碼入口都必須呼叫共用 validator。
+    /// 同時禁止控制器自行再呼叫 Image.Load，避免只有掛名呼叫檢查卻繞回全格式解碼器。
+    /// </summary>
+    [Fact]
+    public void Upload_AllCallSites_UseSharedValidator()
+    {
+        var assembly = typeof(MemberInfoController).Assembly;
+        var validator = assembly.GetType("ChurchReport.Services.Images.ImageInputValidator");
+        Assert.NotNull(validator);
+        foreach (var (type, name) in new[]
+        {
+            (typeof(MemberInfoController), "NormalizeUploadedImage"),
+            (typeof(MemberInfoController), "CreateThumbnailIfNeeded"),
+            (typeof(MemberInfoController), "CreateFitThumbnail"),
+            (typeof(PersonalController), "UploadContactImage"),
+            (typeof(PersonalController), "CreateThumbnailIfNeeded"),
+            (typeof(NewPersonController), "TryUploadNewPersonImageAsync")
+        })
+        {
+            var root = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
+            var calls = CalledMethods(ExecutableBody(root)).ToArray();
+            Assert.Contains(calls, method => method.DeclaringType == validator);
+            Assert.DoesNotContain(calls, method => method.DeclaringType?.Namespace == "SixLabors.ImageSharp"
+                && method.DeclaringType.Name == "Image" && method.Name.StartsWith("Load", StringComparison.Ordinal));
+        }
+    }
+
+    /// <summary>
+    /// 合法、拒絕與非 seekable 上傳都必須關閉自己開啟的 stream；同時跑 A/B 合成輸入，
+    /// 防止 static 保存當次圖檔或重用別人的輸出。樣本釋放後計數必須回到零。
+    /// </summary>
+    [Theory]
+    [InlineData("member")][InlineData("personal")][InlineData("new-attached")][InlineData("new-direct")]
+    public async Task Upload_ConcurrentInputs_DrainStreamsAndKeepResultsIsolated(string route)
+    {
+        var valid = ImageFixture.CreateSample("png");
+        var invalid = ImageFixture.CreateSample("webp");
+        var tasks = Enumerable.Range(0, 12).Select(async index =>
+        {
+            var allowed = index % 2 == 0;
+            var bytes = allowed ? valid : invalid;
+            var file = new TrackedImageFile(bytes, seekable: index % 3 != 0);
+            var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
+            Assert.Equal(allowed, attempt.Success);
+            if (allowed) Assert.NotNull(attempt.Bytes); else Assert.Null(attempt.Bytes);
+            Assert.Equal(0, file.ActiveStreams);
+            Assert.True(file.OpenCount > 0);
+        });
+        await Task.WhenAll(tasks);
+    }
+
+    /// <summary>
+    /// SDK 故障是非預期失敗；公開 action 要回原有失敗 schema 並經共用 owner 留一筆診斷。
+    /// 注入的敏感假訊息不可進 JSON 或日誌；owner、registration 與暫存目錄確定性清理。
+    /// </summary>
+    [Theory]
+    [InlineData("personal")][InlineData("new-direct")]
+    public async Task Upload_UnexpectedFailure_ReportsOriginalExceptionWithoutPrivateMessage(string route)
+    {
+        var directory = Path.Combine(Path.GetTempPath(), "speechmessage-image-" + Guid.NewGuid().ToString("N"));
+        try
+        {
+            await using (var diagnostics = new ExceptionDiagnostics(directory))
+            {
+                // 只啟動記憶體假 sender，避免把「未啟動 consumer」狀態混成第二筆失敗事件。
+                diagnostics.StartNotifications((_, _) => Task.CompletedTask);
+                using var registration = ExceptionReporting.Attach(diagnostics);
+                var file = new TrackedImageFile(ImageFixture.CreateSample("jpeg"));
+                var attempt = await ImageFixture.AttemptUpload(route, file.Bytes,
+                    new InvalidOperationException("synthetic-private-value"), file);
+                Assert.False(attempt.Success);
+                Assert.Null(attempt.Bytes);
+                Assert.DoesNotContain("synthetic-private-value", attempt.Message ?? string.Empty);
+                Assert.Equal(0, file.ActiveStreams);
+            }
+            var record = Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
+            Assert.Contains("System.InvalidOperationException", record);
+            Assert.DoesNotContain("synthetic-private-value", record);
+        }
+        finally
+        {
+            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
+        }
+    }
+
+    /// <summary>使用真實診斷 owner 證明預期拒絕不誤報，並觀察 action／sink 確實沒有輸出可寫入的照片。</summary>
+    private static async Task AssertRejected(string route, byte[] bytes, string? messagePart)
+    {
+        var directory = Path.Combine(Path.GetTempPath(), "speechmessage-image-" + Guid.NewGuid().ToString("N"));
+        try
+        {
+            await using (var diagnostics = new ExceptionDiagnostics(directory))
+            {
+                // 只啟動記憶體假 sender，避免把「未啟動 consumer」狀態混成第二筆失敗事件。
+                diagnostics.StartNotifications((_, _) => Task.CompletedTask);
+                using var registration = ExceptionReporting.Attach(diagnostics);
+                var file = new TrackedImageFile(bytes);
+                var attempt = await ImageFixture.AttemptUpload(route, bytes, suppliedFile: file);
+                Assert.False(attempt.Success);
+                Assert.Null(attempt.Bytes);
+                Assert.NotEmpty(attempt.Message ?? string.Empty);
+                if (messagePart != null) Assert.Contains(messagePart, attempt.Message!);
+                Assert.Equal(0, file.ActiveStreams);
+            }
+            Assert.False(File.Exists(Path.Combine(directory, "Exception.log"))
+                && new FileInfo(Path.Combine(directory, "Exception.log")).Length > 0);
+        }
+        finally
+        {
+            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
+        }
+    }
+
+    /// <summary>合成 PNG IHDR 與 IEND，不配置任何大像素陣列；CRC 正確但刻意沒有可完整解碼的 IDAT。</summary>
+    internal static byte[] PngHeader(int width, int height)
+    {
+        var bytes = Convert.FromHexString("[REDACTED_LONG_LITERAL]");
+        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
+        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
+        uint crc = 0xffffffff;
+        foreach (var value in bytes.AsSpan(12, 17))
+        {
+            crc ^= value;
+            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
+        }
+        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);
+        return bytes;
+    }
+
+    /// <summary>async 方法真正執行的內容在狀態機 MoveNext，不能只檢查建立 task 的外殼。</summary>
+    private static MethodInfo ExecutableBody(MethodInfo method) =>
+        method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
+            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? method;
+
+    /// <summary>依 CLR opcode 的 operand 長度解析 IL，避免把常數位元組誤認為 call token。</summary>
+    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
+    {
+        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
+            .Where(field => field.FieldType == typeof(OpCode))
+            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opcode => (ushort)opcode.Value);
+        var il = method.GetMethodBody()!.GetILAsByteArray()!;
+        for (var position = 0; position < il.Length;)
+        {
+            ushort value = il[position++];
+            if (value == 0xfe) value = (ushort)(0xfe00 | il[position++]);
+            var operand = opcodes[value].OperandType;
+            if (operand == OperandType.InlineMethod)
+                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, position))!;
+            position += operand switch
+            {
+                OperandType.InlineNone => 0,
+                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
+                OperandType.InlineVar => 2,
+                OperandType.InlineI8 or OperandType.InlineR => 8,
+                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
+                _ => 4
+            };
+        }
+    }
+}
+
+/// <summary>只提供有界測試內容的不可 seek 串流；讀取計數以實際 bytes 為準，不提供偽造長度。</summary>
+internal sealed class CountedNonSeekableStream(byte[] bytes) : MemoryStream(bytes, writable: false)
+{
+    /// <summary>不暴露可重設位置能力，強迫產品走有界副本。</summary>
+    public override bool CanSeek => false;
+    /// <summary>由底層實際讀取位置計數，不由待測程式的常數推導結果。</summary>
+    internal long ReadBytes => base.Position;
+    /// <summary>模擬網路來源不支援長度。</summary>
+    public override long Length => throw new NotSupportedException();
+    /// <summary>模擬網路來源不可重設位置。</summary>
+    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
+}
+
+/// <summary>每次 OpenReadStream 交出不同的單一 owner 串流，借用原始假資料但不保存使用者狀態。</summary>
+internal sealed class TrackedImageFile(byte[] bytes, bool seekable = true) : IFormFile
+{
+    /// <summary>測試自有的合成資料，沒有真實照片或個資。</summary>
+    internal byte[] Bytes => bytes;
+    /// <summary>歸零代表所有產品開啟的串流已確定釋放。</summary>
+    internal int ActiveStreams { get; private set; }
+    /// <summary>確保測試確實讀取影像而非在無關的前置條件失敗。</summary>
+    internal int OpenCount { get; private set; }
+    /// <summary>刻意使用允許的宣告型態來驗證實際內容。</summary>
+    public string ContentType => "image/jpeg";
+    /// <summary>不讀取真實 HTTP 標頭。</summary>
+    public string ContentDisposition => string.Empty;
+    /// <summary>只有本測試物件擁有的空標頭。</summary>
+    public IHeaderDictionary Headers { get; } = new HeaderDictionary();
+    /// <summary>合成內容的實際長度。</summary>
+    public long Length => bytes.Length;
+    /// <summary>固定上傳欄位名稱。</summary>
+    public string Name => "imageFile";
+    /// <summary>所有實際格式都偽裝成 JPG，不能以宣告代替驗證。</summary>
+    public string FileName => "synthetic.jpg";
+    /// <summary>釋放由呼叫端負責；計數僅存在本測試實例，不進 static。</summary>
+    public Stream OpenReadStream()
+    {
+        OpenCount++; ActiveStreams++;
+        return new TrackedStream(bytes, seekable, () => ActiveStreams--);
+    }
+    /// <summary>僅為 IFormFile 相容入口，同樣確定性釋放來源。</summary>
+    public void CopyTo(Stream target) { using var stream = OpenReadStream(); stream.CopyTo(target); }
+    /// <summary>非同步相容入口不建立背景工作，取消由呼叫端傳入。</summary>
+    public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
+    {
+        using var stream = OpenReadStream();
+        await stream.CopyToAsync(target, cancellationToken);
+    }
+
+    /// <summary>借用測試陣列並記錄 Dispose；非 seekable 故障注入驗證產品有界暫存後再辨識。</summary>
+    private sealed class TrackedStream(byte[] content, bool canSeek, Action closed) : MemoryStream(content, writable: false)
+    {
+        private bool disposed;
+        /// <summary>故意模擬無法重設位置的表單串流。</summary>
+        public override bool CanSeek => canSeek;
+        /// <summary>不容許以 Position 繞過 CanSeek=false 的宣告。</summary>
+        public override long Position
+        {
+            get => canSeek ? base.Position : throw new NotSupportedException();
+            set { if (!canSeek) throw new NotSupportedException(); base.Position = value; }
+        }
+        /// <summary>回報恰一次釋放；底層陣列由測試案例擁有。</summary>
+        protected override void Dispose(bool disposing)
+        {
+            if (!disposed) { disposed = true; closed(); }
+            base.Dispose(disposing);
+        }
+    }
+}

diff --git a/Directory.Packages.props b/Directory.Packages.props
new file mode 100644
index 00000000..5e0c5e3c
--- /dev/null
+++ b/Directory.Packages.props
@@ -0,0 +1,73 @@
+<Project>
+  <!-- 中央套件版本僅管理實際程式專案，包含方案外工具與尚未移除的孤兒專案。
+       docs 與 CCG 任務封存是歷史參考快照，明確維持非 CPM，避免其原始版本失效。
+       目錄經 MSBuild 正規化並保留尾端分隔符號，避免誤排除 docs-like 等相近名稱。 -->
+  <PropertyGroup>
+    <_SpeechMessagePackageProjectDirectory>$([MSBuild]::NormalizeDirectory('$(MSBuildProjectDirectory)'))</_SpeechMessagePackageProjectDirectory>
+    <_SpeechMessagePackageDocsDirectory>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', 'docs'))</_SpeechMessagePackageDocsDirectory>
+    <_SpeechMessagePackageArchiveDirectory>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', '.ccg', 'tasks'))</_SpeechMessagePackageArchiveDirectory>
+    <_SpeechMessagePackageSnapshot Condition="$([System.String]::Copy('$(_SpeechMessagePackageProjectDirectory)').StartsWith('$(_SpeechMessagePackageDocsDirectory)', System.StringComparison.OrdinalIgnoreCase)) Or $([System.String]::Copy('$(_SpeechMessagePackageProjectDirectory)').StartsWith('$(_SpeechMessagePackageArchiveDirectory)', System.StringComparison.OrdinalIgnoreCase))">true</_SpeechMessagePackageSnapshot>
+    <ManagePackageVersionsCentrally Condition="'$(_SpeechMessagePackageSnapshot)' != 'true'">true</ManagePackageVersionsCentrally>
+    <ManagePackageVersionsCentrally Condition="'$(_SpeechMessagePackageSnapshot)' == 'true'">false</ManagePackageVersionsCentrally>
+  </PropertyGroup>
+
+  <!-- Extensions 與 Newtonsoft.Json 依核定規則統一；其他有效套件維持現有版本。
+       預留 net48、CRM、合併與分析器套件；此表不會自行啟用任何依賴。
+       ChurchReport 註解內的 ASP.NET Core 2.2 套件維持停用，不列入中央版本。 -->
+  <ItemGroup Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
+    <PackageVersion Include="coverlet.collector" Version="6.0.0" />
+    <PackageVersion Include="coverlet.msbuild" Version="6.0.0" />
+    <PackageVersion Include="DevExtreme.AspNet.Core" Version="23.1.5" />
+    <PackageVersion Include="DevExtreme.AspNet.Data" Version="5.1.0" />
+    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
+    <PackageVersion Include="ILRepack.Lib.MSBuild.Task" Version="2.0.48" />
+    <PackageVersion Include="Microsoft.AspNet.WebApi.Client" Version="6.0.0" />
+    <PackageVersion Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" />
+    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" />
+    <PackageVersion Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.56" />
+    <PackageVersion Include="Microsoft.CrmSdk.Workflow" Version="9.0.2.56" />
+    <PackageVersion Include="Microsoft.Extensions.Caching.Abstractions" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Extensions.Primitives" Version="10.0.0" />
+    <PackageVersion Include="Microsoft.Identity.Client" Version="4.87.0" />
+    <PackageVersion Include="Microsoft.Identity.Client.Extensions.Msal" Version="4.87.0" />
+    <PackageVersion Include="Microsoft.IdentityModel.Protocols.WsTrust" Version="6.8.0" />
+    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
+    <PackageVersion Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" />
+    <PackageVersion Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.32" />
+    <PackageVersion Include="Moq" Version="4.20.70" />
+    <PackageVersion Include="Newtonsoft.Json" Version="13.0.4" />
+    <PackageVersion Include="RestSharp" Version="112.1.0" />
+    <PackageVersion Include="SixLabors.ImageSharp" Version="3.2.0" />
+    <PackageVersion Include="System.ComponentModel.Annotations" Version="5.0.0" />
+    <PackageVersion Include="System.DirectoryServices" Version="9.0.0" />
+    <PackageVersion Include="System.DirectoryServices.AccountManagement" Version="9.0.0" />
+    <PackageVersion Include="System.Drawing.Common" Version="9.0.0" />
+    <PackageVersion Include="System.IO.FileSystem.AccessControl" Version="5.0.0" />
+    <PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />
+    <PackageVersion Include="System.Security.Cryptography.ProtectedData" Version="10.0.11" />
+    <PackageVersion Include="System.Security.Cryptography.Xml" Version="10.0.14" />
+    <PackageVersion Include="System.Security.Principal.Windows" Version="4.5.1" />
+    <PackageVersion Include="System.ServiceModel.Federation" Version="10.0.652802" />
+    <PackageVersion Include="System.ServiceModel.Http" Version="10.0.652802" />
+    <PackageVersion Include="System.ServiceModel.Primitives" Version="10.0.652802" />
+    <PackageVersion Include="System.Text.Encoding.CodePages" Version="9.0.0" />
+    <PackageVersion Include="System.Text.Json" Version="6.0.11" />
+    <PackageVersion Include="xunit" Version="2.6.6" />
+    <PackageVersion Include="xunit.runner.visualstudio" Version="2.5.6" />
+  </ItemGroup>
+</Project>

diff --git a/PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj b/PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj
index 89346fc0..2292ffb6 100644
--- a/PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj
+++ b/PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj
@@ -1,4 +1,4 @@
-﻿<Project Sdk="Microsoft.NET.Sdk">
+<Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
@@ -46,12 +46,14 @@ void CreateRecord(IOrganizationService svc)
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Microsoft.IdentityModel.Protocols.WsTrust" Version="6.8.0" />
-    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.1.32" />
-    <PackageReference Include="System.Security.Cryptography.Xml" Version="10.0.11" />
-    <PackageReference Include="System.ServiceModel.Federation" Version="10.0.652802" />
-    <PackageReference Include="System.ServiceModel.Http" Version="10.0.652802" />
-    <PackageReference Include="System.ServiceModel.Primitives" Version="10.0.652802" />
+    <PackageReference Include="Microsoft.IdentityModel.Protocols.WsTrust" />
+    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
+    <!-- WCF／WS-Trust 的傳遞相依曾帶入有弱點的舊版；直接參考由中央版本表升級的安全修補版。 -->
+    <PackageReference Include="System.Security.Cryptography.Pkcs" />
+    <PackageReference Include="System.Security.Cryptography.Xml" />
+    <PackageReference Include="System.ServiceModel.Federation" />
+    <PackageReference Include="System.ServiceModel.Http" />
+    <PackageReference Include="System.ServiceModel.Primitives" />
   </ItemGroup>
 
   <ItemGroup>

diff --git a/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs b/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
index e7c8462e..a0c7c9c3 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
@@ -26,6 +26,7 @@ using Microsoft.Extensions.Caching.Memory;
 using Microsoft.Xrm.Sdk;
 using Microsoft.Xrm.Sdk.Query;
 using SixLabors.ImageSharp;
+using ChurchReport.Services.Images;
 using SixLabors.ImageSharp.Formats.Jpeg;
 using SixLabors.ImageSharp.Processing;
 using System;
@@ -1050,6 +1051,7 @@ namespace ChurchReport.Controllers
         /// 上傳並更新「指定會友」的大頭照（會友細節彈窗用）。
         /// 與 /Personal/UploadContactImage(只改登入者自己)不同：這裡可改「其他會友」，
         /// 因此先用 CanViewContact 把關——能在清單看到/開啟該會友細節的人才可變更其照片。
+        /// 格式或尺寸不符是預期拒絕；其他失敗經共用入口上報，JSON 不洩漏原始例外訊息。
         /// POST: /MemberInfo/UploadContactImage
         /// </summary>
         [HttpPost]
@@ -1104,13 +1106,19 @@ namespace ChurchReport.Controllers
                     imageVersion = DateTime.Now.Ticks
                 });
             }
+            catch (ImageInputRejectedException rejected)
+            {
+                return Json(new { success = false, message = rejected.Message });
+            }
             catch (System.ServiceModel.FaultException faultEx)
             {
-                return Json(new { success = false, message = "CRM 更新失敗：" + faultEx.Message });
+                ChurchReportLineAdminNotificationService.ReportException("MemberInfo.UploadContactImage", faultEx);
+                return Json(new { success = false, message = "CRM 更新失敗，請稍後再試" });
             }
             catch (Exception ex)
             {
-                return Json(new { success = false, message = "上傳失敗：" + ex.Message });
+                ChurchReportLineAdminNotificationService.ReportException("MemberInfo.UploadContactImage", ex);
+                return Json(new { success = false, message = "上傳失敗，請稍後再試" });
             }
         }
 
@@ -1185,47 +1193,27 @@ namespace ChurchReport.Controllers
         }
 
         /// <summary>
-        /// 讀取上傳圖片並依 EXIF 自動轉正(解決手機直拍旋轉)，再「置中補邊成正方形」(補白、不裁切)，轉存 JPEG。
-        /// 補正方形的原因：CRM 的 entityimage 以正方形顯示大頭照，直接存直式照片會被裁掉頭頂與下方；
-        /// 先補成正方形後，存進 CRM 不會切到上下，完整人像在 CRM、清單、會友細節都看得到。處理失敗時退回原始位元組。
+        /// 以共用限定格式與尺寸守門讀取上傳，再依 EXIF 轉正、補白為正方形及 JPEG90。
+        /// 每次呼叫獨占 input、Image 與 output 並在回傳前釋放；只回傳新陣列，不保存於 static。
+        /// 解碼拒絕必須交回 action，不再回退保存未驗證的原始內容。
         /// </summary>
         private static byte[] NormalizeUploadedImage(IFormFile imageFile)
         {
-            byte[] raw;
-            using (var read = imageFile.OpenReadStream())
-            using (var buffer = new MemoryStream())
-            {
-                read.CopyTo(buffer);
-                raw = buffer.ToArray();
-            }
-
-            try
+            using var input = imageFile.OpenReadStream();
+            using var image = ImageInputValidator.Load(input);
+            image.Mutate(x => x.AutoOrient());
+            // 保留會友照片補白而非裁切的契約，防止 CRM 正方形顯示裁掉人像。
+            if (image.Width != image.Height)
             {
-                using var input = new MemoryStream(raw);
-                using var image = Image.Load(input);
-                image.Mutate(x => x.AutoOrient());
-
-                // 置中補邊成正方形(補白、完全不裁切)，避免 CRM 正方形大頭照裁掉頭頂/下方。
-                if (image.Width != image.Height)
+                var side = Math.Max(image.Width, image.Height);
+                image.Mutate(x => x.Resize(new ResizeOptions
                 {
-                    var side = Math.Max(image.Width, image.Height);
-                    image.Mutate(x => x.Resize(new ResizeOptions
-                    {
-                        Size = new Size(side, side),
-                        Mode = ResizeMode.BoxPad,
-                        PadColor = Color.White
-                    }));
-                }
-
-                using var output = new MemoryStream();
-                image.Save(output, new JpegEncoder { Quality = 90 });
-                return output.ToArray();
-            }
-            catch
-            {
-                // 影像處理失敗 → 至少存原始檔，不讓上傳整個失敗。
-                return raw;
+                    Size = new Size(side, side), Mode = ResizeMode.BoxPad, PadColor = Color.White
+                }));
             }
+            using var output = new MemoryStream();
+            image.Save(output, new JpegEncoder { Quality = 90 });
+            return output.ToArray();
         }
 
         /// <summary>會友照片更新後，清掉本控制器各尺寸的影像快取(全尺寸＋常用縮圖)。</summary>
@@ -3332,12 +3320,13 @@ namespace ChurchReport.Controllers
             return entity[alias]?.ToString() ?? string.Empty;
         }
 
+        /// <summary>以共用守門解碼，裁切 JPEG82；來源與影像由當次 using 擁有，拒絕時回原陣列，不讓頁面拋錯。</summary>
         private static byte[] CreateThumbnailIfNeeded(byte[] originalBytes, int size)
         {
             try
             {
                 using var input = new MemoryStream(originalBytes);
-                using var image = Image.Load(input);
+                using var image = ImageInputValidator.Load(input);
 
                 if (image.Width <= size && image.Height <= size)
                 {
@@ -3355,19 +3344,24 @@ namespace ChurchReport.Controllers
                 image.Save(output, new JpegEncoder { Quality = 82 });
                 return output.ToArray();
             }
-            catch
+            catch (ImageInputRejectedException)
             {
                 return originalBytes;
             }
+            catch (Exception exception)
+            {
+                ChurchReportLineAdminNotificationService.ReportException("MemberInfo.Thumbnail", exception);
+                return originalBytes;
+            }
         }
 
-        /// <summary>等比縮放使整張照片「完整塞入」size×size(不裁切，保留完整頭部)；原圖較小則回原圖。供會友細節大圖使用。</summary>
+        /// <summary>共用守門後等比縮放為 JPEG85；原圖較小或輸入被拒絕時回原陣列。所有解碼資源在當次 using 釋放，不改既有 EXIF 行為。</summary>
         private static byte[] CreateFitThumbnail(byte[] originalBytes, int size)
         {
             try
             {
                 using var input = new MemoryStream(originalBytes);
-                using var image = Image.Load(input);
+                using var image = ImageInputValidator.Load(input);
 
                 if (image.Width <= size && image.Height <= size)
                 {
@@ -3384,8 +3378,13 @@ namespace ChurchReport.Controllers
                 image.Save(output, new JpegEncoder { Quality = 85 });
                 return output.ToArray();
             }
-            catch
+            catch (ImageInputRejectedException)
+            {
+                return originalBytes;
+            }
+            catch (Exception exception)
             {
+                ChurchReportLineAdminNotificationService.ReportException("MemberInfo.Thumbnail", exception);
                 return originalBytes;
             }
         }

diff --git a/SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs b/SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs
index a0ca736c..f79c9ca9 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs
@@ -17,6 +17,8 @@
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
 using ChurchReport.Models;
+using ChurchReport.Services;
+using ChurchReport.Services.Images;
 using ChurchReport.Tools;
 using ChurchReport.ViewModels;
 using DevExtreme.AspNet.Data;
@@ -352,7 +354,7 @@ namespace ChurchReport.Controllers
         }
 
         /// <summary>
-        /// 儲存新增的新人資料
+        /// 儲存新人；附圖拒絕只追加固定提示，不回滾已建立的新人或誘發重送。
         /// </summary>
         /// <param name="aPersonFormViewModel">新人表單 ViewModel</param>
         [HttpPost]
@@ -374,13 +376,31 @@ namespace ChurchReport.Controllers
                     // 新增成功後的處理
                     HandleSuccessfulNewPersonCreation(aPersonFormViewModel);
 
-                    // 嘗試上傳新人照片（若有選擇檔案）
-                    await TryUploadNewPersonImageAsync(imageFile);
+                    // 新人已建立，照片拒絕不能改回建立失敗，避免使用者重送造成重複新人。
+                    // 只有未儲存的照片被拒絕；資料建立結果維持成功，並在原訊息欄位提示。
+                    var imageWarning = string.Empty;
+                    try
+                    {
+                        await TryUploadNewPersonImageAsync(imageFile);
+                    }
+                    catch (ImageInputRejectedException rejected)
+                    {
+                        imageWarning = ImageInputText.AttachmentRejected(rejected);
+                    }
+                    catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
+                    {
+                        throw;
+                    }
+                    catch (Exception exception)
+                    {
+                        ChurchReportLineAdminNotificationService.ReportException("NewPerson.SaveNewPersonImage", exception);
+                        imageWarning = ImageInputText.GetText("Image.Upload.AttachmentFailed");
+                    }
 
                     // 重設表單
                     ResetNewPersonForm();
 
-                    return Json(new { status = "1", message = result });
+                    return Json(new { status = "1", message = result + imageWarning });
                 }
                 else
                 {
@@ -390,6 +410,11 @@ namespace ChurchReport.Controllers
                     return Json(new { status = "2", message = result });
                 }
             }
+            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
+            {
+                // 已明確取消的 caller 不進共用失敗通知，保留正常取消語意。
+                throw;
+            }
             catch (Exception e)
             {
                 return HandleError(e, "SaveNewPerson");
@@ -397,8 +422,10 @@ namespace ChurchReport.Controllers
         }
 
         /// <summary>
-        /// 上傳新人照片（由前端在新增成功後呼叫）
+        /// 新人建立後獨立上傳照片，維持 success／message 回應。
+        /// 內容拒絕不通知；其他故障傳真實 Exception 至共用入口，固定回應不洩漏解析或連線訊息。
         /// </summary>
+        /// <param name="imageFile">當次請求的未受信任圖片。</param>
         [HttpPost]
         [Route("/NewPerson/UploadNewPersonImage")]
         public async Task<IActionResult> UploadNewPersonImage(IFormFile imageFile)
@@ -406,125 +433,52 @@ namespace ChurchReport.Controllers
             try
             {
                 await TryUploadNewPersonImageAsync(imageFile);
-
-                return Json(new
-                {
-                    success = true,
-                    message = "大頭照上傳成功"
-                });
+                return Json(new { success = true, message = "大頭照上傳成功" });
             }
-            catch (Exception ex)
+            catch (ImageInputRejectedException rejected)
             {
-                return Json(new
-                {
-                    success = false,
-                    message = $"大頭照上傳失敗: {ex.Message}"
-                });
+                return Json(new { success = false, message = rejected.Message });
+            }
+            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
+            {
+                throw;
+            }
+            catch (Exception exception)
+            {
+                ChurchReportLineAdminNotificationService.ReportException("NewPerson.UploadNewPersonImage", exception);
+                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.PhotoFailed") });
             }
         }
 
         /// <summary>
-        /// 上傳新人照片到新人記錄
-        /// ✅ 支援 EXIF Orientation 自動修正（解決直拍照片旋轉問題）
+        /// 新人兩個上傳入口共用的照片處理；保留既有宣告白名單、5 MiB 限制與無檔略過行為。
+        /// 限定解碼、EXIF 轉正後才更新 entityimage，不再於解碼失敗時保存原檔。
+        /// 每次呼叫獨占並釋放所有串流與 Image，SDK 服務只借用目前 request scope；
+        /// 不保存影像、Session 或 credential 至 static，拒絕及其他例外交由各 action 的最終邊界處理。
         /// </summary>
         private async Task TryUploadNewPersonImageAsync(IFormFile imageFile)
         {
-            if (imageFile == null || imageFile.Length == 0)
-            {
-                return;
-            }
-
-            const long maxFileSize = 5 * 1024 * 1024; // 5MB
-            if (imageFile.Length > maxFileSize)
-            {
-                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 檔案過大");
-                return;
-            }
-
+            if (imageFile == null || imageFile.Length == 0) return;
+            if (imageFile.Length > 5 * 1024 * 1024) return;
             var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
             var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
             var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
             var contentType = imageFile.ContentType.ToLowerInvariant();
-
-            if (!Array.Exists(allowedExtensions, ext => ext == fileExtension) ||
-                !Array.Exists(allowedContentTypes, type => type == contentType))
-            {
-                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 檔案格式錯誤");
-                return;
-            }
-
-            if (!Guid.TryParse(InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId, out var contactId))
-            {
-                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 新人 ContactId 無效");
-                return;
-            }
-
-            try
-            {
-                // ========================================
-                // ✅ 修正 EXIF Orientation（解決直拍照片旋轉問題）
-                // ========================================
-                byte[] imageBytes;
-                using (var inputStream = imageFile.OpenReadStream())
-                using (var outputStream = new MemoryStream())
-                {
-                    try
-                    {
-                        // 使用 ImageSharp 載入並修正圖片方向
-                        using (var image = await Image.LoadAsync(inputStream))
-                        {
-                            System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 📐 原始圖片尺寸: {image.Width}x{image.Height}");
-
-                            // 檢查 EXIF Orientation
-                            var exifProfile = image.Metadata.ExifProfile;
-                            if (exifProfile != null && exifProfile.TryGetValue(ExifTag.Orientation, out IExifValue<ushort> orientationValue))
-                            {
-                                System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 📱 EXIF Orientation: {orientationValue.Value}");
-                            }
-
-                            // ✅ 自動修正旋轉（根據 EXIF 資訊）
-                            image.Mutate(x => x.AutoOrient());
-
-                            // 移除 EXIF Orientation 標記（避免重複旋轉）
-                            if (image.Metadata.ExifProfile != null)
-                            {
-                                image.Metadata.ExifProfile.RemoveValue(ExifTag.Orientation);
-                            }
-
-                            System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ✅ EXIF 方向已修正");
-
-                            // 儲存為高品質 JPEG
-                            var encoder = new JpegEncoder { Quality = 90 };
-                            await image.SaveAsync(outputStream, encoder);
-                        }
-
-                        imageBytes = outputStream.ToArray();
-                        System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ✅ 圖片已處理: {imageBytes.Length} bytes");
-                    }
-                    catch (Exception imageProcessEx)
-                    {
-                        System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ⚠️ ImageSharp 處理失敗，使用原始檔案: {imageProcessEx.Message}");
-
-                        // 降級方案：如果 ImageSharp 處理失敗，使用原始檔案
-                        inputStream.Position = 0;
-                        using (var fallbackStream = new MemoryStream())
-                        {
-                            imageFile.CopyTo(fallbackStream);
-                            imageBytes = fallbackStream.ToArray();
-                        }
-                    }
-                }
-
-                var service = OrganizationService;
-
-                var contactToUpdate = new Entity("contact", contactId);
-                contactToUpdate["entityimage"] = imageBytes;
-                service.Update(contactToUpdate);
-            }
-            catch (Exception ex)
+            if (!Array.Exists(allowedExtensions, ext => ext == fileExtension)
+                || !Array.Exists(allowedContentTypes, type => type == contentType)) return;
+            if (!Guid.TryParse(InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId, out var contactId)) return;
+            byte[] imageBytes;
+            using (var input = imageFile.OpenReadStream())
+            using (var image = await ImageInputValidator.LoadAsync(input, HttpContext.RequestAborted))
+            using (var output = new MemoryStream())
             {
-                System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 圖片上傳失敗: {ex.Message}");
+                image.Mutate(x => x.AutoOrient());
+                image.Metadata.ExifProfile?.RemoveValue(ExifTag.Orientation);
+                await image.SaveAsync(output, new JpegEncoder { Quality = 90 }, HttpContext.RequestAborted);
+                imageBytes = output.ToArray();
             }
+            var contactToUpdate = new Entity("contact", contactId) { ["entityimage"] = imageBytes };
+            OrganizationService.Update(contactToUpdate);
         }
 
         /// <summary>

diff --git a/SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs b/SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs
index 10727cbd..d47d7f3d 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs
@@ -12,6 +12,8 @@
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
 using ChurchReport.Models;
+using ChurchReport.Services;
+using ChurchReport.Services.Images;
 
 using ChurchReport.Tools;
 
@@ -83,410 +85,85 @@ namespace ChurchReport.Controllers
 
 
         /// <summary>
-
-        /// 上傳並更新 Contact 大頭照
-
-        /// POST: /Personal/UploadContactImage
-
+        /// 僅更新目前 Session 登入 Contact 的照片，保留既有宣告白名單與 5 MiB 限制。
+        /// 內容必經共用格式與尺寸守門；EXIF 轉正後輸出 JPEG90，不再退回儲存原始檔。
+        /// input、image、output 只屬當次請求並確定性釋放，不保留姓名、圖片或服務至 static。
+        /// 預期輸入拒絕不通知；其他失敗經共用入口上報，回應不包含底層例外訊息。
         /// </summary>
-
-        /// <param name="imageFile">上傳的圖片檔案</param>
-
-        /// <returns>JSON 結果：成功或失敗訊息</returns>
-
+        /// <param name="imageFile">未受信任的上傳圖片。</param>
+        /// <returns>維持 success／message 與成功時 imageUrl 的既有 JSON schema。</returns>
         [HttpPost]
-
         [Route("/Personal/UploadContactImage")]
-
         public async Task<IActionResult> UploadContactImage(IFormFile imageFile)
-
         {
-
-            
-
             try
-
             {
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ========== 開始上傳大頭照 ==========");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 時間: {DateTime.Now}");
-
-
-
-                // ========================================
-
-                // 步驟 1: 驗證上傳檔案
-
-                // ========================================
-
                 if (imageFile == null || imageFile.Length == 0)
-
-                {
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 未選擇檔案");
-
-                    return Json(new
-
-                    {
-
-                        success = false,
-
-                        message = "請選擇要上傳的圖片檔案"
-
-                    });
-
-                }
-
-
-
-                // 檢查檔案大小（限制 5MB）
-
-                const long maxFileSize = 5 * 1024 * 1024; // 5MB
-
-                if (imageFile.Length > maxFileSize)
-
-                {
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 檔案過大: {imageFile.Length} bytes");
-
-                    return Json(new
-
-                    {
-
-                        success = false,
-
-                        message = "圖片檔案過大，請選擇小於 5MB 的圖片"
-
-                    });
-
-                }
-
-
-
-                // 檢查檔案類型
-
+                    return Json(new { success = false, message = "請選擇要上傳的圖片檔案" });
+                if (imageFile.Length > 5 * 1024 * 1024)
+                    return Json(new { success = false, message = "圖片檔案過大，請選擇小於 5MB 的圖片" });
                 var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
-
                 var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
-
                 var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
-
                 var contentType = imageFile.ContentType.ToLowerInvariant();
-
-
-
-                if (!Array.Exists(allowedExtensions, ext => ext == fileExtension) ||
-
-                    !Array.Exists(allowedContentTypes, type => type == contentType))
-
-                {
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 不支援的檔案類型: {fileExtension}, {contentType}");
-
-                    return Json(new
-
-                    {
-
-                        success = false,
-
-                        message = "只支援 JPG、PNG、GIF 格式的圖片"
-
-                    });
-
-                }
-
-
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 檔案驗證通過");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案名稱: {imageFile.FileName}");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案大小: {imageFile.Length} bytes");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案類型: {contentType}");
-
-
-
-                // ========================================
-
-                // 步驟 2: 取得當前登入使用者的 Contact
-
-                // ========================================
+                if (!Array.Exists(allowedExtensions, ext => ext == fileExtension)
+                    || !Array.Exists(allowedContentTypes, type => type == contentType))
+                    return Json(new { success = false, message = "只支援 JPG、PNG、GIF 格式的圖片" });
 
                 var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
-
                 if (loginContact == null)
-
-                {
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 無法取得登入使用者資訊");
-
-                    return Json(new
-
-                    {
-
-                        success = false,
-
-                        message = "無法取得登入使用者資訊，請重新登入"
-
-                    });
-
-                }
-
-
-
+                    return Json(new { success = false, message = "無法取得登入使用者資訊，請重新登入" });
                 var contactId = loginContact.Id;
-
-                var fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 使用者: {fullName} (ID: {contactId})");
-
-
-
-                // ========================================
-
-                // 步驟 3: 讀取並修正圖片 EXIF Orientation（解決直拍旋轉問題）
-
-                // ========================================
-
                 byte[] imageBytes;
-
-                using (var inputStream = imageFile.OpenReadStream())
-
-                using (var outputStream = new MemoryStream())
-
+                using (var input = imageFile.OpenReadStream())
+                using (var image = await ImageInputValidator.LoadAsync(input, HttpContext.RequestAborted))
+                using (var output = new MemoryStream())
                 {
-
-                    try
-
-                    {
-
-                        // 使用 ImageSharp 載入圖片
-
-                        using (var image = await Image.LoadAsync(inputStream))
-
-                        {
-
-                            System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 原始圖片尺寸: {image.Width}x{image.Height}");
-
-
-
-                            // 檢查 EXIF Orientation（使用 ImageSharp 3.x API）
-
-                            var exifProfile = image.Metadata.ExifProfile;
-
-                            if (exifProfile != null && exifProfile.TryGetValue(ExifTag.Orientation, out IExifValue<ushort> orientationValue))
-
-                            {
-
-                                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? EXIF Orientation: {orientationValue.Value}");
-
-                            }
-
-                            else
-
-                            {
-
-                                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 無 EXIF Orientation 資訊");
-
-                            }
-
-
-
-                            // ? 自動修正旋轉（根據 EXIF 資訊）
-
-                            image.Mutate(x => x.AutoOrient());
-
-
-
-                            // 移除 EXIF Orientation 標記（避免重複旋轉）
-
-                            if (image.Metadata.ExifProfile != null)
-
-                            {
-
-                                image.Metadata.ExifProfile.RemoveValue(ExifTag.Orientation);
-
-                            }
-
-
-
-                            System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? EXIF 方向已修正，圖片已正規化");
-
-
-
-                            // 儲存為 JPEG（高品質）
-
-                            var encoder = new JpegEncoder
-
-                            {
-
-                                Quality = 90 // 90% 品質（平衡檔案大小與畫質）
-
-                            };
-
-                            await image.SaveAsync(outputStream, encoder);
-
-                        }
-
-
-
-                        imageBytes = outputStream.ToArray();
-
-                        System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 圖片已處理並壓縮: {imageBytes.Length} bytes");
-
-                    }
-
-                    catch (Exception imageProcessEx)
-
-                    {
-
-                        System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? ImageSharp 處理失敗，使用原始檔案: {imageProcessEx.Message}");
-
-
-
-                        // 降級方案：如果 ImageSharp 處理失敗，使用原始檔案
-
-                        inputStream.Position = 0;
-
-                        using (var fallbackStream = new MemoryStream())
-
-                        {
-
-                            await inputStream.CopyToAsync(fallbackStream);
-
-                            imageBytes = fallbackStream.ToArray();
-
-                        }
-
-                    }
-
+                    image.Mutate(x => x.AutoOrient());
+                    image.Metadata.ExifProfile?.RemoveValue(ExifTag.Orientation);
+                    await image.SaveAsync(output, new JpegEncoder { Quality = 90 }, HttpContext.RequestAborted);
+                    imageBytes = output.ToArray();
                 }
-
-
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 最終圖片大小: {imageBytes.Length} bytes");
-
-
-
-                // ========================================
-
-                // 步驟 4: 更新 CRM Contact 的 EntityImage
-
-                // ========================================
-
                 var service = OrganizationService;
-
-
-
-                var contactToUpdate = new Entity("contact", contactId);
-
-                contactToUpdate["entityimage"] = imageBytes;
-
-
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 開始更新 CRM Contact...");
-
+                var contactToUpdate = new Entity("contact", contactId) { ["entityimage"] = imageBytes };
                 service.Update(contactToUpdate);
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? CRM Contact 更新成功");
-
-                // CRM 更新成功後立即清除目前 Contact 的所有個人照片快取。
-                // 快取鍵刻意只由已驗證的 contactId 與尺寸組成，不能把使用者提供的
-                // 任意路徑或 session 值當成快取隔離邊界；清除完整圖與所有支援縮圖後，
-                // 後續 request 才會重新從同一位 Contact 讀取最新影像，避免新圖被舊圖覆蓋。
+                // 先完成 authoritative Update，再清除目前 Contact 的舊照片快取；不發布處理中的影像。
                 var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                 InvalidatePersonalImageCache(memoryCache, contactId);
-
-
-
-                // ========================================
-
-                // 步驟 5: 更新 Session 中的 Contact 資料
-
-                // ========================================
-
                 try
-
                 {
-
-                    // 重新查詢 Contact 以取得更新後的資料
-
                     var updatedContact = service.Retrieve("contact", contactId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
-
                     InMemoryContext.PersonalInfomationModel.m_LoginContact = updatedContact;
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? Session 中的 Contact 已更新");
-
                 }
-
-                catch (Exception ex)
-
+                catch (Exception exception)
                 {
-
-                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 更新 Session 失敗（不影響主要功能）: {ex.Message}");
-
+                    // 照片已更新，不以 Session 刷新失敗誘發使用者重送；仍須上報真實故障。
+                    ChurchReportLineAdminNotificationService.ReportException("Personal.RefreshImageContact", exception);
                 }
-
-
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ========== 大頭照上傳完成 ==========");
-
-
-
                 return Json(new
-
                 {
-
-                    success = true,
-
-                    message = "大頭照上傳成功！",
-
+                    success = true, message = "大頭照上傳成功！",
                     imageUrl = $"/Personal/GetContactImage?contactId={contactId}&timestamp={DateTime.Now.Ticks}"
-
                 });
-
             }
-
-            catch (System.ServiceModel.FaultException faultEx)
-
+            catch (ImageInputRejectedException rejected)
             {
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? CRM 錯誤: {faultEx.Message}");
-
-                return Json(new
-
-                {
-
-                    success = false,
-
-                    message = $"CRM 更新失敗: {faultEx.Message}"
-
-                });
-
+                return Json(new { success = false, message = rejected.Message });
             }
-
-            catch (Exception ex)
-
+            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
             {
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 未預期的錯誤: {ex.Message}");
-
-                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 錯誤堆疊: {ex.StackTrace}");
-
-                return Json(new
-
-                {
-
-                    success = false,
-
-                    message = $"上傳失敗: {ex.Message}"
-
-                });
-
+                throw;
+            }
+            catch (System.ServiceModel.FaultException exception)
+            {
+                ChurchReportLineAdminNotificationService.ReportException("Personal.UploadContactImage", exception);
+                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.UpdateFailed") });
+            }
+            catch (Exception exception)
+            {
+                ChurchReportLineAdminNotificationService.ReportException("Personal.UploadContactImage", exception);
+                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.Failed") });
             }
-
         }
 
         /// <summary>
@@ -622,12 +299,13 @@ namespace ChurchReport.Controllers
             
         }
 
+        /// <summary>限定格式及尺寸後裁切 JPEG82；解碼資源僅屬當次呼叫，拒絕時沿用原陣列回退且不向頁面拋錯。</summary>
         private static byte[] CreateThumbnailIfNeeded(byte[] originalBytes, int size)
         {
             try
             {
                 using var input = new MemoryStream(originalBytes);
-                using var image = Image.Load(input);
+                using var image = ImageInputValidator.Load(input);
 
                 if (image.Width <= size && image.Height <= size)
                 {
@@ -649,9 +327,13 @@ namespace ChurchReport.Controllers
 
                 return output.ToArray();
             }
-            catch
+            catch (ImageInputRejectedException)
+            {
+                return originalBytes;
+            }
+            catch (Exception exception)
             {
-                // 若縮圖處理失敗，回退原始圖避免中斷顯示
+                ChurchReportLineAdminNotificationService.ReportException("Personal.Thumbnail", exception);
                 return originalBytes;
             }
         }

diff --git a/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.cs b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.cs
new file mode 100644
index 00000000..c96361f5
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.cs
@@ -0,0 +1,30 @@
+using System;
+using System.Collections.Frozen;
+
+namespace ChurchReport.Services.Images;
+
+/// <summary>
+/// 圖片信任邊界的固定顯示文字。只複製生成資料的繁中欄位至凍結字典，
+/// 保留現有網站語系，不改變執行緒文化，也不保存請求、教會或使用者的資料。
+/// </summary>
+/// <remarks>
+/// 容量只由編譯時資源決定；不把執行期拒絕原因加入字典。所有公開入口僅供同組件
+/// 使用固定資源鍵，缺鍵直接失敗，避免回傳空訊息而讓守門出現假綠燈。
+/// </remarks>
+internal static class ImageInputText
+{
+    /// <summary>完成固定資料複製後才發布，不公開生成器的可變陣列。</summary>
+    private static readonly FrozenDictionary<string, string> Text =
+        ImageInputTextData.Rows.ToFrozenDictionary(row => row[0]!, row => row[1]!, StringComparer.Ordinal);
+
+    /// <summary>取回不可變文字；鍵只來自程式，不接受使用者輸入。</summary>
+    /// <param name="key">本組件固定的資源鍵。</param>
+    /// <returns>既有繁中介面使用的完整文字。</returns>
+    internal static string GetText(string key) => Text[key];
+
+    /// <summary>已建立新人後僅提示照片未儲存；原因必須來自固定的輸入拒絕例外。</summary>
+    /// <param name="rejection">不含使用者內容或原始解析訊息的固定拒絕。</param>
+    /// <returns>只屬當次回應的說明，永不加入靜態集合。</returns>
+    internal static string AttachmentRejected(ImageInputRejectedException rejection) =>
+        GetText("Image.Upload.AttachmentRejected").Replace("{reason}", rejection.Message, StringComparison.Ordinal);
+}

diff --git a/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.resx b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.resx
new file mode 100644
index 00000000..5f9e5d37
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputText.resx
@@ -0,0 +1,17 @@
+<?xml version="1.0" encoding="utf-8"?>
+<root>
+  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
+  <resheader name="version"><value>2.0</value></resheader>
+  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
+  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
+  <!-- 固定輸入拒絕與故障說明由 I18nCheck 產生資料表；不含檔名、帳密或影像內容。 -->
+  <data name="Image.Input.UnsupportedFormat" xml:space="preserve"><value>圖片內容格式不符，只支援 JPG、PNG、GIF 格式的圖片</value></data>
+  <data name="Image.Input.CorruptContent" xml:space="preserve"><value>圖片內容損壞，請重新選擇圖片</value></data>
+  <data name="Image.Input.InvalidDimensions" xml:space="preserve"><value>圖片尺寸過大，單邊不得超過 8192 像素且總像素不得超過 40,000,000</value></data>
+  <data name="Image.Input.InvalidSize" xml:space="preserve"><value>圖片檔案過大，請選擇小於 5MB 的圖片</value></data>
+  <data name="Image.Upload.Failed" xml:space="preserve"><value>上傳失敗，請稍後再試</value></data>
+  <data name="Image.Upload.UpdateFailed" xml:space="preserve"><value>CRM 更新失敗，請稍後再試</value></data>
+  <data name="Image.Upload.PhotoFailed" xml:space="preserve"><value>大頭照上傳失敗，請稍後再試</value></data>
+  <data name="Image.Upload.AttachmentRejected" xml:space="preserve"><value>（照片未上傳：{reason}）</value></data>
+  <data name="Image.Upload.AttachmentFailed" xml:space="preserve"><value>（照片未上傳，請稍後再試）</value></data>
+</root>

diff --git a/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputValidator.cs b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputValidator.cs
new file mode 100644
index 00000000..4ed4272d
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Services/Images/ImageInputValidator.cs
@@ -0,0 +1,178 @@
+using System;
+using System.IO;
+using System.Threading;
+using System.Threading.Tasks;
+using ImageConfiguration = SixLabors.ImageSharp.Configuration;
+using SixLabors.ImageSharp;
+using SixLabors.ImageSharp.Formats;
+using SixLabors.ImageSharp.Formats.Gif;
+using SixLabors.ImageSharp.Formats.Jpeg;
+using SixLabors.ImageSharp.Formats.Png;
+using SixLabors.ImageSharp.Memory;
+
+namespace ChurchReport.Services.Images;
+
+/// <summary>
+/// ChurchReport 所有上傳及縮圖共用的影像信任邊界。只註冊 JPEG、PNG、GIF，
+/// 先辨識尺寸再解碼，禁止用宣告的副檔名或 Content-Type 代替內容驗證。
+/// </summary>
+/// <remarks>
+/// 設定不對外公開、發布後不再變動，僅包含解碼器與配置策略，不保存請求或影像。
+/// 解碼器不保留跨請求的像素池；每次回傳的 Image 由呼叫端 using 擁有。
+/// 暫存只限非 seekable 輸入，最多 5 MiB，返回前釋放，不建立檔案、背景工作或共用快取。
+/// </remarks>
+internal static class ImageInputValidator
+{
+    private const int MaximumSide = 8192;
+    private const long MaximumPixels = 40_000_000;
+    private const int MaximumEncodedBytes = 5 * 1024 * 1024;
+    private static readonly ImageConfiguration RestrictedConfiguration = CreateConfiguration();
+
+    /// <summary>
+    /// 同步檢查並解碼單張影像。輸入由呼叫端擁有，回傳影像的所有權移交呼叫端；
+    /// 失敗時只有本方法建立的暫存會被釋放，不處置借用的來源串流。
+    /// </summary>
+    /// <param name="input">目前請求自有的影像串流，位置必須在影像開頭。</param>
+    /// <returns>只包含第一影格的已驗證影像，呼叫端必須 Dispose。</returns>
+    /// <exception cref="ImageInputRejectedException">格式、尺寸或內容不符合輸入契約。</exception>
+    internal static Image Load(Stream input)
+    {
+        using var buffer = input.CanSeek ? null : BufferInput(input);
+        var source = buffer ?? input;
+        var start = source.Position;
+        var options = CreateOptions();
+        try
+        {
+            ValidateDimensions(Image.Identify(options, source));
+            source.Position = start;
+            return Image.Load(options, source);
+        }
+        catch (UnknownImageFormatException)
+        {
+            throw ImageInputRejectedException.UnsupportedFormat();
+        }
+        catch (InvalidImageContentException)
+        {
+            throw ImageInputRejectedException.CorruptContent();
+        }
+    }
+
+    /// <summary>
+    /// 非同步版本沿用相同解碼器及尺寸檢查；辨識後重設同一請求的串流位置，
+    /// 不把影像、取消來源或 request 保存到 static。正常 caller 取消原樣往外傳遞。
+    /// </summary>
+    /// <param name="input">呼叫端擁有的來源，最長只存續至當次上傳結束。</param>
+    /// <param name="cancellationToken">HTTP caller 的取消權杖，不能以解碼例外猜測正常取消。</param>
+    /// <returns>由呼叫端確定性釋放的第一影格。</returns>
+    internal static async Task<Image> LoadAsync(Stream input, CancellationToken cancellationToken = default)
+    {
+        using var buffer = input.CanSeek ? null : await BufferInputAsync(input, cancellationToken);
+        var source = buffer ?? input;
+        var start = source.Position;
+        var options = CreateOptions();
+        try
+        {
+            ValidateDimensions(await Image.IdentifyAsync(options, source, cancellationToken));
+            source.Position = start;
+            return await Image.LoadAsync(options, source, cancellationToken);
+        }
+        catch (UnknownImageFormatException)
+        {
+            throw ImageInputRejectedException.UnsupportedFormat();
+        }
+        catch (InvalidImageContentException)
+        {
+            throw ImageInputRejectedException.CorruptContent();
+        }
+    }
+
+    /// <summary>
+    /// 獨立設定不能從 Configuration.Default 複製，否則 TIFF 等非允許格式仍會被註冊。
+    /// 禁用內部像素池，避免診斷或空閒期間持有前一次請求的影像；不修改程序全域設定。
+    /// </summary>
+    private static ImageConfiguration CreateConfiguration() => new(
+        new JpegConfigurationModule(), new PngConfigurationModule(), new GifConfigurationModule())
+    {
+        MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = 0 })
+    };
+
+    /// <summary>
+    /// 每次解碼自有 options。輸出既有契約是單張 JPEG，只需第一影格；
+    /// 限制 GIF／APNG 的影格數，避免低檔案大小但大量影格造成配置放大。
+    /// </summary>
+    private static DecoderOptions CreateOptions() => new() { Configuration = RestrictedConfiguration, MaxFrames = 1 };
+
+    /// <summary>先用 long 計算總像素，避免整數溢位繞過；只讀標頭，不配置完整像素。</summary>
+    private static void ValidateDimensions(ImageInfo info)
+    {
+        if (info.Width <= 0 || info.Height <= 0
+            || info.Width > MaximumSide || info.Height > MaximumSide
+            || (long)info.Width * info.Height > MaximumPixels)
+            throw ImageInputRejectedException.InvalidDimensions();
+    }
+
+    /// <summary>非 seekable 來源只建立有界記憶體副本；讀到上限加一即拒絕，不依賴不可信 Length。</summary>
+    private static MemoryStream BufferInput(Stream input)
+    {
+        var buffer = new MemoryStream();
+        try
+        {
+            var block = new byte[8192];
+            int read;
+            while ((read = input.Read(block, 0, (int)Math.Min(block.Length, MaximumEncodedBytes + 1 - buffer.Length))) > 0)
+            {
+                buffer.Write(block, 0, read);
+                if (buffer.Length > MaximumEncodedBytes) throw ImageInputRejectedException.InvalidSize();
+            }
+            buffer.Position = 0;
+            return buffer;
+        }
+        catch
+        {
+            buffer.Dispose();
+            throw;
+        }
+    }
+
+    /// <summary>異步複製使用相同上限；成功後將 owner 移交外層 using，任何故障先釋放再傳遞。</summary>
+    private static async Task<MemoryStream> BufferInputAsync(Stream input, CancellationToken cancellationToken)
+    {
+        var buffer = new MemoryStream();
+        try
+        {
+            var block = new byte[8192];
+            int read;
+            while ((read = await input.ReadAsync(block.AsMemory(0,
+                       (int)Math.Min(block.Length, MaximumEncodedBytes + 1 - buffer.Length)), cancellationToken)) > 0)
+            {
+                buffer.Write(block, 0, read);
+                if (buffer.Length > MaximumEncodedBytes) throw ImageInputRejectedException.InvalidSize();
+            }
+            buffer.Position = 0;
+            return buffer;
+        }
+        catch
+        {
+            buffer.Dispose();
+            throw;
+        }
+    }
+}
+
+/// <summary>
+/// 可預期的影像輸入拒絕；訊息完全由程式控制，不保留原始解析例外、檔案內容或使用者資料。
+/// 控制器只捕捉此型別作輸入拒絕，其餘例外仍交給既有共用診斷入口。
+/// </summary>
+internal sealed class ImageInputRejectedException : Exception
+{
+    /// <summary>僅接受本型別內部固定訊息，避免任意使用者內容進入 JSON。</summary>
+    private ImageInputRejectedException(string message) : base(message) { }
+    /// <summary>辨識不到允許格式，包括偽裝成 JPG 的 WebP、TIFF 與其他格式。</summary>
+    internal static ImageInputRejectedException UnsupportedFormat() => new(ImageInputText.GetText("Image.Input.UnsupportedFormat"));
+    /// <summary>辨識到格式但內容結構損壞，不外送底層解析訊息。</summary>
+    internal static ImageInputRejectedException CorruptContent() => new(ImageInputText.GetText("Image.Input.CorruptContent"));
+    /// <summary>標頭超過核定尺寸上限時先拒絕，不配置像素。</summary>
+    internal static ImageInputRejectedException InvalidDimensions() => new(ImageInputText.GetText("Image.Input.InvalidDimensions"));
+    /// <summary>無法 seek 的實際輸入超過既有 5 MiB 上限。</summary>
+    internal static ImageInputRejectedException InvalidSize() => new(ImageInputText.GetText("Image.Input.InvalidSize"));
+}

diff --git a/ToolUtility/ToolUtility.csproj b/ToolUtility/ToolUtility.csproj
index 08efa963..b694c6b5 100644
--- a/ToolUtility/ToolUtility.csproj
+++ b/ToolUtility/ToolUtility.csproj
@@ -14,38 +14,38 @@
     <!-- Microsoft Power Platform Dataverse Client -->
     
     <!-- Microsoft Extensions -->
-    <PackageReference Include="Microsoft.Extensions.Caching.Abstractions" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Primitives" Version="9.0.0" />
+    <PackageReference Include="Microsoft.Extensions.Caching.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
+    <PackageReference Include="Microsoft.Extensions.Configuration" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
+    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
+    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Http" />
+    <PackageReference Include="Microsoft.Extensions.Logging" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Options" />
+    <PackageReference Include="Microsoft.Extensions.Primitives" />
     
     <!-- Microsoft Identity -->
-    <PackageReference Include="Microsoft.Identity.Client" Version="4.87.0" />
-    <PackageReference Include="Microsoft.Identity.Client.Extensions.Msal" Version="4.87.0" />
+    <PackageReference Include="Microsoft.Identity.Client" />
+    <PackageReference Include="Microsoft.Identity.Client.Extensions.Msal" />
     
     <!-- JSON -->
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
+    <PackageReference Include="Newtonsoft.Json" />
     
     <!-- System Libraries -->
-    <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
-    <PackageReference Include="System.IO.FileSystem.AccessControl" Version="5.0.0" />
-    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.11" />
-    <PackageReference Include="System.ServiceModel.Http" Version="10.0.652802" />
-    <PackageReference Include="System.ServiceModel.Primitives" Version="10.0.652802" />
+    <PackageReference Include="System.ComponentModel.Annotations" />
+    <PackageReference Include="System.IO.FileSystem.AccessControl" />
+    <PackageReference Include="System.Security.Cryptography.ProtectedData" />
+    <PackageReference Include="System.ServiceModel.Http" />
+    <PackageReference Include="System.ServiceModel.Primitives" />
     
     <!-- Windows-specific libraries -->
-    <PackageReference Include="System.DirectoryServices" Version="9.0.0" />
-    <PackageReference Include="System.DirectoryServices.AccountManagement" Version="9.0.0" />
-    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
-    <PackageReference Include="System.Text.Encoding.CodePages" Version="9.0.0" NoWarn="NU1510" />
+    <PackageReference Include="System.DirectoryServices" />
+    <PackageReference Include="System.DirectoryServices.AccountManagement" />
+    <PackageReference Include="System.Drawing.Common" />
+    <PackageReference Include="System.Text.Encoding.CodePages" NoWarn="NU1510" />
   </ItemGroup>
 
   <ItemGroup>
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.