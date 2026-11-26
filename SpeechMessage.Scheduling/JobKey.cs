/*
檔案整體說明
目的與定位：共用排程器的三段工作識別純值，供SQL持久化索引與產品處理器名稱配對；不把顯示文字或傳入索引當成業務事件身分。
主要功能與流程：Product、Name及BusinessKey由伺服器組合根及業務事件提供，ToString採冒號分隔。正式驗證限制與Tenancy識別相同的有界ASCII，不引用其internal方法。
使用邊界與維護：本檔不取得授權、不查資料、不保存HttpContext或資源；排程服務另核對產品、組織與既有工作範圍。值由呼叫端及單次SQL操作持有，測試必須拒絕路徑分隔、控制字元及過長輸入。
*/
namespace SpeechMessage.Scheduling;

/// <summary>伺服器提供的穩定工作鍵；格式有效不代表已授權任何租戶或業務事件。</summary>
public sealed record JobKey
{
    /// <summary>固定部署產品；不可由HTTP路由決定。</summary>
    public string Product { get; }
    /// <summary>產品註冊的處理器名稱；不是任意反射型別名稱。</summary>
    public string Name { get; }
    /// <summary>權威事件衍生的穩定業務鍵，跨程序重送必須相同。</summary>
    public string BusinessKey { get; }
    /// <summary>三段各驗1至128字元的識別；不修剪或正規化大小寫，任何錯誤均在SQL之前拒絕。</summary>
    public JobKey(string product, string name, string businessKey)
    {
        Product = RequireIdentifier(product, nameof(product));
        Name = RequireIdentifier(name, nameof(name));
        BusinessKey = RequireIdentifier(businessKey, nameof(businessKey));
    }
    /// <summary>以固定三段表示工作身分；不能將此值寫入含個資的診斷。</summary>
    public override string ToString() => $"{Product}:{Name}:{BusinessKey}";
    /// <summary>依Tenancy相同格式檢查純識別，不引用其internal實作或接受設定路徑。</summary>
    internal static string RequireIdentifier(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !char.IsAsciiLetterOrDigit(value[0]))
            throw new ArgumentException("A bounded scheduling identifier is required.", parameter);
        foreach (var character in value)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.')
                throw new ArgumentException("Scheduling identifiers cannot contain delimiters.", parameter);
        return value;
    }
}
