// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/PersonalController.ImageUpload.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class PersonalController、class BatchImageRequest
// 主要成員：UploadContactImage、GetContactImage、CreateThumbnailIfNeeded、ApplyImageResponseCacheHeaders、ToSvgDataUri、GetDefaultImage、GetContactImagesBatch、ContactIds、Size
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、ChurchReport.ViewModels、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Services.Images;

using ChurchReport.Tools;

using ChurchReport.ViewModels;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Caching.Memory;

using Microsoft.Xrm.Sdk;

using System;

using System.IO;

using System.Threading.Tasks;

using ToolUtilityNameSpace.ConnectionOperations;

using ToolUtilityNameSpace.DependencyInjection;

// ? 新增 SixLabors.ImageSharp，用於處理 EXIF Orientation

using SixLabors.ImageSharp;

using SixLabors.ImageSharp.Processing;

using SixLabors.ImageSharp.Formats.Jpeg;

using SixLabors.ImageSharp.Metadata.Profiles.Exif;

using System.Collections.Generic;

using System.Linq;



namespace ChurchReport.Controllers

{

    /// <summary>

    /// 個人資訊管理控制器 - 圖片上傳部分

    /// 處理 Contact 大頭照上傳與更新

    /// </summary>

    public partial class PersonalController

    {
        private const int PersonalImageMinimumThumbnailSize = 32;
        private const int PersonalImageMaximumThumbnailSize = 256;

        /// <summary>
        /// 取得目前 HTTP request scope 的 Dataverse 服務。服務由 DI 容器建立與釋放，
        /// 最大生命週期限定於目前 request；此 partial 不自行借還或保存連線，避免跨使用者
        /// 重用可變傳輸狀態。若在沒有 request scope 的情況呼叫，立即失敗而不建立臨時連線。
        /// </summary>
        private IOrganizationService OrganizationService =>
            HttpContext?.RequestServices?.GetService(typeof(IOrganizationService)) as IOrganizationService
            ?? throw new InvalidOperationException("目前 request 未註冊 Dataverse 服務。");

        #region 大頭照上傳與更新



