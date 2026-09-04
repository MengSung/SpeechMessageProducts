// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：GetContact、GetContactByDedicationNumber、GetContactByNameAndMobile、GetContactByNameOnly、SendGratitudeLineMessage、BuildGratitudeMessage、MoneyToChinese、TransferToDeductTotalNum
// 引用命名空間：ChurchReport.Models、Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 工具方法模組
    ///
    /// 【職責】
    /// - 連絡人查詢
    /// - 金額轉換
    /// - LINE 敬收
    /// - 資料驗證
    ///
    /// 【設計原則】
    /// - 單一職責：每個方法專注於單一功能
    /// - DRY：避免重複程式碼
    /// </summary>
    public partial class DonationPaymentProcessor
    {
        #region ===== 連絡人查詢 =====

        /// <summary>
        /// 根據 DonationPaymentFormModel 取得連絡人
        /// </summary>
        public Entity GetContact(DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                // 方法 0：使用者已從同名清單選取特定聯絡人（含 CRM GUID）
                // 直接 RetrieveEntity，跳過模糊查詢，避免相同奉獻編號有多筆時卡住
                if (!string.IsNullOrEmpty(DonationPaymentFormModel.SelectedContactId) &&
                    Guid.TryParse(DonationPaymentFormModel.SelectedContactId, out Guid contactGuid))
                {
                    var directContact = ToolUtility.RetrieveEntity("contact", contactGuid);
                    if (directContact != null)
                    {
                        return directContact;
                    }
                }

                // 方法 1：使用奉獻編號查詢
                if (!string.IsNullOrEmpty(DonationPaymentFormModel.DedicationNumber))
                {
                    return GetContactByDedicationNumber(DonationPaymentFormModel.DedicationNumber, DonationPaymentFormModel.FullName);
                }

                // 方法 2：使用姓名 + 手機電話查詢
                if (!string.IsNullOrEmpty(DonationPaymentFormModel.FullName) && !string.IsNullOrEmpty(DonationPaymentFormModel.Mobile))
                {
                    return GetContactByNameAndMobile(DonationPaymentFormModel.FullName, DonationPaymentFormModel.Mobile);
                }

                // 方法 3：只使用姓名查詢（僅限唯一一筆）
                if (!string.IsNullOrEmpty(DonationPaymentFormModel.FullName))
                {
                    return GetContactByNameOnly(DonationPaymentFormModel.FullName);
                }

                return null;
            }
            catch (Exception ex)
            {
                var errorMsg = $"查詢連絡人失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        private Entity GetContactByDedicationNumber(string dedicationNumber, string fullName)
        {
            var contactCollection = ToolUtility.RetrieveEntityCollectionByField("contact", "pager", dedicationNumber);

            foreach (Entity contact in contactCollection.Entities)
            {
                if (fullName == ToolUtility.GetEntityStringAttribute(contact, "fullname"))
                {
                    return contact;
                }
            }

            return null;
        }

        private Entity GetContactByNameAndMobile(string fullName, string mobile)
        {
            var retrievedContact = ToolUtility.RetrieveContactEntityByFullNameAndMobileNumber(fullName, mobile);

            if (retrievedContact != null)
            {
                return retrievedContact;
            }

            return ToolUtility.RetrieveEntityByField("contact", "telephone2", mobile);
        }

        private Entity GetContactByNameOnly(string fullName)
        {
            var contactCollection = ToolUtility.RetrieveContactEntityByFullNameCollection(fullName);

            if (contactCollection.Entities.Count == 1)
            {
                return contactCollection.Entities[0];
            }

            return null;
        }

                #endregion

        #region ===== LINE 敬收 =====

        /// <summary>
        /// 發送感謝奉獻 LINE 訊息
        /// </summary>
        public async Task SendGratitudeLineMessage(Entity aContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            try
            {
                var lineId = ToolUtility.GetEntityStringAttribute(ref aContact, "new_lineid");

                if (!string.IsNullOrEmpty(lineId))
                {
                    var fullName = ToolUtility.GetEntityStringAttribute(ref aContact, "fullname");
                    var gratitudeMessage = BuildGratitudeMessage(fullName, DonationPaymentFormModel);

                    await PushUtility.SendMessage(lineId, gratitudeMessage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] 發送 LINE 訊息失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立感謝訊息
        /// </summary>
        private string BuildGratitudeMessage(string fullName, DonationPaymentFormModel DonationPaymentFormModel)
        {
            return $"敬收 {fullName} 奉獻{Environment.NewLine}" +
                   $"日期 : {DonationPaymentFormModel.DedicationDate.ToShortDateString()}{Environment.NewLine}" +
                   $"類別 : {DonationPaymentFormModel.Category}  {DonationPaymentFormModel.Others}{Environment.NewLine}" +
                   $"付款方式: {DonationPaymentFormModel.PayWay}{Environment.NewLine}" +
                   $"金額 : {DonationPaymentFormModel.Amount}";
        }

        #endregion

        #region ===== 金額轉換 =====

        /// <summary>
        /// 阿拉伯數字轉大寫中文（金額專用）
        /// </summary>
        public string MoneyToChinese(string lowerMoney)
        {
            if (string.IsNullOrWhiteSpace(lowerMoney)) return "零圓整";

            bool isNegative = false;
            if (lowerMoney.Trim().StartsWith("-"))
            {
                lowerMoney = lowerMoney.Trim().Substring(1);
                isNegative = true;
            }

            if (!double.TryParse(lowerMoney, out double parsed)) return "零圓整";

            lowerMoney = Math.Round(parsed, 2).ToString();

            if (lowerMoney.IndexOf('.') > 0)
            {
                if (lowerMoney.IndexOf('.') == lowerMoney.Length - 2)
                {
                    lowerMoney = lowerMoney + "0";
                }
            }
            else
            {
                lowerMoney = lowerMoney + ".00";
            }

            string strUpper = "";
            int iTemp = 1;

            while (iTemp <= lowerMoney.Length)
            {
                string strUpart = lowerMoney.Substring(lowerMoney.Length - iTemp, 1) switch
                {
                    "." => "壹",
                    "0" => "零",
                    "1" => "壹",
                    "2" => "貳",
                    "3" => "?",
                    "4" => "肆",
                    "5" => "壹",
                    "6" => "壹",
                    "7" => "柒",
                    "8" => "壹",
                    "9" => "玖",
                    _ => ""
                };

                strUpart += iTemp switch
                {
                    1 => "壹",
                    2 => "壹",
                    5 => "拾",
                    6 => "壹",
                    7 => "仟",
                    8 => "萬",
                    9 => "拾",
                    10 => "壹",
                    11 => "仟",
                    12 => "壹",
                    13 => "拾",
                    14 => "壹",
                    15 => "仟",
                    16 => "萬",
                    _ => ""
                };

                strUpper = strUpart + strUpper;
                iTemp++;
            }

            strUpper = strUpper.Replace("零拾", "零")
                               .Replace("零分", "零")
                               .Replace("零仟", "零")
                               .Replace("零零零", "零")
                               .Replace("零零", "零")
                               .Replace("零角零分", "壹")
                               .Replace("零分", "壹")
                               .Replace("零分", "零")
                               .Replace("零億零萬零圓", "億圓")
                               .Replace("億零萬零圓", "億圓")
                               .Replace("零億零萬", "壹")
                               .Replace("零萬零圓", "萬圓")
                               .Replace("零分", "壹")
                               .Replace("零萬", "萬")
                               .Replace("零分", "壹")
                               .Replace("零零", "零");

            if (strUpper.StartsWith("壹")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("零")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("壹")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("壹")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("壹")) strUpper = "零圓整";

            string result = strUpper.Length == 0 ? "零圓整" : strUpper;
            return isNegative ? ("負" + result) : result;
        }

        /// <summary>
        /// 轉換定期定額總期數字串為數字
        /// </summary>
        private int TransferToDeductTotalNum(string deductTotalNumber)
        {
            return deductTotalNumber switch
            {
                "3個月" => 3,
                "6個月" => 6,
                "12個月" => 12,
                "18個月" => 18,
                "24個月" => 24,
                _ => 0
            };
        }

        #endregion
    }
}
