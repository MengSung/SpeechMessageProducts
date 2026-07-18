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