        /// <summary>
        /// 僅更新目前 Session 登入 Contact 的照片，保留既有宣告白名單與 5 MiB 限制。
        /// 內容必經共用格式與尺寸守門；EXIF 轉正後輸出 JPEG90，不再退回儲存原始檔。
        /// input、image、output 只屬當次請求並確定性釋放，不保留姓名、圖片或服務至 static。
        /// 預期輸入拒絕不通知；其他失敗經共用入口上報，回應不包含底層例外訊息。
        /// </summary>
        /// <param name="imageFile">未受信任的上傳圖片。</param>
        /// <returns>維持 success／message 與成功時 imageUrl 的既有 JSON schema。</returns>
        [HttpPost]
        [Route("/Personal/UploadContactImage")]
        public async Task<IActionResult> UploadContactImage(IFormFile imageFile)
        {
            try
            {
                if (imageFile == null || imageFile.Length == 0)
                    return Json(new { success = false, message = "請選擇要上傳的圖片檔案" });
                if (imageFile.Length > 5 * 1024 * 1024)
                    return Json(new { success = false, message = "圖片檔案過大，請選擇小於 5MB 的圖片" });
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                var contentType = imageFile.ContentType.ToLowerInvariant();
                if (!Array.Exists(allowedExtensions, ext => ext == fileExtension)
                    || !Array.Exists(allowedContentTypes, type => type == contentType))
                    return Json(new { success = false, message = "只支援 JPG、PNG、GIF 格式的圖片" });

                var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
                if (loginContact == null)
                    return Json(new { success = false, message = "無法取得登入使用者資訊，請重新登入" });
                var contactId = loginContact.Id;
                byte[] imageBytes;
                using (var input = imageFile.OpenReadStream())
                using (var image = await ImageInputValidator.LoadAsync(input, HttpContext.RequestAborted))
                using (var output = new MemoryStream())
                {
                    image.Mutate(x => x.AutoOrient());
                    image.Metadata.ExifProfile?.RemoveValue(ExifTag.Orientation);
                    await image.SaveAsync(output, new JpegEncoder { Quality = 90 }, HttpContext.RequestAborted);
                    imageBytes = output.ToArray();
                }
                var service = OrganizationService;
                var contactToUpdate = new Entity("contact", contactId) { ["entityimage"] = imageBytes };
                service.Update(contactToUpdate);
                // 先完成 authoritative Update，再清除目前 Contact 的舊照片快取；不發布處理中的影像。
                var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                InvalidatePersonalImageCache(memoryCache, contactId);
                try
                {
                    var updatedContact = service.Retrieve("contact", contactId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
                    InMemoryContext.PersonalInfomationModel.m_LoginContact = updatedContact;
                }
                catch (Exception exception)
                {
                    // 照片已更新，不以 Session 刷新失敗誘發使用者重送；仍須上報真實故障。
                    ChurchReportLineAdminNotificationService.ReportException("Personal.RefreshImageContact", exception);
                }
                return Json(new
                {
                    success = true, message = "大頭照上傳成功！",
                    imageUrl = $"/Personal/GetContactImage?contactId={contactId}&timestamp={DateTime.Now.Ticks}"
                });
            }
            catch (ImageInputRejectedException rejected)
            {
                return Json(new { success = false, message = rejected.Message });
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (System.ServiceModel.FaultException exception)
            {
                ChurchReportLineAdminNotificationService.ReportException("Personal.UploadContactImage", exception);
                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.UpdateFailed") });
            }
            catch (Exception exception)
            {
                ChurchReportLineAdminNotificationService.ReportException("Personal.UploadContactImage", exception);
                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.Failed") });
            }
        }

        /// <summary>
        /// 清除指定 Contact 的個人照片快取。
        /// </summary>
        /// <param name="cache">目前應用程式的記憶體快取；為 null 時不建立暫時快取，也不拋出例外。</param>
        /// <param name="contactId">已由目前登入者解析並驗證的 Contact 識別碼。</param>
        /// <remarks>
        /// 個人照片同時以完整圖與 32 至 256 像素縮圖快取；尺寸範圍與讀取端的
        /// <c>Math.Clamp</c> 保持一致。這裡在 CRM 更新成功後同步移除
        /// 所有可能的鍵，讓下一次讀取重新建立最新影像，並避免舊 byte[] 在快取期限內
        /// 跨 request 保留。方法不持有、複製或延長任何 request、session 或連線資源。
        /// </remarks>
        private static void InvalidatePersonalImageCache(IMemoryCache cache, Guid contactId)
        {
            if (cache == null)
            {
                return;
            }

            cache.Remove($"contact-image-full:{contactId:N}");

            for (var size = PersonalImageMinimumThumbnailSize;
                 size <= PersonalImageMaximumThumbnailSize;
                 size++)
            {
                cache.Remove($"contact-image-thumb:{contactId:N}:{size}");
            }
        }



        /// <summary>

        /// 取得 Contact 大頭照

        /// GET: /Personal/GetContactImage?contactId={guid}

        /// </summary>

        /// <param name="contactId">Contact ID</param>

        /// <returns>圖片 byte[] 或預設圖片</returns>

        [HttpGet]
        [Route("/Personal/GetContactImage")]
        public IActionResult GetContactImage(string contactId, int size = 80)
        {
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImage] 取得大頭照: {contactId}, size={size}");

                // size <= 0 表示取得原始圖片（彈窗顯示用），不進行縮圖處理
                var returnOriginal = (size <= 0);
                var thumbSize = returnOriginal
                    ? 0
                    : Math.Clamp(size, PersonalImageMinimumThumbnailSize, PersonalImageMaximumThumbnailSize);

                // 如果沒有提供 contactId，使用當前登入使用者
                Guid contactGuid;
                if (string.IsNullOrWhiteSpace(contactId))
                {
                    var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
                    if (loginContact == null)
                    {
                        return GetDefaultImage();
                    }
                    contactGuid = loginContact.Id;
                }
                else if (!Guid.TryParse(contactId, out contactGuid))
                {
                    System.Diagnostics.Debug.WriteLine($"[GetContactImage] 無效的 Contact ID: {contactId}");
                    return GetDefaultImage();
                }

                var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                var cacheKey = returnOriginal
                    ? $"contact-image-full:{contactGuid:N}"
                    : $"contact-image-thumb:{contactGuid:N}:{thumbSize}";

                if (memoryCache != null && memoryCache.TryGetValue(cacheKey, out byte[] cachedImageBytes) && cachedImageBytes != null)
                {
                    ApplyImageResponseCacheHeaders();
                    return File(cachedImageBytes, "image/jpeg");
                }

                var service = OrganizationService;

                // 只查詢 entityimage 欄位以提升效能
                var contact = service.Retrieve("contact", contactGuid,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet(
                        "entityimage",
                        "gendercode",
                        ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));

                if (contact.Contains("entityimage") && contact["entityimage"] != null)
                {
                    var originalBytes = (byte[])contact["entityimage"];
                    var outputBytes = returnOriginal ? originalBytes : CreateThumbnailIfNeeded(originalBytes, thumbSize);

                    memoryCache?.Set(
                        cacheKey,
                        outputBytes,
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                                SlidingExpiration = TimeSpan.FromMinutes(10),
                            Size = Math.Max(1, outputBytes.Length / 1024)
                        });

                    ApplyImageResponseCacheHeaders();
                    return File(outputBytes, "image/jpeg");
                }

                System.Diagnostics.Debug.WriteLine("[GetContactImage] Contact 沒有大頭照，返回性別剪影");
                var linePictureUrl = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                    contact.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                if (!string.IsNullOrEmpty(linePictureUrl))
                {
                    Response.Headers["Cache-Control"] = "private, max-age=300";
                    return Redirect(linePictureUrl);
                }

                var genderDefault = contact.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("gendercode")?.Value;
                return Content(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.ForGender(genderDefault), "image/svg+xml");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImage] 錯誤: {ex.Message}");
                return GetDefaultImage();
            }
            
        }

        /// <summary>限定格式及尺寸後裁切 JPEG82；解碼資源僅屬當次呼叫，拒絕時沿用原陣列回退且不向頁面拋錯。</summary>
        private static byte[] CreateThumbnailIfNeeded(byte[] originalBytes, int size)
        {
            try
            {
                using var input = new MemoryStream(originalBytes);
                using var image = ImageInputValidator.Load(input);

                if (image.Width <= size && image.Height <= size)
                {
                    return originalBytes;
                }

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));

                using var output = new MemoryStream();
                image.Save(output, new JpegEncoder
                {
                    Quality = 82
                });

                return output.ToArray();
            }
            catch (ImageInputRejectedException)
            {
                return originalBytes;
            }
            catch (Exception exception)
            {
                ChurchReportLineAdminNotificationService.ReportException("Personal.Thumbnail", exception);
                return originalBytes;
            }
        }

        private void ApplyImageResponseCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "private, max-age=1800";
            Response.Headers["Vary"] = "Accept-Encoding";
        }

        /// <summary>把 SVG 字串轉成可直接當 img src 的 data URI(base64，避免特殊字元轉義問題)。</summary>
        private static string ToSvgDataUri(string svg)
        {
            return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg ?? string.Empty));
        }

        /// <summary>
        /// 返回預設大頭照（純色圓形圖示）
        /// </summary>
        private IActionResult GetDefaultImage()

        {

            // 產生一個簡單的 SVG 預設圖示

            var svg = ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.Neutral;



            return Content(svg, "image/svg+xml");

        }

        /// <summary>
        /// 批次取得多個 Contact 大頭照（Base64 Data URL 格式）
        /// POST: /Personal/GetContactImagesBatch
        /// 透過單一 CRM RetrieveMultiple 查詢，一次取得所有所需照片，
        /// 大幅減少 N+1 HTTP 請求與 CRM 連線開銷。
        /// </summary>
        /// <param name="request">包含 ContactIds 陣列與縮圖尺寸</param>
        /// <returns>JSON: { success, images: { contactId: dataUrl } }</returns>
        [HttpPost]
        [Route("/Personal/GetContactImagesBatch")]
        public IActionResult GetContactImagesBatch([FromBody] BatchImageRequest request)
        {
            
            try
            {
                if (request?.ContactIds == null || request.ContactIds.Length == 0)
                {
                    return Json(new { success = true, images = new Dictionary<string, string>(), sources = new Dictionary<string, string>() });
                }

                var thumbSize = Math.Clamp(
                    request.Size > 0 ? request.Size : 48,
                    PersonalImageMinimumThumbnailSize,
                    PersonalImageMaximumThumbnailSize);
                var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                var result = new Dictionary<string, string>();
                var sources = new Dictionary<string, string>();
                var uncachedGuids = new List<Guid>();

                // [計時診斷] 暫時性效能量測，僅讀取耗時與位元組大小，不影響任何邏輯/安全。確認瓶頸後即可移除。
                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                long connMs = 0, crmMs = 0, imgTicks = 0, inBytes = 0, outBytes = 0;
                int withPhoto = 0;

                // ========================================
                // 步驟 1: 優先從 MemoryCache 取得已快取的縮圖
                // ========================================
                foreach (var idStr in request.ContactIds)
                {
                    if (!Guid.TryParse(idStr, out var guid)) continue;

                    var cacheKey = $"contact-image-thumb:{guid:N}:{thumbSize}";
                    if (memoryCache != null && memoryCache.TryGetValue(cacheKey, out byte[] cached) && cached != null)
                    {
                        result[idStr] = "data:image/jpeg;base64," + Convert.ToBase64String(cached);
                        sources[idStr] = "primary";
                    }
                    else
                    {
                        uncachedGuids.Add(guid);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[GetContactImagesBatch] 快取命中: {result.Count}, 需查詢 CRM: {uncachedGuids.Count}");
                int cacheHitCount = result.Count; // [計時診斷] 步驟1結束時 result 內全是快取命中

                // ========================================
                // 步驟 2: 批次查詢 CRM 取得未快取的照片
                // ========================================
                if (uncachedGuids.Count > 0)
                {
                    var swConn = System.Diagnostics.Stopwatch.StartNew();
                    var service = OrganizationService;
                    swConn.Stop(); connMs = swConn.ElapsedMilliseconds; // [計時診斷] 連線池取得連線耗時

                    var query = new Microsoft.Xrm.Sdk.Query.QueryExpression("contact")
                    {
                        ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                            "entityimage",
                            "contactid",
                            "gendercode",
                            ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute)
                    };
                    query.Criteria.AddCondition(
                        "contactid",
                        Microsoft.Xrm.Sdk.Query.ConditionOperator.In,
                        uncachedGuids.Select(g => (object)g).ToArray());

                    var swCrm = System.Diagnostics.Stopwatch.StartNew();
                    var queryResult = service.RetrieveMultiple(query);
                    swCrm.Stop(); crmMs = swCrm.ElapsedMilliseconds; // [計時診斷] 單一 RetrieveMultiple(含 entityimage 傳輸) 耗時

                    foreach (var entity in queryResult.Entities)
                    {
                        var contactIdStr = entity.Id.ToString();
                        if (entity.Contains("entityimage") && entity["entityimage"] != null)
                        {
                            var originalBytes = (byte[])entity["entityimage"];
                            var _imgStart = System.Diagnostics.Stopwatch.GetTimestamp(); // [計時診斷] 累計解碼/縮圖耗時
                            var outputBytes = CreateThumbnailIfNeeded(originalBytes, thumbSize);
                            imgTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _imgStart;
                            inBytes += originalBytes.Length; outBytes += outputBytes.Length; withPhoto++; // [計時診斷] 原圖/縮圖位元組

                            // 寫入 MemoryCache（與 GetContactImage 共用相同 cache key）
                            var cacheKey = $"contact-image-thumb:{entity.Id:N}:{thumbSize}";
                            memoryCache?.Set(cacheKey, outputBytes, new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                                SlidingExpiration = TimeSpan.FromMinutes(10),
                                Size = Math.Max(1, outputBytes.Length / 1024)
                            });

                            result[contactIdStr] = "data:image/jpeg;base64," + Convert.ToBase64String(outputBytes);
                            sources[contactIdStr] = "primary";
                        }
                        else
                        {
                            // 無照片：批次直接帶回性別剪影(SVG data URI)，前端就不必再逐筆打 GetContactImage。
                            var linePictureUrl = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                                entity.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                            if (!string.IsNullOrEmpty(linePictureUrl))
                            {
                                result[contactIdStr] = linePictureUrl;
                                sources[contactIdStr] = "line";
                            }
                            else
                            {
                                var gender = entity.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("gendercode")?.Value;
                                result[contactIdStr] = ToSvgDataUri(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.ForGender(gender));
                                sources[contactIdStr] = "fallback";
                            }
                        }
                    }
                }

                // [計時診斷] 一行彙總：total/conn/crm/img 各自耗時 + 原圖(inKB)/縮圖(outKB)大小，用來判定瓶頸在 CRM 傳輸還是本地解碼
                var imgMs = imgTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                // [計時診斷] 改用 Debug.WriteLine（[Conditional("DEBUG")]）：Release 建置時整行(含字串)會被編譯器移除，
                // 不會寫入 Logs\Trace.log；僅在 Debug 模式診斷時輸出。Stopwatch 量測仍會跑但不產生任何輸出。
                System.Diagnostics.Debug.WriteLine(
                    $"[BatchImg-Timing] ep=Personal total={swTotal.ElapsedMilliseconds}ms conn={connMs}ms crm={crmMs}ms img={imgMs}ms | req={request.ContactIds.Length} cacheHit={cacheHitCount} crmQ={uncachedGuids.Count} photo={withPhoto} inKB={inBytes / 1024} outKB={outBytes / 1024}");

                // 批次回應不設瀏覽器快取（資料已由 MemoryCache 快取）
                Response.Headers["Cache-Control"] = "private, no-store";
                return Json(new { success = true, images = result, sources });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImagesBatch] 錯誤: {ex.Message}");
                return Json(new { success = false, images = new Dictionary<string, string>(), sources = new Dictionary<string, string>() });
            }
            
        }

        #endregion

    }

    /// <summary>
    /// 批次取得聯絡人照片的請求模型
    /// </summary>
    public class BatchImageRequest
    {
        /// <summary>聯絡人 ID 陣列</summary>
        public string[] ContactIds { get; set; }

        /// <summary>縮圖尺寸（像素）</summary>
        public int Size { get; set; }
    }

}
