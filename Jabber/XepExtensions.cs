using System.Text;
using System.Text.RegularExpressions;

namespace XmppClient;

// ============================================================================
// XEP-0085: Chat State Notifications
// ============================================================================

public enum ChatState
{
    Active,      // Benutzer ist im Chat aktiv
    Composing,   // Benutzer tippt gerade
    Paused,      // Benutzer hat aufgehört zu tippen
    Inactive,    // Benutzer ist inaktiv
    Gone         // Benutzer hat Chat verlassen
}

public static class ChatStateExtensions
{
    public static string ToXml(this ChatState state) => state switch
    {
        ChatState.Active    => "<active xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Composing => "<composing xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Paused    => "<paused xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Inactive  => "<inactive xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Gone      => "<gone xmlns='http://jabber.org/protocol/chatstates'/>",
        _ => ""
    };

    public static ChatState? ParseChatState(string xml)
    {
        if (xml.Contains("<active"))    return ChatState.Active;
        if (xml.Contains("<composing")) return ChatState.Composing;
        if (xml.Contains("<paused"))    return ChatState.Paused;
        if (xml.Contains("<inactive"))  return ChatState.Inactive;
        if (xml.Contains("<gone"))      return ChatState.Gone;
        return null;
    }
}

// ============================================================================
// XEP-0184: Message Delivery Receipts
// ============================================================================

public sealed class MessageReceipt
{
    public string MessageId { get; }
    public string From { get; }
    public DateTime ReceivedAt { get; }

    public MessageReceipt(string messageId, string from)
    {
        MessageId = messageId;
        From = from;
        ReceivedAt = DateTime.UtcNow;
    }
}

public sealed class ReceiptTracker
{
    private readonly Dictionary<string, PendingReceipt> _pending = new();
    private readonly object _lock = new();

    public event Action<string, string>? OnReceiptReceived; // messageId, from

