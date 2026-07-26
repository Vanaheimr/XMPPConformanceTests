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

using System.Xml.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0060: Verwaltet PubSub-Abonnements und verarbeitet eingehende Events.
/// </summary>
public sealed class PubSubManager
{

    /// <summary>Der Namespace der PubSub-Benachrichtigungen.</summary>
    public const string EventNamespace = "http://jabber.org/protocol/pubsub#event";

    private readonly HashSet<string> _subscribedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pubsubService;
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public event Action<PubSubEvent>? OnEvent;
    public event Action<string, bool, string?>? OnSubscriptionResult; // nodeId, success, error

    public PubSubManager(string pubsubService = "pubsub", ILogger? logger = null)
    {
        _pubsubService = pubsubService;
        _logger = logger ?? NullLogger.Instance;
    }

    public string PubSubService => _pubsubService;

    /// <summary>
    /// Verarbeitet eine eingehende PubSub-Event-Nachricht mit Spoofing-Schutz
    /// </summary>
    public bool ProcessEvent(XElement stanza, string from, string expectedPubSubJid)
    {
        var bareFrom = JidUtilities.Bare(from);
        var expectedBare = JidUtilities.Bare(expectedPubSubJid);

        // SPOOFING-SCHUTZ: Events dürfen nur vom PubSub-Service kommen
        if (!string.Equals(bareFrom, expectedBare, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PubSub-Spoofing erkannt! Von: {From}, erwartet: {Expected}",
                               from, expectedPubSubJid);
            return false;
        }

        var eventElement = stanza.Child(EventNamespace, "event");

        if (eventElement is null)
            return false;

        // Items- oder Retract-Event: beide stecken in <items node='…'/>.
        var itemsElement = eventElement.Child(EventNamespace, "items");

        if (itemsElement is not null)
        {

            var nodeId    = itemsElement.Attr("node") ?? "";
            var retracted = itemsElement.Children(EventNamespace, "retract").ToList();

            if (retracted.Count > 0)
            {

                var retractEvent = new PubSubEvent(nodeId, PubSubEventType.Retract);

                foreach (var retract in retracted)
                {
                    var retractId = retract.Attr("id");
                    if (retractId is not null)
                        retractEvent.RetractedIds.Add(retractId);
                }

                OnEvent?.Invoke(retractEvent);
                return true;

            }

            var itemsEvent = new PubSubEvent(nodeId, PubSubEventType.Items);

            foreach (var item in itemsElement.Children(EventNamespace, "item"))
            {

                var itemId = item.Attr("id");

                if (itemId is null)
                    continue;

                // Die Nutzlast bleibt als rohes XML erhalten - was darin steht,
                // ist anwendungsspezifisch. Ein <item/> ganz ohne Inhalt ist
                // zulässig; das frühere Muster verlangte ein Tag-Paar und
                // übersah selbstschliessende Items.
                itemsEvent.Items.Add(new PubSubItem(itemId,
                                                    nodeId,
                                                    string.Concat(item.Nodes())));

            }

            OnEvent?.Invoke(itemsEvent);
            return true;

        }

        if (eventElement.Child(EventNamespace, "purge") is { } purge)
        {
            OnEvent?.Invoke(new PubSubEvent(purge.Attr("node") ?? "", PubSubEventType.Purge));
            return true;
        }

        if (eventElement.Child(EventNamespace, "delete") is { } delete)
        {
            OnEvent?.Invoke(new PubSubEvent(delete.Attr("node") ?? "", PubSubEventType.Delete));
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
}
