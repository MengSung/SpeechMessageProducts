using System;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 查無奉獻者時建立 contact 與奉獻編號規則的產品服務。
    ///
    /// 這個類別刻意不放進 SpeechMessage.Payments 或 SpeechMessage.Payments.AspNetCore：
    /// 「contact.lastname」、「contact.pager」、「奉獻編號 0/9 開頭」都是 ChurchReport 的 CRM
    /// 與資料管理規則，不是金流 provider 協定，也不是未來產品必然共用的 ASP.NET Core 流程。
    ///
    /// DonationPaymentManager 只保留 MVC 公開入口；真正的查重、建立、編號與錯誤轉頁集中在此處，
    /// 讓未來若要重新啟用自動奉獻編號時，只需要修改這個小型服務。
    /// </summary>
    public sealed class DonationContactCreationService
    {
        private readonly ToolUtilityClass _utility;
        private readonly Func<object, JsonResult> _json;
        private readonly Func<string, object, RedirectToActionResult> _redirectToAction;
        private readonly Action<string> _notifyRegistrationError;

        public DonationContactCreationService(
            ToolUtilityClass utility,
            Func<object, JsonResult> json,
            Func<string, object, RedirectToActionResult> redirectToAction,
            Action<string> notifyRegistrationError)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _redirectToAction = redirectToAction ?? throw new ArgumentNullException(nameof(redirectToAction));
            _notifyRegistrationError = notifyRegistrationError ?? throw new ArgumentNullException(nameof(notifyRegistrationError));
        }

        /// <summary>
        /// 依姓名建立新 contact，並維持舊 AJAX 回應格式。
        ///
        /// 目前保留原行為：建立 contact 後不立即啟用自動奉獻編號，因為舊程式碼也將該段註解掉。
        /// 自動編號方法仍留在此服務內，代表「編號規則的責任歸屬」已被移出 manager；
        /// 若日後產品決定恢復自動編號，應在這裡開啟，而不是回到 manager 裡新增 CRM 細節。
        /// </summary>
        public IActionResult CreateContact(string fullName)
        {
            try
            {
                EntityCollection queriedContacts = _utility.RetrieveContactEntityByFullNameCollection(fullName);

                if (queriedContacts.Entities.Count == 0)
                {
                    Entity contactToCreate = new Entity("contact");
                    _utility.SetEntityStringAttribute(ref contactToCreate, "lastname", fullName);
                    _utility.CreateEntity(contactToCreate);

                    return _json(new { status = "1", message = "成功建立了" + fullName });
                }

                return _json(new { status = "2", message = "錯誤: 有同名同姓的" + fullName });
            }
            catch (Exception exception)
            {
                string errorString = "錯誤訊息 : FullName = " + GetType().FullName +
                    " , Time = " + DateTime.Now +
                    " , Description = " + exception;

                _notifyRegistrationError(errorString);
                return _redirectToAction("DisplayErrorView", new { ErrorMessage = exception.Message });
            }
        }

        /// <summary>
        /// 自動奉獻編號規則。
        ///
        /// 姓名長度小於等於 5 視為個人，使用 0 開頭；較長名稱視為公司/組織，使用 9 開頭。
        /// 這是 ChurchReport 歷史資料規則，未來產品不應被迫套用。
        /// </summary>
        private void AutoDedicationNumbering(string fullName, Entity createdContact)
        {
            if (fullName.Length <= 5)
            {
                SetDedicationNumber("0", createdContact);
            }
            else
            {
                SetDedicationNumber("9", createdContact);
            }
        }

        /// <summary>
        /// 依指定開頭查出目前最大奉獻編號並加一後寫回 contact.pager。
        /// </summary>
        private void SetDedicationNumber(string startNumber, Entity createdContact)
        {
            EntityCollection contactCollection = _utility.QueryContatsByStartedDedicationNumber(startNumber);
            if (contactCollection.Entities.Count > 0)
            {
                int newNumber = Convert.ToInt32(_utility.GetEntityStringAttribute(contactCollection.Entities[0], "pager")) + 1;

                _utility.SetEntityStringAttribute(createdContact, "pager", startNumber + newNumber.ToString());
                _utility.UpdateEntity(createdContact);
            }
        }
    }
}