    /// <summary>
    /// Registriert eine gesendete Nachricht für Receipt-Tracking
    /// </summary>
    public void TrackMessage(string messageId, string to)
    {
        var bareTo = GetBareJid(to);
        lock (_lock)
        {
            _pending[messageId] = new PendingReceipt(messageId, bareTo, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Verarbeitet eine eingehende Receipt mit Spoofing-Schutz
    /// </summary>
    public bool ProcessReceipt(string receiptId, string from)
    {
        var bareFrom = GetBareJid(from);
        
        lock (_lock)
        {
            if (!_pending.TryGetValue(receiptId, out var pending))
            {
                // Receipt für unbekannte Nachricht - ignorieren
                return false;
            }

            // SPOOFING-SCHUTZ: Receipt muss vom erwarteten Empfänger kommen
            if (!string.Equals(pending.ExpectedFrom, bareFrom, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[!] Receipt-Spoofing erkannt! Erwartet: {pending.ExpectedFrom}, Erhalten: {bareFrom}");
                return false;
            }

            _pending.Remove(receiptId);
        }

        OnReceiptReceived?.Invoke(receiptId, from);
        return true;
    }

    /// <summary>
    /// Prüft auf Timeout (alte unbestätigte Nachrichten)
    /// </summary>
    public IEnumerable<string> GetTimedOutMessages(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        lock (_lock)
        {
            return _pending
                .Where(kvp => kvp.Value.SentAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    private static string GetBareJid(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }

    private sealed record PendingReceipt(string MessageId, string ExpectedFrom, DateTime SentAt);
}

public static class ReceiptBuilder
{
    /// <summary>
    /// Erzeugt das XML für eine Receipt-Anfrage (in ausgehende Nachricht einfügen)
    /// </summary>
    public static string RequestXml => "<request xmlns='urn:xmpp:receipts'/>";

    /// <summary>
    /// Erzeugt eine Receipt-Antwort
    /// </summary>
    public static string CreateReceipt(string to, string originalMessageId)
    {
        return $"<message to='{XmlEscape(to)}'>" +
               $"<received xmlns='urn:xmpp:receipts' id='{XmlEscape(originalMessageId)}'/>" +
               $"</message>";
    }

    /// <summary>
    /// Prüft ob eine Nachricht eine Receipt-Anfrage enthält
    /// </summary>
    public static bool HasReceiptRequest(string messageXml)
    {
        return messageXml.Contains("xmlns='urn:xmpp:receipts'") && 
               messageXml.Contains("<request");
    }

    /// <summary>
    /// Extrahiert die Receipt-ID aus einer Receipt-Nachricht
    /// </summary>
    public static string? ExtractReceiptId(string messageXml)
    {
        var match = Regex.Match(messageXml, @"<received[^>]+id=['""]([^'""]+)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");
}

// ============================================================================
// XEP-0280: Message Carbons
// ============================================================================

public sealed class CarbonMessage
{
    public bool IsSent { get; }          // true = von mir gesendet, false = empfangen
    public string OriginalFrom { get; }
    public string OriginalTo { get; }
    public string? Body { get; }
    public string? MessageId { get; }
    public DateTime ReceivedAt { get; }

    public CarbonMessage(bool isSent, string originalFrom, string originalTo, string? body, string? messageId)
    {
        IsSent = isSent;
        OriginalFrom = originalFrom;
        OriginalTo = originalTo;
        Body = body;
        MessageId = messageId;
        ReceivedAt = DateTime.UtcNow;
    }
}

public sealed class CarbonManager
{
    private readonly string _myBareJid;
    private bool _enabled;

    public bool IsEnabled => _enabled;
    
    public event Action<CarbonMessage>? OnCarbonReceived;

    public CarbonManager(string myBareJid)
    {
        _myBareJid = GetBareJid(myBareJid);
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>
    /// Verarbeitet eine Carbon-Nachricht mit Spoofing-Schutz
    /// </summary>
    public bool ProcessCarbon(string messageXml, string from)
    {
        var bareFrom = GetBareJid(from);

        // KRITISCHER SPOOFING-SCHUTZ: 
        // Carbons dürfen NUR vom eigenen Bare-JID kommen (= vom Server)!
        if (!string.Equals(bareFrom, _myBareJid, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[!] Carbon-Spoofing erkannt! Von: {from}, erwartet: {_myBareJid}");
            return false;
        }

        // Prüfe ob sent oder received
        var isSent = messageXml.Contains("<sent xmlns='urn:xmpp:carbons:2'");
        var isReceived = messageXml.Contains("<received xmlns='urn:xmpp:carbons:2'");

        if (!isSent && !isReceived)
        {
            return false;
        }

        // Extrahiere die forwarded Message
        var forwardedMatch = Regex.Match(messageXml, 
            @"<forwarded[^>]*xmlns='urn:xmpp:forward:0'[^>]*>(.*?)</forwarded>",
            RegexOptions.Singleline);

        if (!forwardedMatch.Success)
        {
            return false;
        }

        var forwardedXml = forwardedMatch.Groups[1].Value;

        // Extrahiere die Original-Nachricht
        var originalFrom = ExtractAttribute(forwardedXml, "from");
        var originalTo = ExtractAttribute(forwardedXml, "to");
        var body = ExtractElement(forwardedXml, "body");
        var msgId = ExtractAttribute(forwardedXml, "id");

        if (originalFrom == null || originalTo == null)
        {
            return false;
        }

        var carbon = new CarbonMessage(isSent, originalFrom, originalTo, body, msgId);
        OnCarbonReceived?.Invoke(carbon);
        return true;
    }

    /// <summary>
    /// Erzeugt das IQ zum Aktivieren von Carbons
    /// </summary>
    public static string EnableIq(string id = "carbons-enable")
    {
        return $"<iq type='set' id='{id}'>" +
               $"<enable xmlns='urn:xmpp:carbons:2'/>" +
               $"</iq>";
    }

    /// <summary>
    /// Erzeugt das IQ zum Deaktivieren von Carbons
    /// </summary>
    public static string DisableIq(string id = "carbons-disable")
    {
        return $"<iq type='set' id='{id}'>" +
               $"<disable xmlns='urn:xmpp:carbons:2'/>" +
               $"</iq>";
    }

    private static string GetBareJid(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }

    private static string? ExtractAttribute(string xml, string name)
    {
        var match = Regex.Match(xml, $@"{name}=['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractElement(string xml, string name)
    {
        var match = Regex.Match(xml, $@"<{name}>([^<]*)</{name}>");
        return match.Success ? match.Groups[1].Value : null;
    }
}

// ============================================================================
// XEP-0060: Publish-Subscribe (PubSub)
// ============================================================================

public sealed class PubSubItem
{
    public string Id { get; }
    public string NodeId { get; }
    public string Payload { get; }
    public string? Publisher { get; }
    public DateTime ReceivedAt { get; }

    public PubSubItem(string id, string nodeId, string payload, string? publisher = null)
    {
        Id = id;
        NodeId = nodeId;
        Payload = payload;
        Publisher = publisher;
        ReceivedAt = DateTime.UtcNow;
    }
}

public sealed class PubSubEvent
{
    public string NodeId { get; }
    public PubSubEventType Type { get; }
    public List<PubSubItem> Items { get; } = [];
    public List<string> RetractedIds { get; } = [];

    public PubSubEvent(string nodeId, PubSubEventType type)
    {
        NodeId = nodeId;
        Type = type;
    }
}

public enum PubSubEventType
{
    Items,      // Neue/aktualisierte Items
    Retract,    // Items gelöscht
    Purge,      // Node geleert
    Delete,     // Node gelöscht
    Configuration // Node-Config geändert
}

public sealed class PubSubManager
{
    private readonly HashSet<string> _subscribedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pubsubService;
    private readonly object _lock = new();

    public event Action<PubSubEvent>? OnEvent;
    public event Action<string, bool, string?>? OnSubscriptionResult; // nodeId, success, error

    public PubSubManager(string pubsubService = "pubsub")
    {
        _pubsubService = pubsubService;
    }

    public string PubSubService => _pubsubService;

    /// <summary>
    /// Verarbeitet eine eingehende PubSub-Event-Nachricht mit Spoofing-Schutz
    /// </summary>
    public bool ProcessEvent(string messageXml, string from, string expectedPubSubJid)
    {
        var bareFrom = GetBareJid(from);
        var expectedBare = GetBareJid(expectedPubSubJid);

        // SPOOFING-SCHUTZ: Events dürfen nur vom PubSub-Service kommen
        if (!string.Equals(bareFrom, expectedBare, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[!] PubSub-Spoofing erkannt! Von: {from}, erwartet: {expectedPubSubJid}");
            return false;
        }

        // Parse event
        var eventMatch = Regex.Match(messageXml,
            @"<event\s+xmlns='http://jabber\.org/protocol/pubsub#event'[^>]*>(.*?)</event>",
            RegexOptions.Singleline);

        if (!eventMatch.Success)
        {
            return false;
        }

        var eventXml = eventMatch.Groups[1].Value;

        // Items Event
        var itemsMatch = Regex.Match(eventXml, @"<items\s+node=['""]([^'""]+)['""][^>]*>(.*?)</items>", RegexOptions.Singleline);
        if (itemsMatch.Success)
        {
            var nodeId = itemsMatch.Groups[1].Value;
            var itemsXml = itemsMatch.Groups[2].Value;
            
            var pubsubEvent = new PubSubEvent(nodeId, PubSubEventType.Items);
            
            var items = Regex.Matches(itemsXml, @"<item\s+id=['""]([^'""]+)['""][^>]*>(.*?)</item>", RegexOptions.Singleline);
            foreach (Match item in items)
            {
                pubsubEvent.Items.Add(new PubSubItem(
                    item.Groups[1].Value,
                    nodeId,
                    item.Groups[2].Value
                ));
            }

            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Retract Event
        var retractMatch = Regex.Match(eventXml, @"<items\s+node=['""]([^'""]+)['""][^>]*>.*?<retract\s+id=['""]([^'""]+)['""]", RegexOptions.Singleline);
        if (retractMatch.Success)
        {
            var nodeId = retractMatch.Groups[1].Value;
            var retractId = retractMatch.Groups[2].Value;
            
            var pubsubEvent = new PubSubEvent(nodeId, PubSubEventType.Retract);
            pubsubEvent.RetractedIds.Add(retractId);
            
            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Purge Event
        var purgeMatch = Regex.Match(eventXml, @"<purge\s+node=['""]([^'""]+)['""]");
        if (purgeMatch.Success)
        {
            var pubsubEvent = new PubSubEvent(purgeMatch.Groups[1].Value, PubSubEventType.Purge);
            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Delete Event
        var deleteMatch = Regex.Match(eventXml, @"<delete\s+node=['""]([^'""]+)['""]");
        if (deleteMatch.Success)
        {
            var pubsubEvent = new PubSubEvent(deleteMatch.Groups[1].Value, PubSubEventType.Delete);
            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        return false;
    }

    public void AddSubscription(string nodeId)
    {
        lock (_lock) _subscribedNodes.Add(nodeId);
    }

    public void RemoveSubscription(string nodeId)
    {
        lock (_lock) _subscribedNodes.Remove(nodeId);
    }

    public bool IsSubscribed(string nodeId)
    {
        lock (_lock) return _subscribedNodes.Contains(nodeId);
    }

    private static string GetBareJid(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }
}

public static class PubSubBuilder
{
    /// <summary>
    /// Subscribe to a node
    /// </summary>
    public static string Subscribe(string pubsubJid, string nodeId, string myJid, string id = "pubsub-sub")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<subscribe node='{XmlEscape(nodeId)}' jid='{XmlEscape(myJid)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Unsubscribe from a node
    /// </summary>
    public static string Unsubscribe(string pubsubJid, string nodeId, string myJid, string id = "pubsub-unsub")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<unsubscribe node='{XmlEscape(nodeId)}' jid='{XmlEscape(myJid)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Publish an item to a node
    /// </summary>
    public static string Publish(string pubsubJid, string nodeId, string itemId, string payload, string id = "pubsub-pub")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<publish node='{XmlEscape(nodeId)}'>" +
               $"<item id='{XmlEscape(itemId)}'>{payload}</item>" +
               $"</publish></pubsub></iq>";
    }

    /// <summary>
    /// Retract (delete) an item from a node
    /// </summary>
    public static string Retract(string pubsubJid, string nodeId, string itemId, string id = "pubsub-retract")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<retract node='{XmlEscape(nodeId)}'>" +
               $"<item id='{XmlEscape(itemId)}'/>" +
               $"</retract></pubsub></iq>";
    }

    /// <summary>
    /// Get items from a node
    /// </summary>
    public static string GetItems(string pubsubJid, string nodeId, int? maxItems = null, string id = "pubsub-get")
    {
        var maxAttr = maxItems.HasValue ? $" max_items='{maxItems}'" : "";
        return $"<iq type='get' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<items node='{XmlEscape(nodeId)}'{maxAttr}/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Create a new node
    /// </summary>
    public static string CreateNode(string pubsubJid, string nodeId, string id = "pubsub-create")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<create node='{XmlEscape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Delete a node
    /// </summary>
    public static string DeleteNode(string pubsubJid, string nodeId, string id = "pubsub-delete")
    {
        return $"<iq type='set' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub#owner'>" +
               $"<delete node='{XmlEscape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Discover nodes on a pubsub service
    /// </summary>
    public static string DiscoverNodes(string pubsubJid, string id = "pubsub-disco")
    {
        return $"<iq type='get' to='{XmlEscape(pubsubJid)}' id='{id}'>" +
               $"<query xmlns='http://jabber.org/protocol/disco#items'/>" +
               $"</iq>";
    }

    private static string XmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");
}
