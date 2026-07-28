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

using System.Text;
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0030: Service Discovery - fragt Features anderer Entities ab und
/// beantwortet eingehende disco#info-Anfragen.
/// </summary>
public sealed class DiscoManager
{

    /// <summary>Der Namespace von disco#info.</summary>
    public const string InfoNamespace = "http://jabber.org/protocol/disco#info";

    /// <summary>Der Namespace von disco#items.</summary>
    public const string ItemsNamespace = "http://jabber.org/protocol/disco#items";

    /// <summary>Der Namespace der Datenformulare (XEP-0004/XEP-0128).</summary>
    private const string DataFormNamespace = "jabber:x:data";

    private readonly Func<string, Task> _sendStanza;
    private readonly Dictionary<string, TaskCompletionSource<DiscoInfo?>> _infoQueries = new();
    private readonly Dictionary<string, TaskCompletionSource<DiscoItems?>> _itemsQueries = new();
    private readonly object _lock = new();
    private int _counter;

    /// <summary>
    /// Eine disco-Abfrage wurde mit einem Stanza-Fehler beantwortet. Die
    /// zugehörige Query liefert dann null - anders als bei einem Timeout ist
    /// hier aber bekannt, warum.
    /// </summary>
    public event Action<string, StanzaError>? OnQueryError;

    // Lokale Features die wir unterstützen
    public List<DiscoIdentity> LocalIdentities { get; } = [
        new("client", "console", "XMPP Console Client")
    ];

    public List<string> LocalFeatures { get; } = [
        "http://jabber.org/protocol/disco#info",
        "http://jabber.org/protocol/disco#items",
        "urn:xmpp:ping",
        "urn:xmpp:receipts",
        "urn:xmpp:carbons:2",
        "urn:xmpp:chat-markers:0",
        "http://jabber.org/protocol/chatstates",
        "http://jabber.org/protocol/caps"
    ];

    public DiscoManager(Func<string, Task> sendStanza)
    {
        _sendStanza = sendStanza;
    }

    /// <summary>
    /// Fragt disco#info ab
    /// </summary>
    public async Task<DiscoInfo?> QueryInfoAsync(string jid, string? node = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var id = $"disco-info-{Interlocked.Increment(ref _counter)}";
        var tcs = new TaskCompletionSource<DiscoInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock) _infoQueries[id] = tcs;

