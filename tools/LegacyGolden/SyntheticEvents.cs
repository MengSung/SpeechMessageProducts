/*
檔案整體說明
目的與定位：依 P3-01 指定的十二類 LINE webhook 官方結構產生完全合成的離線 JSON fixture，讓後續事件處理契約有固定輸入。
主要功能與流程：每份 request 只承載一個事件，包含 destination、source、mode、timestamp、ULID webhookEventId 與 deliveryContext；message 的五種 subtype 及七種其餘事件各一份。樣本採固定假識別與假 nonce，合成性質在工具 README 說明，不往官方 JSON 偷加欄位。
使用邊界與維護：本檔沒有 LINE client、簽章、私人 ID、真實地點或使用者資料，也不建立網路或背景資源。固定值是協定形狀用的純資料，不能當作有效 token 或實機回傳。每次建立獨立 JsonObject，沒有共享可變快照；新增事件需另經範圍核定及解析斷言，不能把本十二類宣稱官方完整事件集合。
*/
using System.Globalization;
using System.Text.Json.Nodes;

namespace LegacyGolden;

/// <summary>固定十二類樣本的純資料工廠；呼叫端獨占回傳的可變 JSON 候選。</summary>
internal static class SyntheticEvents
{
    /// <summary>依固定順序產生官方 request envelope；所有識別只用零值及合成 nonce，不會對外發送。</summary>
    internal static SortedDictionary<string, JsonObject> Create()
    {
        var result = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var kinds = new[] { "text", "image", "audio", "location", "sticker", "follow", "unfollow", "join", "leave", "postback", "accountLink", "memberJoined" };
        for (var index = 0; index < kinds.Length; index++)
        {
            var kind = kinds[index];
            var message = index < 5;
            var item = new JsonObject
            {
                ["type"] = message ? "message" : kind, ["mode"] = "active", ["timestamp"] = 1735689600000L,
                ["source"] = kind is "join" or "leave" or "memberJoined"
                    ? new JsonObject { ["type"] = "group", ["groupId"] = "C00000000000000000000000000000000" }
                    : User(),
                ["webhookEventId"] = "01JGFJJZ00" + (index + 1).ToString("D16", CultureInfo.InvariantCulture),
                ["deliveryContext"] = new JsonObject { ["isRedelivery"] = false }
            };
            if (kind is not ("unfollow" or "leave")) item["replyToken"] = "00000000000000000000000000000000";
            if (message)
            {
                var payload = new JsonObject { ["id"] = (10000000000000L + index).ToString(CultureInfo.InvariantCulture), ["type"] = kind };
                switch (kind)
                {
                    case "text": payload["text"] = "synthetic-text"; payload["quoteToken"] = "synthetic-quote-token"; break;
                    case "image": payload["contentProvider"] = new JsonObject { ["type"] = "line" }; payload["quoteToken"] = "synthetic-quote-token"; break;
                    case "audio": payload["duration"] = 1000; payload["contentProvider"] = new JsonObject { ["type"] = "line" }; break;
                    case "location": payload["title"] = "Synthetic origin"; payload["address"] = "Synthetic address"; payload["latitude"] = 0; payload["longitude"] = 0; break;
                    case "sticker": payload["packageId"] = "1"; payload["stickerId"] = "1"; payload["stickerResourceType"] = "STATIC"; payload["keywords"] = new JsonArray("synthetic"); payload["quoteToken"] = "synthetic-quote-token"; break;
                }
                item["message"] = payload;
            }
            else if (kind == "postback") item["postback"] = new JsonObject { ["data"] = "action=synthetic", ["params"] = new JsonObject { ["date"] = "2025-01-01" } };
            else if (kind == "accountLink") item["link"] = new JsonObject { ["result"] = "ok", ["nonce"] = "synthetic-nonce-not-a-credential" };
            else if (kind == "memberJoined") item["joined"] = new JsonObject { ["members"] = new JsonArray(User()) };
            else if (kind == "follow") item["follow"] = new JsonObject { ["isUnblocked"] = false };
            result.Add("synthetic-" + (message ? "message-" : "") + kind + ".json", new JsonObject
            {
                ["destination"] = "U00000000000000000000000000000000", ["events"] = new JsonArray(item)
            });
        }
        return result;
    }

    /// <summary>每次建立新的假 user 物件，禁止同一 JsonNode 被跨事件父節點或呼叫共用。</summary>
    private static JsonObject User() => new() { ["type"] = "user", ["userId"] = "U00000000000000000000000000000000" };
}
