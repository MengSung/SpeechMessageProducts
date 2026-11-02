namespace I18nCheck;

/// <summary>只操作本機來源與產生檔的命令列入口，不建立 D365、LINE 或背景工作連線。</summary>
internal static class Program
{
    /// <summary>在單一命令生命週期內完成掃描或產生，回傳可供 CI 判定的結束碼。</summary>
    /// <param name="args">scan、generate 或 verify 與其選項。</param>
    /// <returns>成功為零，驗證或輸入失敗為非零。</returns>
    private static int Main(string[] args) => CommandRunner.Run(RepositoryScanner.FindRoot(Environment.CurrentDirectory), args, Console.Out);
}