        var nodeAttr = node != null ? $" node='{XmlEscaping.Escape(node)}'" : "";
        await _sendStanza(
            $"<iq type='get' to='{XmlEscaping.Escape(jid)}' id='{id}'>" +
            $"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttr}/></iq>");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _infoQueries.Remove(id);
            return null;
        }
    }

    /// <summary>
    /// Fragt disco#items ab
    /// </summary>
    public async Task<DiscoItems?> QueryItemsAsync(string jid, string? node = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var id = $"disco-items-{Interlocked.Increment(ref _counter)}";
        var tcs = new TaskCompletionSource<DiscoItems?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock) _itemsQueries[id] = tcs;

        var nodeAttr = node != null ? $" node='{XmlEscaping.Escape(node)}'" : "";
        await _sendStanza(
            $"<iq type='get' to='{XmlEscaping.Escape(jid)}' id='{id}'>" +
            $"<query xmlns='http://jabber.org/protocol/disco#items'{nodeAttr}/></iq>");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _itemsQueries.Remove(id);
            return null;
        }
    }

    /// <summary>
    /// Verarbeitet einen Stanza-Fehler auf eine offene disco-Anfrage.
    ///
    /// Ohne diese Behandlung landete ein <c>iq type='error'</c> in
    /// ProcessInfoResult; dort findet der Parser mangels <c>&lt;query/&gt;</c>
    /// nichts und lieferte ein leeres, aber erfolgreiches Ergebnis - eine
    /// abgelehnte Abfrage war von einer Entity ohne Features nicht zu
    /// unterscheiden.
    /// </summary>
    public bool ProcessError(string id, StanzaError error)
    {

        TaskCompletionSource<DiscoInfo?>?   infoTcs   = null;
        TaskCompletionSource<DiscoItems?>?  itemsTcs  = null;

        lock (_lock)
        {
            if (_infoQueries.TryGetValue(id, out infoTcs))
                _infoQueries.Remove(id);

            else if (_itemsQueries.TryGetValue(id, out itemsTcs))
                _itemsQueries.Remove(id);
        }

        if (infoTcs is null && itemsTcs is null)
            return false;

        infoTcs?.TrySetResult(null);
        itemsTcs?.TrySetResult(null);

        OnQueryError?.Invoke(id, error);

        return true;

    }

    /// <summary>
    /// Verarbeitet eine disco#info Antwort.
    ///
    /// Das frühere Muster für Identitäten schloss den Schrägstrich aus
    /// (<c>&lt;identity([^/&gt;]+)/?&gt;</c>), damit es das schliessende
    /// <c>/&gt;</c> nicht mitfrisst - ein Name mit Schrägstrich liess die
    /// Identität also ganz verschwinden. Beim Feature-Muster musste
    /// <c>var</c> das erste Attribut sein, sonst fehlte das Feature in der
    /// Liste und die Gegenstelle wirkte weniger fähig, als sie ist.
    /// </summary>
    public bool ProcessInfoResult(string id, XElement iq, string from)
    {
        TaskCompletionSource<DiscoInfo?>? tcs;
        lock (_lock)
        {
            if (!_infoQueries.TryGetValue(id, out tcs))
                return false;
            _infoQueries.Remove(id);
        }

        var info  = new DiscoInfo { From = from };
        var query = iq.Child(InfoNamespace, "query");

        if (query is not null)
        {

            foreach (var identity in query.Children(InfoNamespace, "identity"))
                info.Identities.Add(new DiscoIdentity(identity.Attr("category") ?? "",
                                                      identity.Attr("type")     ?? "",
                                                      identity.Attr("name")));

            foreach (var feature in query.Children(InfoNamespace, "feature"))
            {
                var var = feature.Attr("var");
                if (var is not null)
                    info.Features.Add(var);
            }

            // XEP-0128: erweiterte Angaben als Datenformular. Ausgewertet wird
            // der Inhalt nicht - nur festgehalten, dass er da war, weil der
            // Verification String nach XEP-0115 über ihn mitgeht.
            info.HasExtendedInfo = query.Elements()
                                        .Any(child => child.Name.NamespaceName == DataFormNamespace &&
                                                      child.Name.LocalName     == "x");

        }

        tcs.TrySetResult(info);
        return true;
    }

    /// <summary>
    /// Verarbeitet eine disco#items Antwort
    /// </summary>
    public bool ProcessItemsResult(string id, XElement iq, string from)
    {
        TaskCompletionSource<DiscoItems?>? tcs;
        lock (_lock)
        {
            if (!_itemsQueries.TryGetValue(id, out tcs))
                return false;
            _itemsQueries.Remove(id);
        }

        var items = new DiscoItems { From = from };
        var query = iq.Child(ItemsNamespace, "query");

        if (query is not null)
        {
            foreach (var item in query.Children(ItemsNamespace, "item"))
            {
                var jid = item.Attr("jid");
                if (jid is not null)
                    items.Items.Add(new DiscoItem(jid, item.Attr("node"), item.Attr("name")));
            }
        }

        tcs.TrySetResult(items);
        return true;
    }

    /// <summary>
    /// Beantwortet eine disco#info Anfrage
    /// </summary>
    public Task RespondInfoAsync(string id, string? from, string? node = null)
    {
        // Ohne 'from' kam die Anfrage vom eigenen Server (RFC 6120,
        // Abschnitt 8.1.1.1); die Antwort geht dann ohne 'to' dorthin zurück.
        var toAttr = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";

        var sb = new StringBuilder();
        sb.Append($"<iq type='result' id='{XmlEscaping.Escape(id)}'{toAttr}>");

        var nodeAttr = node != null ? $" node='{XmlEscaping.Escape(node)}'" : "";
        sb.Append($"<query xmlns='http://jabber.org/protocol/disco#info'{nodeAttr}>");

        foreach (var identity in LocalIdentities)
        {
            sb.Append($"<identity category='{identity.Category}' type='{identity.Type}'");
            if (identity.Name != null)
                sb.Append($" name='{XmlEscaping.Escape(identity.Name)}'");
            sb.Append("/>");
        }

        foreach (var feature in LocalFeatures)
        {
            sb.Append($"<feature var='{feature}'/>");
        }

        sb.Append("</query></iq>");
        return _sendStanza(sb.ToString());
    }

}
