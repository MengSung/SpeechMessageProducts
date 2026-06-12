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

                if (access == MemberInfoAccess.Church)
                {
                    return LoadChurchMemberRows(loadOptions);
                }

                if (access == MemberInfoAccess.ShepherdList)
                {
                    var rows = LoadShepherdMemberRows();
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
                var records = ToolUtility.RetrievePresentRecordByFetchXmlAndContainEpiredDate(fullName, contactGuid.ToString());

                if (records?.Entities != null)
                {
                    foreach (var record in records.Entities)
                    {
                        Entity fullRecord;
                        try
                        {
                            fullRecord = service.Retrieve(
                                "new_present_record",
                                record.Id,
                                new ColumnSet("new_sunday_present_this_week", "new_group_present_this_week", "new_explanation"));
                        }
                        catch
                        {
                            fullRecord = record;
                        }

                        rows.Add(new ContactPresentRecordRow
                        {
                            PresentRecordId = record.Id.ToString(),
                            FullName = fullName,
                            Sunday = ToolUtility.GetEntityIntAttribute(fullRecord, "new_sunday_present_this_week") > 0,
                            SmallGroup = ToolUtility.GetEntityIntAttribute(fullRecord, "new_group_present_this_week") > 0,
                            PrayItem = ToolUtility.GetEntityStringAttribute(fullRecord, "new_explanation")
                        });
                    }
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

        private List<MemberInfoListRowViewModel> LoadShepherdMemberRows()
        {
            var rowsByContact = new Dictionary<string, MemberInfoListRowViewModel>(StringComparer.OrdinalIgnoreCase);
            var groupNamesByContact = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            EnsureShepherdListsLoaded();

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

            return rowsByContact.Values
                .OrderBy(r => r.SmallGroupName)
                .ThenBy(r => r.FullName)
                .ToList();
        }

        private object LoadChurchMemberRows(DataSourceLoadOptions loadOptions)
        {
            var service = ToolUtility.m_Crm2011OrganizationService;
            var take = loadOptions?.Take > 0 ? Math.Min(loadOptions.Take, MaxPageSize) : DefaultPageSize;
            var skip = loadOptions?.Skip > 0 ? loadOptions.Skip : 0;
            var pageNumber = skip / take + 1;
            var searchValue = GetSearchTerm(loadOptions);

            var query = BuildCurrentContactQuery(new ColumnSet("contactid", "fullname", "mobilephone", "customertypecode", "statecode"), searchValue);
            query.PageInfo = new PagingInfo
            {
                Count = take,
                PageNumber = pageNumber,
                ReturnTotalRecordCount = true
            };
            query.AddOrder("fullname", OrderType.Ascending);

            var contacts = service.RetrieveMultiple(query);
            var ids = contacts.Entities.Select(e => e.Id).ToList();
            var groupMap = GetSmallGroupNamesForContacts(ids);

            var rows = contacts.Entities.Select(contact =>
            {
                groupMap.TryGetValue(contact.Id, out var groupNames);
                return new MemberInfoListRowViewModel
                {
                    ContactId = contact.Id.ToString(),
                    FullName = ToolUtility.GetEntityStringAttribute(contact, "fullname"),
                    Phone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone"),
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

        private QueryExpression BuildCurrentContactQuery(ColumnSet columns, string searchValue)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = columns
            };

            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

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
                foreach (var connection in connections.Entities)
                {
                    var record1 = connection.GetAttributeValue<EntityReference>("record1id");
                    var record2 = connection.GetAttributeValue<EntityReference>("record2id");
                    var isRecord1 = record1 != null && record1.Id == contactId;
                    var target = isRecord1 ? record2 : record1;
                    var role = isRecord1
                        ? connection.GetAttributeValue<EntityReference>("record1roleid")
                        : connection.GetAttributeValue<EntityReference>("record2roleid");

                    var roleName = role?.Name ?? string.Empty;
                    var targetName = target?.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(roleName) && string.IsNullOrWhiteSpace(targetName))
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
