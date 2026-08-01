/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Anwendungsnaher XMPP-Client.
///
/// Kapselt eine <see cref="XMPPConnection"/> und die Sitzungslogik, die sonst
/// in der Benutzeroberfläche landet: aktueller Chatpartner, offene
/// Kontaktanfragen, zuletzt empfangene Nachrichten-ID sowie zusammengesetzte
/// Operationen (etwa "Kontaktanfrage annehmen" = subscribed senden,
/// Gegenanfrage stellen und aus der Warteliste entfernen).
///
/// Die Klasse gibt keinerlei Ausgaben aus; alles läuft über die Events und
/// die übergebene <see cref="ILoggerFactory"/>.
/// </summary>
public sealed class XMPPClient : IAsyncDisposable
{

    #region Data

    private readonly XMPPConnection _connection;
    private readonly ILogger _logger;
    private readonly List<string> _pendingSubscriptions = [];
    private readonly object _pendingLock = new();

    /// <summary>
    /// Gültige Werte für das &lt;show/&gt;-Element (RFC 6121, Abschnitt 4.7.2.1).
    /// "available" ist die Abwesenheit von &lt;show/&gt; und daher mit erlaubt.
    /// </summary>
    private static readonly string[] ValidShowValues = ["available", "away", "chat", "dnd", "xa"];

    #endregion

    #region Properties

    /// <summary>
    /// Die zugrundeliegende Verbindung - für Statusabfragen und Sonderfälle.
    /// </summary>
    public XMPPConnection Connection => _connection;

    public Roster Roster => _connection.Roster;
    public ConnectionState State => _connection.State;
    public string FullJid => _connection.FullJid;
    public string BareJid => _connection.BareJid;
    public string Domain => _connection.Domain;
    public string WebSocketUri => _connection.WebSocketUri;
    public IReadOnlyList<string> ServerFeatures => _connection.ServerFeatures;
    public IReadOnlyList<string> LocalFeatures => _connection.Disco?.LocalFeatures ?? [];

    public bool IsConnected => _connection.State == ConnectionState.Connected;
    public bool CarbonsEnabled => _connection.Carbons?.IsEnabled == true;
    public StreamManagementManager? StreamManagement => _connection.StreamManagement;

    /// <summary>
    /// JID des aktuellen Chatpartners; null, wenn kein Chat aktiv ist.
    /// </summary>
    public string? CurrentChatPartner { get; private set; }

    /// <summary>
    /// ID der zuletzt empfangenen Nachricht - Bezugspunkt für Chat Markers
    /// ohne explizite ID.
    /// </summary>
    public string? LastReceivedMessageId { get; private set; }

    /// <summary>
    /// Die zuletzt an einen Empfänger geschickte Nachricht - Bezugspunkt für
    /// eine Korrektur nach XEP-0308.
    /// </summary>
    /// <remarks>
    /// Je Empfänger und nicht insgesamt: Abschnitt 5 lässt nur die jeweils
    /// letzte Nachricht <b>an denselben Empfänger</b> berichtigen. Ein
    /// einzelner Merkposten wäre nach jedem Themenwechsel falsch - und zwar
    /// so, dass die Korrektur beim vorigen Gesprächspartner landet.
    /// </remarks>
    private readonly Dictionary<string, string> _lastSentTo = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Noch nicht beantwortete Kontaktanfragen, in Eingangsreihenfolge.
    /// </summary>
    public IReadOnlyList<string> PendingSubscriptions
    {
        get { lock (_pendingLock) return _pendingSubscriptions.ToList(); }
    }

    // Konfiguration - wirkt bei Verbindungsaufbau bzw. Reconnect
    public bool KeepaliveEnabled
    {
        get => _connection.KeepaliveEnabled;
        set => _connection.KeepaliveEnabled = value;
    }

    public TimeSpan KeepaliveInterval
    {
        get => _connection.KeepaliveInterval;
        set => _connection.KeepaliveInterval = value;
    }

