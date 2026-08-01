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

    /// <summary>
    /// Die bestätigten Abonnements, nach dem Knoten.
    /// </summary>
    /// <remarks>
    /// <b>Bestätigt heisst: Der Dienst hat zugesagt.</b> Bis D71 stand hier
    /// eine blosse Namensmenge, und eingetragen wurde beim Absenden der
    /// Anfrage - ein abgelehntes Abonnement stand danach als bestehendes da.
    /// Was jetzt hier liegt, hat der Dienst gesagt und nicht dieser Client
    /// vermutet: die Kennung, die er vergeben hat, und die Adresse, unter der
    /// er es tat.
    /// </remarks>
    private readonly Dictionary<String, PubSubSubscription> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _pubsubService;
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public event Action<PubSubEvent>? OnEvent;

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

        var eventElement = stanza.Child(EventNamespace, "event");

        if (eventElement is null)
            return false;

        // Der Knoten zuerst, denn die Absenderprüfung braucht ihn: Erlaubt ist
        // nicht ein Absender, sondern ein Absender für einen bestimmten Knoten.
        var nodeId = NodeOf(eventElement);

        if (!IsAcceptableSource(from, nodeId, expectedPubSubJid))
        {
            _logger.LogWarning("PubSub-Spoofing erkannt! Von: {From}, Knoten: {Node}, erwartet: {Expected}",
                               from, nodeId, expectedPubSubJid);
            return false;
        }

        // Items- oder Retract-Event: beide stecken in <items node='…'/>.
        var itemsElement = eventElement.Child(EventNamespace, "items");

        if (itemsElement is not null)
        {

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

        if (eventElement.Child(EventNamespace, "purge") is not null)
        {
            OnEvent?.Invoke(new PubSubEvent(nodeId, PubSubEventType.Purge));
            return true;
        }

        if (eventElement.Child(EventNamespace, "delete") is not null)
        {
            OnEvent?.Invoke(new PubSubEvent(nodeId, PubSubEventType.Delete));
            return true;
        }

        return false;

    }

    /// <summary>
    /// Der Knoten, um den es in einem Event geht - aus <c>items</c>,
    /// <c>purge</c> oder <c>delete</c>, je nachdem, was dasteht.
    /// </summary>
    private static String NodeOf(XElement eventElement)
        => eventElement.Elements()
                       .FirstOrDefault(e => e.Name.NamespaceName == EventNamespace &&
                                            e.Name.LocalName is "items" or "purge" or "delete")
                      ?.Attr("node") ?? "";

    /// <summary>
    /// Darf von diesem Absender eine Meldung über diesen Knoten kommen?
    /// </summary>
    /// <remarks>
    /// <b>Bis D71 war die Antwort allein der konfigurierte Dienst</b> -
    /// richtig für einen PubSub-Service als eigene Komponente, falsch für PEP:
    /// Dort kommt die Meldung vom Konto selbst (XEP-0163, Abschnitt 4.3), und
    /// jede einzelne galt deshalb als Fälschung. Aufgefallen ist es nicht,
    /// weil niemand ein Abonnement hatte, dessen Meldungen jemand erwartete -
    /// OMEMO geht seinen eigenen Weg.
    ///
    /// Die zweite Erlaubnis ist deshalb <b>an den Knoten gebunden und nicht an
    /// den Absender</b>: Wer bei Bob den Wetterknoten abonniert hat, hat damit
    /// nicht erlaubt, dass Bob ihm Meldungen über jeden erdachten Knoten
    /// schickt.
    /// </remarks>
    private Boolean IsAcceptableSource(String from, String nodeId, String expectedPubSubJid)
    {

        var bareFrom = JidUtilities.Bare(from);

        if (String.Equals(bareFrom, JidUtilities.Bare(expectedPubSubJid),
                          StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SubscriptionOf(nodeId) is { } abo &&
               String.Equals(bareFrom, JidUtilities.Bare(abo.ServiceJid),
                             StringComparison.OrdinalIgnoreCase);

    }

    /// <summary>
    /// Trägt ein zugesagtes Abonnement ein.
    /// </summary>
    public void AddSubscription(PubSubSubscription subscription)
    {
        lock (_lock) _subscriptions[subscription.NodeId] = subscription;
    }

    public void RemoveSubscription(String nodeId)
    {
        lock (_lock) _subscriptions.Remove(nodeId);
    }

    public Boolean IsSubscribed(String nodeId)
    {
        lock (_lock) return _subscriptions.ContainsKey(nodeId);
    }

    /// <summary>
    /// Das Abonnement dieses Knotens, oder null.
    /// </summary>
    public PubSubSubscription? SubscriptionOf(String nodeId)
    {
        lock (_lock) return _subscriptions.TryGetValue(nodeId, out var abo) ? abo : null;
    }
}
