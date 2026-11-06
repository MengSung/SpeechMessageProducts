/*
 * 檔案整體說明
 *
 * 目的與定位：使用一次CRM aggregate查詢取得目前有效會友數量，避免為計數下載全部會友；服務借用已授權SDK，計數結果不包含會友明細，也不代替逐筆存取驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoCurrentContactCounter。
 * 主要操作入口包含 Count。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.Services.MemberInfo
{
    public static class MemberInfoCurrentContactCounter
    {
        private const string CountAlias = "currentcontactcount";

        public static int Count(IOrganizationService service, int closedStatus)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            var fetchXml =
                $"<fetch aggregate=\"true\">" +
                "<entity name=\"contact\">" +
                $"<attribute name=\"contactid\" alias=\"{CountAlias}\" aggregate=\"countcolumn\" />" +
                "<filter type=\"and\">" +
                "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />" +
                "<filter type=\"or\">" +
                "<condition attribute=\"customertypecode\" operator=\"null\" />" +
                $"<condition attribute=\"customertypecode\" operator=\"ne\" value=\"{closedStatus.ToString(CultureInfo.InvariantCulture)}\" />" +
                "</filter>" +
                "</filter>" +
                "</entity>" +
                "</fetch>";

            var row = service.RetrieveMultiple(new FetchExpression(fetchXml))
                ?.Entities
                ?.FirstOrDefault();
            var value = row?.GetAttributeValue<AliasedValue>(CountAlias)?.Value;
            return value == null
                ? 0
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
    }
}
