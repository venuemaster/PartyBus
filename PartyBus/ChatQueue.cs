using System;
using System.Collections.Generic;

namespace PartyBus;

/// <summary>
/// Every line the plugin sends goes through here, one at a time, with a gap between them,
/// so the game's chat spam limit ("Your message was not heard...") never kicks in.
/// An optional callback runs right after its line actually goes out.
/// </summary>
public class ChatQueue
{
    private sealed class Item
    {
        public string Line = string.Empty;
        public Action? After;
        public Action? Before;
        public object? Owner;
        public int Retries;
    }

    private readonly Plugin plugin;
    private readonly LinkedList<Item> items = new();
    private DateTime nextAllowed = DateTime.MinValue;
    private Item? lastItem;
    private DateTime lastSentAt = DateTime.MinValue;
    private int blockedStreak;

    /// <summary>How many times the game has blocked a message this session (shown in the window).</summary>
    public int BlockedCount { get; private set; }

    private DateTime lastBlockedAt = DateTime.MinValue;

    public ChatQueue(Plugin plugin) => this.plugin = plugin;

    public int Pending => items.Count;

    public void Enqueue(string line, Action? after = null, object? owner = null, Action? before = null)
        => items.AddLast(new Item { Line = line, After = after, Owner = owner, Before = before });

    /// <summary>Drops every queued line that belongs to <paramref name="owner"/> (e.g. a cancelled countdown).</summary>
    public void Remove(object owner)
    {
        var n = items.First;
        while (n != null) { var next = n.Next; if (ReferenceEquals(n.Value.Owner, owner)) items.Remove(n); n = next; }
    }

    public void Clear() { items.Clear(); lastItem = null; }

    /// <summary>
    /// Called when the game says "Your message was not heard...". The line that was just sent is put back
    /// at the front of the queue and re-sent after a longer pause (its callback, e.g. GO, has already run,
    /// so the retry only re-sends the text).
    /// </summary>
    public void OnBlocked()
    {
        BlockedCount++;
        if ((DateTime.Now - lastBlockedAt).TotalSeconds > 30) blockedStreak = 0;
        lastBlockedAt = DateTime.Now;
        blockedStreak++;
        var wait = Math.Min(4.0 + 2.0 * blockedStreak, 12.0);
        nextAllowed = DateTime.Now.AddSeconds(wait);
        if (lastItem == null || (DateTime.Now - lastSentAt).TotalSeconds > 5) return;
        if (lastItem.Retries >= 3) { lastItem = null; return; }
        items.AddFirst(new Item { Line = lastItem.Line, Owner = lastItem.Owner, Before = lastItem.Before, Retries = lastItem.Retries + 1 });
        lastItem = null;
    }

    public void Update()
    {
        if (items.Count == 0 || DateTime.Now < nextAllowed) return;
        var item = items.First!.Value;
        items.RemoveFirst();
        try { item.Before?.Invoke(); }
        catch (Exception ex) { Plugin.Log.Error(ex, "Chat queue pre-send step failed"); }
        var sent = Chat.Send(item.Line);
        var gap = Math.Clamp(plugin.Config.SecondsBetweenMessages, 1.0f, 10.0f);
        nextAllowed = DateTime.Now.AddSeconds(gap);
        if (sent) { lastItem = item; lastSentAt = DateTime.Now; }
        if (item.Retries > 0) return;   // a re-send: its callback already ran the first time
        try { item.After?.Invoke(); }
        catch (Exception ex) { Plugin.Log.Error(ex, "Chat queue callback failed"); }
    }
}
