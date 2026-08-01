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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0060: Baut PubSub-IQ-Stanzas.
/// </summary>
public static class PubSubBuilder
{
    /// <summary>
    /// Subscribe to a node
    /// </summary>
    public static string Subscribe(string pubsubJid, string nodeId, string myJid, string id = "pubsub-sub")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<subscribe node='{XmlEscaping.Escape(nodeId)}' jid='{XmlEscaping.Escape(myJid)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Unsubscribe from a node
    /// </summary>
    /// <param name="subId">
    /// Die Kennung des Abonnements aus der Zusage des Dienstes, oder null.
    /// Vorgeschrieben, sobald ein JID mehrere Abonnements auf denselben Knoten
    /// hält (XEP-0060, Abschnitt 6.2.3.1).
    /// </param>
    public static string Unsubscribe(string pubsubJid, string nodeId, string myJid, string id = "pubsub-unsub", string? subId = null)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<unsubscribe node='{XmlEscaping.Escape(nodeId)}' jid='{XmlEscaping.Escape(myJid)}'" +
               (subId is not null ? $" subid='{XmlEscaping.Escape(subId)}'" : "") +
               "/></pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 6.3.1: Die Einstellungen eines Abonnements abfragen.
    /// </summary>
    public static string GetOptions(string pubsubJid, string nodeId, string myJid, string id = "pubsub-opts", string? subId = null)
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<options node='{XmlEscaping.Escape(nodeId)}' jid='{XmlEscaping.Escape(myJid)}'" +
               (subId is not null ? $" subid='{XmlEscaping.Escape(subId)}'" : "") +
               "/></pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 6.3.5: Die Einstellungen eines Abonnements setzen.
    /// </summary>
    /// <param name="form">
    /// Das abgeschickte Datenformular als fertiges XML - es wird wie eine
    /// Nutzlast durchgereicht und nicht escaped.
    /// </param>
    public static string SetOptions(string pubsubJid, string nodeId, string myJid, string id, string? subId, string form)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<options node='{XmlEscaping.Escape(nodeId)}' jid='{XmlEscaping.Escape(myJid)}'" +
               (subId is not null ? $" subid='{XmlEscaping.Escape(subId)}'" : "") +
               $">{form}</options></pubsub></iq>";
    }

    /// <summary>
    /// Publish an item to a node
    /// </summary>
    /// <remarks>
    /// <paramref name="payload"/> wird bewusst NICHT escaped - es ist rohes
    /// XML. Aufrufer müssen sicherstellen, dass es wohlgeformt ist.
    /// </remarks>
    public static string Publish(string pubsubJid, string nodeId, string itemId, string payload, string id = "pubsub-pub")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<publish node='{XmlEscaping.Escape(nodeId)}'>" +
               $"<item id='{XmlEscaping.Escape(itemId)}'>{payload}</item>" +
               $"</publish></pubsub></iq>";
    }

    /// <summary>
    /// Get items from a node
    /// </summary>
    public static string GetItems(string pubsubJid, string nodeId, int? maxItems = null, string id = "pubsub-get")
    {
        var maxAttr = maxItems.HasValue ? $" max_items='{maxItems}'" : "";
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<items node='{XmlEscaping.Escape(nodeId)}'{maxAttr}/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Create a new node
    /// </summary>
    public static string CreateNode(string pubsubJid, string nodeId, string id = "pubsub-create")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<create node='{XmlEscaping.Escape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// Delete a node
    /// </summary>
    public static string DeleteNode(string pubsubJid, string nodeId, string id = "pubsub-delete")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub#owner'>" +
               $"<delete node='{XmlEscaping.Escape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }
}
