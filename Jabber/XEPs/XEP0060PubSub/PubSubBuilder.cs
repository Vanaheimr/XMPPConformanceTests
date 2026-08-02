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
    /// XEP-0060, Abschnitt 5.6: Die eigenen Abonnements abfragen.
    /// </summary>
    /// <param name="nodeId">
    /// Auf welchen Knoten eingeschränkt, oder null für alle.
    /// </param>
    public static string GetSubscriptions(string pubsubJid, string id = "pubsub-subs", string? nodeId = null)
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               "<subscriptions" +
               (nodeId is not null ? $" node='{XmlEscaping.Escape(nodeId)}'" : "") +
               "/></pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 5.7: Die eigenen Rollen abfragen.
    /// </summary>
    public static string GetAffiliations(string pubsubJid, string id = "pubsub-affs")
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'><affiliations/></pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.9.1: Die Rollen an einem eigenen Knoten abfragen.
    /// </summary>
    public static string GetNodeAffiliations(string pubsubJid, string nodeId, string id = "pubsub-nodeaffs")
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<affiliations node='{XmlEscaping.Escape(nodeId)}'/>" +
               "</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.9.2: Eine Rolle setzen.
    /// </summary>
    public static string SetAffiliation(string pubsubJid, string nodeId, string id, string jid, string affiliation)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<affiliations node='{XmlEscaping.Escape(nodeId)}'>" +
               $"<affiliation jid='{XmlEscaping.Escape(jid)}' affiliation='{affiliation}'/>" +
               "</affiliations></pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.8.1: Die Abonnenten eines eigenen Knotens
    /// abfragen.
    /// </summary>
    /// <remarks>
    /// Sieht aus wie die Sammelabfrage aus Abschnitt 5.6 und fragt das
    /// Gegenteil: nicht „wo hänge ich überall", sondern „wer hängt an meinem
    /// Knoten". Zu unterscheiden sind die beiden allein am Namensraum.
    /// </remarks>
    public static string GetNodeSubscriptions(string pubsubJid, string nodeId, string id = "pubsub-nodesubs")
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<subscriptions node='{XmlEscaping.Escape(nodeId)}'/>" +
               "</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.8.2: Ein Abonnement des eigenen Knotens beenden.
    /// </summary>
    /// <param name="subId">
    /// Ein bestimmtes Abonnement, oder null für alle dieses JIDs an diesem
    /// Knoten.
    /// </param>
    /// <remarks>
    /// <b>Nur beenden und nicht anmelden</b>, obwohl derselbe Abschnitt auch
    /// das zulässt. Ein Client, der einen anderen ungefragt anmelden kann,
    /// braucht dafür keinen Namen in dieser Datei: Wer das will, sagt, was er
    /// tut. Und der Testserver dieses Projekts weist es ohnehin ab.
    /// </remarks>
    public static string RemoveSubscriber(string pubsubJid, string nodeId, string id, string jid, string? subId = null)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<subscriptions node='{XmlEscaping.Escape(nodeId)}'>" +
               $"<subscription jid='{XmlEscaping.Escape(jid)}' subscription='none'" +
               (subId is not null ? $" subid='{XmlEscaping.Escape(subId)}'" : "") +
               "/></subscriptions></pubsub></iq>";
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
    /// XEP-0060, Abschnitt 7.2: Einen einzelnen Eintrag zurücknehmen.
    /// </summary>
    /// <remarks>
    /// Im gewöhnlichen Namensraum und nicht in dem des Eigentümers:
    /// Zurücknehmen darf, wer auch veröffentlichen darf. Und mit Kennung -
    /// „nimm irgendetwas zurück" gibt es nicht, dafür ist das Leeren da.
    /// </remarks>
    public static string Retract(string pubsubJid, string nodeId, string itemId, string id = "pubsub-retract")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<retract node='{XmlEscaping.Escape(nodeId)}'>" +
               $"<item id='{XmlEscaping.Escape(itemId)}'/>" +
               "</retract></pubsub></iq>";
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

    /// <summary>Der Namensraum der Eigentümer-Anfragen (XEP-0060, Abschnitt 8).</summary>
    public const string OwnerNamespace = "http://jabber.org/protocol/pubsub#owner";

    /// <summary>
    /// Create a new node
    /// </summary>
    /// <param name="configuration">
    /// Das abgeschickte Knotenformular als fertiges XML, oder null. Anlegen
    /// und einstellen in einem Zug (XEP-0060, Abschnitt 8.1.3): Zwei Schritte
    /// hätten eine Lücke, in der der Knoten offen steht.
    /// </param>
    public static string CreateNode(string pubsubJid, string nodeId, string id = "pubsub-create", string? configuration = null)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<create node='{XmlEscaping.Escape(nodeId)}'/>" +
               (configuration is not null ? $"<configure>{configuration}</configure>" : "") +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.2.1: Die Einstellungen eines Knotens abfragen.
    /// </summary>
    public static string GetNodeConfig(string pubsubJid, string nodeId, string id = "pubsub-cfg")
    {
        return $"<iq type='get' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<configure node='{XmlEscaping.Escape(nodeId)}'/>" +
               "</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.2.4: Die Einstellungen eines Knotens setzen.
    /// </summary>
    public static string SetNodeConfig(string pubsubJid, string nodeId, string id, string form)
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<configure node='{XmlEscaping.Escape(nodeId)}'>{form}</configure>" +
               "</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.4: Einen Knoten löschen.
    /// </summary>
    public static string DeleteNode(string pubsubJid, string nodeId, string id = "pubsub-delete")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<delete node='{XmlEscaping.Escape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }

    /// <summary>
    /// XEP-0060, Abschnitt 8.5: Einen Knoten leeren.
    /// </summary>
    /// <remarks>
    /// Sieht dem Löschen zum Verwechseln ähnlich und meint etwas anderes: Der
    /// Knoten bleibt, seine Abonnenten bleiben, nur der Inhalt geht.
    /// </remarks>
    public static string PurgeNode(string pubsubJid, string nodeId, string id = "pubsub-purge")
    {
        return $"<iq type='set' to='{XmlEscaping.Escape(pubsubJid)}' id='{id}'>" +
               $"<pubsub xmlns='{OwnerNamespace}'>" +
               $"<purge node='{XmlEscaping.Escape(nodeId)}'/>" +
               $"</pubsub></iq>";
    }
}
