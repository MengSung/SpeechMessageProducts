// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/MemberInfoController.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class MemberInfoController
// 主要成員：Index、LoadDistrictTree、SearchDistrictTree、LoadGroupMembers、LoadUngroupedMembers、Detail、GetContactImage、GetContactImagesBatch、ResyncLineProfiles、UploadContactImage
// 引用命名空間：ChurchReport.Models、ChurchReport.Services、ChurchReport.Services.MemberInfo、ChurchReport.Tools、ChurchReport.ViewModels、DevExtreme.AspNet.Data、DevExtreme.AspNet.Mvc、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Services.ContactAvatar;
using ChurchReport.Services.MemberInfo;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using ChurchReport.ViewModels.MemberInfoTree;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    public class MemberInfoController : BaseChurchController
    {
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int CrmInClauseChunkSize = 500;
        private const string ChurchTreeCacheKey = "member-info-tree:church";
        private const string ChurchGroupedCurrentIdsCacheKey = "member-info-tree:grouped-current-ids:church";
        private readonly IMemoryCache memberInfoMemoryCache;

        private sealed class UngroupedContactPage
        {
            public List<Entity> Contacts { get; init; } = new List<Entity>();
            public int TotalCount { get; init; }
        }

        public MemberInfoController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, toolUtilityProvider, connectionPool)
        {
            memberInfoMemoryCache = memoryCache
                ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// 讀取 Package03 contact image 的獨立、預設關閉 DTO-only 路徑。
        ///
        /// 這個 action 不取代 <see cref="GetContactImage"/>：舊路由仍包含 CRM entityimage、LINE redirect 與
        /// gender avatar 語意，而 Package03 typed DTO 只允許 PNG/JPEG bytes。feature gate 是取得 deployment
        /// configuration 後的第一個決策；關閉時不 parse contact locator、不讀取 session scope、不建立 typed
        /// client 或 I/O。開啟後，先驗證 server-side MemberInfo scope，再 parse browser locator，最後以
        /// <see cref="CanViewContact"/> 完成 target authorization。回應不使用 cache、legacy fallback 或 retry。
        /// </summary>
        /// <param name="contactId">browser 提供的 contact locator；它不是 profile、owner、connector 或權限依據。</param>
        /// <returns>已授權 contact 的安全 PNG/JPEG bytes；拒絕、故障與無圖片均回傳固定 404。</returns>
        [HttpGet]
        [Route("/MemberInfo/Package03ContactImage")]
        public async Task<IActionResult> Package03ContactImage(string contactId)
        {
            // IConfiguration 是 deployment-owned singleton，僅用來讀 gate/profile；不含 request identity、
            // session、credential 或 connector。false gate 的判斷仍先於所有使用者資料與外部資源。
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (!DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration))
            {
                return NotFound();
            }

            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return NotFound();
                }

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return NotFound();
                }

                var package03Client = DonationDynamicsAccessBootstrap.TryCreatePackage03SpecialResourceClient(configuration);
                if (package03Client is null)
                {
                    return NotFound();
                }

                var service = new Package03ContactImageReadService(
                    package03Client,
                    DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias);
                var result = await service.RetrieveAsync(contactGuid, HttpContext.RequestAborted).ConfigureAwait(false);
                return File(result.GetImageBytes(), result.ContentType);
            }
            // 取消必須保留給 ASP.NET Core 與下游 executor owner；不可把已終止 request 轉為一般 404，
            // 否則可能掩蓋 lease／transport 的確定性清理結果，或誘發上層重送。
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// 讀取完整 Package03 contact-image display union 的預設關閉路由。
        /// deployment base/sub gate 是第一個 executable decision；未開啟時不 hydrate Session、不解析 browser
        /// locator、不建立 typed client 或觸及外部 I/O。啟用後只接受 server-authorized contact，並將單次 typed
        /// projection 的 image、LINE redirect 與 default avatar 分支映射為 MVC 回應；本 action 不持有 client、
        /// lease、stream、cache 或背景工作，所有可重用 transport 資源仍由 process host 唯一擁有與釋放。
        /// </summary>
        /// <param name="contactId">browser 提供的 GUID locator；它絕不是 profile、connector、owner 或授權依據。</param>
        /// <param name="size">圖片 branch 的顯示邊長；小於等於零保留原圖，其他值限制為 32..256。</param>
        /// <param name="fit">圖片 branch 是否完整等比置入；false 時沿用既有中心裁切語意。</param>
        /// <returns>已驗證 image、LINE redirect 或 default avatar；任何拒絕或非取消 fault 都是固定 404。</returns>
        [HttpGet]
        [Route("/MemberInfo/Package03FullContactImage")]
        public async Task<IActionResult> Package03FullContactImage(string contactId, int size = 80, bool fit = false)
        {
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (!DonationDynamicsAccessBootstrap.IsPackage03MemberInfoFullContactImageReadEnabled(configuration))
            {
                return NotFound();
            }

            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return NotFound();
                }

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return NotFound();
                }

                var package03Client = DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoFullContactImageReadClient(configuration);
                if (package03Client is null)
                {
                    return NotFound();
                }

                var service = new Package03MemberInfoFullContactImageReadService(
                    package03Client,
                    DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias);
                var result = await service.RetrieveAsync(contactGuid, HttpContext.RequestAborted).ConfigureAwait(false);

                switch (result.Kind)
                {
                    case Package03MemberInfoFullContactImageReadResultKind.Image:
                    {
                        var returnOriginal = size <= 0;
                        var thumbnailSize = returnOriginal ? 0 : Math.Clamp(size, 32, 256);
                        var imageBytes = result.GetImageBytes();
                        var outputBytes = returnOriginal
                            ? imageBytes
                            : (fit
                                ? CreateFitThumbnail(imageBytes, thumbnailSize)
                                : CreateThumbnailIfNeeded(imageBytes, thumbnailSize));
                        ApplyImageResponseCacheHeaders();
                        return File(outputBytes, result.ContentType!);
                    }

                    case Package03MemberInfoFullContactImageReadResultKind.LineRedirect:
                        ApplyImageResponseCacheHeaders();
                        return Redirect(result.LineRedirectUrl!);

                    case Package03MemberInfoFullContactImageReadResultKind.DefaultAvatar:
                        ApplyImageResponseCacheHeaders();
                        return Content(DefaultAvatarSvg.ForGender(result.GenderCode), "image/svg+xml");

                    default:
                        return NotFound();
                }
            }
            // 取消必須原樣離開 action，讓下游 executor/lease owner 對已取消或 transport uncertain 的資源
            // 執行 fault eviction 與 deterministic cleanup；一般錯誤不回顯 typed transport 或 contact 資料。
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return NotFound();
            }
        }

        [HttpGet]
        [Route("/MemberInfo/Index")]
        public IActionResult Index()
        {
            try
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

                var access = GetAccess();
                if (string.IsNullOrEmpty(access))
                {
                    return Forbid();
                }

                // 只有「全教會」管理者才看得到、用得到「重新同步LINE」按鈕（全教會範圍的批次操作）。
                ViewBag.MemberInfoCanResync = (access == MemberInfoAccess.Church);
                ViewBag.MemberInfoScope = access == MemberInfoAccess.Church ? "church" : "shepherd";
                return View("MemberInfoGrid");
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.Index");
            }
        }

        [HttpGet]
        [Route("/MemberInfo/LoadDistrictTree")]
        public IActionResult LoadDistrictTree()
        {
            IOrganizationService service = null;
            var timing = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                TraceMemberInfoTreePhase("LoadDistrictTree", "start", timing);
                EnsureCorrectUserData();
                TraceMemberInfoTreePhase("LoadDistrictTree", "user-data-ready", timing);
                var access = GetAccess();
                TraceMemberInfoTreePhase("LoadDistrictTree", "access=" + (access ?? "none"), timing);
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return Forbid();
                }

                var memoryCache = GetMemberInfoMemoryCache();
                if (access == MemberInfoAccess.Church &&
                    memoryCache != null &&
                    memoryCache.TryGetValue(ChurchTreeCacheKey, out DistrictTreeViewModel cachedTree) &&
                    cachedTree != null)
                {
                    return Json(cachedTree);
                }

                TraceMemberInfoTreePhase("LoadDistrictTree", "acquire-connection", timing);
                service = GetConnection();
                TraceMemberInfoTreePhase("LoadDistrictTree", "connection-acquired", timing);
                var closedStatus = GetRequiredClosedCustomerTypeValue(service);
                TraceMemberInfoTreePhase("LoadDistrictTree", "closed-status-ready", timing);
                var descriptors = GetVisibleSmallGroupDescriptors(service, access);
                TraceMemberInfoTreePhase("LoadDistrictTree", "descriptors=" + descriptors.Count, timing);
                var memberships = FetchGroupMemberships(
                    service,
                    descriptors.Select(group => group.ListId).ToList(),
                    closedStatus);
                TraceMemberInfoTreePhase("LoadDistrictTree", "memberships=" + memberships.Count, timing);
                var includeUngrouped = access == MemberInfoAccess.Church;
                var allCurrentContactCount = includeUngrouped
                    ? MemberInfoCurrentContactCounter.Count(service, closedStatus)
                    : 0;
                TraceMemberInfoTreePhase(
                    "LoadDistrictTree",
                    "current-contact-count=" + allCurrentContactCount,
                    timing);

                var tree = DistrictTreeBuilder.Build(
                    descriptors,
                    memberships,
                    allCurrentContactCount,
                    includeUngrouped,
                    includeUngrouped ? "church" : "shepherd");
                TraceMemberInfoTreePhase("LoadDistrictTree", "tree-built", timing);

                if (includeUngrouped && memoryCache != null)
                {
                    var groupedIds = memberships
                        .Where(row => row.IsCurrent && Guid.TryParse(row.ContactId, out _))
                        .Select(row => Guid.Parse(row.ContactId))
                        .ToHashSet();
                    SetTreeCache(memoryCache, ChurchTreeCacheKey, tree, Math.Max(1, descriptors.Count));
                    SetTreeCache(
                        memoryCache,
                        ChurchGroupedCurrentIdsCacheKey,
                        groupedIds,
                        Math.Max(1, groupedIds.Count));
                }

                TraceMemberInfoTreePhase("LoadDistrictTree", "complete", timing);
                return Json(tree);
            }
            catch (Exception ex)
            {
                TraceMemberInfoTreePhase("LoadDistrictTree", "error=" + ex.GetType().Name, timing);
                return HandleError(ex, "MemberInfo.LoadDistrictTree");
            }
            finally
            {
                ReleaseConnection(service);
                TraceMemberInfoTreePhase("LoadDistrictTree", "connection-released", timing);
            }
        }

        /// <summary>
        /// 搜尋目前登入者可檢視的小組與會友樹狀資料。搜尋文字只是資料篩選條件，不能決定 profile、connector、
        /// owner、endpoint 或 credential；action 會先還原 server session 與授權範圍，並在投影前再次收斂 contact。
        /// ORG-CALL-00040 的 Package03 metadata gate 預設關閉；關閉時保持既有 metadata 相容路徑，開啟時只使用
        /// 一份 request-local typed snapshot 做 customertypecode 搜尋與列投影。取消、typed fault 或無可用 client
        /// 不得 fallback/retry；legacy CRM connection 仍由 finally 唯一釋放，沒有 request DTO、token 或 metadata 被保留。
        /// </summary>
        /// <param name="search">既有 browser 搜尋文字；不得作為 Dynamics 路由或授權依據。</param>
        /// <returns>只含已授權 contact 的樹狀搜尋 DTO，或既有禁止／錯誤結果。</returns>
        [HttpGet]
        [Route("/MemberInfo/SearchDistrictTree")]
        public async Task<IActionResult> SearchDistrictTree(string search)
        {
            IOrganizationService service = null;
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var useTypedCommitmentMetadata =
                DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration);

            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return Forbid();
                }

                if (string.IsNullOrWhiteSpace(search))
                {
                    return Json(new MemberInfoTreeSearchResultViewModel());
                }

                service = GetConnection();
                var typedCommitmentOptions = await LoadCommitmentTypeOptionsAsync(
                    configuration,
                    useTypedCommitmentMetadata,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                var closedStatus = GetRequiredClosedCustomerTypeValue(service, typedCommitmentOptions);
                var descriptors = GetVisibleSmallGroupDescriptors(service, access);
                var memberships = FetchGroupMemberships(
                    service,
                    descriptors.Select(group => group.ListId).ToList(),
                    closedStatus);
                // 搜尋採三段式安全資料流：先以「在籍且未結案」條件查出候選聯絡人，接著批次授權，
                // 最後才把通過授權者組成完整資料列。候選資料絕不直接進入回應，避免搜尋功能繞過可見範圍。
                // 此處一次取齊資料列所需欄位，授權完成後即可沿用同批 Entity，不必再逐人查詢 CRM。
                var statusValues = GetCustomerTypeValuesMatchingText(service, search, typedCommitmentOptions);
                var query = BuildStrictCurrentContactQuery(
                    GetTreeContactColumns(),
                    search,
                    closedStatus,
                    statusValues);
                var matchingContacts = RetrieveAllEntities(service, query);
                var matchingIds = matchingContacts
                    .Select(contact => contact.Id)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                // 小組長的批次授權以目前可見小組成員為邊界；全教會權限仍須通過在籍／未結案檢查。
                // 這一步回傳的 allowedIds 是後續樹節點與列資料唯一可信的聯絡人集合。
                var visibleMembershipContactIds = memberships
                    .Select(row => Guid.TryParse(row.ContactId, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                var allowedIds = CanViewContactsBatch(
                    matchingIds,
                    service,
                    closedStatus,
                    visibleMembershipContactIds);
                // 在補關係文字及建立 DTO 前再次用 allowedIds 收斂候選 Entity，確保完整列不會夾帶未授權資料。
                matchingContacts = matchingContacts.Where(contact => allowedIds.Contains(contact.Id)).ToList();
                var relations = BatchRelationGoals(service, matchingContacts.Select(contact => contact.Id).ToList());
                var rows = BuildMemberRows(service, matchingContacts, relations, typedCommitmentOptions);

                var result = MemberInfoTreeSearchBuilder.Build(
                    memberships,
                    allowedIds.Select(id => id.ToString()).ToList(),
                    access == MemberInfoAccess.Church,
                    rows);
                return Json(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HandleError(ex, "MemberInfo.SearchDistrictTree");
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 載入指定且已由 server scope 驗證的小組成員。listId 只可定位既有可見小組，不能取得另一個使用者的
        /// 授權、Dynamics profile、connector 或 credential。Package03 metadata sub-gate 關閉時維持 legacy 行為；
        /// 開啟時，此 action 只取得一次固定 profile/workload 的 request-local typed snapshot，並禁止對
        /// customertypecode 的 legacy fallback。取消與 typed fault 原樣傳播；finally 是 legacy connection 的唯一 owner。
        /// </summary>
        /// <param name="listId">browser 小組 locator；必須通過既有可見小組 allowlist。</param>
        /// <param name="search">既有成員篩選文字；不能影響組態、身分或連線選擇。</param>
        /// <returns>只含目前授權範圍內會友列的 JSON，或既有禁止／錯誤結果。</returns>
        [HttpGet]
        [Route("/MemberInfo/LoadGroupMembers")]
        public async Task<IActionResult> LoadGroupMembers(string listId, string search)
        {
            IOrganizationService service = null;
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var useTypedCommitmentMetadata =
                DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration);

            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
                {
                    return Forbid();
                }

                if (!Guid.TryParse(listId, out var requestedListId))
                {
                    return Forbid();
                }

                service = GetConnection();
                var descriptors = GetVisibleSmallGroupDescriptors(service, access);
                var visibleListIds = descriptors.Select(group => group.ListId).ToList();
                if (!MemberInfoScopeGuard.IsListAllowed(access, visibleListIds, listId))
                {
                    return Forbid();
                }

                // listId 的 target authorization 完成後才可開始任何 Package03 I/O。metadata 不帶會員資料，
                // 但仍屬 profile-bound outbound operation，不能讓未授權 browser locator 提前觸發它。
                var typedCommitmentOptions = await LoadCommitmentTypeOptionsAsync(
                    configuration,
                    useTypedCommitmentMetadata,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                var closedStatus = GetRequiredClosedCustomerTypeValue(service, typedCommitmentOptions);

                var memberships = FetchGroupMemberships(
                    service,
                    new[] { requestedListId.ToString() },
                    closedStatus);
                var memberIds = memberships
                    .Where(row => row.IsCurrent && Guid.TryParse(row.ContactId, out _))
                    .Select(row => Guid.Parse(row.ContactId))
                    .Distinct()
                    .ToList();
                var contacts = FetchContactsByIds(
                    service,
                    memberIds,
                    search,
                    closedStatus,
                    typedCommitmentOptions);
                var allowedIds = CanViewContactsBatch(
                    contacts.Select(contact => contact.Id).ToList(),
                    service,
                    closedStatus,
                    memberIds);
                contacts = contacts.Where(contact => allowedIds.Contains(contact.Id)).ToList();

                var relations = BatchRelationGoals(service, contacts.Select(contact => contact.Id).ToList());
                var rows = MemberInfoCommitmentTypeSort.OrderRows(
                    BuildMemberRows(service, contacts, relations, typedCommitmentOptions));
                return Json(new { data = rows });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HandleError(ex, "MemberInfo.LoadGroupMembers");
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 載入目前 Church scope 可見的未分組會員頁面。此 action 只接受既有 page/search input，並先由 server
        /// session 還原使用者與授權範圍；browser 不可選擇 Dynamics profile、connector、owner、endpoint 或 credential。
        /// ORG-CALL-00024 的 Package02 aggregate base/sub-gate 與 ORG-CALL-00040 的 Package03 metadata base/sub-gate
        /// 都是 deployment-owned 且預設關閉：關閉時各自維持既有相容路徑且不建立 typed client／host／pool；開啟時，
        /// aggregate 只替換 non-empty commitment count，而 metadata 只建立一次 request-local typed snapshot 供排序、
        /// label 與 closed-status 使用。typed fault 或取消必須原樣離開，不得 fallback、retry 或混合 legacy metadata。
        /// legacy CRM connection 的 acquire/release 仍由 action local <c>service</c> 與 <c>finally</c> 唯一擁有；
        /// DTO、metadata、exception、cancellation token 與 response model 都不寫入 static、cache、Session、singleton
        /// 或 background work，避免跨使用者、profile 或 generation 泄漏。此為 local-only candidate，並不代表 CE、
        /// 流量切換、P7.5 或 P8 已完成。
        /// </summary>
        [HttpGet]
        [Route("/MemberInfo/LoadUngroupedMembers")]
        public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)
        {
            IOrganizationService service = null;

            // configuration 只屬 deployment composition，不包含使用者、Session、connector、credential 或 profile
            // 選擇權。先讀 gate 只決定本 request 是否可嘗試 ORG-CALL-00024 typed count；false 仍走既有相容
            // count，且不會建立 typed client/process host/pool。真正 Church scope、contact authorization 和 legacy
            // page responsibility 仍在後方既有流程，避免 gate 本身成為 caller-controlled authority。
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var useTypedUngroupedCommitmentCount =
                DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(configuration);
            var useTypedCommitmentMetadata =
                DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration);

            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                if (access != MemberInfoAccess.Church)
                {
                    return Forbid();
                }

                service = GetConnection();
                var typedCommitmentOptions = await LoadCommitmentTypeOptionsAsync(
                    configuration,
                    useTypedCommitmentMetadata,
                    HttpContext.RequestAborted).ConfigureAwait(false);
                var closedStatus = GetRequiredClosedCustomerTypeValue(service, typedCommitmentOptions);
                var descriptors = GetVisibleSmallGroupDescriptors(service, access);
                var usesCommitmentSort = TryGetCommitmentTypeSort(loadOptions, out var commitmentDescending);
                // enabled typed count 本身使用 Data8 的即時 membership snapshot；為避免 legacy empty/page branch
                // 讀到最長三分鐘前的 grouped-id cache，這個 request 一律重新取得同一 server-derived exclusion set。
                // CRM 讀取本來就沒有跨多個獨立 query 的 transaction snapshot；完整 atomic page snapshot 必須由未來
                // 同時遷移 empty/page capability 的 child 處理，尚未具備該證據前 gate 仍維持 false。
                var groupedIds = GetChurchGroupedCurrentIds(
                    service,
                    descriptors,
                    closedStatus,
                    bypassCache: useTypedUngroupedCommitmentCount && usesCommitmentSort);
                var statusValues = GetCustomerTypeValuesMatchingText(service, search, typedCommitmentOptions);
                UngroupedContactPage contactPage;
                if (usesCommitmentSort)
                {
                    contactPage = await LoadUngroupedCommitmentTypePageAsync(
                        service,
                        GetTreeContactColumns(),
                        search,
                        groupedIds,
                        closedStatus,
                        statusValues,
                        loadOptions,
                        commitmentDescending,
                        configuration,
                        useTypedUngroupedCommitmentCount,
                        typedCommitmentOptions,
                        HttpContext.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    var query = BuildUngroupedContactQuery(
                        GetTreeContactColumns(),
                        search,
                        groupedIds,
                        closedStatus,
                        statusValues,
                        loadOptions);
                    var page = service.RetrieveMultiple(query);
                    var skip = loadOptions?.Skip > 0 ? loadOptions.Skip : 0;
                    contactPage = new UngroupedContactPage
                    {
                        Contacts = page.Entities.ToList(),
                        TotalCount = page.TotalRecordCount >= 0
                            ? page.TotalRecordCount
                            : skip + page.Entities.Count + (page.MoreRecords ? 1 : 0)
                    };
                }

                var contacts = contactPage.Contacts;
                var allowedIds = CanViewContactsBatch(
                    contacts.Select(contact => contact.Id).ToList(),
                    service,
                    closedStatus);
                contacts = contacts.Where(contact => allowedIds.Contains(contact.Id)).ToList();

                var relations = BatchRelationGoals(service, contacts.Select(contact => contact.Id).ToList());
                var rows = BuildMemberRows(service, contacts, relations, typedCommitmentOptions);
                return Json(new { data = rows, totalCount = contactPage.TotalCount });
            }
            // 取消必須原樣交給 ASP.NET Core 與 typed executor/lease owner；不可把取消轉成 legacy response、
            // retry 或一般頁面錯誤，否則會掩蓋下游對不確定 transport 的 deterministic cleanup。
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HandleError(ex, "MemberInfo.LoadUngroupedMembers");
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        [HttpGet]
        public object LoadMemberInfoList(DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();
                var access = GetAccess();
                var photoOnly = IsPhotoOnlyRequested();

                if (access == MemberInfoAccess.Church)
                {
                    return LoadChurchMemberRows(loadOptions, photoOnly);
                }

                if (access == MemberInfoAccess.ShepherdList)
                {
                    var rows = LoadShepherdMemberRows(photoOnly);
                    return DataSourceLoader.Load(rows, loadOptions);
                }

                return DataSourceLoader.Load(new List<MemberInfoListRowViewModel>(), loadOptions);
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadMemberInfoList");
            }
        }

        [HttpGet]
        public IActionResult Detail(string contactId)
        {
            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return Forbid();
                }

                var service = ToolUtility.m_Crm2011OrganizationService;
                var contact = service.Retrieve("contact", contactGuid, GetContactDetailColumns());
                // CRM 可能以 DateTime 的 Year=1 表示未填或無效日期；在 ViewModel 邊界正規化為 null，
                // 讓 Razor 只需處理「有效生日／未設定」兩種狀態，不會顯示 0001/01/01。
                var birthDate = contact.GetAttributeValue<DateTime?>("birthdate");
                if (birthDate.HasValue && birthDate.Value.Year <= 1)
                {
                    birthDate = null;
                }

                var model = new MemberInfoDetailViewModel
                {
                    ContactId = contactGuid.ToString(),
                    FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                    Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                    Address = ToolUtility.GetEntityStringAttribute(contact, "address2_line1"),
                    Gender = GetOptionSetText(contact, "gendercode"),
                    BirthDate = birthDate,
                    MembershipStatus = GetOptionSetText(contact, "customertypecode"),
                    SpiritualIdentity = GetOptionSetText(contact, "new_spiriitual_identity"),
                    RelationGoals = GetRelationGoals(contactGuid),
                    AvatarSource = ResolveContactAvatarSource(contact)
                };

                // 下拉選項(會員身分/信仰狀態)：用「共用快取」的 OptionSet 服務一次取得全部選項，
                // 避免每次開細節都打 CRM metadata；目前值以整數送給前端 <select> 預選。
                var optionSvc = GetSharedOptionSetService();
                model.MembershipStatusOptions = BuildOptionItems(optionSvc, "customertypecode");
                model.SpiritualIdentityOptions = BuildOptionItems(optionSvc, "new_spiriitual_identity");

                var membershipValue = ToolUtility.GetOptionSetAttribute(contact, "customertypecode");
                model.MembershipStatusValue = membershipValue >= 0 ? membershipValue : (int?)null;
                var spiritualValue = ToolUtility.GetOptionSetAttribute(contact, "new_spiriitual_identity");
                model.SpiritualIdentityValue = spiritualValue >= 0 ? spiritualValue : (int?)null;

                return PartialView("_MemberDetailPopup", model);
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.Detail");
            }
        }

        /// <summary>
        /// 讀取目前使用者可檢視 contact 的個人出席紀錄。P7.4 present-read base/sub gate 關閉時，會完整保留既有
        /// ToolUtility/CRM SDK 路徑以維持畫面相容；gate 開啟後，只在 deployment configuration 的第一個決策、
        /// server session 還原、contact GUID parse 與 <see cref="CanViewContact"/> 都完成後，才使用獨立 DTO-only
        /// ProductClient path。browser 的 contactId 永遠只是 locator，不能決定 profile、connector、owner、endpoint
        /// 或 credential。typed result、ViewModel list 與 DataSourceLoader input 均是 request-local，沒有 cache、
        /// retry、fallback 或外部資源 owner；client/lease/transport cleanup 仍屬 process host。
        /// </summary>
        /// <param name="contactId">瀏覽器提供的 target locator；必須由 server session scope 驗證才可使用。</param>
        /// <param name="loadOptions">DevExtreme 載入選項，只會套用於本次 action 建立的 local row list。</param>
        /// <returns>既有 legacy 或 gated typed path 產生的資料來源結果；取消會原樣傳遞給 ASP.NET Core。</returns>
        [HttpGet]
        public async Task<object> LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)
        {
            // IConfiguration 是 deployment-owned singleton；在 false gate 時，這是唯一新工作，絕不 hydration session、
            // parse target、組成 ProductClient、取得 host/pool 或送出 I/O，故 rollback 不會遺留 request/transport state。
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (!DonationDynamicsAccessBootstrap.IsPackage02MemberInfoPresentReadEnabled(configuration))
            {
                return LoadContactPresentRecordsLegacy(contactId, loadOptions);
            }

            try
            {
                return await LoadContactPresentRecordsTypedAsync(contactId, loadOptions, configuration)
                    .ConfigureAwait(false);
            }
            // request 取消必須離開 generic handler，讓 ASP.NET Core、executor 與 process-host owner 依既有順序
            // fault/evict/release 不確定 transport；將取消包裝為一般錯誤會掩蓋 cleanup 結果並可能誘發上游重送。
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HandleError(ex, "MemberInfo.LoadContactPresentRecords");
            }
        }

        /// <summary>
        /// 維持 gate=false 的既有 ToolUtility 相容路徑。此 helper 不供 typed branch 呼叫，避免已啟用 capability
        /// 於 fault、無 client 或資料異常時回到 SDK 查詢。legacy CRM service 的取得與釋放語意保持原 action 行為；
        /// 本次 P7.4 child 沒有變更其 query、cache、retry 或 traffic，未來完整移除仍屬 P7.5 gate。
        /// </summary>
        /// <param name="contactId">既有 browser locator，由 legacy path 自行完成原有授權檢查。</param>
        /// <param name="loadOptions">既有 DevExtreme 載入選項。</param>
        /// <returns>與原 action 相同的 legacy DataSourceLoader result。</returns>
        private object LoadContactPresentRecordsLegacy(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return DataSourceLoader.Load(new List<ContactPresentRecordRow>(), loadOptions);
                }

                var service = ToolUtility.m_Crm2011OrganizationService;
                var contact = service.Retrieve("contact", contactGuid, new ColumnSet("fullname"));
                var fullName = ToolUtility.GetEntityStringAttribute(contact, "fullname");

                var rows = new List<ContactPresentRecordRow>();

                // ✅ 直接以「聯絡人」lookup 查該連絡人的所有個人聚會與靈修記錄(new_present_record)。
                // 先前用的 RetrievePresentRecordByFetchXmlAndContainEpiredDate 只會回「有關懷期限」的紀錄
                // （多為新人跟進單），一般週報出席紀錄沒有關懷期限 → 查不到 → 前台空白。
                var presentQuery = new QueryExpression("new_present_record")
                {
                    ColumnSet = new ColumnSet(
                        "new_present_recordid",
                        "new_sunday_present_this_week",
                        "new_group_present_this_week",
                        "new_explanation",
                        "new_sunday_date")
                };
                presentQuery.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactGuid);
                presentQuery.AddOrder("new_sunday_date", OrderType.Descending);

                var records = service.RetrieveMultiple(presentQuery);
                foreach (var record in records.Entities)
                {
                    rows.Add(new ContactPresentRecordRow
                    {
                        PresentRecordId = record.Id.ToString(),
                        FullName = fullName,
                        SundayDate = record.GetAttributeValue<DateTime?>("new_sunday_date") is DateTime sd && sd.Year > 1 ? sd : (DateTime?)null,
                        Sunday = ToolUtility.GetEntityIntAttribute(record, "new_sunday_present_this_week") > 0,
                        SmallGroup = ToolUtility.GetEntityIntAttribute(record, "new_group_present_this_week") > 0,
                        PrayItem = ToolUtility.GetEntityStringAttribute(record, "new_explanation")
                    });
                }

                return DataSourceLoader.Load(rows, loadOptions);
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadContactPresentRecords");
            }
        }

        /// <summary>
        /// 執行 gate=true 的唯一 typed DTO path。必須先完成既有 session 資料還原與 contact authorization，才可
        /// 組成 deployment-owned client；不讀取 contact Entity、不建立 QueryExpression、不使用 ToolUtility 或
        /// GetConnection，也不 catch/retry/fallback。任何 typed fault 會回到公開 action 的受控 generic handler，
        /// 而 cancellation 原樣離開；所有 row mapping 完成後才呼叫 DataSourceLoader，杜絕 partial result publication。
        /// </summary>
        /// <param name="contactId">只作已授權 target locator 的 browser 字串。</param>
        /// <param name="loadOptions">只套用於完成的 action-local row list。</param>
        /// <param name="configuration">公開 action 已讀取的 deployment-owned configuration，不能由 browser 代換。</param>
        /// <returns>純 scalar typed DTO 投影後的 DevExtreme 資料來源結果。</returns>
        private async Task<object> LoadContactPresentRecordsTypedAsync(
            string contactId,
            DataSourceLoadOptions loadOptions,
            IConfiguration configuration)
        {
            EnsureCorrectUserData();
            if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
            {
                return DataSourceLoader.Load(new List<ContactPresentRecordRow>(), loadOptions);
            }

            var presentRecordClient = DonationDynamicsAccessBootstrap.TryCreatePackage02MemberInfoPresentReadClient(configuration)
                ?? throw new InvalidOperationException(
                    "The Package02 MemberInfo present-record typed client was unavailable after the deployment gate was enabled.");
            var service = new Package02MemberInfoPresentRecordReadService(
                presentRecordClient,
                DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias);
            var typedResult = await service.RetrieveAsync(contactGuid, HttpContext.RequestAborted).ConfigureAwait(false);
            var rows = new List<ContactPresentRecordRow>();
            foreach (var record in typedResult.GetRows())
            {
                rows.Add(new ContactPresentRecordRow
                {
                    PresentRecordId = record.PresentRecordId.ToString(),
                    FullName = record.ContactFullName,
                    SundayDate = record.SundayDate,
                    Sunday = record.Sunday,
                    SmallGroup = record.SmallGroup,
                    PrayItem = record.PrayItem
                });
            }

            return DataSourceLoader.Load(rows, loadOptions);
        }

        /// <summary>
        /// 以目前已授權的聯絡人讀取上課紀錄。Package01 關閉時保留既有 contact fullname 與
        /// ToolUtility 查詢語意；開啟時不再回讀 CRM contact，而以 null 顯示名稱和
        /// <see cref="HttpContext.RequestAborted"/> 呼叫唯一的非同步 typed projection。這可避免
        /// SDK Entity、同步阻塞與已取消 request 的資料延長到另一位使用者；回應 model 只在本 action
        /// 的 request-local 集合建立，例外仍走既有受控錯誤處理。
        /// </summary>
        /// <param name="contactId">既有授權檢查使用的聯絡人識別碼，無效或不可見時回傳空集合。</param>
        /// <param name="loadOptions">DevExtreme 載入選項，只套用於本次 request-local 顯示集合。</param>
        /// <returns>可供 DevExtreme 載入的上課紀錄資料或既有安全錯誤回應。</returns>
        [HttpGet]
        public async Task<object> LoadContactStorLessons(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return DataSourceLoader.Load(new List<MemberInfoStorLessonRow>(), loadOptions);
                }

                var rows = new List<MemberInfoStorLessonRow>();
                var configuration = HttpContext.RequestServices.GetService<IConfiguration>();
                var queryService = new StorLessonQueryService(ToolUtility, configuration ?? new ConfigurationBuilder().Build());
                string? fullName = null;
                if (!queryService.IsPackage01Enabled)
                {
                    var service = ToolUtility.m_Crm2011OrganizationService;
                    var contact = service.Retrieve("contact", contactGuid, new ColumnSet("fullname"));
                    fullName = ToolUtility.GetEntityStringAttribute(contact, "fullname");
                }

                var projections = await queryService.GetByContactAsync(
                    fullName,
                    contactGuid.ToString(),
                    HttpContext.RequestAborted).ConfigureAwait(false);

                foreach (var row in projections)
                {
                    rows.Add(new MemberInfoStorLessonRow
                    {
                        StorLessonsEntityId = row.StorLessonsEntityId,
                        DiscipleLessonsName = row.DiscipleLessonsName,
                        StageName = row.StageName,
                        CurrentComplete = row.CurrentComplete,
                        DiscipleLessonsDateTime = row.DiscipleLessonsDateTime
                    });
                }

                return DataSourceLoader.Load(rows, loadOptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 只有非取消故障可轉成既有錯誤回應；取消例外必須保留原始 token 與堆疊直接交回
                // ASP.NET Core，讓 request-local ProductClient/lease 的既有 owner 完成釋放，且不會
                // 建立可能在用戶端離線後無法送達的資料或診斷回應。
                return HandleError(ex, "MemberInfo.LoadContactStorLessons");
            }
        }

        [HttpGet]
        [Route("/MemberInfo/GetContactImage")]
        public IActionResult GetContactImage(string contactId, int size = 80, bool fit = false)
        {
            IOrganizationService service = null;

            try
            {
                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return GetDefaultImage();
                }

                var returnOriginal = size <= 0;
                var thumbSize = returnOriginal ? 0 : Math.Clamp(size, 32, 256);
                var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                var cacheKey = returnOriginal
                    ? $"member-info-contact-image-full:{contactGuid:N}"
                    : (fit
                        ? $"member-info-contact-image-fit:{contactGuid:N}:{thumbSize}"
                        : $"member-info-contact-image-thumb:{contactGuid:N}:{thumbSize}");

                if (memoryCache != null &&
                    memoryCache.TryGetValue(cacheKey, out byte[] cachedBytes) &&
                    cachedBytes != null)
                {
                    ApplyImageResponseCacheHeaders();
                    return File(cachedBytes, "image/jpeg");
                }

                service = GetConnection();
                var contact = service.Retrieve("contact", contactGuid, new ColumnSet(
                    "entityimage",
                    "gendercode",
                    ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                if (contact.Contains("entityimage") && contact["entityimage"] != null)
                {
                    var originalBytes = (byte[])contact["entityimage"];
                    var outputBytes = returnOriginal
                        ? originalBytes
                        : (fit ? CreateFitThumbnail(originalBytes, thumbSize) : CreateThumbnailIfNeeded(originalBytes, thumbSize));

                    memoryCache?.Set(cacheKey, outputBytes, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                        SlidingExpiration = TimeSpan.FromMinutes(10),
                        Size = Math.Max(1, outputBytes.Length / 1024)
                    });

                    ApplyImageResponseCacheHeaders();
                    return File(outputBytes, "image/jpeg");
                }

                // 無照片 → 依性別回傳上半身剪影
                var linePictureUrl = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                    contact.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                if (!string.IsNullOrEmpty(linePictureUrl))
                {
                    Response.Headers["Cache-Control"] = "private, max-age=300";
                    return Redirect(linePictureUrl);
                }

                var gender = contact.GetAttributeValue<OptionSetValue>("gendercode")?.Value;
                return Content(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.ForGender(gender), "image/svg+xml");
            }
            catch
            {
                return GetDefaultImage();
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        [HttpPost]
        [Route("/MemberInfo/GetContactImagesBatch")]
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
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var uncachedGuids = new List<Guid>();

                // [計時診斷] 暫時性效能量測，僅讀取耗時與位元組大小，不影響任何邏輯/安全。確認瓶頸後即可移除。
                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                long preMs = 0, connMs = 0, crmMs = 0, imgTicks = 0, inBytes = 0, outBytes = 0;
                int withPhoto = 0;
                var swPre = System.Diagnostics.Stopwatch.StartNew(); // [計時診斷] 授權檢查(CanViewContact)+快取查詢迴圈耗時

                // 先一次算出「可檢視」名單，取代迴圈內逐人 CanViewContact 的 N+1 CRM 查詢；授權判斷邏輯與逐筆版完全相同。
                var parsedGuids = new List<Guid>();
                foreach (var id in request.ContactIds.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (Guid.TryParse(id, out var g)) parsedGuids.Add(g);
                }
                var allowedSet = CanViewContactsBatch(parsedGuids);

                foreach (var guid in parsedGuids)
                {
                    if (!allowedSet.Contains(guid))
                    {
                        continue;
                    }

                    var cacheKey = $"member-info-contact-image-thumb:{guid:N}:{thumbSize}";
                    if (memoryCache != null &&
                        memoryCache.TryGetValue(cacheKey, out byte[] cachedBytes) &&
                        cachedBytes != null)
                    {
                        var key = guid.ToString();
                        result[key] = "data:image/jpeg;base64," + Convert.ToBase64String(cachedBytes);
                        sources[key] = "primary";
                    }
                    else
                    {
                        uncachedGuids.Add(guid);
                    }
                }
                swPre.Stop(); preMs = swPre.ElapsedMilliseconds; // [計時診斷]
                int cacheHitCount = result.Count; // [計時診斷] 迴圈結束時 result 內全是快取命中

                if (uncachedGuids.Count > 0)
                {
                    var swConn = System.Diagnostics.Stopwatch.StartNew();
                    service = GetConnection();
                    swConn.Stop(); connMs = swConn.ElapsedMilliseconds; // [計時診斷] 連線池取得連線耗時
                    foreach (var chunk in uncachedGuids.Chunk(CrmInClauseChunkSize))
                    {
                        var query = new QueryExpression("contact")
                        {
                            ColumnSet = new ColumnSet(
                                "contactid",
                                "entityimage",
                                "gendercode",
                                ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute)
                        };
                        query.Criteria.AddCondition(
                            "contactid",
                            ConditionOperator.In,
                            chunk.Select(guid => (object)guid).ToArray());

                        var swCrm = System.Diagnostics.Stopwatch.StartNew();
                        var contacts = service.RetrieveMultiple(query);
                        swCrm.Stop();
                        crmMs += swCrm.ElapsedMilliseconds; // [計時診斷] 分塊 RetrieveMultiple(含 entityimage 傳輸) 累計耗時
                        foreach (var contact in contacts.Entities)
                        {
                            if (contact.Contains("entityimage") && contact["entityimage"] != null)
                            {
                                var originalBytes = (byte[])contact["entityimage"];
                                var _imgStart = System.Diagnostics.Stopwatch.GetTimestamp(); // [計時診斷] 累計解碼/縮圖耗時
                                var outputBytes = CreateThumbnailIfNeeded(originalBytes, thumbSize);
                                imgTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _imgStart;
                                inBytes += originalBytes.Length; outBytes += outputBytes.Length; withPhoto++; // [計時診斷] 原圖/縮圖位元組
                                var cacheKey = $"member-info-contact-image-thumb:{contact.Id:N}:{thumbSize}";

                                memoryCache?.Set(cacheKey, outputBytes, new MemoryCacheEntryOptions
                                {
                                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                                    SlidingExpiration = TimeSpan.FromMinutes(10),
                                    Size = Math.Max(1, outputBytes.Length / 1024)
                                });

                                var key = contact.Id.ToString();
                                result[key] = "data:image/jpeg;base64," + Convert.ToBase64String(outputBytes);
                                sources[key] = "primary";
                            }
                            else
                            {
                                // 無照片：批次直接帶回性別剪影(SVG data URI)，前端就不必再逐筆打 GetContactImage。
                                var linePictureUrl = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                                    contact.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                                if (!string.IsNullOrEmpty(linePictureUrl))
                                {
                                    var key = contact.Id.ToString();
                                    result[key] = linePictureUrl;
                                    sources[key] = "line";
                                }
                                else
                                {
                                    var gender = contact.GetAttributeValue<OptionSetValue>("gendercode")?.Value;
                                    var key = contact.Id.ToString();
                                    result[key] = ToSvgDataUri(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.ForGender(gender));
                                    sources[key] = "fallback";
                                }
                            }
                        }
                    }
                }

                // [計時診斷] 一行彙總：total/pre/conn/crm/img 各自耗時 + 原圖(inKB)/縮圖(outKB)大小，用來判定瓶頸在 CRM 傳輸還是本地解碼
                var imgMs = imgTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                // [計時診斷] 改用 Debug.WriteLine（[Conditional("DEBUG")]）：Release 建置時整行(含字串)會被編譯器移除，
                // 不會寫入 Logs\Trace.log；僅在 Debug 模式診斷時輸出。Stopwatch 量測仍會跑但不產生任何輸出。
                System.Diagnostics.Debug.WriteLine(
                    $"[BatchImg-Timing] ep=MemberInfo total={swTotal.ElapsedMilliseconds}ms pre={preMs}ms conn={connMs}ms crm={crmMs}ms img={imgMs}ms | req={request.ContactIds.Length} cacheHit={cacheHitCount} crmQ={uncachedGuids.Count} photo={withPhoto} inKB={inBytes / 1024} outKB={outBytes / 1024}");

                Response.Headers["Cache-Control"] = "private, no-store";
                return Json(new { success = true, images = result, sources });
            }
            catch
            {
                return Json(new { success = false, images = new Dictionary<string, string>(), sources = new Dictionary<string, string>() });
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 取得「需要重新同步」的候選聯絡人 ID 清單（在籍、且有 new_lineid 者；不論目前有無 new_line_picture_url）。
        /// 已清空/從未有照片者也要納入，以便偵測對方「解除封鎖/重新加好友/新增照片」後補回。
        /// 前端據此分批呼叫 ResyncLineProfiles，以便即時顯示進度。僅限「全教會」管理者。
        /// GET: /MemberInfo/ResyncLineCandidateIds
        /// </summary>
        [HttpGet]
        [Route("/MemberInfo/ResyncLineCandidateIds")]
        public IActionResult ResyncLineCandidateIds()
        {
            IOrganizationService service = null;
            try
            {
                if (GetAccess() != MemberInfoAccess.Church)
                {
                    return Forbid();
                }

                service = GetConnection();
                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    TopCount = 5000
                };
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.Criteria.AddCondition("new_lineid", ConditionOperator.NotNull);
                // 不限制 new_line_picture_url 必須有值：已清空/從未有照片者也要重新檢查，
                // 因為對方可能已解除封鎖/重新加好友/新增照片，此時要重新向 LINE 取得並補回。
                query.Orders.Add(new OrderExpression("modifiedon", OrderType.Ascending));

                var contacts = service.RetrieveMultiple(query);
                var ids = contacts.Entities.Select(c => c.Id.ToString()).ToList();
                return Json(new { success = true, ids });
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.ResyncLineCandidateIds");
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 對「前端傳來的一批聯絡人 ID」重新同步 LINE 資料（供分批/即時進度使用）。
        /// 有照片網址者先探測該網址能否顯示圖片；「確定失效」或「根本沒有網址」者，皆依 new_lineid 取最新 Profile，
        /// 更新 new_line_picture_url / new_line_status_message / new_line_displayname（set-or-clear）。
        /// 取不到 Profile（多半未加官方帳號好友→403/404）：有舊網址者清空失效網址；本來就沒網址者維持無照片(下次再試)。
        /// 僅限「全教會」管理者。POST: /MemberInfo/ResyncLineProfiles（body: { contactIds: [...] }）
        /// </summary>
        [HttpPost]
        [Route("/MemberInfo/ResyncLineProfiles")]
        public async Task<IActionResult> ResyncLineProfiles([FromBody] BatchImageRequest request)
        {
            IOrganizationService service = null;

            try
            {
                if (GetAccess() != MemberInfoAccess.Church)
                {
                    return Forbid();
                }

                var token = GetResyncLineChannelAccessToken();
                if (string.IsNullOrEmpty(token))
                {
                    return Json(new { success = false, message = "未設定 LINE Channel Access Token" });
                }

                var guids = new List<Guid>();
                if (request?.ContactIds != null)
                {
                    foreach (var raw in request.ContactIds)
                    {
                        if (Guid.TryParse(raw, out var g)) { guids.Add(g); }
                    }
                }
                if (guids.Count == 0)
                {
                    return Json(new { success = true, scanned = 0, okValid = 0, updated = 0, cleared = 0, noPhoto = 0, inconclusive = 0, reasons = new List<string>() });
                }

                service = GetConnection();

                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet(
                        "contactid",
                        "new_lineid",
                        ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute)
                };
                query.Criteria.AddCondition("contactid", ConditionOperator.In, guids.Select(g => (object)g).ToArray());

                var contacts = service.RetrieveMultiple(query);
                var candidates = contacts.Entities
                    .Where(c => !string.IsNullOrWhiteSpace(c.GetAttributeValue<string>("new_lineid")))
                    .ToList();

                // 第一步：對「有存照片網址」者，嚴謹判斷該網址是否「真的能顯示圖片」(平行探測、限流)。
                // 0 = 可顯示(2xx 且 image/*)；1 = 確定無法顯示(有回應但非圖片/非 2xx)；2 = 無法判定(逾時/連線錯誤)；3 = 根本沒有網址(待向 LINE 查詢)。
                var probe = new System.Collections.Concurrent.ConcurrentDictionary<Guid, int>();
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                using (var gate = new System.Threading.SemaphoreSlim(20))
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                    await Task.WhenAll(candidates.Select(async c =>
                    {
                        var url = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                            c.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));
                        if (string.IsNullOrEmpty(url)) { probe[c.Id] = 3; return; } // 沒網址 → 直接走 getProfile（不需探測）
                        await gate.WaitAsync();
                        try { probe[c.Id] = await ProbeImageDisplayableAsync(http, url); }
                        finally { gate.Release(); }
                    }));
                }

                // 第二步：
                //  - 可顯示(0) → 不動。
                //  - 無法判定(2) → 不動，避免誤清。
                //  - 確定失效(1) 或 沒有網址(3) → 依 new_lineid 取最新 Profile：取到照片就更新(補回)；確認無照片則「有舊網址者清空、本來就沒網址者維持無照片」。
                //    取不到 Profile(未加好友/封鎖→403/404)：有舊網址者清空失效網址；本來就沒網址者維持(下次再試 → 對方解除封鎖/重新加好友後即可補回)。
                int okValid = 0, updated = 0, cleared = 0, noPhoto = 0, inconclusive = 0;
                var reasons = new List<string>();
                using (var lineProcessor = new LineMessagingProcessor.LineMessagingProcessorClass(token))
                {
                    foreach (var contact in candidates)
                    {
                        var state = probe.TryGetValue(contact.Id, out var s) ? s : 2;
                        if (state == 0) { okValid++; continue; }       // 照片正常顯示 → 不動
                        if (state == 2) { inconclusive++; continue; }  // 暫時無法判定 → 不動，避免誤清

                        var hadUrl = (state == 1); // 1=原有(失效)網址；3=本來就沒網址
                        var lineId = contact.GetAttributeValue<string>("new_lineid");
                        try
                        {
                            var profile = await lineProcessor.GetUserProfileAsync(lineId);
                            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
                            {
                                var newPic = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(profile.PictureUrl);
                                var upd = new Entity("contact") { Id = contact.Id };
                                // set-or-clear：取回新照片就更新(補回)；LINE 已無照片則寫 null 清空。
                                upd[ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute] =
                                    string.IsNullOrEmpty(newPic) ? null : newPic;
                                upd["new_line_status_message"] =
                                    string.IsNullOrWhiteSpace(profile.StatusMessage) ? null : profile.StatusMessage;
                                if (!string.IsNullOrWhiteSpace(profile.DisplayName))
                                {
                                    upd["new_line_displayname"] = profile.DisplayName;
                                }
                                service.Update(upd);
                                if (!string.IsNullOrEmpty(newPic)) { updated++; }   // 取到/補回照片
                                else if (hadUrl) { cleared++; }                     // 原有網址、確認無照片 → 清空
                                else { noPhoto++; }                                 // 本來無、現仍無
                                continue;
                            }
                            if (reasons.Count < 6) { reasons.Add("profile 回應無 userId"); }
                        }
                        catch (Line.Messaging.LineResponseException lre)
                        {
                            if (reasons.Count < 6) { reasons.Add($"getProfile {(int)lre.StatusCode}: {lre.Message}"); }
                        }
                        catch (Exception ex)
                        {
                            if (reasons.Count < 6) { reasons.Add("getProfile " + ex.GetType().Name + ": " + ex.Message); }
                        }

                        // 取不到 Profile：
                        if (hadUrl)
                        {
                            // 原有網址已確定失效又拿不到新資料 → 清空，改顯示性別剪影、無標示、不計入「顯示照片」。
                            var clear = new Entity("contact") { Id = contact.Id };
                            clear[ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute] = null;
                            service.Update(clear);
                            cleared++;
                        }
                        else
                        {
                            // 本來就沒網址、現在也拿不到(多為未加好友/封鎖) → 維持無照片、不寫入。下次再試。
                            noPhoto++;
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    scanned = candidates.Count,
                    okValid,
                    updated,
                    cleared,
                    noPhoto,
                    inconclusive,
                    reasons = reasons.Distinct().Take(6).ToList()
                });
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.ResyncLineProfiles");
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 讀取目前組織對應的 LINE Channel Access Token（LineMessaging:{Organization}:ChannelAccessToken，
        /// 找不到則退回 DefaultOrganization）。供 ResyncLineProfiles 呼叫 LINE Profile API 使用。
        /// </summary>
        private string GetResyncLineChannelAccessToken()
        {
            try
            {
                var config = HttpContext?.RequestServices?
                    .GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))
                    as Microsoft.Extensions.Configuration.IConfiguration;
                if (config == null) { return string.Empty; }

                var organization = config["CrmConnection:Organization"];
                if (!string.IsNullOrEmpty(organization))
                {
                    var configKey = char.ToUpper(organization[0]) + organization.Substring(1).ToLower();
                    var token = config[$"LineMessaging:{configKey}:ChannelAccessToken"];
                    if (!string.IsNullOrEmpty(token)) { return token; }
                }

                var defaultOrg = config["LineMessaging:DefaultOrganization"] ?? "Jesus";
                return config[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 嚴謹判斷一個圖片網址是否「真的能顯示圖片」（只讀回應標頭、不下載內容）。
        /// 回傳 0 = 可顯示（2xx 且 Content-Type 為 image/*）；1 = 確定無法顯示（有回應但非圖片或非 2xx）；2 = 無法判定（逾時／連線錯誤）。
        /// </summary>
        private static async Task<int> ProbeImageDisplayableAsync(System.Net.Http.HttpClient http, string url)
        {
            try
            {
                using (var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                {
                    var contentType = resp.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
                    var displayable = resp.IsSuccessStatusCode
                        && contentType.StartsWith("image", StringComparison.OrdinalIgnoreCase);
                    return displayable ? 0 : 1;
                }
            }
            catch
            {
                return 2;
            }
        }

        /// <summary>
        /// 上傳並更新「指定會友」的大頭照（會友細節彈窗用）。
        /// 與 /Personal/UploadContactImage(只改登入者自己)不同：這裡可改「其他會友」，
        /// 因此先用 CanViewContact 把關——能在清單看到/開啟該會友細節的人才可變更其照片。
        /// POST: /MemberInfo/UploadContactImage
        /// </summary>
        [HttpPost]
        [Route("/MemberInfo/UploadContactImage")]
        public IActionResult UploadContactImage(string contactId, IFormFile imageFile)
        {
            IOrganizationService service = null;

            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return Json(new { success = false, message = "無權限變更此會友的大頭照" });
                }

                if (imageFile == null || imageFile.Length == 0)
                {
                    return Json(new { success = false, message = "請選擇要上傳的圖片檔案" });
                }

                const long maxFileSize = 5 * 1024 * 1024; // 5MB
                if (imageFile.Length > maxFileSize)
                {
                    return Json(new { success = false, message = "圖片檔案過大，請選擇小於 5MB 的圖片" });
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                var fileExtension = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant() ?? string.Empty;
                var contentType = imageFile.ContentType?.ToLowerInvariant() ?? string.Empty;

                if (!allowedExtensions.Contains(fileExtension) || !allowedContentTypes.Contains(contentType))
                {
                    return Json(new { success = false, message = "只支援 JPG、PNG、GIF 格式的圖片" });
                }

                var imageBytes = NormalizeUploadedImage(imageFile);

                service = GetConnection();
                var contactToUpdate = new Entity("contact", contactGuid);
                contactToUpdate["entityimage"] = imageBytes;
                service.Update(contactToUpdate);

                // 清掉本控制器對此會友的影像快取，讓表格縮圖與彈窗大圖都能取得新照片。
                InvalidateMemberImageCache(contactGuid);

                return Json(new
                {
                    success = true,
                    message = "大頭照上傳成功！",
                    imageVersion = DateTime.Now.Ticks
                });
            }
            catch (System.ServiceModel.FaultException faultEx)
            {
                return Json(new { success = false, message = "CRM 更新失敗：" + faultEx.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "上傳失敗：" + ex.Message });
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 更新「指定會友」的手機/地址/會員身分/信仰狀態(會友細節彈窗左上「上傳」鈕)。
        /// 比照大頭照上傳，以 CanViewContact 把關——能在清單看到/開啟該會友者才可變更其資料。
        /// 「空白略過不動」：手機/地址留空則不覆寫；下拉選「（未設定）」(空值)則該 OptionSet 不動。
        /// POST: /MemberInfo/UpdateContactInfo
        /// </summary>
        [HttpPost]
        [Route("/MemberInfo/UpdateContactInfo")]
        public IActionResult UpdateContactInfo(string contactId, string phone, string address, int? membershipStatusValue, int? spiritualIdentityValue)
        {
            IOrganizationService service = null;

            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return Json(new { success = false, message = "無權限變更此會友資料" });
                }

                var contactToUpdate = new Entity("contact", contactGuid);
                var hasChanges = false;

                if (!string.IsNullOrWhiteSpace(phone))
                {
                    contactToUpdate["mobilephone"] = phone.Trim();
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(address))
                {
                    contactToUpdate["address2_line1"] = address.Trim();
                    hasChanges = true;
                }

                if (membershipStatusValue.HasValue && membershipStatusValue.Value >= 0)
                {
                    contactToUpdate["customertypecode"] = new OptionSetValue(membershipStatusValue.Value);
                    hasChanges = true;
                }

                if (spiritualIdentityValue.HasValue && spiritualIdentityValue.Value >= 0)
                {
                    contactToUpdate["new_spiriitual_identity"] = new OptionSetValue(spiritualIdentityValue.Value);
                    hasChanges = true;
                }

                if (!hasChanges)
                {
                    return Json(new { success = true, message = "無變更" });
                }

                service = GetConnection();
                service.Update(contactToUpdate);

                // 手機/會員身分也顯示在全教會清單列；清掉未搜尋的清單快取，讓預設清單即時反映變更。
                InvalidateChurchRowsCache();

                return Json(new { success = true, message = "已更新" });
            }
            catch (System.ServiceModel.FaultException faultEx)
            {
                return Json(new { success = false, message = "CRM 更新失敗：" + faultEx.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "更新失敗：" + ex.Message });
            }
            finally
            {
                ReleaseConnection(service);
            }
        }

        /// <summary>
        /// 讀取上傳圖片並依 EXIF 自動轉正(解決手機直拍旋轉)，再「置中補邊成正方形」(補白、不裁切)，轉存 JPEG。
        /// 補正方形的原因：CRM 的 entityimage 以正方形顯示大頭照，直接存直式照片會被裁掉頭頂與下方；
        /// 先補成正方形後，存進 CRM 不會切到上下，完整人像在 CRM、清單、會友細節都看得到。處理失敗時退回原始位元組。
        /// </summary>
        private static byte[] NormalizeUploadedImage(IFormFile imageFile)
        {
            byte[] raw;
            using (var read = imageFile.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                read.CopyTo(buffer);
                raw = buffer.ToArray();
            }

            try
            {
                using var input = new MemoryStream(raw);
                using var image = Image.Load(input);
                image.Mutate(x => x.AutoOrient());

                // 置中補邊成正方形(補白、完全不裁切)，避免 CRM 正方形大頭照裁掉頭頂/下方。
                if (image.Width != image.Height)
                {
                    var side = Math.Max(image.Width, image.Height);
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(side, side),
                        Mode = ResizeMode.BoxPad,
                        PadColor = Color.White
                    }));
                }

                using var output = new MemoryStream();
                image.Save(output, new JpegEncoder { Quality = 90 });
                return output.ToArray();
            }
            catch
            {
                // 影像處理失敗 → 至少存原始檔，不讓上傳整個失敗。
                return raw;
            }
        }

        /// <summary>會友照片更新後，清掉本控制器各尺寸的影像快取(全尺寸＋常用縮圖)。</summary>
        private void InvalidateMemberImageCache(Guid contactGuid)
        {
            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
            if (memoryCache == null)
            {
                return;
            }

            // 同一張會友照片在兩處各有快取：本控制器(member-info-*)與個人資料頁(contact-image-*)。
            // 兩種前綴都清，避免改了照片後某一頁仍顯示 30 分鐘的舊圖。
            foreach (var prefix in new[] { "member-info-contact-image", "contact-image" })
            {
                memoryCache.Remove($"{prefix}-full:{contactGuid:N}");
                foreach (var size in new[] { 48, 80, 256 })
                {
                    memoryCache.Remove($"{prefix}-thumb:{contactGuid:N}:{size}");
                    memoryCache.Remove($"{prefix}-fit:{contactGuid:N}:{size}");
                }
            }
        }

        /// <summary>
        /// 全教會清單列(含手機/會員身分)以搜尋字分鍵快取。手機/會員身分被改後，
        /// 至少清掉「未搜尋」的兩個常用變體，讓預設清單即時反映(有搜尋字者最多 3 分鐘後自然過期)。
        /// IMemoryCache 無法依前綴批次移除，故僅清最常見的未搜尋鍵。
        /// </summary>
        private void InvalidateChurchRowsCache()
        {
            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
            if (memoryCache == null)
            {
                return;
            }

            memoryCache.Remove("member-info-church-rows:all:");
            memoryCache.Remove("member-info-church-rows:photo:");
            memoryCache.Remove(ChurchTreeCacheKey);
            memoryCache.Remove(ChurchGroupedCurrentIdsCacheKey);
        }

        /// <summary>以共用快取的 OptionSet 服務取得某 contact 欄位的全部選項(文字+整數值)，供下拉編輯使用。</summary>
        private static List<OptionItem> BuildOptionItems(OptionSetMetadataService optionSvc, string attribute)
        {
            try
            {
                var mapping = optionSvc.GetOptionSetMapping("contact", attribute); // 文字 → 值
                return mapping
                    .Select(kv => new OptionItem { Value = kv.Value, Text = kv.Key })
                    .OrderBy(o => o.Value)
                    .ToList();
            }
            catch
            {
                return new List<OptionItem>();
            }
        }

        private string GetAccess()
        {
            var cached = HttpContext?.Session?.GetString("_MemberInfoAccess");
            if (!string.IsNullOrEmpty(cached))
            {
                ViewBag.MemberInfoAccess = cached;
                return cached;
            }

            var personalModel = InMemoryContext?.PersonalInfomationModel;
            var loginContact = personalModel?.m_LoginContact;
            if (loginContact == null)
            {
                return null;
            }

            var jobTitle = ToolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
            var loginType = InMemoryContext?.ListManager?.LoginType ?? string.Empty;
            var access = MemberInfoAccessResolver.Resolve(jobTitle, loginType);

            if (!string.IsNullOrEmpty(access))
            {
                HttpContext?.Session?.SetString("_MemberInfoAccess", access);
            }

            ViewBag.MemberInfoAccess = access;
            return access;
        }

        private IMemoryCache GetMemberInfoMemoryCache()
        {
            return HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
        }

        private static void TraceMemberInfoTreePhase(
            string operation,
            string phase,
            System.Diagnostics.Stopwatch timing)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[MemberInfoTree] operation={operation} phase={phase} elapsedMs={timing?.ElapsedMilliseconds ?? -1}");
        }

        private static void SetTreeCache<T>(IMemoryCache memoryCache, string key, T value, int size)
        {
            memoryCache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
                SlidingExpiration = TimeSpan.FromMinutes(1),
                Size = Math.Max(1, size)
            });
        }

        /// <summary>
        /// 取得本 request 必須排除的 <c>contact.customertypecode</c>「結案」值。Package03 metadata snapshot
        /// 存在時，值只能從該 immutable、request-local snapshot 精確解析，絕不讀取 legacy OptionSet service、
        /// 共用 metadata cache 或另一個 profile/generation 的資料；缺少或重複標籤時 <see cref="Enumerable.Single{TSource}"/>
        /// 會拋出，讓 action fail closed。snapshot 為 null 僅代表 deployment gate 關閉的既有相容路徑，才可使用
        /// 目前 action 已借用的 legacy CRM service。此方法不保存 option、service、例外或使用者資料；connection
        /// 的唯一釋放 owner 仍是 action 的 <c>finally</c>。
        /// </summary>
        /// <param name="service">gate=false 相容分支使用的目前 request-scoped CRM service。</param>
        /// <param name="typedCommitmentOptions">gate=true 時唯一可用的 immutable request-local metadata snapshot；null 表示 legacy 分支。</param>
        /// <returns>唯一且可用於在籍資料排除條件的「結案」raw choice value。</returns>
        private int GetRequiredClosedCustomerTypeValue(
            IOrganizationService service,
            IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions = null)
        {
            if (typedCommitmentOptions is not null)
            {
                return typedCommitmentOptions
                    .Single(option => option.Label.Equals("結案", StringComparison.Ordinal))
                    .Value;
            }

            // GetOptionSetValue 在找不到且 defaultValue=null 時會拋出異常；不捕獲該異常，讓 legacy compatibility
            // branch 同樣 fail-closed，絕不在無法辨識「結案」時放行資料。此行不可移入 typed branch，否則不同
            // deployment profile/generation 的 metadata 會在同一 response 被混用。
            return GetSharedOptionSetService(service)
                .GetOptionSetValue("contact", "customertypecode", "結案", null);
        }

        private List<SmallGroupDescriptor> GetVisibleSmallGroupDescriptors(
            IOrganizationService service,
            string access)
        {
            if (access == MemberInfoAccess.Church)
            {
                return FetchSmallGroupDescriptors(service, null);
            }

            if (access == MemberInfoAccess.ShepherdList)
            {
                return FetchSmallGroupDescriptors(service, GetShepherdListIds());
            }

            return new List<SmallGroupDescriptor>();
        }

        private HashSet<Guid> GetShepherdListIds()
        {
            var result = new HashSet<Guid>();
            EnsureShepherdListsLoaded();

            var groups = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (groups == null)
            {
                return result;
            }

            foreach (var group in groups)
            {
                if (Guid.TryParse(group.ListEntityId, out var listId) && listId != Guid.Empty)
                {
                    result.Add(listId);
                }
            }

            return result;
        }

        private List<SmallGroupDescriptor> FetchSmallGroupDescriptors(
            IOrganizationService service,
            IReadOnlyCollection<Guid> onlyListIds)
        {
            if (onlyListIds != null && onlyListIds.Count == 0)
            {
                return new List<SmallGroupDescriptor>();
            }

            var entities = new List<Entity>();
            var chunks = onlyListIds == null
                ? new List<List<Guid>> { null }
                : onlyListIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .Chunk(CrmInClauseChunkSize)
                    .Select(chunk => chunk.ToList())
                    .ToList();

            foreach (var chunk in chunks)
            {
                var query = new QueryExpression("list")
                {
                    ColumnSet = new ColumnSet(
                        "listid",
                        "listname",
                        "new_area_name",
                        "new_contact_race_leager_list",
                        "new_contact_family_leader_list",
                        "new_group_time",
                        "new_group_place",
                        "new_contact_list_arealeader")
                };
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.Criteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
                query.Criteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
                if (chunk != null)
                {
                    query.Criteria.AddCondition(
                        "listid",
                        ConditionOperator.In,
                        chunk.Select(id => (object)id).ToArray());
                }
                query.AddOrder("listname", OrderType.Ascending);
                entities.AddRange(RetrieveAllEntities(service, query));
            }

            return entities
                .Where(entity => entity != null && entity.Id != Guid.Empty)
                .GroupBy(entity => entity.Id)
                .Select(group => group.First())
                .Select(entity =>
                {
                    var raceLeader = entity.GetAttributeValue<EntityReference>("new_contact_race_leager_list");
                    var groupLeader = entity.GetAttributeValue<EntityReference>("new_contact_family_leader_list");
                    var areaLeader = entity.GetAttributeValue<EntityReference>("new_contact_list_arealeader");
                    var areaName = entity.GetAttributeValue<string>("new_area_name") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(areaName) && !string.IsNullOrWhiteSpace(areaLeader?.Name))
                    {
                        areaName = areaLeader.Name.Trim() + "牧區";
                    }

                    return new SmallGroupDescriptor
                    {
                        ListId = entity.Id.ToString(),
                        GroupName = entity.GetAttributeValue<string>("listname") ?? string.Empty,
                        AreaName = areaName,
                        RaceLeaderName = raceLeader?.Name ?? string.Empty,
                        RaceLeaderKey = raceLeader?.Id.ToString() ?? string.Empty,
                        GroupTime = entity.GetAttributeValue<string>("new_group_time") ?? string.Empty,
                        GroupPlace = entity.GetAttributeValue<string>("new_group_place") ?? string.Empty,
                        LeaderName = groupLeader?.Name ?? string.Empty
                    };
                })
                .ToList();
        }

        private List<GroupMembershipRow> FetchGroupMemberships(
            IOrganizationService service,
            IReadOnlyCollection<string> listIds,
            int closedStatus)
        {
            var validListIds = (listIds ?? Array.Empty<string>())
                .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (validListIds.Count == 0)
            {
                return new List<GroupMembershipRow>();
            }

            var rows = new Dictionary<string, GroupMembershipRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in validListIds.Chunk(CrmInClauseChunkSize))
            {
                var query = new QueryExpression("listmember")
                {
                    ColumnSet = new ColumnSet("listid", "entityid")
                };
                query.Criteria.AddCondition(
                    "listid",
                    ConditionOperator.In,
                    chunk.Select(id => (object)id).ToArray());

                var contactLink = new LinkEntity(
                    "listmember",
                    "contact",
                    "entityid",
                    "contactid",
                    JoinOperator.Inner)
                {
                    EntityAlias = "member",
                    Columns = new ColumnSet("contactid", "statecode", "customertypecode")
                };
                contactLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                var currentStatus = new FilterExpression(LogicalOperator.Or);
                currentStatus.AddCondition("customertypecode", ConditionOperator.Null);
                currentStatus.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus);
                contactLink.LinkCriteria.Filters.Add(currentStatus);
                query.LinkEntities.Add(contactLink);

                foreach (var entity in RetrieveAllEntities(service, query))
                {
                    var listId = GetListMemberListId(entity);
                    var contactId = GetListMemberContactId(entity);
                    if (listId == Guid.Empty || contactId == Guid.Empty)
                    {
                        continue;
                    }

                    var key = listId.ToString("N") + ":" + contactId.ToString("N");
                    rows[key] = new GroupMembershipRow
                    {
                        ListId = listId.ToString(),
                        ContactId = contactId.ToString(),
                        IsCurrent = true
                    };
                }
            }

            return rows.Values.ToList();
        }

        private QueryExpression BuildStrictCurrentContactQuery(
            ColumnSet columns,
            string search,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = columns
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var currentStatus = new FilterExpression(LogicalOperator.Or);
            currentStatus.AddCondition("customertypecode", ConditionOperator.Null);
            currentStatus.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus);
            query.Criteria.Filters.Add(currentStatus);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = "%" + search.Trim() + "%";
                var searchFilter = new FilterExpression(LogicalOperator.Or);
                searchFilter.AddCondition("fullname", ConditionOperator.Like, pattern);
                searchFilter.AddCondition("mobilephone", ConditionOperator.Like, pattern);
                if (matchingStatusValues != null && matchingStatusValues.Count > 0)
                {
                    searchFilter.AddCondition(
                        "customertypecode",
                        ConditionOperator.In,
                        matchingStatusValues.Select(value => (object)value).ToArray());
                }
                query.Criteria.Filters.Add(searchFilter);
            }

            return query;
        }

        /// <summary>
        /// 維持 gate=false 的既有 metadata 搜尋相容入口。它明確傳入 null，使下游只在 false-gate branch 使用
        /// request-scoped legacy metadata provider；呼叫端不應以此 overload 組合 typed snapshot、profile 或 client。
        /// </summary>
        /// <param name="service">目前 legacy request 唯一擁有的 CRM service。</param>
        /// <param name="search">既有搜尋文字；空白不會產生 metadata 條件。</param>
        /// <returns>legacy label mapping 找到的 unique raw values。</returns>
        private List<int> GetCustomerTypeValuesMatchingText(IOrganizationService service, string search)
            => GetCustomerTypeValuesMatchingText(service, search, null);

        /// <summary>
        /// 將搜尋字比對至承諾類型 option label。typed snapshot 存在時只可比對該 request-local DTO，絕不向
        /// legacy metadata service 補查未知值；null 只代表部署 gate 關閉時的既有相容路徑，仍由目前 request 的
        /// legacy service 取得 metadata。此 helper 不快取、保存或修改 snapshot，避免跨 profile/generation 共用。
        /// </summary>
        /// <param name="service">僅 false-gate compatibility branch 可使用的 request-scoped CRM service。</param>
        /// <param name="search">既有搜尋文字；空白時不執行 metadata lookup。</param>
        /// <param name="typedCommitmentOptions">gate=true 的 immutable request-local option snapshot；null 為 legacy branch。</param>
        /// <returns>可安全加入既有 QueryExpression 的 unique raw values。</returns>
        private List<int> GetCustomerTypeValuesMatchingText(
            IOrganizationService service,
            string search,
            IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return new List<int>();
            }

            var term = search.Trim();
            if (typedCommitmentOptions is not null)
            {
                return typedCommitmentOptions
                    .Where(option => option.Label.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(option => option.Value)
                    .Distinct()
                    .ToList();
            }

            return GetSharedOptionSetService(service)
                .GetOptionSetMapping("contact", "customertypecode")
                .Where(pair => !string.IsNullOrEmpty(pair.Key) &&
                               pair.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(pair => pair.Value)
                .Distinct()
                .ToList();
        }

        private static List<Entity> RetrieveAllEntities(IOrganizationService service, QueryExpression query)
        {
            var entities = new List<Entity>();
            query.PageInfo = new PagingInfo
            {
                Count = 2000,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = false
            };

            while (true)
            {
                var page = service.RetrieveMultiple(query);
                if (page?.Entities != null)
                {
                    entities.AddRange(page.Entities);
                }
                if (page == null || !page.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            return entities;
        }

        /// <summary>
        /// 以有界 CRM in-clause 載入已由上游小組 membership 推導的會友候選列。contact ID 與搜尋條件不授權
        /// profile、connector、owner 或 endpoint；真正可見性仍由後續批次授權收斂。typed snapshot 不為 null 時，
        /// customertypecode 文字比對只使用當前 request 的 DTO，避免回查 legacy metadata；本方法不保存 Entity、
        /// snapshot、token 或連線，service 的釋放仍由 action finally 唯一擁有。
        /// </summary>
        /// <param name="service">目前 action 借用的 legacy CRM connection。</param>
        /// <param name="contactIds">server-derived candidate contact IDs；空集合立刻回傳空列。</param>
        /// <param name="search">既有文字篩選，不是授權或路由輸入。</param>
        /// <param name="closedStatus">本 request 已讀取的結案 option value。</param>
        /// <param name="typedCommitmentOptions">gate=true 的 request-local metadata snapshot；null 保留 legacy mapping。</param>
        /// <returns>尚未序列化且仍待 authorization 的 request-local Entity 集合。</returns>
        private List<Entity> FetchContactsByIds(
            IOrganizationService service,
            IReadOnlyCollection<Guid> contactIds,
            string search,
            int closedStatus,
            IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions = null)
        {
            var result = new Dictionary<Guid, Entity>();
            var ids = (contactIds ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new List<Entity>();
            }

            var statusValues = GetCustomerTypeValuesMatchingText(service, search, typedCommitmentOptions);
            foreach (var chunk in ids.Chunk(CrmInClauseChunkSize))
            {
                var query = BuildStrictCurrentContactQuery(
                    GetTreeContactColumns(),
                    search,
                    closedStatus,
                    statusValues);
                query.Criteria.AddCondition(
                    "contactid",
                    ConditionOperator.In,
                    chunk.Select(id => (object)id).ToArray());
                foreach (var contact in RetrieveAllEntities(service, query))
                {
                    result[contact.Id] = contact;
                }
            }

            return result.Values.ToList();
        }

        private HashSet<Guid> GetChurchGroupedCurrentIds(
            IOrganizationService service,
            IReadOnlyCollection<SmallGroupDescriptor> descriptors,
            int closedStatus,
            bool bypassCache = false)
        {
            var memoryCache = GetMemberInfoMemoryCache();
            if (!bypassCache &&
                memoryCache != null &&
                memoryCache.TryGetValue(
                    ChurchGroupedCurrentIdsCacheKey,
                    out HashSet<Guid> cachedIds) &&
                cachedIds != null)
            {
                return new HashSet<Guid>(cachedIds);
            }

            var memberships = FetchGroupMemberships(
                service,
                (descriptors ?? Array.Empty<SmallGroupDescriptor>())
                    .Select(group => group.ListId)
                    .ToList(),
                closedStatus);
            var groupedIds = memberships
                .Select(row => Guid.TryParse(row.ContactId, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            if (!bypassCache && memoryCache != null)
            {
                SetTreeCache(
                    memoryCache,
                    ChurchGroupedCurrentIdsCacheKey,
                    groupedIds,
                    Math.Max(1, groupedIds.Count));
            }

            return groupedIds;
        }

        private QueryExpression BuildUngroupedBaseQuery(
            ColumnSet columns,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues)
        {
            var query = BuildStrictCurrentContactQuery(
                columns,
                search,
                closedStatus,
                matchingStatusValues);
            foreach (var chunk in (groupedIds ?? Array.Empty<Guid>())
                         .Where(id => id != Guid.Empty)
                         .Distinct()
                         .Chunk(CrmInClauseChunkSize))
            {
                query.Criteria.AddCondition(
                    "contactid",
                    ConditionOperator.NotIn,
                    chunk.Select(id => (object)id).ToArray());
            }

            return query;
        }

        private QueryExpression BuildUngroupedContactQuery(
            ColumnSet columns,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues,
            DataSourceLoadOptions loadOptions)
        {
            var query = BuildUngroupedBaseQuery(
                columns,
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues);

            ApplyUngroupedSort(query, loadOptions);
            var take = loadOptions?.Take > 0
                ? Math.Min(loadOptions.Take, MaxPageSize)
                : DefaultPageSize;
            var skip = loadOptions?.Skip > 0 ? loadOptions.Skip : 0;
            query.PageInfo = new PagingInfo
            {
                Count = take,
                PageNumber = skip / take + 1,
                PagingCookie = null,
                ReturnTotalRecordCount = true
            };
            return query;
        }

        private QueryExpression BuildUngroupedCommitmentSegmentQuery(
            ColumnSet columns,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues,
            MemberInfoCommitmentTypeSegmentKind kind,
            int? optionValue,
            IReadOnlyCollection<int> configuredValues)
        {
            var query = BuildUngroupedBaseQuery(
                columns,
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues);
            switch (kind)
            {
                case MemberInfoCommitmentTypeSegmentKind.Configured:
                    if (!optionValue.HasValue)
                    {
                        throw new InvalidOperationException(
                            "Configured commitment segment requires an OptionSet value.");
                    }
                    query.Criteria.AddCondition(
                        "customertypecode",
                        ConditionOperator.Equal,
                        optionValue.Value);
                    break;
                case MemberInfoCommitmentTypeSegmentKind.Unknown:
                    query.Criteria.AddCondition(
                        "customertypecode",
                        ConditionOperator.NotNull);
                    var knownValues = (configuredValues ?? Array.Empty<int>())
                        .Distinct()
                        .ToArray();
                    if (knownValues.Length > 0)
                    {
                        query.Criteria.AddCondition(
                            "customertypecode",
                            ConditionOperator.NotIn,
                            knownValues.Select(value => (object)value).ToArray());
                    }
                    break;
                case MemberInfoCommitmentTypeSegmentKind.Empty:
                    query.Criteria.AddCondition(
                        "customertypecode",
                        ConditionOperator.Null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            // 每一段的類型順位已由外層 segment sequence 決定；段內只需穩定姓名／ID 排序。
            query.AddOrder("fullname", OrderType.Ascending);
            query.AddOrder("contactid", OrderType.Ascending);
            return query;
        }

        private int CountUngroupedEmptyCommitmentSegment(
            IOrganizationService service,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues)
        {
            var query = BuildUngroupedBaseQuery(
                new ColumnSet("contactid"),
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues);
            query.Criteria.AddCondition("customertypecode", ConditionOperator.Null);
            query.PageInfo = new PagingInfo
            {
                Count = 1,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = true
            };

            var result = service.RetrieveMultiple(query);
            return result.TotalRecordCount >= 0
                ? result.TotalRecordCount
                : result.Entities.Count;
        }

        /// <summary>
        /// 以一支 aggregate FetchXML 取得每個非空 raw value 的筆數。raw value 只作為
        /// metadata segment 的查詢識別鍵，實際先後仍完全由客製化 options sequence 決定。
        /// </summary>
        private IReadOnlyDictionary<int, int> CountUngroupedCommitmentValues(
            IOrganizationService service,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues)
        {
            var query = BuildUngroupedBaseQuery(
                new ColumnSet("contactid", "customertypecode"),
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues);
            var response = (QueryExpressionToFetchXmlResponse)service.Execute(
                new QueryExpressionToFetchXmlRequest { Query = query });
            var countFetch =
                MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(response.FetchXml);
            var rows = service.RetrieveMultiple(new FetchExpression(countFetch));
            return MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows);
        }

        private List<Entity> RetrieveUngroupedSegmentRange(
            IOrganizationService service,
            Func<QueryExpression> createQuery,
            int skip,
            int take)
        {
            var result = new List<Entity>();
            if (take <= 0)
            {
                return result;
            }

            var pageSize = Math.Min(Math.Max(1, take), MaxPageSize);
            var pageNumber = Math.Max(0, skip) / pageSize + 1;
            var offsetOnFirstPage = Math.Max(0, skip) % pageSize;
            var remaining = take;

            while (remaining > 0)
            {
                var query = createQuery();
                query.PageInfo = new PagingInfo
                {
                    Count = pageSize,
                    PageNumber = pageNumber,
                    PagingCookie = null,
                    ReturnTotalRecordCount = false
                };

                var page = service.RetrieveMultiple(query);
                var selected = page.Entities
                    .Skip(offsetOnFirstPage)
                    .Take(remaining)
                    .ToList();
                result.AddRange(selected);
                remaining -= selected.Count;

                if (!page.MoreRecords || page.Entities.Count == 0)
                {
                    break;
                }

                pageNumber++;
                offsetOnFirstPage = 0;
            }

            return result;
        }

        /// <summary>
        /// 依已存在的 metadata segment 順序組合未分組頁面。這個方法只可替換
        /// `ORG-CALL-00024` 的「non-empty raw OptionSet value/count」來源：gate=false 時保留既有 local
        /// aggregate count；gate=true 時由 typed ProductClient 提供同一個 bounded scalar 結果。empty count、
        /// metadata、segment contact retrieve、關係 projection 與 contact authorization 是不同 matrix capability，
        /// 繼續由既有 owner 處理；它們不是 typed fault fallback，也不能被本方法列為已遷移。
        /// </summary>
        /// <param name="service">目前 request 的既有 legacy connection；其 acquire/release 仍由 action finally 唯一擁有。</param>
        /// <param name="columns">既有 contact page projection；不會跨入 Package02 DTO 邊界。</param>
        /// <param name="search">既有 page search；typed operation 另以固定 byte bound 驗證，非 arbitrary FetchXML。</param>
        /// <param name="groupedIds">既有 page 的 server-derived grouped contact exclusion；typed aggregate 不接受它作 caller input。</param>
        /// <param name="closedStatus">既有 page closed status；typed aggregate 自行解析其固定 server-owned metadata。</param>
        /// <param name="matchingStatusValues">既有 metadata label search 結果；typed aggregate 自行使用固定 metadata rule。</param>
        /// <param name="loadOptions">既有 bounded page/sort options；不傳遞給 typed aggregate。</param>
        /// <param name="descending">既有 commitment segment direction。</param>
        /// <param name="configuration">deployment-owned 設定；只有 gate=true 時可用來組成固定 profile typed client。</param>
        /// <param name="useTypedUngroupedCommitmentCount">早於 user/session/client composition 決定的 deployment gate 結果。</param>
        /// <param name="cancellationToken">目前 HTTP request 的取消 token；必須原樣向唯一 typed count operation 傳遞。</param>
        /// <returns>由既有 page owner 建立的 contacts/total count；不保存 SDK entity、DTO、token 或 resource。</returns>
        private async Task<UngroupedContactPage> LoadUngroupedCommitmentTypePageAsync(
            IOrganizationService service,
            ColumnSet columns,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues,
            DataSourceLoadOptions loadOptions,
            bool descending,
            IConfiguration configuration,
            bool useTypedUngroupedCommitmentCount,
            IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions,
            CancellationToken cancellationToken)
        {
            // metadata snapshot 由 action 在同一 request 取得一次；typed branch 絕不能在 segment loader 偷查 legacy
            // provider。null 僅表示 ORG-CALL-00040 gate=false 時保留既有 metadata compatibility path。
            var options = typedCommitmentOptions ?? GetCommitmentTypeOptions(service);
            var configuredValues = options
                .OrderBy(option => option.Order)
                .Select(option => option.Value)
                .Distinct()
                .ToArray();
            var countsByValue = await LoadUngroupedCommitmentCountsAsync(
                service,
                configuration,
                useTypedUngroupedCommitmentCount,
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues,
                cancellationToken).ConfigureAwait(false);
            var emptyCount = CountUngroupedEmptyCommitmentSegment(
                service,
                search,
                groupedIds,
                closedStatus,
                matchingStatusValues);
            var segments = MemberInfoCommitmentTypeSort.BuildSegments(
                configuredValues,
                countsByValue,
                emptyCount,
                descending);
            var skip = loadOptions?.Skip > 0 ? loadOptions.Skip : 0;
            var take = loadOptions?.Take > 0
                ? Math.Min(loadOptions.Take, MaxPageSize)
                : DefaultPageSize;

            var contacts = new List<Entity>();
            foreach (var slice in MemberInfoCommitmentTypeSort.PlanSlices(
                         skip,
                         take,
                         segments))
            {
                contacts.AddRange(RetrieveUngroupedSegmentRange(
                    service,
                    () => BuildUngroupedCommitmentSegmentQuery(
                        columns,
                        search,
                        groupedIds,
                        closedStatus,
                        matchingStatusValues,
                        slice.Kind,
                        slice.Value,
                        configuredValues),
                    slice.Skip,
                    slice.Take));
            }

            return new UngroupedContactPage
            {
                Contacts = contacts,
                TotalCount = segments.Sum(segment => segment.Count)
            };
        }

        /// <summary>
        /// 選擇 ORG-CALL-00024 的唯一 aggregate count implementation。gate=false 只執行既有 local legacy
        /// aggregate，這是 rollback compatibility path；gate=true 則必須使用 Package02 typed client。若 typed
        /// client 無法組成、transport/DTO 驗證失敗或 request 被取消，例外原樣向外傳播，絕不呼叫 legacy aggregate
        /// 取得替代結果、retry 或發布 partial count。如此可避免一個 request 對同一 capability 混用兩條 CRM path。
        /// </summary>
        /// <param name="service">只在 gate=false legacy compatibility branch 使用的 request-scoped CRM service。</param>
        /// <param name="configuration">deployment-owned configuration；不得由 search 或 HTTP caller 覆寫 profile/connector。</param>
        /// <param name="useTypedUngroupedCommitmentCount">base/sub-gate 已驗證的 immutable request decision。</param>
        /// <param name="search">既有 optional search；只送給 typed request 的 bounded text field。</param>
        /// <param name="groupedIds">只屬 legacy compatibility query 的 server-derived exclusion set。</param>
        /// <param name="closedStatus">只屬 legacy compatibility query 的 metadata result。</param>
        /// <param name="matchingStatusValues">只屬 legacy compatibility query 的 metadata result。</param>
        /// <param name="cancellationToken">HTTP cancellation；typed branch 不 catch、不保存、不註冊它。</param>
        /// <returns>request-local value/count map；typed branch 的 map 是 service defensive copy。</returns>
        private async Task<IReadOnlyDictionary<int, int>> LoadUngroupedCommitmentCountsAsync(
            IOrganizationService service,
            IConfiguration configuration,
            bool useTypedUngroupedCommitmentCount,
            string search,
            IReadOnlyCollection<Guid> groupedIds,
            int closedStatus,
            IReadOnlyCollection<int> matchingStatusValues,
            CancellationToken cancellationToken)
        {
            if (!useTypedUngroupedCommitmentCount)
            {
                return CountUngroupedCommitmentValues(
                    service,
                    search,
                    groupedIds,
                    closedStatus,
                    matchingStatusValues);
            }

            var package02Client = DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(configuration)
                ?? throw new InvalidOperationException(
                    "The Package02 ungrouped commitment typed client was unavailable after the deployment gate was enabled.");
            var countService = new Package02UngroupedCommitmentReadService(
                package02Client,
                DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias);
            return (await countService.RetrieveAsync(search, cancellationToken).ConfigureAwait(false)).GetCounts();
        }

        private static bool TryGetCommitmentTypeSort(
            DataSourceLoadOptions loadOptions,
            out bool descending)
        {
            descending = false;
            var sort = loadOptions?.Sort?.FirstOrDefault();
            if (sort == null)
            {
                return true;
            }

            var isCommitmentType =
                string.Equals(
                    sort.Selector,
                    MemberInfoCommitmentTypeSort.Selector,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    sort.Selector,
                    "MembershipStatus",
                    StringComparison.OrdinalIgnoreCase);
            if (!isCommitmentType)
            {
                return false;
            }

            descending = sort.Desc;
            return true;
        }

        private static void ApplyUngroupedSort(QueryExpression query, DataSourceLoadOptions loadOptions)
        {
            var applied = false;
            if (loadOptions?.Sort != null)
            {
                foreach (var sort in loadOptions.Sort)
                {
                    var attribute = MapUngroupedSortAttribute(sort?.Selector);
                    if (attribute == null)
                    {
                        continue;
                    }
                    query.AddOrder(attribute, sort.Desc ? OrderType.Descending : OrderType.Ascending);
                    applied = true;
                }
            }

            if (!applied)
            {
                query.AddOrder("fullname", OrderType.Ascending);
            }
            query.AddOrder("contactid", OrderType.Ascending);
        }

        private static string MapUngroupedSortAttribute(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return null;
            if (string.Equals(selector, "FullName", StringComparison.OrdinalIgnoreCase)) return "fullname";
            if (string.Equals(selector, "Gender", StringComparison.OrdinalIgnoreCase)) return "gendercode";
            if (string.Equals(selector, "BirthDate", StringComparison.OrdinalIgnoreCase)) return "birthdate";
            if (string.Equals(selector, "Phone", StringComparison.OrdinalIgnoreCase)) return "mobilephone";
            if (string.Equals(selector, "SpiritualIdentity", StringComparison.OrdinalIgnoreCase)) return "new_spiriitual_identity";
            if (string.Equals(selector, "Address", StringComparison.OrdinalIgnoreCase)) return "address2_line1";
            return null;
        }

        private Dictionary<Guid, string> BatchRelationGoals(
            IOrganizationService service,
            IReadOnlyCollection<Guid> contactIds)
        {
            var requestedIds = (contactIds ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToHashSet();
            var items = requestedIds.ToDictionary(
                id => id,
                _ => new List<(string Role, string TargetName)>());

            foreach (var chunk in requestedIds.Chunk(CrmInClauseChunkSize))
            {
                try
                {
                    var values = chunk.Select(id => (object)id).ToArray();
                    var query = new QueryExpression("connection")
                    {
                        ColumnSet = new ColumnSet("record1id", "record2id", "record1roleid", "record2roleid")
                    };
                    query.Criteria.FilterOperator = LogicalOperator.Or;
                    query.Criteria.AddCondition("record1id", ConditionOperator.In, values);
                    query.Criteria.AddCondition("record2id", ConditionOperator.In, values);
                    query.AddOrder("connectionid", OrderType.Ascending);

                    foreach (var connection in RetrieveAllEntities(service, query))
                    {
                        var record1 = connection.GetAttributeValue<EntityReference>("record1id");
                        var record2 = connection.GetAttributeValue<EntityReference>("record2id");
                        if (record1 != null && requestedIds.Contains(record1.Id))
                        {
                            var role = connection.GetAttributeValue<EntityReference>("record2roleid");
                            items[record1.Id].Add((role?.Name ?? string.Empty, record2?.Name ?? string.Empty));
                        }
                        if (record2 != null && requestedIds.Contains(record2.Id))
                        {
                            var role = connection.GetAttributeValue<EntityReference>("record1roleid");
                            items[record2.Id].Add((role?.Name ?? string.Empty, record1?.Name ?? string.Empty));
                        }
                    }
                }
                catch
                {
                    // 某些 CRM 環境不開放 connection；保留成員列並將關係／目標留空。
                }
            }

            return items.ToDictionary(
                pair => pair.Key,
                pair => RelationGoalFormatter.Format(pair.Value));
        }

        /// <summary>
        /// 將已授權的 contact Entity 投影成短生命週期會友列。typedCommitmentOptions 不為 null 時，承諾類型
        /// label/order 只能從同一 request 的 immutable Package03 snapshot 讀取，未知值輸出空字串；null 則只代表
        /// gate=false 的 legacy compatibility branch。性別與屬靈身分仍沿用既有非本 child 的 metadata owner。
        /// 本方法不快取 Entity、DTO、profile、token、授權結果或 connection；所有結果由 action 立即 JSON 序列化。
        /// </summary>
        /// <param name="service">目前 action 的 legacy CRM service，僅供既有非承諾類型 metadata 與 false-gate fallback。</param>
        /// <param name="contacts">已由 caller 授權收斂的 request-local Entity 集合。</param>
        /// <param name="relationGoalsByContact">同一 request 批次投影的關係文字。</param>
        /// <param name="typedCommitmentOptions">Package03 gate=true 時唯一允許的承諾 metadata snapshot。</param>
        /// <returns>不含 CRM Entity 或可變 metadata graph 的 row view-model 清單。</returns>
        private List<GroupMemberRowViewModel> BuildMemberRows(
            IOrganizationService service,
            IEnumerable<Entity> contacts,
            IReadOnlyDictionary<Guid, string> relationGoalsByContact,
            IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions = null)
        {
            var optionService = GetSharedOptionSetService(service);
            // true branch 的承諾 metadata 已由 caller 以固定 Package03 profile/workload 建立為 request-local copy。
            // 只能在 false-gate compatibility branch 讀 legacy provider；這條分界避免同一回應混用兩個 metadata path。
            var commitmentOptions = typedCommitmentOptions ?? GetCommitmentTypeOptions(service);
            var commitmentByValue = commitmentOptions
                .GroupBy(option => option.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var rows = new List<GroupMemberRowViewModel>();
            foreach (var contact in contacts ?? Enumerable.Empty<Entity>())
            {
                var relationGoals = string.Empty;
                relationGoalsByContact?.TryGetValue(contact.Id, out relationGoals);
                // 搜尋、分組與未分組端點共用此列 DTO；統一把 CRM 的 Year=1 哨兵值轉成 null，
                // 避免不同入口對同一生日產生不一致的顯示結果。
                var birthDate = contact.GetAttributeValue<DateTime?>("birthdate");
                if (birthDate.HasValue && birthDate.Value.Year <= 1)
                {
                    birthDate = null;
                }
                // raw OptionSet value 只用來查 metadata 對照；真正送到前端的排序鍵是客製化集合順位，
                // 絕不比較 raw 整數大小。metadata 未知舊值保留 has-value=true、order=null，避免資料遺失。
                var membershipStatusValue =
                    contact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value;
                MemberInfoCommitmentTypeOption commitmentOption = null;
                if (membershipStatusValue.HasValue)
                {
                    commitmentByValue.TryGetValue(
                        membershipStatusValue.Value,
                        out commitmentOption);
                }

                rows.Add(new GroupMemberRowViewModel
                {
                    ContactId = contact.Id.ToString(),
                    FullName = contact.GetAttributeValue<string>("fullname") ?? string.Empty,
                    Gender = ResolveOptionSetText(optionService, contact, "gendercode"),
                    BirthDate = birthDate,
                    Phone = contact.GetAttributeValue<string>("mobilephone") ?? string.Empty,
                    SpiritualIdentity = ResolveOptionSetText(optionService, contact, "new_spiriitual_identity"),
                    Address = contact.GetAttributeValue<string>("address2_line1") ?? string.Empty,
                    MembershipStatusOrder = commitmentOption?.Order,
                    HasMembershipStatusValue = membershipStatusValue.HasValue,
                    MembershipStatus = membershipStatusValue.HasValue
                        ? commitmentOption?.Label
                            ?? (typedCommitmentOptions is null
                                ? ResolveOptionSetText(optionService, contact, "customertypecode")
                                : string.Empty)
                        : string.Empty,
                    RelationGoals = relationGoals ?? string.Empty
                });
            }

            return rows;
        }

        private static string ResolveOptionSetText(
            OptionSetMetadataService optionService,
            Entity entity,
            string attributeName)
        {
            try
            {
                var value = entity?.GetAttributeValue<OptionSetValue>(attributeName)?.Value;
                return value.HasValue
                    ? optionService.GetOptionSetText("contact", attributeName, value.Value) ?? string.Empty
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 樹狀成員列與搜尋結果共用的 CRM 投影，集中提供列顯示及頭像呈現所需的識別資料；
        /// ColumnSet 只決定回傳屬性，在籍／結案資格仍由查詢條件與批次授權流程另行約束。
        /// 搜尋端點可沿用同一批已授權 Entity 組列，避免為每位候選人再發一次詳細資料查詢。
        /// </summary>
        private static ColumnSet GetTreeContactColumns()
        {
            return new ColumnSet(
                "contactid",
                "fullname",
                "gendercode",
                "birthdate",
                "mobilephone",
                "new_spiriitual_identity",
                "address2_line1",
                "customertypecode",
                "statecode");
        }

        private bool CanViewContact(Guid contactId)
        {
            if (contactId == Guid.Empty)
            {
                return false;
            }

            var access = GetAccess();
            if (access == MemberInfoAccess.Church)
            {
                return IsCurrentContact(contactId);
            }

            if (access == MemberInfoAccess.ShepherdList)
            {
                var allowed = GetShepherdContactIds();
                return allowed.Contains(contactId.ToString()) && IsCurrentContact(contactId);
            }

            return false;
        }

        /// <summary>
        /// 批次版授權檢查：與逐筆 <see cref="CanViewContact"/> 的允許/拒絕判斷「完全等價」，
        /// 但把「逐人各打一支 CRM Retrieve(statecode/customertypecode)」改成「一支 RetrieveMultiple 全部撈回」，
        /// 消除 N+1 查詢。回傳「可檢視」的 contactId 集合。
        /// 安全性：沿用同樣的 GetAccess()/GetShepherdContactIds()/IsCurrentContactEntity()，名單與逐筆版相同；
        /// 不快取授權結果、不放任何使用者專屬資料進共用快取、不碰 session。
        /// </summary>
        private HashSet<Guid> CanViewContactsBatch(IReadOnlyCollection<Guid> contactIds)
        {
            var closedStatus = TryGetClosedCustomerTypeValue();
            if (!closedStatus.HasValue)
            {
                return new HashSet<Guid>();
            }

            return CanViewContactsBatch(
                contactIds,
                ToolUtility.m_Crm2011OrganizationService,
                closedStatus.Value);
        }

        private HashSet<Guid> CanViewContactsBatch(
            IReadOnlyCollection<Guid> contactIds,
            IOrganizationService service,
            int closedStatus,
            IReadOnlyCollection<Guid> shepherdScopeContactIds = null)
        {
            var allowed = new HashSet<Guid>();
            var validGuids = (contactIds ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (validGuids.Count == 0 || service == null)
            {
                return allowed;
            }

            var access = GetAccess();
            if (access != MemberInfoAccess.Church && access != MemberInfoAccess.ShepherdList)
            {
                return allowed;
            }

            HashSet<string> shepherdAllowed = null;
            if (access == MemberInfoAccess.ShepherdList)
            {
                shepherdAllowed = shepherdScopeContactIds == null
                    ? GetShepherdContactIds()
                    : shepherdScopeContactIds
                        .Where(id => id != Guid.Empty)
                        .Select(id => id.ToString())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (shepherdAllowed.Count == 0)
                {
                    return allowed;
                }
            }

            try
            {
                foreach (var chunk in validGuids.Chunk(CrmInClauseChunkSize))
                {
                    var query = BuildStrictCurrentContactQuery(
                        new ColumnSet("contactid", "statecode", "customertypecode"),
                        string.Empty,
                        closedStatus,
                        Array.Empty<int>());
                    query.Criteria.AddCondition(
                        "contactid",
                        ConditionOperator.In,
                        chunk.Select(id => (object)id).ToArray());

                    foreach (var contact in service.RetrieveMultiple(query).Entities)
                    {
                        if (access == MemberInfoAccess.ShepherdList &&
                            !shepherdAllowed.Contains(contact.Id.ToString()))
                        {
                            continue;
                        }
                        allowed.Add(contact.Id);
                    }
                }
            }
            catch
            {
                return new HashSet<Guid>();
            }

            return allowed;
        }

        private void EnsureShepherdListsLoaded()
        {
            var listManager = InMemoryContext?.ListManager;
            if (listManager == null)
            {
                return;
            }

            var loaded = listManager.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if ((loaded == null || loaded.Count == 0) && !string.IsNullOrEmpty(listManager.m_Password))
            {
                listManager.SetupListManager(
                    listManager.m_Account,
                    listManager.m_Password,
                    listManager.m_SelectDate != default ? listManager.m_SelectDate : DateTime.Now);
            }
        }

        private HashSet<string> GetShepherdContactIds()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EnsureShepherdListsLoaded();

            var groupRecords = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (groupRecords == null)
            {
                return result;
            }

            foreach (var group in groupRecords)
            {
                if (!Guid.TryParse(group.ListEntityId, out var listGuid))
                {
                    continue;
                }

                EntityCollection members;
                try
                {
                    members = ToolUtility.RetrieveMemberListCollectionByListId(listGuid);
                }
                catch
                {
                    continue;
                }

                foreach (var member in members.Entities)
                {
                    var contactId = GetListMemberContactId(member);
                    if (contactId != Guid.Empty)
                    {
                        result.Add(contactId.ToString());
                    }
                }
            }

            return result;
        }

        private List<MemberInfoListRowViewModel> LoadShepherdMemberRows(bool photoOnly)
        {
            var rowsByContact = new Dictionary<string, MemberInfoListRowViewModel>(StringComparer.OrdinalIgnoreCase);
            var groupNamesByContact = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            EnsureShepherdListsLoaded();

            var resolveMembershipStatus = CreateMembershipStatusResolver();
            var service = ToolUtility.m_Crm2011OrganizationService;
            var groupRecords = InMemoryContext?.ListManager?.m_MultiGroupList?.m_WeeklyReportRecordListData;
            if (groupRecords == null)
            {
                return new List<MemberInfoListRowViewModel>();
            }

            foreach (var group in groupRecords)
            {
                if (!Guid.TryParse(group.ListEntityId, out var listGuid))
                {
                    continue;
                }

                EntityCollection members;
                try
                {
                    members = ToolUtility.RetrieveMemberListCollectionByListId(listGuid);
                }
                catch
                {
                    continue;
                }

                foreach (var member in members.Entities)
                {
                    var contactId = GetListMemberContactId(member);
                    if (contactId == Guid.Empty)
                    {
                        continue;
                    }

                    Entity contact;
                    try
                    {
                        contact = service.Retrieve("contact", contactId, GetContactListColumns());
                    }
                    catch
                    {
                        continue;
                    }

                    if (!IsCurrentContactEntity(contact))
                    {
                        continue;
                    }

                    var key = contactId.ToString();
                    if (!rowsByContact.TryGetValue(key, out var row))
                    {
                        row = new MemberInfoListRowViewModel
                        {
                            ContactId = key,
                            FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                            Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                            MembershipStatus = resolveMembershipStatus(contact),
                            SmallGroupName = group.Name ?? string.Empty
                        };
                        rowsByContact[key] = row;
                        groupNamesByContact[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    if (!string.IsNullOrWhiteSpace(group.Name))
                    {
                        groupNamesByContact[key].Add(group.Name);
                    }
                }
            }

            foreach (var pair in rowsByContact)
            {
                if (groupNamesByContact.TryGetValue(pair.Key, out var names) && names.Count > 0)
                {
                    pair.Value.SmallGroupName = string.Join("、", names.OrderBy(n => n));
                }
            }

            var rows = rowsByContact.Values
                .OrderBy(r => r.SmallGroupName)
                .ThenBy(r => r.FullName)
                .ToList();

            if (photoOnly)
            {
                var ids = new List<Guid>();
                foreach (var row in rows)
                {
                    if (Guid.TryParse(row.ContactId, out var guid))
                    {
                        ids.Add(guid);
                    }
                }

                var withPhoto = GetContactIdsWithPhoto(ids);
                rows = rows
                    .Where(r => Guid.TryParse(r.ContactId, out var guid) && withPhoto.Contains(guid))
                    .ToList();
            }

            return rows;
        }

        private object LoadChurchMemberRows(DataSourceLoadOptions loadOptions, bool photoOnly)
        {
            // 依「計算欄位」(例如小組名稱)排序時，CRM 查詢層無法排序 → 改載入整份(過濾後)清單，
            // 於記憶體排序+分頁；其餘(姓名/手機/預設)仍走 CRM 伺服器端分頁(較快)。
            if (ChurchSortRequiresInMemory(loadOptions))
            {
                return LoadChurchMemberRowsInMemory(loadOptions, photoOnly);
            }

            var service = ToolUtility.m_Crm2011OrganizationService;
            var take = loadOptions?.Take > 0 ? Math.Min(loadOptions.Take, MaxPageSize) : DefaultPageSize;
            var skip = loadOptions?.Skip > 0 ? loadOptions.Skip : 0;
            var pageNumber = skip / take + 1;
            var searchValue = GetSearchTerm(loadOptions);

            var query = BuildCurrentContactQuery(new ColumnSet("contactid", "fullname", "mobilephone", "customertypecode", "statecode"), searchValue, photoOnly);
            query.PageInfo = new PagingInfo
            {
                Count = take,
                PageNumber = pageNumber,
                ReturnTotalRecordCount = true
            };
            ApplyChurchMemberSort(query, loadOptions);

            var contacts = service.RetrieveMultiple(query);
            var ids = contacts.Entities.Select(e => e.Id).ToList();
            var groupMap = GetSmallGroupNamesForContacts(ids);

            var resolveMembershipStatus = CreateMembershipStatusResolver();
            var rows = contacts.Entities.Select(contact =>
            {
                groupMap.TryGetValue(contact.Id, out var groupNames);
                return new MemberInfoListRowViewModel
                {
                    ContactId = contact.Id.ToString(),
                    FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                    Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                    MembershipStatus = resolveMembershipStatus(contact),
                    SmallGroupName = groupNames ?? string.Empty
                };
            }).ToList();

            var totalCount = contacts.TotalRecordCount >= 0
                ? contacts.TotalRecordCount
                : skip + rows.Count + (contacts.MoreRecords ? 1 : 0);

            return new
            {
                data = rows,
                totalCount
            };
        }

        /// <summary>
        /// 全教會清單採「伺服器端分頁」(CRM PageInfo)，排序必須在 CRM 查詢層套用（分頁之前），
        /// 否則只會排到「當前頁」而看起來像沒排序。這裡把 DataGrid 要求的排序(loadOptions.Sort)
        /// 轉成 CRM AddOrder。未指定、或欄位無法於 CRM 直接排序(如計算得來的小組名稱)時，
        /// 退回預設 fullname 遞增。
        /// </summary>
        private static void ApplyChurchMemberSort(QueryExpression query, DataSourceLoadOptions loadOptions)
        {
            var applied = false;
            var sort = loadOptions?.Sort;

            if (sort != null)
            {
                foreach (var sortInfo in sort)
                {
                    var attribute = MapChurchSortAttribute(sortInfo?.Selector);
                    if (attribute == null)
                    {
                        continue;
                    }

                    query.AddOrder(attribute, sortInfo.Desc ? OrderType.Descending : OrderType.Ascending);
                    applied = true;
                }
            }

            if (!applied)
            {
                query.AddOrder("fullname", OrderType.Ascending);
            }
        }

        /// <summary>把 DataGrid 欄位 dataField 對應到可在 CRM 排序的 contact 屬性；不可排序者回 null。</summary>
        private static string MapChurchSortAttribute(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return null;
            }

            if (string.Equals(selector, "FullName", StringComparison.OrdinalIgnoreCase))
            {
                return "fullname";
            }

            if (string.Equals(selector, "Phone", StringComparison.OrdinalIgnoreCase))
            {
                return "mobilephone";
            }

            if (string.Equals(selector, "MembershipStatus", StringComparison.OrdinalIgnoreCase))
            {
                return "customertypecode";
            }

            // 其餘（例如 SmallGroupName 為查詢後計算的小組名稱）CRM 無法直接排序。
            return null;
        }

        /// <summary>是否有「CRM 無法排序的欄位」(如計算得來的小組名稱)被要求排序？是則需改走記憶體排序。</summary>
        private static bool ChurchSortRequiresInMemory(DataSourceLoadOptions loadOptions)
        {
            var sort = loadOptions?.Sort;
            if (sort == null)
            {
                return false;
            }

            foreach (var sortInfo in sort)
            {
                if (sortInfo?.Selector == null)
                {
                    continue;
                }

                if (MapChurchSortAttribute(sortInfo.Selector) == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 全教會：取得整份(過濾後)清單(含小組名稱、帶快取)，交給 DataSourceLoader 於記憶體
        /// 套用排序(含計算欄位 SmallGroupName)與分頁。僅在依計算欄位排序時才走此路徑。
        /// </summary>
        private object LoadChurchMemberRowsInMemory(DataSourceLoadOptions loadOptions, bool photoOnly)
        {
            var searchValue = GetSearchTerm(loadOptions);
            var rows = GetChurchMemberRowsCached(searchValue, photoOnly);
            // 已是記憶體清單 → DataSourceLoader 套用排序(含 SmallGroupName)＋分頁，速度快。
            return DataSourceLoader.Load(rows, loadOptions);
        }

        /// <summary>
        /// 取整份全教會清單(含小組名稱)並快取數分鐘。第一次建立較慢，之後翻頁/改排序皆讀快取，
        /// 不再每次重打 CRM —— 這是「按小組排序等很久」的主因。依搜尋字＋是否只看有照片分開快取。
        /// </summary>
        private List<MemberInfoListRowViewModel> GetChurchMemberRowsCached(string searchValue, bool photoOnly)
        {
            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
            var cacheKey = "member-info-church-rows:" + (photoOnly ? "photo:" : "all:") + (searchValue ?? string.Empty);

            if (memoryCache != null &&
                memoryCache.TryGetValue(cacheKey, out List<MemberInfoListRowViewModel> cached) &&
                cached != null)
            {
                return cached;
            }

            var rows = BuildAllChurchMemberRows(searchValue, photoOnly);

            memoryCache?.Set(cacheKey, rows, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
                SlidingExpiration = TimeSpan.FromMinutes(1),
                Size = Math.Max(1, rows.Count)
            });

            return rows;
        }

        /// <summary>建立整份全教會清單：一次撈所有(過濾後)聯絡人 ＋ 一次撈所有小組成員，合併為資料列。</summary>
        private List<MemberInfoListRowViewModel> BuildAllChurchMemberRows(string searchValue, bool photoOnly)
        {
            var service = ToolUtility.m_Crm2011OrganizationService;

            var query = BuildCurrentContactQuery(
                new ColumnSet("contactid", "fullname", "mobilephone", "customertypecode", "statecode"),
                searchValue,
                photoOnly);
            query.AddOrder("fullname", OrderType.Ascending); // 穩定基準次序；真正排序由 DataSourceLoader 套用

            var contacts = RetrieveAllContacts(service, query);
            var groupMap = GetAllSmallGroupNames(); // 一次撈回所有人的小組名稱，取代逐 200 筆查詢

            var resolveMembershipStatus = CreateMembershipStatusResolver();
            return contacts.Select(contact =>
            {
                groupMap.TryGetValue(contact.Id, out var groupNames);
                return new MemberInfoListRowViewModel
                {
                    ContactId = contact.Id.ToString(),
                    FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                    Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                    MembershipStatus = resolveMembershipStatus(contact),
                    SmallGroupName = groupNames ?? string.Empty
                };
            }).ToList();
        }

        /// <summary>分頁迴圈取回查詢的所有資料列（CRM 單次回傳有上限；逐頁累加直到 MoreRecords 為 false）。</summary>
        private static List<Entity> RetrieveAllContacts(IOrganizationService service, QueryExpression query)
        {
            var all = new List<Entity>();
            query.PageInfo = new PagingInfo
            {
                Count = 2000,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = false
            };

            while (true)
            {
                var page = service.RetrieveMultiple(query);
                if (page?.Entities != null)
                {
                    all.AddRange(page.Entities);
                }

                if (page == null || !page.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            return all;
        }

        /// <summary>一次撈回所有「小組名單」成員 → 小組名稱(以 PagingCookie 逐頁累加)，回傳 contactId → 小組名稱字串。</summary>
        private Dictionary<Guid, string> GetAllSmallGroupNames()
        {
            var names = new Dictionary<Guid, HashSet<string>>();
            var result = new Dictionary<Guid, string>();

            try
            {
                var service = ToolUtility.m_Crm2011OrganizationService;
                var query = new QueryExpression("listmember")
                {
                    ColumnSet = new ColumnSet("entityid", "listid")
                };

                var listLink = new LinkEntity("listmember", "list", "listid", "listid", JoinOperator.Inner)
                {
                    EntityAlias = "list",
                    Columns = new ColumnSet("listname")
                };
                listLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                listLink.LinkCriteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
                listLink.LinkCriteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
                query.LinkEntities.Add(listLink);

                query.PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1,
                    PagingCookie = null,
                    ReturnTotalRecordCount = false
                };

                while (true)
                {
                    var page = service.RetrieveMultiple(query);
                    foreach (var listMember in page.Entities)
                    {
                        var contactId = GetListMemberContactId(listMember);
                        if (contactId == Guid.Empty)
                        {
                            continue;
                        }

                        var listName = GetAliasedString(listMember, "list.listname");
                        if (string.IsNullOrWhiteSpace(listName))
                        {
                            continue;
                        }

                        if (!names.TryGetValue(contactId, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            names[contactId] = set;
                        }

                        set.Add(listName);
                    }

                    if (!page.MoreRecords)
                    {
                        break;
                    }

                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = page.PagingCookie;
                }
            }
            catch
            {
                // 取小組名稱失敗時回傳已收集到的部分，不讓整個清單壞掉。
            }

            foreach (var pair in names)
            {
                result[pair.Key] = string.Join("、", pair.Value.OrderBy(n => n));
            }

            return result;
        }

        private QueryExpression BuildCurrentContactQuery(ColumnSet columns, string searchValue, bool photoOnly = false)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = columns
            };

            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            // 只看「真正有大頭照」的會友：entityimageid 為影像主鍵，有照片才會有值(輕量、不需抓影像位元組)。
            if (photoOnly)
            {
                var photoFilter = new FilterExpression(LogicalOperator.Or);
                photoFilter.AddCondition("entityimageid", ConditionOperator.NotNull);
                photoFilter.AddCondition(
                    ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute,
                    ConditionOperator.NotNull);
                query.Criteria.Filters.Add(photoFilter);
            }

            var closedStatus = TryGetClosedCustomerTypeValue();
            if (closedStatus.HasValue)
            {
                var currentContactStatusFilter = new FilterExpression(LogicalOperator.Or);
                currentContactStatusFilter.AddCondition("customertypecode", ConditionOperator.Null);
                currentContactStatusFilter.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus.Value);
                query.Criteria.Filters.Add(currentContactStatusFilter);
            }

            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                var search = "%" + searchValue.Trim() + "%";
                var searchFilter = new FilterExpression(LogicalOperator.Or);
                searchFilter.AddCondition("fullname", ConditionOperator.Like, search);
                searchFilter.AddCondition("mobilephone", ConditionOperator.Like, search);

                // 會員身分(customertypecode)是 OptionSet(整數)，無法用 Like 比對文字 → 先把搜尋字
                // 比對到符合的選項值，再用 customertypecode In (...) 一併納入同一組 OR 條件。
                var statusValues = GetCustomerTypeValuesMatchingText(searchValue);
                if (statusValues.Count > 0)
                {
                    searchFilter.AddCondition("customertypecode", ConditionOperator.In, statusValues.Select(v => (object)v).ToArray());
                }

                query.Criteria.Filters.Add(searchFilter);
            }

            return query;
        }

        /// <summary>
        /// 從 DevExtreme 載入選項取得搜尋字串。
        /// DataGrid 搜尋面板 + 遠端篩選時，搜尋字串可能以 SearchValue 或包在 Filter
        /// （例如 [["FullName","contains","胡"],"or",["Phone","contains","胡"]]）傳來，兩者都處理。
        /// </summary>
        private string GetSearchTerm(DataSourceLoadOptions loadOptions)
        {
            var fromFilter = ExtractFilterSearchValue(loadOptions?.Filter as System.Collections.IList);
            if (!string.IsNullOrWhiteSpace(fromFilter))
            {
                return fromFilter.Trim();
            }

            var raw = Request?.Query["searchValue"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().Trim('"');
        }

        /// <summary>從 DevExtreme filter 運算式遞迴取出第一個條件值（搜尋面板各欄位用同一搜尋字）。</summary>
        private static string ExtractFilterSearchValue(System.Collections.IList filter)
        {
            if (filter == null || filter.Count == 0)
            {
                return null;
            }

            // 葉節點條件：[field(string), operator(string), value]
            if (filter.Count == 3 && filter[0] is string && filter[1] is string op &&
                IsConditionOperator(op) && filter[2] != null)
            {
                return filter[2].ToString();
            }

            // 群組（含 "and"/"or" 連接子）→ 逐一遞迴
            foreach (var item in filter)
            {
                if (item is System.Collections.IList sub)
                {
                    var found = ExtractFilterSearchValue(sub);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static bool IsConditionOperator(string op)
        {
            switch (op)
            {
                case "contains":
                case "notcontains":
                case "startswith":
                case "endswith":
                case "=":
                case "<>":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>讀取前端傳來的「只看有照片」旗標(photoOnly=true)。</summary>
        private bool IsPhotoOnlyRequested()
        {
            var raw = Request?.Query["photoOnly"].FirstOrDefault();
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>查出指定聯絡人中「真正有大頭照」者(entityimageid 不為空)。以小批次查詢，失敗回空集合。</summary>
        private HashSet<Guid> GetContactIdsWithPhoto(IReadOnlyCollection<Guid> contactIds)
        {
            var result = new HashSet<Guid>();
            if (contactIds == null || contactIds.Count == 0)
            {
                return result;
            }

            try
            {
                var service = ToolUtility.m_Crm2011OrganizationService;
                var idList = contactIds.Distinct().ToList();
                const int batchSize = 200;

                for (var i = 0; i < idList.Count; i += batchSize)
                {
                    var chunk = idList.GetRange(i, Math.Min(batchSize, idList.Count - i));
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid")
                    };
                    query.Criteria.AddCondition("contactid", ConditionOperator.In, chunk.Select(id => (object)id).ToArray());
                    var photoFilter = new FilterExpression(LogicalOperator.Or);
                    photoFilter.AddCondition("entityimageid", ConditionOperator.NotNull);
                    photoFilter.AddCondition(
                        ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute,
                        ConditionOperator.NotNull);
                    query.Criteria.Filters.Add(photoFilter);

                    foreach (var entity in service.RetrieveMultiple(query).Entities)
                    {
                        result.Add(entity.Id);
                    }
                }
            }
            catch
            {
                // entityimageid 不可查時不讓整頁壞掉；photoOnly 牧養名單顯示為空，預設(全部)不受影響。
            }

            return result;
        }

        private Dictionary<Guid, string> GetSmallGroupNamesForContacts(IReadOnlyCollection<Guid> contactIds)
        {
            var result = new Dictionary<Guid, string>();
            if (contactIds == null || contactIds.Count == 0)
            {
                return result;
            }

            try
            {
                var query = new QueryExpression("listmember")
                {
                    ColumnSet = new ColumnSet("entityid", "listid")
                };
                query.Criteria.AddCondition("entityid", ConditionOperator.In, contactIds.Select(id => (object)id).ToArray());

                var listLink = new LinkEntity("listmember", "list", "listid", "listid", JoinOperator.Inner)
                {
                    EntityAlias = "list",
                    Columns = new ColumnSet("listname")
                };
                listLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                listLink.LinkCriteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
                listLink.LinkCriteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
                query.LinkEntities.Add(listLink);

                var listMembers = ToolUtility.m_Crm2011OrganizationService.RetrieveMultiple(query);
                var names = new Dictionary<Guid, HashSet<string>>();

                foreach (var listMember in listMembers.Entities)
                {
                    var contactId = GetListMemberContactId(listMember);
                    if (contactId == Guid.Empty)
                    {
                        continue;
                    }

                    var listName = GetAliasedString(listMember, "list.listname");
                    if (string.IsNullOrWhiteSpace(listName))
                    {
                        continue;
                    }

                    if (!names.TryGetValue(contactId, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        names[contactId] = set;
                    }

                    set.Add(listName);
                }

                foreach (var item in names)
                {
                    result[item.Key] = string.Join("、", item.Value.OrderBy(n => n));
                }
            }
            catch
            {
                // The group column is supplementary. Leave it blank if the CRM relationship query is unavailable.
            }

            return result;
        }

        private bool IsCurrentContact(Guid contactId)
        {
            try
            {
                var contact = ToolUtility.m_Crm2011OrganizationService.Retrieve(
                    "contact",
                    contactId,
                    new ColumnSet("statecode", "customertypecode"));

                return IsCurrentContactEntity(contact);
            }
            catch
            {
                return false;
            }
        }

        private bool IsCurrentContactEntity(Entity contact)
        {
            if (contact == null)
            {
                return false;
            }

            var state = contact.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? -1;
            if (state != 0)
            {
                return false;
            }

            var closedStatus = TryGetClosedCustomerTypeValue();
            if (closedStatus.HasValue &&
                contact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value == closedStatus.Value)
            {
                return false;
            }

            return true;
        }

        private int? TryGetClosedCustomerTypeValue()
        {
            try
            {
                // 改用共用快取的 OptionSet 服務：「結案」狀態值整個 App 只查一次 CRM metadata，之後全走快取，
                // 避免逐人(逐列)各打一支 metadata 查詢。文字→值對照非使用者專屬資料，快取安全。
                return GetSharedOptionSetService().GetOptionSetValue("contact", "customertypecode", "結案", null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>建立以「共用」記憶體快取支援的 OptionSetMetadataService（metadata 整個 App 只查一次、快取 24h）。</summary>
        private OptionSetMetadataService GetSharedOptionSetService()
        {
            return new OptionSetMetadataService(
                ToolUtility.m_Crm2011OrganizationService,
                null,
                memberInfoMemoryCache);
        }

        private OptionSetMetadataService GetSharedOptionSetService(IOrganizationService service)
        {
            return new OptionSetMetadataService(service, null, memberInfoMemoryCache);
        }

        /// <summary>
        /// 取得 contact.customertypecode 的客製化排列快照；同一個 App 共用 schema metadata 快取。
        /// </summary>
        private IReadOnlyList<MemberInfoCommitmentTypeOption> GetCommitmentTypeOptions(
            IOrganizationService service)
        {
            return new MemberInfoCommitmentTypeMetadataProvider(
                service,
                memberInfoMemoryCache).GetOptions();
        }

        /// <summary>
        /// 依 ORG-CALL-00040 的 immutable gate 決定承諾 metadata 來源。gate=false 時回傳 null，讓呼叫端明確保留
        /// 既有 legacy compatibility path，且不 bind profile、不解析 process host、不建立 typed client 或 outbound I/O。
        /// gate=true 時只建立固定 deployment profile、固定 workload 與固定 metadata target 的 Package03 service；client
        /// unavailable、typed fault 或取消一律原樣傳播，禁止 retry、partial snapshot 與 legacy fallback。此方法不快取
        /// result、token、client 或例外；typed facade 的 pool/lease/connection cleanup 仍由 Generic Host owner 負責。
        /// </summary>
        /// <param name="configuration">deployment-owned base/sub gate 與 profile 設定；不得由 HTTP、Session 或 browser 覆寫。</param>
        /// <param name="useTypedCommitmentMetadata">action 早期固定的 gate 決策，避免同一 request 重新讀取可變設定。</param>
        /// <param name="cancellationToken">目前 HTTP request 的取消 token；不保存或註冊，原樣送往 typed service。</param>
        /// <returns>gate=true 時的新 read-only snapshot；gate=false 時為 null 以選擇既有 compatibility branch。</returns>
        private async Task<IReadOnlyList<MemberInfoCommitmentTypeOption>?> LoadCommitmentTypeOptionsAsync(
            IConfiguration configuration,
            bool useTypedCommitmentMetadata,
            CancellationToken cancellationToken)
        {
            if (!useTypedCommitmentMetadata)
            {
                return null;
            }

            var package03Client = DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoCommitmentMetadataReadClient(configuration)
                ?? throw new InvalidOperationException(
                    "The Package03 MemberInfo commitment metadata typed client was unavailable after the deployment gate was enabled.");
            var metadataService = new Package03MemberInfoCommitmentMetadataReadService(
                package03Client,
                DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias);
            return (await metadataService.RetrieveAsync(cancellationToken).ConfigureAwait(false)).GetOptions();
        }

        /// <summary>
        /// 把搜尋字比對到「會員身分(customertypecode)」選項文字(以 contains、不分大小寫)，回傳所有相符的選項值。
        /// 供全教會伺服器端搜尋使用：OptionSet 無法 Like 文字，需先轉成值再用 In 篩選。
        /// </summary>
        private List<int> GetCustomerTypeValuesMatchingText(string searchValue)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(searchValue))
            {
                return result;
            }

            try
            {
                var mapping = GetSharedOptionSetService().GetOptionSetMapping("contact", "customertypecode"); // 文字→值
                var term = searchValue.Trim();
                foreach (var pair in mapping)
                {
                    if (!string.IsNullOrEmpty(pair.Key) &&
                        pair.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(pair.Value);
                    }
                }
            }
            catch
            {
                // 取不到 metadata 時回空集合：搜尋退回只比對姓名/手機，不讓清單壞掉。
            }

            return result;
        }

        /// <summary>
        /// 建一個「會員身分(customertypecode) 值→文字」解析器：以「共用」的記憶體快取建立單一
        /// OptionSetMetadataService。第一次查一次 CRM Metadata 後，同一份清單(數百~數千列)即全部
        /// 走快取字典查詢，避免「每列各自 new 服務、各自打 CRM Metadata」造成大量往返。
        /// </summary>
        private Func<Entity, string> CreateMembershipStatusResolver()
        {
            var metadataService = GetSharedOptionSetService();

            return contact =>
            {
                try
                {
                    if (contact == null || !contact.Contains("customertypecode"))
                    {
                        return string.Empty;
                    }

                    var value = ToolUtility.GetOptionSetAttribute(contact, "customertypecode");
                    if (value < 0)
                    {
                        return string.Empty;
                    }

                    return metadataService.GetOptionSetText("contact", "customertypecode", value);
                }
                catch
                {
                    return string.Empty;
                }
            };
        }

        private string GetOptionSetText(Entity entity, string attributeName)
        {
            try
            {
                if (entity == null || !entity.Contains(attributeName))
                {
                    return string.Empty;
                }

                var value = ToolUtility.GetOptionSetAttribute(entity, attributeName);
                if (value < 0)
                {
                    return string.Empty;
                }

                var service = new OptionSetMetadataService(ToolUtility.m_Crm2011OrganizationService);
                return service.GetOptionSetText(entity.LogicalName, attributeName, value);
            }
            catch
            {
                return string.Empty;
            }
        }

        private List<RelationGoalItem> GetRelationGoals(Guid contactId)
        {
            var result = new List<RelationGoalItem>();

            try
            {
                var query = new QueryExpression("connection")
                {
                    ColumnSet = new ColumnSet("record1id", "record2id", "record1roleid", "record2roleid")
                };
                query.Criteria.FilterOperator = LogicalOperator.Or;
                query.Criteria.AddCondition("record1id", ConditionOperator.Equal, contactId);
                query.Criteria.AddCondition("record2id", ConditionOperator.Equal, contactId);

                var connections = ToolUtility.m_Crm2011OrganizationService.RetrieveMultiple(query);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var connection in connections.Entities)
                {
                    var record1 = connection.GetAttributeValue<EntityReference>("record1id");
                    var record2 = connection.GetAttributeValue<EntityReference>("record2id");
                    var isRecord1 = record1 != null && record1.Id == contactId;
                    var target = isRecord1 ? record2 : record1;
                    // 顯示「對象（目標）的角色」：對象是 record2 時取 record2roleid，反之取 record1roleid。
                    // （原本取登入連絡人自己的角色，導致顯示「丈夫」而非對象的「妻子」、且對象角色空白。）
                    var role = isRecord1
                        ? connection.GetAttributeValue<EntityReference>("record2roleid")
                        : connection.GetAttributeValue<EntityReference>("record1roleid");

                    var roleName = role?.Name ?? string.Empty;
                    var targetName = target?.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(roleName) && string.IsNullOrWhiteSpace(targetName))
                    {
                        continue;
                    }

                    // 去重：雙向查詢（含 Dynamics 自動建立的反向 connection）會讓同一段關係出現兩次
                    var dedupKey = roleName + "|" + (target?.Id.ToString() ?? targetName);
                    if (!seen.Add(dedupKey))
                    {
                        continue;
                    }

                    result.Add(new RelationGoalItem
                    {
                        Role = roleName,
                        TargetName = targetName
                    });
                }
            }
            catch
            {
                // CRM connection may be disabled or inaccessible in some environments.
            }

            return result;
        }

        private static ColumnSet GetContactListColumns()
        {
            return new ColumnSet("contactid", "fullname", "mobilephone", "customertypecode", "statecode");
        }

        /// <summary>
        /// 詳細彈窗單次 Retrieve 的完整欄位契約。性別與生日刻意併入原查詢，避免為兩個唯讀欄位新增 CRM 往返；
        /// entityimageid／LINE 圖片網址只用來判斷頭像來源，不在此載入影像位元組。
        /// </summary>
        private static ColumnSet GetContactDetailColumns()
        {
            return new ColumnSet(
                "contactid",
                "fullname",
                "mobilephone",
                "address2_line1",
                "gendercode",
                "birthdate",
                "customertypecode",
                "new_spiriitual_identity",
                "entityimageid",
                ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute,
                "statecode");
        }

        private static string ResolveContactAvatarSource(Entity contact)
        {
            if (contact == null)
            {
                return "fallback";
            }

            if (contact.Contains("entityimageid") && contact["entityimageid"] != null)
            {
                return "primary";
            }

            var linePictureUrl = ChurchReport.Services.ContactAvatar.ContactAvatarUrl.NormalizeHttpUrl(
                contact.GetAttributeValue<string>(ChurchReport.Services.ContactAvatar.ContactAvatarUrl.LinePictureUrlAttribute));

            return string.IsNullOrEmpty(linePictureUrl) ? "fallback" : "line";
        }

        private static Guid GetListMemberContactId(Entity listMember)
        {
            if (listMember == null || !listMember.Contains("entityid"))
            {
                return Guid.Empty;
            }

            if (listMember["entityid"] is EntityReference entityRef)
            {
                return entityRef.Id;
            }

            if (listMember["entityid"] is Guid guid)
            {
                return guid;
            }

            return Guid.Empty;
        }

        private static Guid GetListMemberListId(Entity listMember)
        {
            if (listMember == null || !listMember.Contains("listid"))
            {
                return Guid.Empty;
            }

            if (listMember["listid"] is EntityReference entityRef)
            {
                return entityRef.Id;
            }

            if (listMember["listid"] is Guid guid)
            {
                return guid;
            }

            return Guid.Empty;
        }

        private static string GetAliasedString(Entity entity, string alias)
        {
            if (entity == null || !entity.Contains(alias))
            {
                return string.Empty;
            }

            if (entity[alias] is AliasedValue aliased)
            {
                return aliased.Value?.ToString() ?? string.Empty;
            }

            return entity[alias]?.ToString() ?? string.Empty;
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
                image.Save(output, new JpegEncoder { Quality = 82 });
                return output.ToArray();
            }
            catch
            {
                return originalBytes;
            }
        }

        /// <summary>等比縮放使整張照片「完整塞入」size×size(不裁切，保留完整頭部)；原圖較小則回原圖。供會友細節大圖使用。</summary>
        private static byte[] CreateFitThumbnail(byte[] originalBytes, int size)
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
                    Mode = ResizeMode.Max
                }));

                using var output = new MemoryStream();
                image.Save(output, new JpegEncoder { Quality = 85 });
                return output.ToArray();
            }
            catch
            {
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

        private IActionResult GetDefaultImage()
        {
            // 未授權/查無資料時回傳中性剪影（不洩漏性別）
            return Content(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.Neutral, "image/svg+xml");
        }
    }
}
