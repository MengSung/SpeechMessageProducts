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
    /// ���y�B�z�� - �u���k�Ҳ�
    ///
    /// �i¾�d�j
    /// - �s���H�d��
    /// - ���B�ഫ
    /// - LINE �q��
    /// - �������
    ///
    /// �i�]�p��h�j
    /// - ��@¾�d�G�C�Ӥ�k�M�`���@�\��
    /// - DRY�G�קK���Ƶ{���X
    /// </summary>
    public partial class DonationPaymentProcessor
    {
        #region ===== �s���H�d�� =====

        /// <summary>
        /// �ھ� DonationPaymentFormModel ���o�s���H
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

        #region ===== LINE �q�� =====

        /// <summary>
        /// �o�e�P�©^�m LINE �T��
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
                System.Diagnostics.Trace.WriteLine($"[DonationPaymentProcessor] �o�e LINE �T������: {ex.Message}");
            }
        }

        /// <summary>
        /// �إ߷P�°T��
        /// </summary>
        private string BuildGratitudeMessage(string fullName, DonationPaymentFormModel DonationPaymentFormModel)
        {
            return $"�q�� {fullName} �^�m{Environment.NewLine}" +
                   $"��� : {DonationPaymentFormModel.DedicationDate.ToShortDateString()}{Environment.NewLine}" +
                   $"���O : {DonationPaymentFormModel.Category}  {DonationPaymentFormModel.Others}{Environment.NewLine}" +
                   $"�I�ڤ覡: {DonationPaymentFormModel.PayWay}{Environment.NewLine}" +
                   $"���B : {DonationPaymentFormModel.Amount}";
        }

        #endregion

        #region ===== ���B�ഫ =====

        /// <summary>
        /// ���ԧB�Ʀr��j�g����]���B�M�Ρ^
        /// </summary>
        public string MoneyToChinese(string lowerMoney)
        {
            if (string.IsNullOrWhiteSpace(lowerMoney)) return "�s���";

            bool isNegative = false;
            if (lowerMoney.Trim().StartsWith("-"))
            {
                lowerMoney = lowerMoney.Trim().Substring(1);
                isNegative = true;
            }

            if (!double.TryParse(lowerMoney, out double parsed)) return "�s���";

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
                    "." => "��",
                    "0" => "�s",
                    "1" => "��",
                    "2" => "�L",
                    "3" => "?",
                    "4" => "�v",
                    "5" => "��",
                    "6" => "��",
                    "7" => "�m",
                    "8" => "��",
                    "9" => "�h",
                    _ => ""
                };

                strUpart += iTemp switch
                {
                    1 => "��",
                    2 => "��",
                    5 => "�B",
                    6 => "��",
                    7 => "�a",
                    8 => "�U",
                    9 => "�B",
                    10 => "��",
                    11 => "�a",
                    12 => "��",
                    13 => "�B",
                    14 => "��",
                    15 => "�a",
                    16 => "�U",
                    _ => ""
                };

                strUpper = strUpart + strUpper;
                iTemp++;
            }

            strUpper = strUpper.Replace("�s�B", "�s")
                               .Replace("�s��", "�s")
                               .Replace("�s�a", "�s")
                               .Replace("�s�s�s", "�s")
                               .Replace("�s�s", "�s")
                               .Replace("�s���s��", "��")
                               .Replace("�s��", "��")
                               .Replace("�s��", "�s")
                               .Replace("�s���s�U�s��", "����")
                               .Replace("���s�U�s��", "����")
                               .Replace("�s���s�U", "��")
                               .Replace("�s�U�s��", "�U��")
                               .Replace("�s��", "��")
                               .Replace("�s�U", "�U")
                               .Replace("�s��", "��")
                               .Replace("�s�s", "�s");

            if (strUpper.StartsWith("��")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("�s")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("��")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("��")) strUpper = strUpper.Substring(1);
            if (strUpper.StartsWith("��")) strUpper = "�s���";

            string result = strUpper.Length == 0 ? "�s���" : strUpper;
            return isNegative ? ("�t" + result) : result;
        }

        /// <summary>
        /// �ഫ�w���w�B�`���Ʀr�ꬰ�Ʀr
        /// </summary>
        private int TransferToDeductTotalNum(string deductTotalNumber)
        {
            return deductTotalNumber switch
            {
                "3�Ӥ�" => 3,
                "6�Ӥ�" => 6,
                "12�Ӥ�" => 12,
                "18�Ӥ�" => 18,
                "24�Ӥ�" => 24,
                _ => 0
            };
        }

        #endregion
    }
}
