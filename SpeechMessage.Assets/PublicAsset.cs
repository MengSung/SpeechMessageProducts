/*
檔案整體說明
目的與定位：F7完成原子發布後交給呼叫端的不可變純值，讓F5只接收公開PNG網址與最終內容SHA，不借用影像或檔案handle。
主要功能與流程：store在完整驗證、寫入、flush及關閉後產生此結果；網址包含產品、租戶與隨機新檔名，SHA對最終PNG位元組計算，不是登入票據或存取授權。
使用邊界與維護：兩欄皆為不可變值，不保存來源Stream、使用者、Session、credential或可處置資源；Logo為明確公開素材，呼叫端不可拿此契約傳遞私人附件。
*/
namespace SpeechMessage.Assets;

/// <summary>已完整發布的公開PNG描述；不保存上傳影像或資源owner，也不代表任何使用者授權。</summary>
/// <param name="Url">可供LINE取得的HTTPS PNG不可變網址，不能指向私人檔案。</param>
/// <param name="Sha256">最終PNG的完整小寫SHA256，只作完整性描述，不是秘密或授權。</param>
public sealed record PublicAsset(Uri Url, string Sha256);