    public bool StreamManagementEnabled
    {
        get => _connection.StreamManagementEnabled;
        set => _connection.StreamManagementEnabled = value;
    }

    #endregion

    #region Events

    /// <summary>Eine Chat-Nachricht wurde empfangen.</summary>
    public event Action<XMPPMessage>? OnMessage;

    /// <summary>XEP-0280: Eine Nachricht wurde von/an ein anderes eigenes Gerät gespiegelt.</summary>
    public event Action<CarbonMessage>? OnCarbonMessage;

    /// <summary>XEP-0085: Ein Kontakt hat seinen Tippstatus geändert.</summary>
    public event Action<string, ChatState>? OnChatState;

    /// <summary>XEP-0333: Ein Chat Marker wurde empfangen.</summary>
    public event Action<ChatMarker>? OnChatMarker;

    /// <summary>XEP-0184: Eine gesendete Nachricht wurde zugestellt.</summary>
    public event Action<string, string>? OnReceiptReceived; // from, messageId

    /// <summary>Presence-Änderung eines Kontakts.</summary>
    public event Action<string, string>? OnPresenceChanged; // from, type

    /// <summary>XEP-0060: PubSub-Event vom Service.</summary>
    public event Action<PubSubEvent>? OnPubSubEvent;

    /// <summary>Eine neue Kontaktanfrage; sie liegt danach in <see cref="PendingSubscriptions"/>.</summary>
    public event Action<string, string>? OnSubscriptionRequest; // from, status

    /// <summary>Ein Kontakt wurde dem Roster hinzugefügt.</summary>
    public event Action<RosterItem>? OnRosterItemAdded;

    /// <summary>Ein Kontakt wurde aus dem Roster entfernt.</summary>
    public event Action<string>? OnRosterItemRemoved;

    /// <summary>XEP-0115: Capabilities einer Gegenstelle wurden ermittelt.</summary>
    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    /// <summary>Der Verbindungszustand hat sich geändert.</summary>
    public event Action<ConnectionState, ConnectionState>? OnStateChanged;

    /// <summary>Ein Fehler ist aufgetreten (bereits geloggt).</summary>
    public event Action<string>? OnError;

    /// <summary>Ein Spoofing-Versuch wurde abgewehrt (bereits geloggt).</summary>
    public event Action<string>? OnSpoofingAttempt;

    /// <summary>
    /// RFC 6120, Abschnitt 8.3: Eine Stanza wurde abgelehnt. Der erste
    /// Parameter ist der Absender des Fehlers, null bei einem Fehler vom
    /// eigenen Server.
    /// </summary>
    public event Action<string?, StanzaError>? OnStanzaError;

    /// <summary>
    /// RFC 6120, Abschnitt 4.9: Der Server hat den Stream mit einem Fehler
    /// beendet. Ist er nicht wiederholbar, unterbleibt der Reconnect.
    /// </summary>
    public event Action<StreamError>? OnStreamError;

    /// <summary>Rohes XML, ein- und ausgehend - für Debug-Anzeigen.</summary>
    public event Action<string>? OnRawXml;

    /// <summary>Der aktuelle Chatpartner wurde gewechselt oder zurückgesetzt.</summary>
    public event Action<string?>? OnChatPartnerChanged;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Erstellt einen neuen XMPP-Client.
    /// </summary>
    /// <param name="jid">Bare-JID im Format user@domain</param>
    /// <param name="password">Passwort für die SASL-Authentifizierung</param>
    /// <param name="wsUri">
    /// WebSocket-Endpunkt. Ohne Angabe wird das <c>host-meta</c> der Domain
    /// gefragt (XEP-0156); findet sich dort keiner, bleibt es bei
    /// wss://{domain}:5443/ws (ejabberd-Vorgabe).
    /// </param>
    /// <param name="LoggerFactory">Optionale Logger-Factory; ohne Angabe wird nicht geloggt</param>
    public XMPPClient(string          jid,
                      string          password,
                      string?         wsUri           = null,
                      ILoggerFactory? LoggerFactory   = null)
    {

        _logger      = LoggerFactory is not null
                           ? LoggerFactory.CreateLogger<XMPPClient>()
                           : NullLogger<XMPPClient>.Instance;

        _connection  = new XMPPConnection(jid, password, wsUri, LoggerFactory);

        WireUpConnection();

    }

