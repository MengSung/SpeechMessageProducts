/*
 * 檔案整體說明
 *
 * 目的與定位：本檔依官方事件及訊息type將JSON投影為具體事件模型；解析不等於驗簽，入口必須先驗證同一原始body。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookEventParser。
 * 主要操作入口包含 Parse、ParseEvents。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging.Webhooks
{
    public static class WebhookEventParser
    {
        public static IEnumerable<WebhookEvent> Parse(string webhookContent)
        {
            dynamic dynamicObject = JsonConvert.DeserializeObject(webhookContent);
            if (dynamicObject == null) { yield break; }

            foreach (var ev in dynamicObject.events)
            {
                var webhookEvent = WebhookEvent.CreateFrom(ev);
                if (webhookEvent == null) { continue; }
                yield return webhookEvent;
            }
        }

        public static IEnumerable<WebhookEvent> ParseEvents(dynamic events)
        {
            foreach (var ev in events)
            {
                var webhookEvent = WebhookEvent.CreateFrom(ev);
                if (webhookEvent == null) { continue; }
                yield return webhookEvent;
            }
        }

    }
}
