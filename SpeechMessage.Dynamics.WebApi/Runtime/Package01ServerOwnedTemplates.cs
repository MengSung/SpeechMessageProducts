// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/Package01ServerOwnedTemplates.cs
// 目的：Package 0/1 的 server-owned 查詢模板（誰能查、查什麼，由伺服器決定）。
//
// 保母教學：
// - 產品只能傳 typed 參數（contactId、日期...），不能傳 FetchXML 文字。
// - 模板裡的 entity / attribute / option-set 值是固定的。
// - 參數用 {{name}} 占位，編碼後再替換。
// - 這層刻意不引用任何 legacy CRM SDK 型別。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 單一 server-owned 模板描述。
/// </summary>
public sealed class ServerOwnedTemplate
{
    public required string TemplateId { get; init; }
    public required string TemplateKind { get; init; }
    public string? EntitySetName { get; init; }
    public string? FetchXmlTemplate { get; init; }
    public string? ODataRelativePathTemplate { get; init; }
}

/// <summary>
/// Package 0/1 模板目錄。
/// </summary>
public static class Package01ServerOwnedTemplates
{
    private static readonly IReadOnlyDictionary<string, ServerOwnedTemplate> ByTemplateId =
        Build().ToDictionary(x => x.TemplateId, StringComparer.Ordinal);

    public static bool TryGet(string templateId, out ServerOwnedTemplate? template)
        => ByTemplateId.TryGetValue(templateId, out template);

    public static bool TryGetByOperation(OperationDefinition definition, out ServerOwnedTemplate? template)
    {
        template = null;
        if (definition is null)
        {
            return false;
        }

        return TryGet(definition.TemplateId, out template);
    }

