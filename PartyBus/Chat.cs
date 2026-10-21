using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace PartyBus;

/// <summary>Sends a line through the game's chat box, exactly as if the host typed it.
/// Only ever called from a button the host clicks.</summary>
internal static class Chat
{
    private const int MaxBytes = 500;

    public static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(char.IsControl(c) ? ' ' : c);
        var s = sb.ToString().Trim();
        while (Encoding.UTF8.GetByteCount(s) > MaxBytes)
            s = s[..^4] + "...";
        return s;
    }

    public static unsafe bool Send(string text)
    {
        var line = Clean(text);
        if (line.Length == 0) return false;
        try
        {
            var str = Utf8String.FromString(line);
            UIModule.Instance()->ProcessChatBoxEntry(str);
            str->Dtor(true);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not send chat message");
            return false;
        }
    }
}
