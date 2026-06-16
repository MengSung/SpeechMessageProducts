using ChurchReport.Models;
using ChurchReport.Services;
using ChurchReport.Services.MemberInfo;
using ChurchReport.Tools;
using ChurchReport.ViewModels;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Controllers
{
    public class MemberInfoController : BaseChurchController
    {
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;

        public MemberInfoController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
            : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
        {
        }

        [HttpGet]
        [Route("/MemberInfo/Index")]
        public IActionResult Index()
        {
            try
            {
                SetupBasicViewBag();
                SetMultiGroupLayoutParameter();

                if (string.IsNullOrEmpty(GetAccess()))
                {
                    return Forbid();
                }

                return View("MemberInfoGrid");
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.Index");
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

                var model = new MemberInfoDetailViewModel
                {
                    ContactId = contactGuid.ToString(),
                    FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                    Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
                    Address = ToolUtility.GetEntityStringAttribute(contact, "address2_line1"),
                    MembershipStatus = GetOptionSetText(contact, "customertypecode"),
                    SpiritualIdentity = GetOptionSetText(contact, "new_spiriitual_identity"),
                    RelationGoals = GetRelationGoals(contactGuid)
                };

                return PartialView("_MemberDetailPopup", model);
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.Detail");
            }
        }

        [HttpGet]
        public object LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)
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

        [HttpGet]
        public object LoadContactStorLessons(string contactId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                EnsureCorrectUserData();

                if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
                {
                    return DataSourceLoader.Load(new List<MemberInfoStorLessonRow>(), loadOptions);
                }

                var service = ToolUtility.m_Crm2011OrganizationService;
                var contact = service.Retrieve("contact", contactGuid, new ColumnSet("fullname"));
                var fullName = ToolUtility.GetEntityStringAttribute(contact, "fullname");

                var rows = new List<MemberInfoStorLessonRow>();
                var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml(fullName, contactGuid.ToString());

                if (storLessons?.Entities != null)
                {
                    foreach (var lessonEntity in storLessons.Entities)
                    {
                        var lesson = lessonEntity;
                        var discipleLessonId = ToolUtility.GetEntityLookupAttribute(ref lesson, "new_new_disciple_lessons_new_stor_les");

                        var classStartDate = DateTime.MinValue;
                        var stageName = string.Empty;

                        if (discipleLessonId != Guid.Empty)
                        {
                            try
                            {
                                var discipleLesson = ToolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId);
                                classStartDate = ToolUtility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date");
                                stageName = ToolUtility.GetEntityStringAttribute(ref discipleLesson, "new_now_stage_name");
                            }
                            catch
                            {
                                // Keep the lesson row visible even when the linked course cannot be read.
                            }
                        }

                        rows.Add(new MemberInfoStorLessonRow
                        {
                            StorLessonsEntityId = lesson.Id.ToString(),
                            DiscipleLessonsName = ToolUtility.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les"),
                            StageName = stageName,
                            CurrentComplete = ToolUtility.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
                            DiscipleLessonsDateTime = classStartDate
                        });
                    }
                }

                return DataSourceLoader.Load(rows, loadOptions);
            }
            catch (Exception ex)
            {
                return HandleError(ex, "MemberInfo.LoadContactStorLessons");
            }
        }

        [HttpGet]
        [Route("/MemberInfo/GetContactImage")]
        public IActionResult GetContactImage(string contactId, int size = 80)
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
                    : $"member-info-contact-image-thumb:{contactGuid:N}:{thumbSize}";

                if (memoryCache != null &&
                    memoryCache.TryGetValue(cacheKey, out byte[] cachedBytes) &&
                    cachedBytes != null)
                {
                    ApplyImageResponseCacheHeaders();
                    return File(cachedBytes, "image/jpeg");
                }

                service = GetConnection();
                var contact = service.Retrieve("contact", contactGuid, new ColumnSet("entityimage", "gendercode"));
                if (contact.Contains("entityimage") && contact["entityimage"] != null)
                {
                    var originalBytes = (byte[])contact["entityimage"];
                    var outputBytes = returnOriginal ? originalBytes : CreateThumbnailIfNeeded(originalBytes, thumbSize);

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
                    return Json(new { success = true, images = new Dictionary<string, string>() });
                }

                var thumbSize = Math.Clamp(request.Size > 0 ? request.Size : 48, 32, 256);
                var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var uncachedGuids = new List<Guid>();

                foreach (var id in request.ContactIds.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Guid.TryParse(id, out var guid) || !CanViewContact(guid))
                    {
                        continue;
                    }

                    var cacheKey = $"member-info-contact-image-thumb:{guid:N}:{thumbSize}";
                    if (memoryCache != null &&
                        memoryCache.TryGetValue(cacheKey, out byte[] cachedBytes) &&
                        cachedBytes != null)
                    {
                        result[guid.ToString()] = "data:image/jpeg;base64," + Convert.ToBase64String(cachedBytes);
                    }
                    else
                    {
                        uncachedGuids.Add(guid);
                    }
                }

                if (uncachedGuids.Count > 0)
                {
                    service = GetConnection();
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid", "entityimage")
                    };
                    query.Criteria.AddCondition("contactid", ConditionOperator.In, uncachedGuids.Select(g => (object)g).ToArray());

                    var contacts = service.RetrieveMultiple(query);
                    foreach (var contact in contacts.Entities)
                    {
                        if (!contact.Contains("entityimage") || contact["entityimage"] == null)
                        {
                            continue;
                        }

                        var originalBytes = (byte[])contact["entityimage"];
                        var outputBytes = CreateThumbnailIfNeeded(originalBytes, thumbSize);
                        var cacheKey = $"member-info-contact-image-thumb:{contact.Id:N}:{thumbSize}";

                        memoryCache?.Set(cacheKey, outputBytes, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                            SlidingExpiration = TimeSpan.FromMinutes(10),
                            Size = Math.Max(1, outputBytes.Length / 1024)
                        });

                        result[contact.Id.ToString()] = "data:image/jpeg;base64," + Convert.ToBase64String(outputBytes);
                    }
                }

                Response.Headers["Cache-Control"] = "private, no-store";
                return Json(new { success = true, images = result });
            }
            catch
            {
                return Json(new { success = false, images = new Dictionary<string, string>() });
            }
            finally
            {
                ReleaseConnection(service);
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

        /// <summary>讀取上傳圖片並依 EXIF 自動轉正(解決手機直拍旋轉)，轉存 JPEG；處理失敗時退回原始位元組。</summary>
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
                }
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
                query.Criteria.AddCondition("entityimageid", ConditionOperator.NotNull);
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
                    query.Criteria.AddCondition("entityimageid", ConditionOperator.NotNull);

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
                var service = new OptionSetMetadataService(ToolUtility.m_Crm2011OrganizationService);
                return service.GetOptionSetValue("contact", "customertypecode", "結案", null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>建立以「共用」記憶體快取支援的 OptionSetMetadataService（metadata 整個 App 只查一次、快取 24h）。</summary>
        private OptionSetMetadataService GetSharedOptionSetService()
        {
            var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
            return new OptionSetMetadataService(ToolUtility.m_Crm2011OrganizationService, null, memoryCache);
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

        private static ColumnSet GetContactDetailColumns()
        {
            return new ColumnSet(
                "contactid",
                "fullname",
                "mobilephone",
                "address2_line1",
                "customertypecode",
                "new_spiriitual_identity",
                "statecode");
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

        private void ApplyImageResponseCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "private, max-age=1800";
            Response.Headers["Vary"] = "Accept-Encoding";
        }

        private IActionResult GetDefaultImage()
        {
            // 未授權/查無資料時回傳中性剪影（不洩漏性別）
            return Content(ChurchReport.Services.ContactAvatar.DefaultAvatarSvg.Neutral, "image/svg+xml");
        }
    }
}