    private static IEnumerable<ServerOwnedTemplate> Build()
    {
        // WhoAmI：OData function。
        yield return new ServerOwnedTemplate
        {
            TemplateId = "WhoAmI",
            TemplateKind = "odata-function",
            ODataRelativePathTemplate = "WhoAmI"
        };

        // Option-set metadata：固定 OData route，參數只允許 logical name。
        yield return new ServerOwnedTemplate
        {
            TemplateId = "metadata.optionset.by.attribute.v1",
            TemplateKind = "odata-route",
            ODataRelativePathTemplate =
                "EntityDefinitions(LogicalName='{{entityLogicalName}}')/Attributes(LogicalName='{{attributeLogicalName}}')"
        };

        // fee.dedication.bycontact.v1
        yield return new ServerOwnedTemplate
        {
            TemplateId = "fee.dedication.bycontact.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_fees",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
                  <entity name="new_fee">
                    <attribute name="new_feeid" />
                    <attribute name="new_name" />
                    <attribute name="createdon" />
                    <attribute name="new_pay_date" />
                    <attribute name="new_fee_shoud_pay" />
                    <attribute name="new_fee_really_paid" />
                    <attribute name="new_pay_way" />
                    <attribute name="new_category" />
                    <attribute name="new_others" />
                    <order attribute="new_name" descending="false" />
                    <filter type="and">
                      <condition attribute="new_contact_new_fee" operator="eq" uitype="contact" value="{{contactId}}"{{contactNameAttr}} />
                      <condition attribute="new_category" operator="not-null" />
                    </filter>
                  </entity>
                </fetch>
                """
        };

        // fee.dedication.bycontactdaterange.v1
        yield return new ServerOwnedTemplate
        {
            TemplateId = "fee.dedication.bycontactdaterange.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_fees",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
                  <entity name="new_fee">
                    <attribute name="new_feeid" />
                    <attribute name="new_name" />
                    <attribute name="createdon" />
                    <attribute name="new_pay_date" />
                    <attribute name="new_fee_shoud_pay" />
                    <attribute name="new_fee_really_paid" />
                    <attribute name="new_pay_way" />
                    <attribute name="new_category" />
                    <attribute name="new_others" />
                    <attribute name="new_paid_period" />
                    <order attribute="new_name" descending="false" />
                    <filter type="and">
                      <condition attribute="new_contact_new_fee" operator="eq" uitype="contact" value="{{contactId}}"{{contactNameAttr}} />
                      <condition attribute="new_category" operator="not-null" />
                      <condition attribute="new_pay_status" operator="in">
                        <value>100000001</value>
                        <value>100000002</value>
                        <value>100000003</value>
                        <value>100000004</value>
                        <value>100000006</value>
                      </condition>
                      <condition attribute="new_pay_date" operator="on-or-after" value="{{startDate}}" />
                      <condition attribute="new_pay_date" operator="on-or-before" value="{{endDate}}" />
                    </filter>
                  </entity>
                </fetch>
                """
        };

        // fees.by.dedication.period.v1
        yield return new ServerOwnedTemplate
        {
            TemplateId = "fees.by.dedication.period.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_fees",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="500">
                  <entity name="new_fee">
                    <attribute name="new_feeid" />
                    <attribute name="new_name" />
                    <attribute name="createdon" />
                    <attribute name="new_paid_period" />
                    <order attribute="createdon" descending="true" />
                    <filter type="and">
                      <condition attribute="new_dedication_booking_new_fee" operator="eq" uitype="new_dedication_booking" value="{{dedicationBookingId}}"{{dedicationBookingNameAttr}} />
                      <condition attribute="new_paid_period" operator="eq" value="{{paidPeriod}}" />
                      <condition attribute="statecode" operator="eq" value="0" />
                    </filter>
                  </entity>
                </fetch>
                """
        };

        // fees.editor.load.disciplelesson.v1
        // 第一版把 fee editor 需要的 stor lessons 投影收斂成單一 FetchXML。
        yield return new ServerOwnedTemplate
        {
            TemplateId = "fees.editor.load.disciplelesson.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_stor_lessonses",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="1000">
                  <entity name="new_stor_lessons">
                    <attribute name="createdon" />
                    <attribute name="new_contact_new_stor_lessons" />
                    <attribute name="new_fee" />
                    <attribute name="new_pay_date" />
                    <attribute name="new_new_disciple_lessons_new_stor_les" />
                    <attribute name="new_stor_lessonsid" />
                    <order attribute="createdon" descending="true" />
                    <filter type="and">
                      <condition attribute="new_new_disciple_lessons_new_stor_les" operator="eq" uitype="new_disciple_lessons" value="{{discipleLessonId}}" />
                      <condition attribute="statecode" operator="eq" value="0" />
                    </filter>
                    <link-entity name="contact" from="contactid" to="new_contact_new_stor_lessons" visible="false" link-type="outer" alias="contact">
                      <attribute name="fullname" />
                      <attribute name="mobilephone" />
                    </link-entity>
                  </entity>
                </fetch>
                """
        };

        // lessons.stor.by.contact.v1
        yield return new ServerOwnedTemplate
        {
            TemplateId = "lessons.stor.by.contact.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_stor_lessonses",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="1000">
                  <entity name="new_stor_lessons">
                    <attribute name="createdon" />
                    <attribute name="new_contact_new_stor_lessons" />
                    <attribute name="new_fee" />
                    <attribute name="new_pay_date" />
                    <attribute name="new_current_complete" />
                    <attribute name="new_new_disciple_lessons_new_stor_les" />
                    <attribute name="new_stor_lessonsid" />
                    <order attribute="createdon" descending="true" />
                    <filter type="and">
                      <condition attribute="new_contact_new_stor_lessons" operator="eq" uitype="contact" value="{{contactId}}"{{contactNameAttr}} />
                      <condition attribute="statecode" operator="eq" value="0" />
                    </filter>
                    <link-entity name="contact" from="contactid" to="new_contact_new_stor_lessons" visible="false" link-type="outer" alias="contact">
                      <attribute name="mobilephone" />
                      <attribute name="emailaddress1" />
                    </link-entity>
                    <link-entity name="new_disciple_lessons" from="new_disciple_lessonsid" to="new_new_disciple_lessons_new_stor_les" alias="lesson">
                      <attribute name="new_name" />
                      <filter type="and">
                        <condition attribute="new_classification" operator="in">
                          <value>100000000</value>
                          <value>100000001</value>
                        </condition>
                      </filter>
                    </link-entity>
                  </entity>
                </fetch>
                """
        };

        // lessons.stor.by.disciplelesson.v1
        yield return new ServerOwnedTemplate
        {
            TemplateId = "lessons.stor.by.disciplelesson.v1",
            TemplateKind = "fetchxml",
            EntitySetName = "new_stor_lessonses",
            FetchXmlTemplate =
                """
                <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" top="1000">
                  <entity name="new_stor_lessons">
                    <attribute name="createdon" />
                    <attribute name="new_contact_new_stor_lessons" />
                    <attribute name="new_fee" />
                    <attribute name="new_pay_date" />
                    <attribute name="new_new_disciple_lessons_new_stor_les" />
                    <attribute name="new_stor_lessonsid" />
                    <order attribute="createdon" descending="true" />
                    <filter type="and">
                      <condition attribute="new_enroll_status" operator="not-in">
                        <value>100000007</value>
                        <value>100000009</value>
                        <value>100000003</value>
                      </condition>
                      <condition attribute="new_new_disciple_lessons_new_stor_les" operator="eq" uitype="new_disciple_lessons" value="{{discipleLessonId}}"{{lessonNameAttr}} />
                      <condition attribute="statuscode" operator="ne" value="2" />
                      <condition attribute="statecode" operator="eq" value="0" />
                    </filter>
                    <link-entity name="contact" from="contactid" to="new_contact_new_stor_lessons" visible="false" link-type="outer" alias="contact">
                      <attribute name="fullname" />
                      <attribute name="mobilephone" />
                    </link-entity>
                  </entity>
                </fetch>
                """
        };
    }
}
