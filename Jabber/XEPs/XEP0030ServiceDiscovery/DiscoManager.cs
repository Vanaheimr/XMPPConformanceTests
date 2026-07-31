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
/// XEP-0030: Service Discovery - fragt Features und Untereinheiten anderer
/// Entities ab und beantwortet eingehende disco#info- und
/// disco#items-Anfragen.
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

    /// <summary>
    /// XEP-0128: Die eigenen erweiterten Angaben, die an jede
    /// disco#info-Antwort angehängt werden.
    /// </summary>
    /// <remarks>
    /// Leer als Vorgabe, und das mit Absicht. Was hier steht, erfährt jeder
    /// Kontakt, ohne zu fragen - Software, Fassung und Betriebssystem sind
    /// genau die Angaben, aus denen sich ein Gerät wiedererkennen lässt. Wer
    /// sie veröffentlichen will, tut es; von selbst geschieht es nicht.
    ///
    /// Der Inhalt geht in den Verification String nach XEP-0115 ein (siehe
    /// <see cref="EntityCapsManager"/>). Er wird also mit angekündigt, und die
    /// Gegenstelle rechnet ihn nach - ändern lässt er sich deshalb nur
    /// zusammen mit einer neuen Presence.
    /// </remarks>
    public List<DiscoForm> LocalForms { get; } = [];

    /// <summary>
    /// XEP-0030, Abschnitt 4: Die eigenen Untereinheiten, die eine
    /// disco#items-Abfrage aufzählt.
    /// </summary>
    /// <remarks>
    /// Leer als Vorgabe, denn ein Client hat keine. Genau deshalb muss die
    /// Abfrage trotzdem beantwortet werden: <c>LocalFeatures</c> kündigt
    /// <c>disco#items</c> an, und angekündigt und dann verweigert ist die eine
    /// Kombination, die es nicht geben darf.
    ///
    /// <b>„Ich habe keine" und „frag mich nicht" sind verschiedene
    /// Auskünfte.</b> Ein <c>&lt;service-unavailable/&gt;</c> sagt das Zweite;
    /// wahr ist das Erste.
    /// </remarks>
    public List<DiscoItem> LocalItems { get; } = [];

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
                                                      identity.Attr("name"),
                                                      identity.Attribute(XNamespace.Xml + "lang")?.Value));

            foreach (var feature in query.Children(InfoNamespace, "feature"))
            {
                var var = feature.Attr("var");
                if (var is not null)
                    info.Features.Add(var);
            }

            // XEP-0128: erweiterte Angaben als Datenformular. Übernommen wird,
            // was dasteht - welche Formulare für den Verification String
            // zählen und welche nach XEP-0115, Abschnitt 5.4 zu übergehen
            // sind, entscheidet der EntityCapsManager. Ein Parser, der schon
            // aussortiert, nimmt der Prüfung die Grundlage.
            foreach (var form in query.Elements()
                                      .Where(child => child.Name.NamespaceName == DataFormNamespace &&
                                                      child.Name.LocalName     == "x"))
            {

                var fields = new List<DiscoField>();

                foreach (var field in form.Elements()
                                          .Where(child => child.Name.LocalName == "field"))
                {

                    var var = field.Attr("var");

                    if (var is null)
                        continue;

                    fields.Add(new DiscoField(var,
                                              field.Attr("type"),
                                              [.. field.Elements()
                                                       .Where (v => v.Name.LocalName == "value")
                                                       .Select(v => v.Value)]));

                }

                info.Forms.Add(new DiscoForm(fields));

            }

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
            // Ohne dieses Attribut ergäbe unsere Antwort bei der Gegenstelle
            // einen anderen Hash als den, den wir ankündigen.
            if (identity.Language != null)
                sb.Append($" xml:lang='{XmlEscaping.Escape(identity.Language)}'");
            sb.Append("/>");
        }

        foreach (var feature in LocalFeatures)
        {
            sb.Append($"<feature var='{feature}'/>");
        }

        // XEP-0128: die erweiterten Angaben, falls welche hinterlegt sind.
        foreach (var form in LocalForms)
        {

            sb.Append($"<x xmlns='{DataFormNamespace}' type='result'>");

            foreach (var field in form.Fields)
            {

                sb.Append($"<field var='{XmlEscaping.Escape(field.Var)}'");

                if (field.Type is not null)
                    sb.Append($" type='{XmlEscaping.Escape(field.Type)}'");

                sb.Append('>');

                foreach (var value in field.Values)
                    sb.Append($"<value>{XmlEscaping.Escape(value)}</value>");

                sb.Append("</field>");

            }

            sb.Append("</x>");

        }

        sb.Append("</query></iq>");
        return _sendStanza(sb.ToString());
    }

    /// <summary>
    /// Beantwortet eine disco#items Anfrage mit <see cref="LocalItems"/>.
    /// </summary>
    /// <remarks>
    /// <b>Ohne <c>node</c>-Parameter, und das ist Absicht.</b> Ein Zweig, den
    /// es hier nicht gibt, wird nicht hier beantwortet, sondern gar nicht -
    /// darüber entscheidet der Aufrufer, denn eine leere Liste hiesse „diesen
    /// Zweig gibt es, er ist leer". Ein Parameter, der nie einen Wert bekommt,
    /// sähe aus wie eine Fähigkeit und wäre keine.
    /// </remarks>
    public Task RespondItemsAsync(string id, string? from)
    {

        // Ohne 'from' kam die Anfrage vom eigenen Server (RFC 6120,
        // Abschnitt 8.1.1.1); die Antwort geht dann ohne 'to' dorthin zurück.
        var toAttr = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";

        var sb = new StringBuilder();
        sb.Append($"<iq type='result' id='{XmlEscaping.Escape(id)}'{toAttr}>");
        sb.Append($"<query xmlns='{ItemsNamespace}'>");

        foreach (var item in LocalItems)
        {

            sb.Append($"<item jid='{XmlEscaping.Escape(item.Jid)}'");

            if (item.Node != null)
                sb.Append($" node='{XmlEscaping.Escape(item.Node)}'");

            if (item.Name != null)
                sb.Append($" name='{XmlEscaping.Escape(item.Name)}'");

            sb.Append("/>");

        }

        sb.Append("</query></iq>");
        return _sendStanza(sb.ToString());

    }

}
