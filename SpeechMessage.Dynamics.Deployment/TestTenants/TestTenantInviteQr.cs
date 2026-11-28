/*
檔案整體說明
目的與定位：F12 邀請的本機手機 QR 交付，將已核驗的 LIFF 與加好友網址輸出至固定使用者目錄。
主要流程：以既有 ZXing.Net 編碼及 ImageSharp PNG 寫入唯一暫存檔，成功 flush 後原子發布為 invite-{code}.png 及 friend-{code}.png；不保存一般日誌或 JSON 個資。
資源擁有權與隔離：圖像、像素與檔案僅屬當次呼叫，using 確定性釋放；路徑由 CLI 固定，拒絕 link 與任意代碼，不接受 HTTP 指定輸出路徑。邀請網址只為明確交付而含憑證，不傳給外部圖片服務。
失敗與驗證：失敗只清自己暫存檔，不刪已發布的歷史證據；測試解碼合成 QR 及注入錯誤路徑，不使用真邀請或外部 API。
*/
using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.QrCode;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>單次生成的本機交付器，不保留 token、圖像或使用者狀態。</summary>
internal static class TestTenantInviteQr
{
    /// <summary>以固定白名單網址產生兩張 PNG，回傳僅含檔案路徑的結果。</summary>
    internal static (string Invite, string Friend) Write(string root, string code, string inviteUrl, string friendUrl)
    {
        if (code is not { Length: 4 } || code[0] is not ('c' or 'b' or 'g') || code.Skip(1).Any(character => character is < '0' or > '9') ||
            !Uri.TryCreate(inviteUrl, UriKind.Absolute, out var invite) || invite.Scheme != Uri.UriSchemeHttps || invite.Host != "liff.line.me" ||
            !invite.IsDefaultPort || invite.UserInfo.Length != 0 || inviteUrl.Length > 512 || !invite.Fragment.StartsWith("#token=", StringComparison.Ordinal) ||
            !Uri.TryCreate(friendUrl, UriKind.Absolute, out var friend) || friend.Scheme != Uri.UriSchemeHttps || friend.Host != "line.me" ||
            !friend.IsDefaultPort || friend.UserInfo.Length != 0 || !friend.AbsolutePath.StartsWith("/R/ti/p/", StringComparison.Ordinal) || friendUrl.Length > 256)
            throw new ArgumentException("Test tenant QR handoff scope is invalid.");
        var directory = Path.GetFullPath(root);
        CheckPath(directory);
        Directory.CreateDirectory(directory);
        var invitePath = Path.Combine(directory, "invite-" + code + ".png");
        var friendPath = Path.Combine(directory, "friend-" + code + ".png");
        WriteImage(invitePath, inviteUrl);
        WriteImage(friendPath, friendUrl);
        return (invitePath, friendPath);
    }

    /// <summary>不以外部圖片服務處理邀請；本地矩陣及圖像唯一存活至 PNG 完成寫入。</summary>
    private static void WriteImage(string path, string content)
    {
        CheckPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var matrix = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, 512, 512);
            using var image = new Image<Rgba32>(matrix.Width, matrix.Height);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++) row[x] = matrix[x, y] ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
                }
            });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                image.SaveAsPng(stream);
                stream.Flush(flushToDisk: true);
            }
            CheckPath(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>所有已存在的祖先及目標拒絕 reparse point，避免使用者固定交付路徑被導出至其他位置。</summary>
    private static void CheckPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Test tenant QR handoff path is unsafe.");
    }
}
