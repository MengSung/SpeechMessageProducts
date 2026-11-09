/*
檔案整體說明
目的與定位：保存單次來源讀取的不可變文字與追溯中繼資料，供 Roslyn 舊 matcher 編譯及回覆靜態分析共用。
主要功能與流程：來源 reader 完成嚴格 CP950 往返、P0-05 副本轉碼與原檔 hash 核對後才建立資料；產生器只按相對路徑、原行號及兩份 SHA256 發布證據。
使用邊界與維護：本型別是純資料，不執行授權、檔案寫入、網路或清理。文字最多由本次呼叫保存，禁止含設定、私人 LINE ID 或真實 CRM 資料；持有它的產生器不得放進靜態快取。行數採原檔實際行，不將末尾換行當額外內容行。
*/
namespace LegacyGolden;

/// <summary>單次呼叫持有的已轉碼來源；所有身分均為白名單相對路徑，沒有機器路徑或使用者狀態。</summary>
/// <param name="Path">由 reader 驗證的固定相對來源路徑。</param>
/// <param name="Text">P0-05 轉碼後的完整文字；只供編譯與靜態分析，不直接發布原碼。</param>
/// <param name="OriginalSha256">唯讀原始位元組的 SHA256，產生後須再次核對。</param>
/// <param name="ConvertedSha256">UTF-8 副本的 SHA256，與原檔分開保存。</param>
/// <param name="LineCount">保持原始行號的內容行數。</param>
internal sealed record SourceDocument(string Path, string Text, string OriginalSha256, string ConvertedSha256, int LineCount);
