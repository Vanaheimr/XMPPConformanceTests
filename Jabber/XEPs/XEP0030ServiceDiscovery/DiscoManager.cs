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
using System.Text.RegularExpressions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0030: Service Discovery - fragt Features anderer Entities ab und
/// beantwortet eingehende disco#info-Anfragen.
/// </summary>
public sealed class DiscoManager
{
    private readonly Func<string, Task> _sendStanza;
    private readonly Dictionary<string, TaskCompletionSource<DiscoInfo>> _infoQueries = new();
    private readonly Dictionary<string, TaskCompletionSource<DiscoItems>> _itemsQueries = new();
    private readonly object _lock = new();
    private int _counter;

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
        var tcs = new TaskCompletionSource<DiscoInfo>();

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
        var tcs = new TaskCompletionSource<DiscoItems>();

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
    /// Verarbeitet eine disco#info Antwort
    /// </summary>
    public bool ProcessInfoResult(string id, string xml, string from)
    {
        TaskCompletionSource<DiscoInfo>? tcs;
        lock (_lock)
        {
            if (!_infoQueries.TryGetValue(id, out tcs))
                return false;
            _infoQueries.Remove(id);
        }

        var info = new DiscoInfo { From = from };

        // Parse identities
        foreach (Match m in Regex.Matches(xml, @"<identity([^/>]+)/?>" ))
        {
            var attrs = m.Groups[1].Value;
            info.Identities.Add(new DiscoIdentity(
                ExtractAttr(attrs, "category") ?? "",
                ExtractAttr(attrs, "type") ?? "",
                ExtractAttr(attrs, "name")
            ));
        }

        // Parse features
        foreach (Match m in Regex.Matches(xml, @"<feature\s+var=['""]([^'""]+)['""]"))
        {
            info.Features.Add(m.Groups[1].Value);
        }

        tcs.TrySetResult(info);
        return true;
    }

    /// <summary>
    /// Verarbeitet eine disco#items Antwort
    /// </summary>
    public bool ProcessItemsResult(string id, string xml, string from)
    {
        TaskCompletionSource<DiscoItems>? tcs;
        lock (_lock)
        {
            if (!_itemsQueries.TryGetValue(id, out tcs))
                return false;
            _itemsQueries.Remove(id);
        }

        var items = new DiscoItems { From = from };

        foreach (Match m in Regex.Matches(xml, @"<item([^/>]+)/?>" ))
        {
            var attrs = m.Groups[1].Value;
            var jid = ExtractAttr(attrs, "jid");
            if (jid != null)
            {
                items.Items.Add(new DiscoItem(jid, ExtractAttr(attrs, "node"), ExtractAttr(attrs, "name")));
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

    private static string? ExtractAttr(string attrs, string name)
    {
        var match = Regex.Match(attrs, $@"{name}\s*=\s*['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }
}
