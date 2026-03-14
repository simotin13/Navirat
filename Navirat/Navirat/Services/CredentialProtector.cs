using System.Security.Cryptography;
using System.Text;

namespace Navirat.Services;

/// <summary>
/// Windows DPAPI を使用してパスワードを現在のユーザーアカウントに紐付けて暗号化します。
/// 同一ユーザー・同一 PC 以外では復号できません。
/// </summary>
public static class CredentialProtector
{
    // 暗号化済み文字列の識別プレフィックス（旧フォーマット平文との区別用）
    private const string Prefix = "DPAPI:";

    /// <summary>平文パスワードを DPAPI で暗号化し Base64 文字列として返します。</summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        var bytes     = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// 暗号化済み文字列を復号して平文を返します。
    /// プレフィックスがない旧フォーマット（平文）はそのまま返します（後方互換）。
    /// </summary>
    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        // 旧フォーマット（平文）との後方互換
        if (!cipherText.StartsWith(Prefix)) return cipherText;

        try
        {
            var base64  = cipherText[Prefix.Length..];
            var bytes   = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // 復号失敗（別ユーザー・別 PC など）は空文字を返す
            return string.Empty;
        }
    }
}
