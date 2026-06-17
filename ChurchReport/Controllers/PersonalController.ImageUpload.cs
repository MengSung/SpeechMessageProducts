using ChurchReport.Models;

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

        #region 大頭照上傳與更新



        /// <summary>

        /// 上傳並更新 Contact 大頭照

        /// POST: /Personal/UploadContactImage

        /// </summary>

        /// <param name="imageFile">上傳的圖片檔案</param>

        /// <returns>JSON 結果：成功或失敗訊息</returns>

        [HttpPost]

        [Route("/Personal/UploadContactImage")]

        public async Task<IActionResult> UploadContactImage(IFormFile imageFile)

        {

            IOrganizationService service = null;

            try

            {

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ========== 開始上傳大頭照 ==========");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 時間: {DateTime.Now}");



                // ========================================

                // 步驟 1: 驗證上傳檔案

                // ========================================

                if (imageFile == null || imageFile.Length == 0)

                {

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 未選擇檔案");

                    return Json(new

                    {

                        success = false,

                        message = "請選擇要上傳的圖片檔案"

                    });

                }



                // 檢查檔案大小（限制 5MB）

                const long maxFileSize = 5 * 1024 * 1024; // 5MB

                if (imageFile.Length > maxFileSize)

                {

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 檔案過大: {imageFile.Length} bytes");

                    return Json(new

                    {

                        success = false,

                        message = "圖片檔案過大，請選擇小於 5MB 的圖片"

                    });

                }



                // 檢查檔案類型

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };

                var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };

                var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

                var contentType = imageFile.ContentType.ToLowerInvariant();



                if (!Array.Exists(allowedExtensions, ext => ext == fileExtension) ||

                    !Array.Exists(allowedContentTypes, type => type == contentType))

                {

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 不支援的檔案類型: {fileExtension}, {contentType}");

                    return Json(new

                    {

                        success = false,

                        message = "只支援 JPG、PNG、GIF 格式的圖片"

                    });

                }



                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 檔案驗證通過");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案名稱: {imageFile.FileName}");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案大小: {imageFile.Length} bytes");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage]   - 檔案類型: {contentType}");



                // ========================================

                // 步驟 2: 取得當前登入使用者的 Contact

                // ========================================

                var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;

                if (loginContact == null)

                {

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 無法取得登入使用者資訊");

                    return Json(new

                    {

                        success = false,

                        message = "無法取得登入使用者資訊，請重新登入"

                    });

                }



                var contactId = loginContact.Id;

                var fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 使用者: {fullName} (ID: {contactId})");



                // ========================================

                // 步驟 3: 讀取並修正圖片 EXIF Orientation（解決直拍旋轉問題）

                // ========================================

                byte[] imageBytes;

                using (var inputStream = imageFile.OpenReadStream())

                using (var outputStream = new MemoryStream())

                {

                    try

                    {

                        // 使用 ImageSharp 載入圖片

                        using (var image = await Image.LoadAsync(inputStream))

                        {

                            System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 原始圖片尺寸: {image.Width}x{image.Height}");



                            // 檢查 EXIF Orientation（使用 ImageSharp 3.x API）

                            var exifProfile = image.Metadata.ExifProfile;

                            if (exifProfile != null && exifProfile.TryGetValue(ExifTag.Orientation, out IExifValue<ushort> orientationValue))

                            {

                                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? EXIF Orientation: {orientationValue.Value}");

                            }

                            else

                            {

                                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 無 EXIF Orientation 資訊");

                            }



                            // ? 自動修正旋轉（根據 EXIF 資訊）

                            image.Mutate(x => x.AutoOrient());



                            // 移除 EXIF Orientation 標記（避免重複旋轉）

                            if (image.Metadata.ExifProfile != null)

                            {

                                image.Metadata.ExifProfile.RemoveValue(ExifTag.Orientation);

                            }



                            System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? EXIF 方向已修正，圖片已正規化");



                            // 儲存為 JPEG（高品質）

                            var encoder = new JpegEncoder

                            {

                                Quality = 90 // 90% 品質（平衡檔案大小與畫質）

                            };

                            await image.SaveAsync(outputStream, encoder);

                        }



                        imageBytes = outputStream.ToArray();

                        System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 圖片已處理並壓縮: {imageBytes.Length} bytes");

                    }

                    catch (Exception imageProcessEx)

                    {

                        System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? ImageSharp 處理失敗，使用原始檔案: {imageProcessEx.Message}");

                        

                        // 降級方案：如果 ImageSharp 處理失敗，使用原始檔案

                        inputStream.Position = 0;

                        using (var fallbackStream = new MemoryStream())

                        {

                            await inputStream.CopyToAsync(fallbackStream);

                            imageBytes = fallbackStream.ToArray();

                        }

                    }

                }



                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 最終圖片大小: {imageBytes.Length} bytes");



                // ========================================

                // 步驟 4: 更新 CRM Contact 的 EntityImage

                // ========================================

                service = GetConnection();



                var contactToUpdate = new Entity("contact", contactId);

                contactToUpdate["entityimage"] = imageBytes;



                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 開始更新 CRM Contact...");

                service.Update(contactToUpdate);

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? CRM Contact 更新成功");



                // ========================================

                // 步驟 5: 更新 Session 中的 Contact 資料

                // ========================================

                try

                {

                    // 重新查詢 Contact 以取得更新後的資料

                    var updatedContact = service.Retrieve("contact", contactId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));

                    InMemoryContext.PersonalInfomationModel.m_LoginContact = updatedContact;

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? Session 中的 Contact 已更新");

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ?? 更新 Session 失敗（不影響主要功能）: {ex.Message}");

                }



                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ========== 大頭照上傳完成 ==========");



                return Json(new

                {

                    success = true,

                    message = "大頭照上傳成功！",

                    imageUrl = $"/Personal/GetContactImage?contactId={contactId}&timestamp={DateTime.Now.Ticks}"

                });

            }

            catch (System.ServiceModel.FaultException faultEx)

            {

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? CRM 錯誤: {faultEx.Message}");

                return Json(new

                {

                    success = false,

                    message = $"CRM 更新失敗: {faultEx.Message}"

                });

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] ? 未預期的錯誤: {ex.Message}");

                System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 錯誤堆疊: {ex.StackTrace}");

                return Json(new

                {

                    success = false,

                    message = $"上傳失敗: {ex.Message}"

                });

            }

            finally

            {

                ReleaseConnection(service);

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
            IOrganizationService service = null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImage] 取得大頭照: {contactId}, size={size}");

                // size <= 0 表示取得原始圖片（彈窗顯示用），不進行縮圖處理
                var returnOriginal = (size <= 0);
                var thumbSize = returnOriginal ? 0 : Math.Clamp(size, 32, 256);

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

                service = GetConnection();

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
            finally
            {
                ReleaseConnection(service);
            }
        }

        private static byte[] CreateThumbnailIfNeeded(byte[] originalBytes, int size)
        {
            try
            {
                using var input = new MemoryStream(originalBytes);
                using var image = Image.Load(input);

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
            catch
            {
                // 若縮圖處理失敗，回退原始圖避免中斷顯示
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
            IOrganizationService service = null;
            try
            {
                if (request?.ContactIds == null || request.ContactIds.Length == 0)
                {
                    return Json(new { success = true, images = new Dictionary<string, string>(), sources = new Dictionary<string, string>() });
                }

                var thumbSize = Math.Clamp(request.Size > 0 ? request.Size : 48, 32, 256);
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
                    service = GetConnection();
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
                System.Diagnostics.Trace.WriteLine(
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
            finally
            {
                ReleaseConnection(service);
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

