// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/NewPersonController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：處理新人跟進 Grid 與 CRM 新人建立流程。讀取只發布 detached snapshot 並在
//           DataSourceLoader 前依 PresentRecordId 驗證；新增成功後的純記憶體 publication 必須
//           在目前 request 內同步完成，不能用 LongRunning fire-and-forget Task 捕獲 Session graph。
// 隔離與資源：IOrganizationService 只從目前 request scope 取得，不跨 request 保存。圖片 stream
//             依既有 using 路徑釋放；Grid callback、ViewModel、credential 與 Session collection
//             都不得進入 static cache、未觀察 Task 或無界佇列。
// 主要型別：class NewPersonController
// 主要成員：NewPersonFollowUpView、SetupNewPersonFollowUpViewBag、LoadNewPersonFollowUp、EnsureNewPersonDataLoaded、InsertNewPresentRecord、UpdateNewPresentRecord、UpdateNewPersonFollowUpData、UpdateAllMemberData、DeleteNewPresentRecord、SaveNewPersonFollowUp
// 引用命名空間：ChurchReport.Models、ChurchReport.Tools、ChurchReport.ViewModels、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Extensions.Caching.Memory
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using System.IO;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
// ✅ 新增 SixLabors.ImageSharp，用於處理 EXIF Orientation（修正直拍照片旋轉問題）
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 新人關懷與追蹤控制器
    /// 處理新人追蹤、關懷、指派小組等功能
    /// </summary>
    public class NewPersonController : BaseChurchController
    {
        /// <summary>
        /// 取得目前 request scope 的 Dataverse 服務；DI 容器負責其最大生命週期與確定性釋放。
        /// 控制器不自行借還或跨 request 保存連線，以維持使用者隔離並避免資源洩漏。
        /// </summary>
        private IOrganizationService OrganizationService =>
            HttpContext?.RequestServices?.GetService(typeof(IOrganizationService)) as IOrganizationService
            ?? throw new InvalidOperationException("目前 request 未註冊 Dataverse 服務。");

        #region 建構函式

        public NewPersonController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
        : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
        }

        #endregion

        #region 新人跟進關懷主頁面

        /// <summary>
        /// 新人跟進關懷列表頁面
        /// 顯示需要關懷的新人清單
        /// </summary>
        [HttpGet]
        [Route("/NewPerson/FollowUpView")]
        public IActionResult NewPersonFollowUpView()
        {
            try
            {
                SetupNewPersonFollowUpViewBag();

                return View(InMemoryContext.SmallGroupDataList.m_NewPersonFollowUpData);
            }
            catch (Exception e)
            {
                return HandleError(e, "NewPersonFollowUpView");
            }
        }

        /// <summary>
        /// 設定新人跟進頁面的 ViewBag
        /// </summary>
        private void SetupNewPersonFollowUpViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();

            ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;
        }

        #endregion

        #region 資料載入

        /// <summary>
        /// 載入新人跟進資料，供 DevExtreme DataGrid 使用。
        /// 每次 request 都重新驗證 Session scope、取得 detached snapshot，並在 consumer boundary
        /// 依 PresentRecordId fail closed；合法同名不同 ID 的新人仍全部保留。
        /// </summary>
        /// <param name="id">清單ID</param>
        /// <param name="loadOptions">載入選項(分頁、排序、篩選)</param>
        [HttpGet]
        public object LoadNewPersonFollowUp(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                // 新人 Grid 與小組 Grid 共用同一個 ListManager 發布入口，確保同一 scope 只建立
                // 一份完整候選。回傳值是深複製 detached snapshot，request 結束後即可回收，且
                // DataSourceLoader 不會列舉正在被另一個 request 修改的 Session Members 集合。
                var snapshot = InMemoryContext.ListManager.EnsureAndGetIntegrateDetachedRead(id);

                var tasks = snapshot
                    .m_SmallGroupDataList.m_NewPersonFollowUpData.Members;

                // 新人 Grid 也有自己的 consumer collection，因此即使它與小組 Grid 共享
                // 同一 detached snapshot，仍要在真正交給 DataSourceLoader 前獨立驗證 stable ID。
                // 不可用 FullName 去重，因為不同 PresentRecordId 的合法同名新人必須全部保留。
                RowPublicationGuard.ValidateRows(
                    tasks,
                    member => member.PresentRecordId,
                    "ChurchReport.WeeklyReport.NewPersonGrid",
                    RowPublicationGuard.DefaultMaximumRowCount,
                    nameof(Member.PresentRecordId),
                    StringComparer.OrdinalIgnoreCase);

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadNewPersonFollowUp");
            }
        }

        /// <summary>
        /// 確保新人跟進資料已載入
        /// </summary>
        private void EnsureNewPersonDataLoaded(string id)
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

            if (weeklyReport == null || !weeklyReport.LoadFlag)
            {
                InMemoryContext.ListManager.SetupIntegrateData(id);
            }
        }

        #endregion

        #region CRUD 操作

        /// <summary>
        /// 新增新人跟進記錄；重送 request 會在同一 instance lock 內依 PresentRecordId 原子拒絕，
        /// 不會用姓名或內容相似度刪除資料，也不會在失敗時留下半加入集合。
        /// </summary>
        /// <param name="values">JSON 格式的資料</param>
        [HttpPost]
    public IActionResult InsertNewPresentRecord(string values)
    {
        try
        {
            // 先驗證目前 Session，再由 SmallGroupData 的 instance synchronization root 解析 JSON
            // 並以同一臨界區拒絕重複 stable ID；這樣重送 POST 不會把同一權威記錄 append 兩次。
            EnsureCorrectUserData();
            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                .m_SmallGroupDataList.m_NewPersonFollowUpData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertNewPresentRecord");
            }
        }

        /// <summary>
        /// 更新新人跟進記錄
        /// 包含指派小組、轉介、關懷記錄等操作
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        /// <param name="values">更新的欄位值(JSON)</param>
        [HttpPut]
        public IActionResult UpdateNewPresentRecord(string key, string values)
        {
            try
            {
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                if (string.IsNullOrWhiteSpace(key))
                {
                    return BadRequest("缺少 PresentRecordId");
                }

                if (string.IsNullOrWhiteSpace(values))
                {
                    return BadRequest("缺少更新資料");
                }

                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 兩個前景集合必須在同一份資料圖鎖內更新；不能讓 SaveIntegrate 在兩次
                // Json.NET 原地繫結之間取得混合快照。
                dataList.UpdateNewPersonAndAllMember(key, values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateNewPresentRecord");
            }
        }

        /// <summary>
        /// 刪除新人跟進記錄
        /// 同時從多個資料集中移除
        /// </summary>
        /// <param name="key">記錄識別碼</param>
        [HttpDelete]
        public IActionResult DeleteNewPresentRecord(string key)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 從同一份資料圖的所有前景集合一次移除，避免 snapshot 看到部分刪除狀態。
                _ = dataList.DeleteMemberFromAllGroups(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeleteNewPresentRecord");
            }
        }

        #endregion

        #region 資料儲存

        /// <summary>
        /// 儲存新人跟進資料
        /// </summary>
        /// <param name="aResult">儲存結果</param>
        [HttpPost]
        public IActionResult SaveNewPersonFollowUp(string aResult)
        {
            try
            {
                return Json(new { status = "1", message = "成功上傳了...." });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveNewPersonFollowUp");
            }
        }

        #endregion

        #region 新增新人

        /// <summary>
        /// 新增新人頁面
        /// 提供新人基本資料輸入表單
        /// </summary>
        [HttpGet]
        [Route("/NewPerson/AddNewPerson")]
        [Route("/NewPerson/NewPerson")]
        public IActionResult NewPerson()
        {
            try
            {
                SetupNewPersonViewBag();
                SetupNewPersonGroupArray();

                return View(InMemoryContext.NewPersonModel.m_PersonFormViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "NewPerson");
            }
        }

        /// <summary>
        /// 設定新增新人頁面的 ViewBag
        /// </summary>
        private void SetupNewPersonViewBag()
        {
            SetupBasicViewBag();
            SetMultiGroupLayoutParameter();
            SetupNewPersonPosition();
        }

        /// <summary>
        /// 設定新人要加入的小組位置
        /// </summary>
        private void SetupNewPersonPosition()
        {
            var multiGroupList = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

            if (multiGroupList.Count == 1)
            {
                // 單一小組 - 不需設定
            }
            else
            {
                string multiGroupIndex = ViewBag.MultiGroupIndex;

                if (multiGroupIndex == "HybridView")
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position =
                        InMemoryContext.ListManager.ActiveListId;
                }
                else if (multiGroupIndex == "SingleMultiGroupView")
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
                else
                {
                    InMemoryContext.NewPersonModel.m_PersonFormViewModel.Position = "";
                }
            }
        }

        /// <summary>
        /// 設定可選擇的小組陣列
        /// </summary>
        private void SetupNewPersonGroupArray()
        {
            InMemoryContext.NewPersonModel.SetupGroupArray(
                InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData,
                InMemoryContext.ListManager.ActiveListId,
                InMemoryContext.ListManager.LoginType);
        }

        /// <summary>
        /// 儲存新增的新人資料
        /// </summary>
        /// <param name="aPersonFormViewModel">新人表單 ViewModel</param>
        [HttpPost]
        public async Task<IActionResult> SaveNewPerson(PersonFormViewModel aPersonFormViewModel, IFormFile imageFile)
        {
            try
            {
                // 驗證必填欄位
                if (string.IsNullOrEmpty(aPersonFormViewModel.Phone))
                {
                    return Json(new { status = "2", message = "新增新人必須要有行動電話" });
                }

                // 上傳新人資料到 CRM
                string result = UploadNewPersonToCrm(aPersonFormViewModel);

                if (result.Contains("成功"))
                {
                    // 新增成功後的處理
                    HandleSuccessfulNewPersonCreation(aPersonFormViewModel);

                    // 嘗試上傳新人照片（若有選擇檔案）
                    await TryUploadNewPersonImageAsync(imageFile);

                    // 重設表單
                    ResetNewPersonForm();

                    return Json(new { status = "1", message = result });
                }
                else
                {
                    // 新增失敗後的處理
                    ResetNewPersonForm();

                    return Json(new { status = "2", message = result });
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveNewPerson");
            }
        }

        /// <summary>
        /// 上傳新人照片（由前端在新增成功後呼叫）
        /// </summary>
        [HttpPost]
        [Route("/NewPerson/UploadNewPersonImage")]
        public async Task<IActionResult> UploadNewPersonImage(IFormFile imageFile)
        {
            try
            {
                await TryUploadNewPersonImageAsync(imageFile);

                return Json(new
                {
                    success = true,
                    message = "大頭照上傳成功"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"大頭照上傳失敗: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 上傳新人照片到新人記錄
        /// ✅ 支援 EXIF Orientation 自動修正（解決直拍照片旋轉問題）
        /// </summary>
        private async Task TryUploadNewPersonImageAsync(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return;
            }

            const long maxFileSize = 5 * 1024 * 1024; // 5MB
            if (imageFile.Length > maxFileSize)
            {
                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 檔案過大");
                return;
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
            var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var contentType = imageFile.ContentType.ToLowerInvariant();

            if (!Array.Exists(allowedExtensions, ext => ext == fileExtension) ||
                !Array.Exists(allowedContentTypes, type => type == contentType))
            {
                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 檔案格式錯誤");
                return;
            }

            if (!Guid.TryParse(InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId, out var contactId))
            {
                System.Diagnostics.Debug.WriteLine("[SaveNewPerson] 圖片上傳失敗: 新人 ContactId 無效");
                return;
            }

            try
            {
                // ========================================
                // ✅ 修正 EXIF Orientation（解決直拍照片旋轉問題）
                // ========================================
                byte[] imageBytes;
                using (var inputStream = imageFile.OpenReadStream())
                using (var outputStream = new MemoryStream())
                {
                    try
                    {
                        // 使用 ImageSharp 載入並修正圖片方向
                        using (var image = await Image.LoadAsync(inputStream))
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 📐 原始圖片尺寸: {image.Width}x{image.Height}");

                            // 檢查 EXIF Orientation
                            var exifProfile = image.Metadata.ExifProfile;
                            if (exifProfile != null && exifProfile.TryGetValue(ExifTag.Orientation, out IExifValue<ushort> orientationValue))
                            {
                                System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 📱 EXIF Orientation: {orientationValue.Value}");
                            }

                            // ✅ 自動修正旋轉（根據 EXIF 資訊）
                            image.Mutate(x => x.AutoOrient());

                            // 移除 EXIF Orientation 標記（避免重複旋轉）
                            if (image.Metadata.ExifProfile != null)
                            {
                                image.Metadata.ExifProfile.RemoveValue(ExifTag.Orientation);
                            }

                            System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ✅ EXIF 方向已修正");

                            // 儲存為高品質 JPEG
                            var encoder = new JpegEncoder { Quality = 90 };
                            await image.SaveAsync(outputStream, encoder);
                        }

                        imageBytes = outputStream.ToArray();
                        System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ✅ 圖片已處理: {imageBytes.Length} bytes");
                    }
                    catch (Exception imageProcessEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] ⚠️ ImageSharp 處理失敗，使用原始檔案: {imageProcessEx.Message}");

                        // 降級方案：如果 ImageSharp 處理失敗，使用原始檔案
                        inputStream.Position = 0;
                        using (var fallbackStream = new MemoryStream())
                        {
                            imageFile.CopyTo(fallbackStream);
                            imageBytes = fallbackStream.ToArray();
                        }
                    }
                }

                var service = OrganizationService;

                var contactToUpdate = new Entity("contact", contactId);
                contactToUpdate["entityimage"] = imageBytes;
                service.Update(contactToUpdate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveNewPerson] 圖片上傳失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 上傳新人資料到 CRM
        /// </summary>
        private string UploadNewPersonToCrm(PersonFormViewModel viewModel)
        {
            return InMemoryContext.NewPersonModel.UploadNewPerson(
                InMemoryContext.ListManager.m_Account,
                InMemoryContext.ListManager.m_Password,
                viewModel);
        }

        /// <summary>
        /// 處理新人新增成功後的記憶體發布。CRM 已在呼叫前完成，本方法只做短暫同步異動，
        /// 不建立背景 Task；因此回應結束後不會仍有工作捕獲 Session、ViewModel 或 credential。
        /// </summary>
        private void HandleSuccessfulNewPersonCreation(PersonFormViewModel viewModel)
        {
            // 此動作只修改目前 Session 的記憶體資料圖，執行時間短且不含 CRM／HTTP I/O，
            // 因此必須在目前 request 內同步完成，不能用未被 owner 管理的 LongRunning task
            // 捕獲 ListManager、Session 或 viewModel。同步完成也讓 SaveNewPerson 回應送出前，
            // 下一個初始 GET 只會看見完整舊圖或完整新圖，不會看見背景 append 的中間狀態。
            if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null &&
                viewModel.Position != "0")
            {
                viewModel.PresentRecordId = InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId;
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList
                    .AddNewPersonToMember(viewModel);
            }
        }

        /// <summary>
        /// 重設新人表單
        /// </summary>
        private void ResetNewPersonForm()
        {
            InMemoryContext.NewPersonModel.ResetPersonFormViewModel(
                InMemoryContext.NewPersonModel.m_PersonFormViewModel);
        }

        #endregion

        #region 小組指派查詢

        /// <summary>
        /// 取得可指派的小組清單
        /// 用於 Lookup 下拉選單
        /// </summary>
        /// <param name="loadOptions">載入選項</param>
        [HttpGet]
        public object AssignSmallGroupGet(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 使用基底控制器的統一驗證方法
                // ========================================
                // 教學說明：
                // 基底控制器已經提供了完整的驗證方法，包含：
                // - Session 和 ListManager 密碼一致性檢查
                // - LINE ID 恢復機制
                // - 安全的日誌記錄（隱藏敏感資訊）
                EnsureCorrectUserData();

                return DataSourceLoader.Load(
                    InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData,
                    loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "AssignSmallGroupGet");
            }
        }

        #endregion
    }
}