    /// <summary>
    /// Erstellt einen Client um eine bereits konfigurierte Verbindung herum.
    /// </summary>
    public XMPPClient(XMPPConnection  connection,
                      ILoggerFactory? LoggerFactory = null)
    {

        _logger      = LoggerFactory is not null
                           ? LoggerFactory.CreateLogger<XMPPClient>()
                           : NullLogger<XMPPClient>.Instance;

        _connection  = connection ?? throw new ArgumentNullException(nameof(connection));

        WireUpConnection();

    }

    private void WireUpConnection()
    {

        _connection.OnMessage += nachricht =>
        {
            if (!string.IsNullOrEmpty(nachricht.MessageId))
                LastReceivedMessageId = nachricht.MessageId;

            OnMessage?.Invoke(nachricht);
        };

        _connection.OnCarbonMessage   += c            => OnCarbonMessage?.Invoke(c);
        _connection.OnChatState       += (from, s)    => OnChatState?.Invoke(from, s);
        _connection.OnChatMarker      += m            => OnChatMarker?.Invoke(m);
        _connection.OnReceiptReceived += (from, id)   => OnReceiptReceived?.Invoke(from, id);
        _connection.OnPresence        += (from, type) => OnPresenceChanged?.Invoke(from, type);
        _connection.OnPubSubEvent     += e            => OnPubSubEvent?.Invoke(e);
        _connection.OnCapsDiscovered  += (from, info) => OnCapsDiscovered?.Invoke(from, info);
        _connection.OnStateChanged    += (o, n)       => OnStateChanged?.Invoke(o, n);
        _connection.OnRawXml          += xml          => OnRawXml?.Invoke(xml);

        _connection.OnError += msg =>
        {
            OnError?.Invoke(msg);
        };

        _connection.OnSpoofingAttempt += msg =>
        {
            _logger.LogWarning("Spoofing-Versuch abgewehrt: {Details}", msg);
            OnSpoofingAttempt?.Invoke(msg);
        };

        _connection.OnStanzaError += (from, error) =>
        {
            _logger.LogInformation("Stanza abgelehnt von {From}: {Error}", from ?? "(Server)", error);
            OnStanzaError?.Invoke(from, error);
        };

        _connection.OnStreamError += error =>
        {
            _logger.LogWarning("Stream-Fehler: {Error} (wiederholbar: {Recoverable})",
                               error, error.IsRecoverable);
            OnStreamError?.Invoke(error);
        };

        _connection.Roster.OnItemAdded   += item => OnRosterItemAdded?.Invoke(item);
        _connection.Roster.OnItemRemoved += jid  => OnRosterItemRemoved?.Invoke(jid);

        _connection.Roster.OnSubscriptionRequest += (from, status) =>
        {
            var bare = JidUtilities.Bare(from);

            lock (_pendingLock)
            {
                if (!_pendingSubscriptions.Contains(bare, StringComparer.OrdinalIgnoreCase))
                    _pendingSubscriptions.Add(bare);
            }

            _logger.LogInformation("Kontaktanfrage von {From}", bare);
            OnSubscriptionRequest?.Invoke(bare, status);
        };

    }

    #endregion

    #region Verbindung

    public Task ConnectAsync(CancellationToken ct = default)
        => _connection.ConnectAsync(ct);

    /// <summary>
    /// Reisst die Verbindung ohne Close-Handshake ab - simuliert einen
    /// Netzwerkausfall und löst den Reconnect aus.
    /// </summary>
    public void KillConnection()
        => _connection.KillConnection();

