namespace XmppClient;

/// <summary>
/// Repräsentiert einen Kontakt im Roster
/// </summary>
public sealed class RosterItem
{
    public string Jid { get; }
    public string? Name { get; set; }
    public SubscriptionState Subscription { get; set; } = SubscriptionState.None;
    public List<string> Groups { get; } = [];
    
    public PresenceState Presence { get; set; } = PresenceState.Offline;
    public string? PresenceStatus { get; set; }
    public DateTime LastSeen { get; set; }

    public RosterItem(string jid)
    {
        Jid = jid.ToLowerInvariant();
    }

    public string DisplayName => Name ?? Jid;
    
    public string BareJid
    {
        get
        {
            var slash = Jid.IndexOf('/');
            return slash > 0 ? Jid[..slash] : Jid;
        }
    }

    public override string ToString()
    {
        var sub = Subscription switch
        {
            SubscriptionState.Both => "↔",
            SubscriptionState.To => "→",
            SubscriptionState.From => "←",
            _ => "○"
        };
        
        var pres = Presence switch
        {
            PresenceState.Available => "●",
            PresenceState.Away => "◐",
            PresenceState.Dnd => "⊘",
            PresenceState.Xa => "◑",
            PresenceState.Chat => "◉",
            _ => "○"
        };
        
        var groups = Groups.Count > 0 ? $" [{string.Join(", ", Groups)}]" : "";
        return $"{pres} {sub} {DisplayName}{groups}";
    }
}

public enum SubscriptionState
{
    None,
    To,
    From,
    Both,
    Remove
}

public enum PresenceState
{
    Offline,
    Available,
    Away,
    Chat,
    Dnd,
    Xa
}

/// <summary>
/// Roster-Manager mit Subscription-Handling
/// </summary>
public sealed class Roster
{
    private readonly Dictionary<string, RosterItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    
    public string? Version { get; set; }
    
    public event Action<RosterItem>? OnItemAdded;
    public event Action<RosterItem>? OnItemUpdated;
    public event Action<string>? OnItemRemoved;
    public event Action<string, string>? OnSubscriptionRequest;

    public IReadOnlyCollection<RosterItem> Items
    {
        get { lock (_lock) return _items.Values.ToList(); }
    }

    public RosterItem? GetItem(string jid)
    {
        var bareJid = GetBareJid(jid);
        lock (_lock)
        {
            return _items.TryGetValue(bareJid, out var item) ? item : null;
        }
    }

    public void ProcessRosterItem(RosterItem newItem)
    {
        var bareJid = GetBareJid(newItem.Jid);
        
        lock (_lock)
        {
            if (_items.TryGetValue(bareJid, out var existing))
            {
                existing.Name = newItem.Name;
                existing.Subscription = newItem.Subscription;
                existing.Groups.Clear();
                existing.Groups.AddRange(newItem.Groups);
                OnItemUpdated?.Invoke(existing);
            }
            else
            {
                _items[bareJid] = newItem;
                OnItemAdded?.Invoke(newItem);
            }
        }
    }

    public void RemoveItem(string jid)
    {
        var bareJid = GetBareJid(jid);
        lock (_lock)
        {
            if (_items.Remove(bareJid))
            {
                OnItemRemoved?.Invoke(bareJid);
            }
        }
    }

    public void RaiseSubscriptionRequest(string from, string status)
    {
        OnSubscriptionRequest?.Invoke(from, status);
    }

    public void UpdatePresence(string from, string type, string? show, string? status)
    {
        var bareJid = GetBareJid(from);
        
        lock (_lock)
        {
            if (!_items.TryGetValue(bareJid, out var item))
            {
                return;
            }
            
            if (type == "unavailable")
            {
                item.Presence = PresenceState.Offline;
                item.PresenceStatus = null;
            }
            else
            {
                item.Presence = show switch
                {
                    "away" => PresenceState.Away,
                    "chat" => PresenceState.Chat,
                    "dnd" => PresenceState.Dnd,
                    "xa" => PresenceState.Xa,
                    _ => PresenceState.Available
                };
                item.PresenceStatus = status;
            }
            
            item.LastSeen = DateTime.UtcNow;
            OnItemUpdated?.Invoke(item);
        }
    }

    private static string GetBareJid(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }
    
    public IEnumerable<RosterItem> GetOnlineContacts()
    {
        lock (_lock)
        {
            return _items.Values
                .Where(i => i.Presence != PresenceState.Offline)
                .OrderBy(i => i.DisplayName)
                .ToList();
        }
    }
    
    public IEnumerable<RosterItem> GetByGroup(string group)
    {
        lock (_lock)
        {
            return _items.Values
                .Where(i => i.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
                .OrderBy(i => i.DisplayName)
                .ToList();
        }
    }
    
    public IEnumerable<string> GetGroups()
    {
        lock (_lock)
        {
            return _items.Values
                .SelectMany(i => i.Groups)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .ToList();
        }
    }
}

/// <summary>
/// Builder für Roster-IQ-Stanzas
/// </summary>
public static class RosterStanzaBuilder
{
    public static string GetRoster(string? version = null)
    {
        var ver = version != null ? $" ver='{version}'" : "";
        return $"<iq type='get' id='roster1'>" +
               $"<query xmlns='jabber:iq:roster'{ver}/>" +
               $"</iq>";
    }

    public static string SetItem(string jid, string? name = null, IEnumerable<string>? groups = null)
    {
        var nameAttr = name != null ? $" name='{XmlEscape(name)}'" : "";
        var groupsXml = groups != null 
            ? string.Join("", groups.Select(g => $"<group>{XmlEscape(g)}</group>"))
            : "";
        
        return $"<iq type='set' id='roster-set-{Guid.NewGuid():N}'>" +
               $"<query xmlns='jabber:iq:roster'>" +
               $"<item jid='{XmlEscape(jid)}'{nameAttr}>{groupsXml}</item>" +
               $"</query></iq>";
    }

    public static string RemoveItem(string jid)
    {
        return $"<iq type='set' id='roster-remove-{Guid.NewGuid():N}'>" +
               $"<query xmlns='jabber:iq:roster'>" +
               $"<item jid='{XmlEscape(jid)}' subscription='remove'/>" +
               $"</query></iq>";
    }

    public static string Subscribe(string jid) =>
        $"<presence to='{XmlEscape(jid)}' type='subscribe'/>";

    public static string Subscribed(string jid) =>
        $"<presence to='{XmlEscape(jid)}' type='subscribed'/>";

    public static string Unsubscribed(string jid) =>
        $"<presence to='{XmlEscape(jid)}' type='unsubscribed'/>";

    public static string Unsubscribe(string jid) =>
        $"<presence to='{XmlEscape(jid)}' type='unsubscribe'/>";

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");
}
