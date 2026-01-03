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
    /// - LINE 通知
    /// - 資料驗證
    /// 
    /// 【設計原則】
    /// - 單一職責：每個方法專注於單一功能
    /// - DRY：避免重複程式碼
    /// </summary>
    public partial class QPayProcessor
    {
        #region ===== 連絡人查詢 =====

        /// <summary>
        /// 根據 QpayModel 取得連絡人
        /// </summary>
        public Entity GetContact(QpayModel QpayModel)
        {
            try
            {
                // 方法 1：使用奉獻編號查詢
                if (!string.IsNullOrEmpty(QpayModel.DedicationNumber))
                {
                    return GetContactByDedicationNumber(QpayModel.DedicationNumber, QpayModel.FullName);
                }

                // 方法 2：使用姓名 + 行動電話查詢
                if (!string.IsNullOrEmpty(QpayModel.FullName) && !string.IsNullOrEmpty(QpayModel.Mobile))
                {
                    return GetContactByNameAndMobile(QpayModel.FullName, QpayModel.Mobile);
                }

                // 方法 3：只使用姓名查詢（必須唯一）
                if (!string.IsNullOrEmpty(QpayModel.FullName))
                {
                    return GetContactByNameOnly(QpayModel.FullName);
                }

                return null;
            }
            catch (Exception ex)
            {
                var errorMsg = $"查詢連絡人失敗: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] {errorMsg}");
                throw new InvalidOperationException(errorMsg, ex);
            }
        }

        /// <summary>
        /// 使用奉獻編號查詢連絡人
        /// </summary>
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

        /// <summary>
        /// 使用姓名和行動電話查詢連絡人
        /// </summary>
        private Entity GetContactByNameAndMobile(string fullName, string mobile)
        {
            var retrievedContact = ToolUtility.RetrieveContactEntityByFullNameAndMobileNumber(fullName, mobile);

            if (retrievedContact != null)
            {
                return retrievedContact;
            }

            // 嘗試使用 telephone2 欄位查詢
            return ToolUtility.RetrieveEntityByField("contact", "telephone2", mobile);
        }

        /// <summary>
        /// 只使用姓名查詢連絡人（必須唯一）
        /// </summary>
        private Entity GetContactByNameOnly(string fullName)
        {
            var contactCollection = ToolUtility.RetrieveContactEntityByFullNameCollection(fullName);

            if (contactCollection.Entities.Count == 1)
            {
                return contactCollection.Entities[0];
            }

            // 如果有多筆同名記錄，回傳 null
            return null;
        }

        #endregion

        #region ===== LINE 通知 =====

        /// <summary>
        /// 發送感謝奉獻 LINE 訊息
        /// </summary>
        public async Task SendGratitudeLineMessage(Entity aContact, QpayModel QpayModel)
        {
            try
            {
                var lineId = ToolUtility.GetEntityStringAttribute(ref aContact, "new_lineid");

                if (!string.IsNullOrEmpty(lineId))
                {
                    var fullName = ToolUtility.GetEntityStringAttribute(ref aContact, "fullname");
                    var gratitudeMessage = BuildGratitudeMessage(fullName, QpayModel);

                    await PushUtility.SendMessage(lineId, gratitudeMessage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[QPayProcessor] 發送 LINE 訊息失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 建立感謝訊息
        /// </summary>
        private string BuildGratitudeMessage(string fullName, QpayModel QpayModel)
        {
            return $"敬收 {fullName} 奉獻{Environment.NewLine}" +
                   $"日期 : {QpayModel.DedicationDate.ToShortDateString()}{Environment.NewLine}" +
                   $"類別 : {QpayModel.Category}  {QpayModel.Others}{Environment.NewLine}" +
                   $"付款方式: {QpayModel.PayWay}{Environment.NewLine}" +
                   $"金額 : {QpayModel.Amount}";
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
                    "." => "圓",
                    "0" => "零",
                    "1" => "壹",
                    "2" => "貳",
                    "3" => "?",
                    "4" => "肆",
                    "5" => "伍",
                    "6" => "陸",
                    "7" => "柒",
                    "8" => "捌",
                    "9" => "玖",
                    _ => ""
                };

                strUpart += iTemp switch
                {
                    1 => "分",
                    2 => "角",
                    5 => "拾",
                    6 => "佰",
                    7 => "仟",
                    8 => "萬",
                    9 => "拾",
                    10 => "佰",
                    11 => "仟",
                    12 => "億",
                    13 => "拾",
                    14 => "佰",
                    15 => "仟",
                    16 => "萬",
                    _ => ""
                };

                strUpper = strUpart + strUpper;
                iTemp++;
            }

            strUpper = strUpper.Replace("零拾", "零")
                               .Replace("零佰", "零")
                               .Replace("零仟", "零")
                               .Replace("零零零", "零")
                               .Replace("零零", "零")
                               .Replace("零角零分", "整")
                               .Replace("零分", "整")
                               .Replace("零角", "零")
                               .Replace("零億零萬零圓", "億圓")
                               .Replace("億零萬零圓", "億圓")
                               .Replace("零億零萬", "億")
                               .Replace("零萬零圓", "萬圓")
                               .Replace("零億", "億")
                               .Replace("零萬", "萬")
                               .Replace("零圓", "圓")
                               .Replace("零零", "零");

            if (strUpper.StartsWith("圓")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("零")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("角")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("分")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("整")) strUpper = "零圓整";

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
