/*
 * 檔案整體說明
 *
 * 目的與定位：新人關懷與追蹤控制器 處理新人追蹤、關懷、指派小組等功能
 *
 * 主要功能與流程：
 * 本檔宣告 NewPersonController。
 * - 取得目前 request scope 的 Dataverse 服務；DI 容器負責其最大生命週期與確定性釋放。 控制器不自行借還或跨 request 保存連線，以維持使用者隔離並避免資源洩漏。
 * - 載入新人跟進資料，供 DevExtreme DataGrid 使用。 每次 request 都重新驗證 Session scope、取得 detached snapshot，並在 consumer boundary 依 
 * PresentRecordId fail closed；合法同名不同 ID 的新人仍全部保留。
 * - 新增新人跟進記錄；重送 request 會在同一 instance lock 內依 PresentRecordId 原子拒絕， 不會用姓名或內容相似度刪除資料，也不會在失敗時留下半加入集合。
 * - 儲存新人；附圖拒絕只追加固定提示，不回滾已建立的新人或誘發重送。
 * 主要操作入口包含 NewPersonFollowUpView、SetupNewPersonFollowUpViewBag、LoadNewPersonFollowUp、EnsureNewPersonDataLoaded、
 * InsertNewPresentRecord、UpdateNewPresentRecord、DeleteNewPresentRecord、SaveNewPersonFollowUp；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Services.Images;
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
                    RowPublicationGuard.NewPersonGridConsumerName,
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
        /// 儲存新人；附圖拒絕只追加固定提示，不回滾已建立的新人或誘發重送。
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

                    // 新人已建立，照片拒絕不能改回建立失敗，避免使用者重送造成重複新人。
                    // 只有未儲存的照片被拒絕；資料建立結果維持成功，並在原訊息欄位提示。
                    var imageWarning = string.Empty;
                    try
                    {
                        await TryUploadNewPersonImageAsync(imageFile);
                    }
                    catch (ImageInputRejectedException rejected)
                    {
                        imageWarning = ImageInputText.AttachmentRejected(rejected);
                    }
                    catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ChurchReportLineAdminNotificationService.ReportException("NewPerson.SaveNewPersonImage", exception);
                        imageWarning = ImageInputText.GetText("Image.Upload.AttachmentFailed");
                    }

                    // 重設表單
                    ResetNewPersonForm();

                    return Json(new { status = "1", message = result + imageWarning });
                }
                else
                {
                    // 新增失敗後的處理
                    ResetNewPersonForm();

                    return Json(new { status = "2", message = result });
                }
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // 已明確取消的 caller 不進共用失敗通知，保留正常取消語意。
                throw;
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveNewPerson");
            }
        }

        /// <summary>
        /// 新人建立後獨立上傳照片，維持 success／message 回應。
        /// 內容拒絕不通知；其他故障傳真實 Exception 至共用入口，固定回應不洩漏解析或連線訊息。
        /// </summary>
        /// <param name="imageFile">當次請求的未受信任圖片。</param>
        [HttpPost]
        [Route("/NewPerson/UploadNewPersonImage")]
        public async Task<IActionResult> UploadNewPersonImage(IFormFile imageFile)
        {
            try
            {
                await TryUploadNewPersonImageAsync(imageFile);
                return Json(new { success = true, message = "大頭照上傳成功" });
            }
            catch (ImageInputRejectedException rejected)
            {
                return Json(new { success = false, message = rejected.Message });
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ChurchReportLineAdminNotificationService.ReportException("NewPerson.UploadNewPersonImage", exception);
                return Json(new { success = false, message = ImageInputText.GetText("Image.Upload.PhotoFailed") });
            }
        }

        /// <summary>
        /// 新人兩個上傳入口共用的照片處理；保留既有宣告白名單、5 MiB 限制與無檔略過行為。
        /// 限定解碼、EXIF 轉正後才更新 entityimage，不再於解碼失敗時保存原檔。
        /// 每次呼叫獨占並釋放所有串流與 Image，SDK 服務只借用目前 request scope；
        /// 不保存影像、Session 或 credential 至 static，拒絕及其他例外交由各 action 的最終邊界處理。
        /// </summary>
        private async Task TryUploadNewPersonImageAsync(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0) return;
            if (imageFile.Length > 5 * 1024 * 1024) return;
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
            var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var contentType = imageFile.ContentType.ToLowerInvariant();
            if (!Array.Exists(allowedExtensions, ext => ext == fileExtension)
                || !Array.Exists(allowedContentTypes, type => type == contentType)) return;
            if (!Guid.TryParse(InMemoryContext.NewPersonModel.m_NewContact.PresentRecordId, out var contactId)) return;
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
            var contactToUpdate = new Entity("contact", contactId) { ["entityimage"] = imageBytes };
            OrganizationService.Update(contactToUpdate);
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
