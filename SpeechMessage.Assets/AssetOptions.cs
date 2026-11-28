/*
檔案整體說明
目的與定位：F7儲存庫啟動時借入的不可變部署描述，只提供受信任實體根與HTTPS公開基底，不接受request或租戶指定根路徑。
主要功能與流程：store建構時驗證並複製正規化設定；目錄必須由主機專用身分擁有且限制其他寫入者，公開路徑後續只附加已驗產品、租戶ID與伺服器檔名。
使用邊界與維護：設定不含使用者、token或連線狀態，沒有reload或Dispose責任；真正影像與檔案owner是store。此值本身不驗ACL、不建立目錄，也不宣稱能防止本機管理員更換檔案。
*/
namespace SpeechMessage.Assets;

/// <summary>單一store owner的固定部署設定；只能由伺服器組合根建立，不可從請求選根或網域。</summary>
/// <param name="RootPath">專用且受ACL保護的本機絕對根；不得與其他產品私密資料共用。</param>
/// <param name="PublicBaseUrl">沒有credential、query或fragment且以斜線結束的HTTPS基底。</param>
public sealed record AssetOptions(string RootPath, Uri PublicBaseUrl);
