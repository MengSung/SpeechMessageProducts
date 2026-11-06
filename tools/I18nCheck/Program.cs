/*
 * 檔案整體說明
 *
 * 目的與定位：只操作本機來源與產生檔的命令列入口，不建立 D365、LINE 或背景工作連線。
 *
 * 主要功能與流程：
 * 本檔宣告 Program。
 * - 在單一命令生命週期內完成掃描或產生，回傳可供 CI 判定的結束碼。
 * 主要操作入口包含 Main。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
namespace I18nCheck;

/// <summary>只操作本機來源與產生檔的命令列入口，不建立 D365、LINE 或背景工作連線。</summary>
internal static class Program
{
    /// <summary>在單一命令生命週期內完成掃描或產生，回傳可供 CI 判定的結束碼。</summary>
    /// <param name="args">scan、generate 或 verify 與其選項。</param>
    /// <returns>成功為零，驗證或輸入失敗為非零。</returns>
    private static int Main(string[] args) => CommandRunner.Run(RepositoryScanner.FindRoot(Environment.CurrentDirectory), args, Console.Out);
}