    public Task DisconnectAsync()
        => _connection.DisconnectAsync();

    /// <summary>
    /// Trennt eine bestehende Verbindung und baut sie neu auf.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            await _connection.DisconnectAsync();

        await _connection.ConnectAsync(ct);
    }

    /// <summary>
    /// XEP-0199: Misst die Round-Trip-Zeit zum Server oder zu einem JID.
    /// </summary>
    public Task<TimeSpan?> PingAsync(string? to = null, CancellationToken ct = default)
        => _connection.PingAsync(to, ct);

    /// <summary>
    /// XEP-0198: Fordert eine Empfangsbestätigung vom Server an.
    /// </summary>
    public Task RequestAckAsync()
        => _connection.RequestAckAsync();

    #endregion

    #region Chatpartner und Nachrichten

    /// <summary>
    /// Setzt den aktuellen Chatpartner. null beendet den Chat ohne
    /// &lt;gone/&gt; zu senden - dafür <see cref="LeaveChatAsync"/> nutzen.
    /// </summary>
    public void SetChatPartner(string? jid)
    {
        var normalized = string.IsNullOrWhiteSpace(jid) ? null : jid.Trim();

        if (string.Equals(CurrentChatPartner, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        CurrentChatPartner = normalized;
        _logger.LogDebug("Chatpartner: {Partner}", normalized ?? "(keiner)");
        OnChatPartnerChanged?.Invoke(normalized);
    }

    /// <summary>
    /// XEP-0085: Sendet &lt;gone/&gt; an den aktuellen Chatpartner und
    /// beendet den Chat.
    /// </summary>
    /// <returns>Der verlassene Chatpartner, oder null wenn keiner aktiv war.</returns>
    public async Task<string?> LeaveChatAsync()
    {
        var partner = CurrentChatPartner;
        if (partner == null)
            return null;

        await _connection.SendChatStateAsync(partner, ChatState.Gone);
        SetChatPartner(null);

        return partner;
    }

    /// <summary>
    /// Sendet eine Nachricht an den aktuellen Chatpartner.
    /// </summary>
    /// <returns>Die Nachrichten-ID, oder null wenn kein Chatpartner gesetzt ist.</returns>
    public async Task<string?> SendMessageAsync(string body)
    {
        var partner = CurrentChatPartner;
        if (partner == null)
            return null;

        return await SendMessageAsync(partner, body);
    }

    /// <summary>
    /// Sendet eine Nachricht an einen beliebigen JID, ohne den aktuellen
    /// Chatpartner zu ändern.
    /// </summary>
    public async Task<string> SendMessageAsync(string to, string body,
                                               MessageType type = MessageType.Chat)
    {

        var id = await _connection.SendMessageAsync(to, body, type: type);

        // Für eine spätere Korrektur (XEP-0308). Gemerkt wird auch das, was
        // nie berichtigt wird - der Preis ist ein Eintrag je Gesprächspartner.
        lock (_lastSentTo)
            _lastSentTo[JidUtilities.Bare(to)] = id;

        return id;

    }

    /// <summary>
    /// XEP-0308: Berichtigt die zuletzt an diesen Empfänger geschickte
    /// Nachricht.
    /// </summary>
    /// <param name="to">Der Empfänger; ohne Angabe der aktuelle Chatpartner.</param>
    /// <param name="body">Der vollständige neue Text.</param>
    /// <returns>
    /// Die ID der Korrektur, oder null - dann gibt es nichts zu berichtigen:
    /// kein Empfänger, oder an diesen ist in dieser Sitzung noch nichts
    /// hinausgegangen.
    /// </returns>
    /// <remarks>
    /// Berichtigt wird ausschliesslich die <b>letzte</b> Nachricht an diesen
    /// Empfänger (Abschnitt 5) - und die Korrektur wird selbst zur letzten,
    /// sodass sich eine Berichtigung wiederum berichtigen lässt. Das ist keine
    /// Spitzfindigkeit, sondern der übliche Fall: Wer sich vertippt, vertippt
    /// sich auch in der Berichtigung.
    /// </remarks>
    public async Task<string?> CorrectLastMessageAsync(string body, string? to = null)
    {

        var empfaenger = to ?? CurrentChatPartner;

        if (empfaenger is null)
            return null;

        var bare = JidUtilities.Bare(empfaenger);

        string? vorherige;

        lock (_lastSentTo)
            if (!_lastSentTo.TryGetValue(bare, out vorherige))
                return null;

        var id = await _connection.SendMessageAsync(empfaenger, body, corrects: vorherige);

        lock (_lastSentTo)
            _lastSentTo[bare] = id;

        return id;

    }

    /// <summary>
    /// XEP-0085: Sendet einen Tippstatus an den aktuellen Chatpartner.
    /// </summary>
    /// <returns>false, wenn kein Chatpartner gesetzt ist.</returns>
    public async Task<bool> SendChatStateAsync(ChatState state)
    {
        var partner = CurrentChatPartner;
        if (partner == null)
            return false;

        await _connection.SendChatStateAsync(partner, state);
        return true;
    }

    /// <summary>
    /// XEP-0333: Sendet einen Chat Marker an den aktuellen Chatpartner.
    /// Ohne <paramref name="messageId"/> wird
    /// <see cref="LastReceivedMessageId"/> verwendet.
    /// </summary>
    /// <returns>Die markierte Nachrichten-ID, oder null wenn kein
    /// Chatpartner gesetzt ist oder keine ID bekannt ist.</returns>
    public async Task<string?> SendMarkerAsync(ChatMarkerType type, string? messageId = null)
    {
        var partner = CurrentChatPartner;
        if (partner == null)
            return null;

        var id = messageId ?? LastReceivedMessageId;
        if (string.IsNullOrEmpty(id))
            return null;

        await _connection.SendChatMarkerAsync(partner, id, type);
        return id;
    }

    /// <summary>
    /// Sendet rohes XML - für Protokollexperimente.
    /// </summary>
    public Task SendRawAsync(string xml)
        => _connection.SendRawAsync(xml);

    #endregion

    #region Presence

    /// <summary>
    /// Prüft, ob ein &lt;show/&gt;-Wert nach RFC 6121 gültig ist.
    /// </summary>
    public static bool IsValidShow(string? show)
        => string.IsNullOrEmpty(show) ||
           ValidShowValues.Contains(show, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Setzt die eigene Presence.
    /// </summary>
    /// <exception cref="ArgumentException">Bei ungültigem show-Wert.</exception>
    public Task SetPresenceAsync(string? show = null, string? status = null)
    {
        if (!IsValidShow(show))
            throw new ArgumentException(
                $"Ungültiger show-Wert '{show}'. Erlaubt: {string.Join(", ", ValidShowValues)}",
                nameof(show));

        // "available" ist die Abwesenheit von <show/>
        var effectiveShow = string.Equals(show, "available", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : show;

        return _connection.SendPresenceAsync(effectiveShow, status);
    }

    #endregion

    #region Roster und Kontaktanfragen

    public Task AddContactAsync(string jid, string? name = null, IEnumerable<string>? groups = null)
        => _connection.AddContactAsync(jid.Trim(), name, groups);

    public Task RemoveContactAsync(string jid)
        => _connection.RemoveContactAsync(jid.Trim());

    /// <summary>
    /// Kündigt das eigene Abonnement auf die Presence eines Kontakts
    /// (RFC 6121, Abschnitt 3.3).
    /// </summary>
    /// <remarks>
    /// Ohne Warteliste und ohne Gegenanfrage, anders als
    /// <see cref="AcceptSubscriptionAsync"/> und
    /// <see cref="DenySubscriptionAsync"/>: Hier ist nichts offen, das
    /// abzuarbeiten wäre. Der Kontakt bleibt im Roster stehen - wer ihn ganz
    /// loswerden will, nimmt <see cref="RemoveContactAsync"/>.
    /// </remarks>
    public Task CancelSubscriptionAsync(string jid)
        => _connection.CancelSubscriptionAsync(jid.Trim());

    /// <summary>
    /// XEP-0352: Sieht gerade ein Mensch hin?
    /// </summary>
    public bool IsActive => _connection.ClientIsActive;

    /// <summary>
    /// XEP-0352: Hat der Server Client State Indication angekündigt?
    /// </summary>
    public bool SupportsClientStateIndication => _connection.SupportsClientStateIndication;

    /// <summary>
    /// XEP-0352: Meldet dem Server, ob gerade ein Mensch hinsieht - inaktiv
    /// heisst, dass er zurückhalten darf, was warten kann.
    /// </summary>
    /// <returns>false, wenn der Server die Erweiterung nicht angekündigt hat.</returns>
    /// <remarks>
    /// Was zurückgehalten wird, entscheidet der Server. Nachrichten mit Text
    /// gehören ausdrücklich nicht dazu - dies ist eine Sparmassnahme für den
    /// Akku und keine Ruhefunktion für den Menschen davor.
    /// </remarks>
    public Task<bool> SetActiveAsync(bool active)
        => _connection.SetClientStateAsync(active);

    /// <summary>
    /// Nimmt eine Kontaktanfrage an: bestätigt die Subscription, stellt eine
    /// Gegenanfrage für beidseitige Sichtbarkeit und räumt die Warteliste auf.
    /// </summary>
    /// <param name="jid">Der Antragsteller; ohne Angabe die älteste offene Anfrage.</param>
    /// <returns>Der bearbeitete JID, oder null wenn keine Anfrage offen war.</returns>
    public async Task<string?> AcceptSubscriptionAsync(string? jid = null)
    {
        var target = ResolvePendingSubscription(jid);
        if (target == null)
            return null;

        await _connection.AcceptSubscriptionAsync(target);

        // Gegenanfrage, damit die Subscription beidseitig wird
        await _connection.AddContactAsync(target);

        RemovePendingSubscription(target);
        _logger.LogInformation("Kontaktanfrage von {Jid} angenommen", target);

        return target;
    }

    /// <summary>
    /// Lässt einen Kontakt im Voraus zu: stellt er künftig eine Anfrage,
    /// beantwortet der Server sie selbst (RFC 6121, Abschnitt 3.4).
    /// </summary>
    /// <param name="jid">Der Kontakt, der zugelassen werden soll.</param>
    /// <returns>
    /// false, wenn der Server Pre-Approval nicht angekündigt hat - dann
    /// <b>darf</b> es nach Abschnitt 3.4.1 gar nicht erst versucht werden.
    /// </returns>
    /// <remarks>
    /// Bewusst nicht über <see cref="AcceptSubscriptionAsync"/>: das nimmt
    /// eine <i>offene</i> Anfrage an und stellt eine Gegenanfrage, damit die
    /// Sichtbarkeit beidseitig wird. Eine Vormerkung tut beides nicht - es
    /// gibt nichts anzunehmen, und wer im Voraus zulässt, hat damit noch nicht
    /// gesagt, dass er den anderen auch selbst sehen will.
    /// </remarks>
    public async Task<bool> PreApproveContactAsync(string jid)
    {

        if (!ServerSupportsPreApproval)
        {
            _logger.LogWarning("Der Server kündigt kein Pre-Approval an - {Jid} wird nicht vorgemerkt", jid);
            return false;
        }

        await _connection.AcceptSubscriptionAsync(jid);

        _logger.LogInformation("Kontakt {Jid} im Voraus zugelassen", jid);

        return true;

    }

    /// <summary>
    /// Hat der Server Subscription-Pre-Approval angekündigt (RFC 6121,
    /// Abschnitt 3.4)?
    /// </summary>
    public bool ServerSupportsPreApproval
        => _connection.ServerFeatures.Contains("urn:xmpp:features:pre-approval");

    /// <summary>
    /// Lehnt eine Kontaktanfrage ab.
    /// </summary>
    /// <param name="jid">Der Antragsteller; ohne Angabe die älteste offene Anfrage.</param>
    /// <returns>Der bearbeitete JID, oder null wenn keine Anfrage offen war.</returns>
    public async Task<string?> DenySubscriptionAsync(string? jid = null)
    {
        var target = ResolvePendingSubscription(jid);
        if (target == null)
            return null;

        await _connection.DenySubscriptionAsync(target);

        RemovePendingSubscription(target);
        _logger.LogInformation("Kontaktanfrage von {Jid} abgelehnt", target);

        return target;
    }

    private string? ResolvePendingSubscription(string? jid)
    {
        if (!string.IsNullOrWhiteSpace(jid))
            return jid.Trim();

        lock (_pendingLock)
            return _pendingSubscriptions.Count > 0 ? _pendingSubscriptions[0] : null;
    }

    private void RemovePendingSubscription(string jid)
    {
        lock (_pendingLock)
            _pendingSubscriptions.RemoveAll(p => string.Equals(p, jid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Kontakte, optional gefiltert nach JID, Anzeigename oder Gruppe.
    /// </summary>
    public IReadOnlyCollection<RosterItem> GetContacts(string? filter = null)
    {
        var items = _connection.Roster.Items;

        if (string.IsNullOrWhiteSpace(filter))
            return items;

        return items.Where(i =>
            i.Jid.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (i.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            i.Groups.Any(g => g.Contains(filter, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    public IEnumerable<RosterItem> GetOnlineContacts() => _connection.Roster.GetOnlineContacts();
    public IEnumerable<string> GetGroups() => _connection.Roster.GetGroups();
    public IEnumerable<RosterItem> GetContactsByGroup(string group) => _connection.Roster.GetByGroup(group);
    public RosterItem? GetContact(string jid) => _connection.Roster.GetItem(jid.Trim());

    #endregion

    #region Service Discovery

    /// <summary>
    /// XEP-0030: Fragt die Features einer Gegenstelle ab.
    /// </summary>
    public Task<DiscoInfo?> DiscoverInfoAsync(string jid, CancellationToken ct = default)
        => _connection.DiscoverInfoAsync(jid, ct);

    /// <summary>
    /// XEP-0030: Fragt die Items/Services einer Gegenstelle ab.
    /// </summary>
    public Task<DiscoItems?> DiscoverItemsAsync(string jid, CancellationToken ct = default)
        => _connection.DiscoverItemsAsync(jid, ct);

    /// <summary>
    /// XEP-0030: Fragt die Features des eigenen Servers ab.
    /// </summary>
    public Task<DiscoInfo?> DiscoverServerInfoAsync(CancellationToken ct = default)
        => _connection.DiscoverInfoAsync(_connection.Domain, ct);

    #endregion

    #region PubSub (XEP-0060)

    public Task PubSubSubscribeAsync(string nodeId, string? service = null)
        => _connection.PubSubSubscribeAsync(nodeId, service);

    public Task PubSubUnsubscribeAsync(string nodeId, string? service = null)
        => _connection.PubSubUnsubscribeAsync(nodeId, service);

    public Task PubSubPublishAsync(string nodeId, string itemId, string payload, string? service = null)
        => _connection.PubSubPublishAsync(nodeId, itemId, payload, service);

    public Task PubSubCreateNodeAsync(string nodeId, string? service = null)
        => _connection.PubSubCreateNodeAsync(nodeId, service);

    public Task PubSubDeleteNodeAsync(string nodeId, string? service = null)
        => _connection.PubSubDeleteNodeAsync(nodeId, service);

    public Task PubSubGetItemsAsync(string nodeId, int? maxItems = null, string? service = null)
        => _connection.PubSubGetItemsAsync(nodeId, maxItems, service);

    #endregion

    public ValueTask DisposeAsync()
        => _connection.DisposeAsync();

}
