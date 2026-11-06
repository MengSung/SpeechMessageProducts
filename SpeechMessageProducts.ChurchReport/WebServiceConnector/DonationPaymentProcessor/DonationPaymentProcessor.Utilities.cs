/*
 * 檔案整體說明
 *
 * 目的與定位：金流處理器 - 工具方法模組 【職責】 - 連絡人查詢 - 金額轉換 - LINE 敬收 - 資料驗證 【設計原則】 - 單一職責：每個方法專注於單一功能 - DRY：避免重複程式碼
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentProcessor。
 * - 財務中文大寫數字字元表。唯讀靜態資料不含 request 或使用者狀態， 可安全由所有 processor 共用，避免每次金額轉換重複配置陣列。
 * - 根據 DonationPaymentFormModel 取得連絡人
 * 主要操作入口包含 GetContact、GetContactByDedicationNumber、GetContactByNameAndMobile、GetContactByNameOnly、
 * SendGratitudeLineMessage、BuildGratitudeMessage、MoneyToChinese、TransferToDeductTotalNum。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.DedicationBooking.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Globalization;
using System.Text;
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
        /// <summary>
        /// 財務中文大寫數字字元表。唯讀靜態資料不含 request 或使用者狀態，
        /// 可安全由所有 processor 共用，避免每次金額轉換重複配置陣列。
        /// </summary>
        private static readonly string[] FinancialDigits =
            { "零", "壹", "貳", "參", "肆", "伍", "陸", "柒", "捌", "玖" };

        /// <summary>四位一組的財務位值單位；索引 0 代表個位。</summary>
        private static readonly string[] FinancialPositions = { "仟", "佰", "拾", "" };

        /// <summary>四位數群組的高位單位；索引 0 代表沒有群組單位。</summary>
        private static readonly string[] FinancialGroupUnits =
            { "", "萬", "億", "兆", "京", "垓", "秭", "穰" };

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
        /// <remarks>
        /// 輸入會先以目前文化及不變文化嘗試解析，再以銀行收據慣例四捨五入至小數兩位。
        /// 整數部分以四位一組處理萬、億、兆等節點，小數部分只輸出角與分；
        /// 金額為整數時以「整」結尾，零值或無法解析時 fail closed 為「零圓整」。
        /// 本方法只使用區域變數與區域 <see cref="StringBuilder"/>，不保存任何使用者、Session、
        /// 租戶或金流資料，因此同一個 processor 被重用時不會發生跨請求狀態殘留。
        /// </remarks>
        /// <param name="lowerMoney">可含負號與小數的阿拉伯數字字串。</param>
        /// <returns>繁體中文財務大寫金額。</returns>
        public string MoneyToChinese(string lowerMoney)
        {
            const string zeroResult = "零圓整";
            if (string.IsNullOrWhiteSpace(lowerMoney))
            {
                return zeroResult;
            }

            var text = lowerMoney.Trim();
            var isNegative = text.StartsWith("-", StringComparison.Ordinal);
            if (isNegative)
            {
                text = text[1..].Trim();
            }

            const NumberStyles amountStyles = NumberStyles.Float | NumberStyles.AllowThousands;
            if (!decimal.TryParse(text, amountStyles, CultureInfo.CurrentCulture, out var amount) &&
                !decimal.TryParse(text, amountStyles, CultureInfo.InvariantCulture, out amount))
            {
                return zeroResult;
            }

            amount = decimal.Round(Math.Abs(amount), 2, MidpointRounding.ToEven);
            if (amount == decimal.Zero)
            {
                return zeroResult;
            }

            var integerPart = decimal.Truncate(amount);
            var fraction = (int)((amount - integerPart) * 100m);
            var result = new StringBuilder(32);

            if (integerPart > decimal.Zero)
            {
                var integerText = integerPart.ToString("0", CultureInfo.InvariantCulture);
                var groupCount = (integerText.Length + 3) / 4;
                var pendingZero = false;
                for (var groupIndex = groupCount - 1; groupIndex >= 0; groupIndex--)
                {
                    var start = Math.Max(0, integerText.Length - ((groupIndex + 1) * 4));
                    var length = integerText.Length - start - (groupIndex * 4);
                    var groupValue = int.Parse(integerText.Substring(start, length), CultureInfo.InvariantCulture);

                    if (groupValue == 0)
                    {
                        if (result.Length > 0)
                        {
                            pendingZero = true;
                        }

                        continue;
                    }

                    if (pendingZero)
                    {
                        result.Append('零');
                        pendingZero = false;
                    }

                    var groupText = groupValue.ToString("D4", CultureInfo.InvariantCulture);
                    var groupHasValue = false;
                    // 低位節點小於 1000 時，前一個萬/億節點與本節點之間必須補一個零，
                    // 例如 10001 應為「壹萬零壹圓」，但 10000 則不需補零。
                    // 若前一個完整群組已是零，外層 pendingZero 已負責補零；
                    // 不可再因本群組首位為零重複追加，否則 100000001 會變成「壹億零零壹」。
                    var groupPendingZero = result.Length > 0
                        && result[result.Length - 1] != '零'
                        && groupText[0] == '0';

                    for (var position = 0; position < 4; position++)
                    {
                        var digit = groupText[position] - '0';
                        if (digit == 0)
                        {
                            if (groupHasValue)
                            {
                                groupPendingZero = true;
                            }

                            continue;
                        }

                        if (groupPendingZero)
                        {
                            result.Append('零');
                            groupPendingZero = false;
                        }

                        result.Append(FinancialDigits[digit]);
                        result.Append(FinancialPositions[position]);
                        groupHasValue = true;
                    }

                    result.Append(groupIndex < FinancialGroupUnits.Length ? FinancialGroupUnits[groupIndex] : string.Empty);
                }

                result.Append('圓');
            }

            if (fraction == 0)
            {
                result.Append("整");
            }
            else
            {
                var jiao = fraction / 10;
                var fen = fraction % 10;
                if (jiao > 0)
                {
                    result.Append(FinancialDigits[jiao]).Append('角');
                }

                if (fen > 0)
                {
                    if (jiao == 0 && integerPart > decimal.Zero)
                    {
                        result.Append('零');
                    }

                    result.Append(FinancialDigits[fen]).Append('分');
                }
            }

            return isNegative ? "負" + result : result.ToString();
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
