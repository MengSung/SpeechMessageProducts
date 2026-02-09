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
        public IActionResult GetContactImage(string contactId)
        {
            IOrganizationService service = null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImage] 取得大頭照: {contactId}");

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
                else
                {
                    if (!Guid.TryParse(contactId, out contactGuid))
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetContactImage] ? 無效的 Contact ID: {contactId}");
                        return GetDefaultImage();
                    }
                }

                service = GetConnection();

                // 只查詢 entityimage 欄位以提升效能
                var contact = service.Retrieve("contact", contactGuid, 
                    new Microsoft.Xrm.Sdk.Query.ColumnSet("entityimage"));

                if (contact.Contains("entityimage") && contact["entityimage"] != null)
                {
                    var imageBytes = (byte[])contact["entityimage"];
                    System.Diagnostics.Debug.WriteLine($"[GetContactImage] ? 找到大頭照: {imageBytes.Length} bytes");
                    return File(imageBytes, "image/jpeg");
                }

                System.Diagnostics.Debug.WriteLine($"[GetContactImage] ?? Contact 沒有大頭照，返回預設圖片");
                return GetDefaultImage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetContactImage] ? 錯誤: {ex.Message}");
                return GetDefaultImage();
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 返回預設大頭照（純色圓形圖示）
        /// </summary>
        private IActionResult GetDefaultImage()
        {
            // 產生一個簡單的 SVG 預設圖示
            var svg = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""150"" height=""150"">
                <circle cx=""75"" cy=""75"" r=""70"" fill=""#4A90E2""/>
                <text x=""75"" y=""95"" font-family=""Arial, sans-serif"" font-size=""60"" fill=""white"" text-anchor=""middle"">??</text>
            </svg>";

            return Content(svg, "image/svg+xml");
        }

        #endregion
    }
}
