/*
檔案整體說明
目的與定位：F7公開Logo的不可變路由scope，僅承載固定產品鍵及權威租戶GUID，供伺服器組合根與儲存庫傳遞。
主要功能與流程：呼叫端完成使用者與租戶授權後建立本值，再由store驗證產品白名單與非空ID後形成獨立目錄；此型別自身不查資料、不產生授權或取得Session。
使用邊界與維護：純資料不擁有串流、像素、快取或Dispose責任，不得由未驗證HTTP參數直接充當權限；更換產品或租戶必須建立新值，不能跨請求修改共用狀態。
*/
namespace SpeechMessage.Assets;

/// <summary>伺服器已授權操作的路由純值；不是使用者或租戶的授權證明，store仍須驗證路徑邊界。</summary>
/// <param name="Product">受信任組合根指定的精確產品常值，不接受自由路徑。</param>
/// <param name="TenantId">權威資料庫租戶ID，不能以draft key或空GUID代替。</param>
public sealed record AssetScope(string Product, Guid TenantId);
