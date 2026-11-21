using System.Text.Json;

namespace Chlorella;

sealed partial class WorksheetEngine
{
    void SetSheetProtection(string command, JsonElement args)
    {
        Sheet sheet = FindSheet(args.GetProperty("sheet").GetString());
        string password = args.GetProperty("password").GetString() ?? "";
        if (password.Length > 15) throw new InvalidOperationException("パスワードは 15 文字以内で入力してください。");
        ushort hash = LegacyPasswordHash(password);
        if (command == "review.protectSheet")
        {
            if (sheet.ProtectionHash.HasValue) throw new InvalidOperationException("シートは既に保護されています。");
            sheet.ProtectionHash = hash;
        }
        else
        {
            if (!sheet.ProtectionHash.HasValue) throw new InvalidOperationException("シートは保護されていません。");
            if (sheet.ProtectionHash.Value != hash) throw new InvalidOperationException("パスワードが違います。");
            sheet.ProtectionHash = null;
        }
    }

    static ushort LegacyPasswordHash(string password)
    {
        if (password.Length == 0) return 0;
        int hash = 0;
        for (int i = password.Length - 1; i >= 0; i--)
        {
            hash = ((hash >> 14) & 1) | ((hash << 1) & 0x7fff);
            hash ^= password[i];
        }
        hash = ((hash >> 14) & 1) | ((hash << 1) & 0x7fff);
        return (ushort)(hash ^ password.Length ^ 0xce4b);
    }
}
