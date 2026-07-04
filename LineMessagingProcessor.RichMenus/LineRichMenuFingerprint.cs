using System.Security.Cryptography;
using System.Text;
using Line.Messaging;
using Newtonsoft.Json;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 依 RichMenu 版面與 PNG 內容產生穩定 fingerprint。
/// Provisioning workflow 會把 fingerprint 短碼附加到 LINE RichMenu 名稱，讓同步流程能判斷遠端選單是否已是最新版本。
/// </summary>
public static class LineRichMenuFingerprint
{
    public static string BuildName(LineRichMenuDefinition definition, byte[] pngBytes)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (pngBytes == null)
        {
            throw new ArgumentNullException(nameof(pngBytes));
        }

        var fingerprint = Create(definition.RichMenu, pngBytes);
        return BuildName(definition, fingerprint);
    }

    public static string BuildName(LineRichMenuDefinition definition, string fingerprint)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var baseName = string.IsNullOrWhiteSpace(definition.RichMenu.Name)
            ? definition.MenuKey
            : definition.RichMenu.Name.Trim();
        var suffix = " v" + ShortVersion(fingerprint);
        var maxBaseLength = Math.Max(1, 300 - suffix.Length);
        if (baseName.Length > maxBaseLength)
        {
            baseName = baseName[..maxBaseLength];
        }

        return baseName + suffix;
    }

    public static string Create(RichMenu richMenu, byte[] pngBytes)
    {
        if (richMenu == null)
        {
            throw new ArgumentNullException(nameof(richMenu));
        }

        if (pngBytes == null)
        {
            throw new ArgumentNullException(nameof(pngBytes));
        }

        var stablePayload = new
        {
            richMenu.Size,
            richMenu.Selected,
            richMenu.ChatBarText,
            richMenu.Areas
        };

        var json = JsonConvert.SerializeObject(stablePayload);
        using var sha256 = SHA256.Create();
        var richMenuBytes = Encoding.UTF8.GetBytes(json);
        var merged = new byte[richMenuBytes.Length + pngBytes.Length];
        Buffer.BlockCopy(richMenuBytes, 0, merged, 0, richMenuBytes.Length);
        Buffer.BlockCopy(pngBytes, 0, merged, richMenuBytes.Length, pngBytes.Length);
        return Convert.ToHexString(sha256.ComputeHash(merged)).ToLowerInvariant();
    }

    public static string ShortVersion(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return "unknown";
        }

        var normalized = fingerprint.Trim();
        return normalized.Length <= 12 ? normalized : normalized[..12];
    }

}
