// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationKeyInDedicationService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationKeyInDedicationService、record DonationContactQuery
// 主要成員：SaveAsync、QueryAsync、AuditQueryAsync、UpdateAsync、BuildQuery、QueryContacts、FillSameNameList、ContactNotFound、ResolvePhoneNumber、NotifyAndThrow
// 引用命名空間：System、System.Linq、System.Threading.Tasks、ChurchReport.Models、ChurchReport.WebServiceConnector、LineMessagingProcessor、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Linq;
using System.Threading.Tasks;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 手動輸入奉獻資料的查詢與更新流程。
    ///
    /// 這個服務處理的是 ChurchReport 的 CRM contact 查詢、同名清單、奉獻收費單查詢以及
    /// 既有 AJAX JSON 回應格式；它不是通用金流能力，因此不能放進 SpeechMessage.Payments
    /// 或 SpeechMessage.Payments.AspNetCore。未來其他產品若也需要「人工補單」流程，應在各自
    /// 產品實作自己的查詢與回應格式，只共用中立的付款核心。
    /// </summary>
    public sealed class DonationKeyInDedicationService
    {
        private readonly ToolUtilityClass _utility;
        private readonly DonationPaymentFormModel _formModel;
        private readonly DonationPaymentProcessor _paymentProcessor;
        private readonly Func<object, JsonResult> _json;
        private readonly Action<string> _notifyError;

        public DonationKeyInDedicationService(
            ToolUtilityClass utility,
            DonationPaymentFormModel formModel,
            DonationPaymentProcessor paymentProcessor,
            Func<object, JsonResult> json,
            Action<string> notifyError)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _formModel = formModel ?? throw new ArgumentNullException(nameof(formModel));
            _paymentProcessor = paymentProcessor ?? throw new ArgumentNullException(nameof(paymentProcessor));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _notifyError = notifyError ?? throw new ArgumentNullException(nameof(notifyError));
        }

        public async Task<IActionResult> SaveAsync(DonationPaymentFormModel donationPaymentFormModel)
        {
            return donationPaymentFormModel.ClickType == "查詢"
                ? await QueryAsync(donationPaymentFormModel)
                : await UpdateAsync(donationPaymentFormModel);
        }

        public async Task<IActionResult> QueryAsync(DonationPaymentFormModel donationPaymentFormModel)
        {
            try
            {
                string dedicationResult = string.Empty;
                DonationContactQuery query = BuildQuery(donationPaymentFormModel);
                EntityCollection contacts = QueryContacts(query);

                if (contacts.Entities.Count == 1)
                {
                    Entity contact = contacts.Entities[0];
                    string dedicationNumber = _utility.GetEntityStringAttribute(contact, "pager");
                    string nationId = _utility.GetEntityStringAttribute(contact, "new_personal_id");
                    string fullName = _utility.GetEntityStringAttribute(contact, "fullname");
                    string homePhone = _utility.GetEntityStringAttribute(contact, "telephone2");
                    string mobile = _utility.GetEntityStringAttribute(contact, "mobilephone");
                    string lastSixDigit = _utility.GetEntityStringAttribute(contact, "new_last_six_digit");
                    Entity retrievedContact = _utility.RetrieveEntity("contact", contact.Id);
                    string churchName = _utility.GetEntityLookupDisplayName(ref retrievedContact, "parentcustomerid");
                    string phoneNumber = ResolvePhoneNumber(mobile, homePhone);

                    return _json(new
                    {
                        status = "1",
                        clicktype = "查詢",
                        DedicationNumber = dedicationNumber,
                        NationId = nationId,
                        FullName = fullName,
                        Mobile = phoneNumber,
                        LastSixDigit = lastSixDigit,
                        message = dedicationResult,
                        DedicationResult = dedicationResult,
                        ChurchName = churchName
                    });
                }

                if (contacts.Entities.Count > 1)
                {
                    FillSameNameList(contacts, includeChurchName: true);
                    return _json(new
                    {
                        status = "2",
                        clicktype = "查詢",
                        DedicationNumber = "",
                        NationId = query.NationId,
                        FullName = "",
                        Mobile = "",
                        message = "",
                        DedicationResult = ""
                    });
                }

                return ContactNotFound(query.FullName);
            }
            catch (Exception e)
            {
                NotifyAndThrow(e);
                throw;
            }
        }

        public async Task<IActionResult> AuditQueryAsync(DonationPaymentFormModel donationPaymentFormModel, Action<Entity> fillFeeList)
        {
            try
            {
                string dedicationResult = string.Empty;
                DonationContactQuery query = BuildQuery(donationPaymentFormModel);
                _formModel.QueryStartDate = donationPaymentFormModel.QueryStartDate;
                _formModel.QueryEndDate = donationPaymentFormModel.QueryEndDate;

                EntityCollection contacts = QueryContacts(query);

                if (contacts.Entities.Count == 1)
                {
                    Entity contact = contacts.Entities[0];
                    string dedicationNumber = _utility.GetEntityStringAttribute(contact, "pager");
                    string nationId = _utility.GetEntityStringAttribute(contact, "new_personal_id");
                    string fullName = _utility.GetEntityStringAttribute(contact, "fullname");
                    string homePhone = _utility.GetEntityStringAttribute(contact, "telephone2");
                    string mobile = _utility.GetEntityStringAttribute(contact, "mobilephone");
                    string lastSixDigit = _utility.GetEntityStringAttribute(contact, "new_last_six_digit");
                    string phoneNumber = ResolvePhoneNumber(mobile, homePhone);

                    fillFeeList(contact);
                    var feeList = DonationFeeQueryService.ToAjaxRows(_formModel.DedicationFeeList);

                    return _json(new
                    {
                        status = "1",
                        clicktype = "查詢",
                        DedicationNumber = dedicationNumber,
                        NationId = nationId,
                        FullName = fullName,
                        Mobile = phoneNumber,
                        LastSixDigit = lastSixDigit,
                        TotalAmount = _formModel.TotalAmount,
                        DedicationFeeList = feeList,
                        message = dedicationResult,
                        DedicationResult = dedicationResult
                    });
                }

                if (contacts.Entities.Count > 1)
                {
                    FillSameNameList(contacts, includeChurchName: false);
                    return _json(new
                    {
                        status = "2",
                        clicktype = "查詢",
                        DedicationNumber = "",
                        NationId = query.NationId,
                        FullName = "",
                        Mobile = "",
                        message = "",
                        DedicationResult = ""
                    });
                }

                return ContactNotFound(query.FullName);
            }
            catch (Exception e)
            {
                NotifyAndThrow(e);
                throw;
            }
        }

        public async Task<IActionResult> UpdateAsync(DonationPaymentFormModel donationPaymentFormModel)
        {
            try
            {
                if (donationPaymentFormModel.Amount == null || donationPaymentFormModel.Amount <= 0)
                {
                    return _json(new { status = "3", clicktype = "上傳", message = "未輸入奉獻金額" });
                }

                string dedicationResult = await _paymentProcessor.SaveKeyInDedication(donationPaymentFormModel);
                string status = dedicationResult.Contains("錯誤", StringComparison.Ordinal) ? "3" : "1";

                return _json(new
                {
                    status,
                    clicktype = "上傳",
                    message = dedicationResult,
                    DedicationResult = dedicationResult
                });
            }
            catch (Exception e)
            {
                NotifyAndThrow(e);
                throw;
            }
        }

        private DonationContactQuery BuildQuery(DonationPaymentFormModel model)
        {
            return new DonationContactQuery(
                model.DedicationNumber ?? "未填奉獻編號",
                model.NationId ?? "未填身分證字號",
                model.FullName ?? "未填姓名",
                model.Mobile ?? "未填手機號碼",
                model.Mobile ?? "未填手機號碼",
                model.LastSixDigit ?? "未填帳戶後六碼");
        }

        private EntityCollection QueryContacts(DonationContactQuery query)
        {
            return _utility.QueryDediccationContatsByFetchXml(
                query.DedicationNumber,
                query.FullName,
                query.HomePhone,
                query.Mobile,
                query.NationId,
                query.LastSixDigit);
        }

        private void FillSameNameList(EntityCollection contacts, bool includeChurchName)
        {
            _formModel.SameNameList.Clear();

            foreach (Entity contact in contacts.Entities)
            {
                var sameName = new SameNameElement
                {
                    SameNameElementId = contact.Id.ToString(),
                    DedicationNumber = _utility.GetEntityStringAttribute(contact, "pager"),
                    NationId = _utility.GetEntityStringAttribute(contact, "new_personal_id"),
                    FullName = _utility.GetEntityStringAttribute(contact, "fullname"),
                    Mobile = ResolvePhoneNumber(
                        _utility.GetEntityStringAttribute(contact, "mobilephone"),
                        _utility.GetEntityStringAttribute(contact, "telephone2")),
                    SmallGroupName = _utility.GetEntityLookupDisplayName(contact, "new_cell_list_contact")
                };

                if (includeChurchName)
                {
                    sameName.ChurchName = _utility.GetEntityLookupDisplayName(contact, "parentcustomerid");
                }

                _formModel.SameNameList.Add(sameName);
            }
        }

        private JsonResult ContactNotFound(string fullName)
        {
            return _json(new
            {
                status = "3",
                clicktype = "查詢",
                message = "沒找到這個連絡人",
                fullname = fullName,
                DedicationResult = "沒找到這個連絡人"
            });
        }

        private static string ResolvePhoneNumber(string mobile, string homePhone)
        {
            return mobile != string.Empty ? mobile : homePhone;
        }

        private void NotifyAndThrow(Exception exception)
        {
            string errorString = "錯誤訊息 : FullName = " + GetType().FullName +
                " , Time = " + DateTime.Now +
                " , Description = " + exception;

            _notifyError(errorString);
        }

        private sealed record DonationContactQuery(
            string DedicationNumber,
            string NationId,
            string FullName,
            string HomePhone,
            string Mobile,
            string LastSixDigit);
    }
}
