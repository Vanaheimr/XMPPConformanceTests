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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0060, Abschnitte 6.1 und 6.2: Abonnieren und Abbestellen eines
    /// PEP-Knotens - die Zusage, auf die ein Client warten kann.
    /// </summary>
    /// <remarks>
    /// <b>Bis hierher sagte der Testserver zu jedem <c>subscribe</c>
    /// <c>&lt;service-unavailable/&gt;</c></b>, weil er die Anfrage gar nicht
    /// kannte. Das ist keine gute Grundlage für einen Client, der lernen soll,
    /// Antworten auszuwerten: Wer nur Absagen kennt, kann nicht zeigen, dass er
    /// eine Zusage richtig liest.
    ///
    /// Und ein Abonnement, das nirgends wirkt, wäre eine Zusage ohne Deckung -
    /// derselbe Fehler, für den in D57 ein nie ausgelöstes Ereignis gestrichen
    /// wurde. Deshalb prüft diese Sammlung nicht nur die Antwort, sondern die
    /// Wirkung: <b>Wer abonniert hat, bekommt die nächste Veröffentlichung -
    /// auch ohne Presence-Berechtigung.</b> Genau darin unterscheidet sich ein
    /// Abonnement von dem, was dieser Server vorher konnte.
    /// </remarks>
    [TestFixture]
    public class PepSubscriptionTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private const String PubSubNamespace = "http://jabber.org/protocol/pubsub";
        private const String ErrorNamespace  = "http://jabber.org/protocol/pubsub#errors";
        private const String Node            = "urn:example:wetter";

        /// <summary>
        /// Schickt ein IQ und gibt die Antwort mit derselben Kennung zurück.
        /// </summary>
        /// <remarks>
        /// Über <see cref="XMPPClient.OnRawXml"/> und nicht über den Client:
        /// Was hier geprüft wird, ist die Antwort des <i>Servers</i>. Ginge sie
        /// durch die Auswertung des Clients, prüfte der Test am Ende beide
        /// zugleich - und ein Fehler wäre nicht mehr zuzuordnen.
        /// </remarks>
        private static async Task<XElement> AskAsync(XMPPClient client, String id, String iq)
        {

            var antworten = new List<String>();

            void Sammeln(String xml)
            {
                if (xml.StartsWith("<<< ", StringComparison.Ordinal) &&
                    xml.Contains($"id='{id}'", StringComparison.Ordinal))
                {
                    lock (antworten)
                        antworten.Add(xml[4..]);
                }
            }

            client.Connection.OnRawXml += Sammeln;

            try
            {

                await client.SendRawAsync(iq);

                await WaitFor(() => { lock (antworten) return antworten.Count > 0; },
                              $"die Antwort auf '{id}'");

                lock (antworten)
                    return XElement.Parse(antworten[0]);

            }
            finally
            {
                client.Connection.OnRawXml -= Sammeln;
            }

        }

        /// <summary>
        /// Sammelt die PubSub-Benachrichtigungen, die bei einem Client
        /// eintreffen.
        /// </summary>
        private static List<String> CollectEvents(XMPPClient client)
        {

            var ereignisse = new List<String>();

            client.Connection.OnRawXml += xml =>
            {
                if (xml.StartsWith("<<< ", StringComparison.Ordinal) &&
                    xml.Contains(PubSubManager.EventNamespace, StringComparison.Ordinal))
                {
                    lock (ereignisse)
                        ereignisse.Add(xml[4..]);
                }
            };

            return ereignisse;

        }

        private static Int32 Count(List<String> ereignisse)
        {
            lock (ereignisse)
                return ereignisse.Count;
        }

        private static String PublishIq(String id, String node, String itemId, String payload)

            => $"<iq type='set' id='{id}'>" +
               $"<pubsub xmlns='{PubSubNamespace}'>" +
               $"<publish node='{node}'><item id='{itemId}'>{payload}</item></publish>" +
               "</pubsub></iq>";

        /// <summary>Der Fehlerzustand einer Antwort, oder null.</summary>
        private static String? ConditionOf(XElement antwort)
            => antwort.Elements().FirstOrDefault(e => e.Name.LocalName == "error")
                     ?.Elements().FirstOrDefault(e => e.Name.NamespaceName ==
                                                      "urn:ietf:params:xml:ns:xmpp-stanzas")
                     ?.Name.LocalName;

        /// <summary>
        /// Die Schwere des Fehlers: modify heisst „so nicht, aber vielleicht
        /// anders", cancel heisst „gar nicht" (RFC 6120, Abschnitt 8.3.2).
        /// </summary>
        private static String? ErrorTypeOf(XElement antwort)
            => antwort.Elements().FirstOrDefault(e => e.Name.LocalName == "error")?.Attr("type");

        /// <summary>Der PubSub-eigene Fehlerzustand einer Antwort, oder null.</summary>
        private static String? PubSubConditionOf(XElement antwort)
            => antwort.Elements().FirstOrDefault(e => e.Name.LocalName == "error")
                     ?.Elements().FirstOrDefault(e => e.Name.NamespaceName == ErrorNamespace)
                     ?.Name.LocalName;

        private static XElement? SubscriptionOf(XElement antwort)
            => antwort.Child(PubSubNamespace, "pubsub")
                     ?.Child(PubSubNamespace, "subscription");

        /// <summary>
        /// Abonniert und gibt die Kennung aus der Zusage zurück.
        /// </summary>
        private async Task<String> SubscribeAsync(XMPPClient client, String id)
        {

            var zusage = await AskAsync(client, id,
                                        PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node,
                                                                client.BareJid, id));

            Assert.That(zusage.Attr("type"), Is.EqualTo("result"), $"Zusage auf '{id}'");

            var subId = SubscriptionOf(zusage)?.Attr("subid");

            Assert.That(subId, Is.Not.Null.And.Not.Empty, $"subid in der Zusage auf '{id}'");

            return subId!;

        }

        /// <summary>
        /// Die Abonnementkennungen aus den SHIM-Kopfzeilen der gesammelten
        /// Benachrichtigungen (XEP-0060, Abschnitt 12.20).
        /// </summary>
        private static List<String> SubIdsIn(List<String> ereignisse)
        {
            lock (ereignisse)
                return [.. ereignisse
                           .Select(e => XElement.Parse(e)
                                                .Child("http://jabber.org/protocol/shim", "headers")
                                               ?.Children("http://jabber.org/protocol/shim", "header")
                                                .FirstOrDefault(h => h.Attr("name") == "SubID")
                                               ?.Value)
                           .Where (s => s is not null)
                           .Select(s => s!)];
        }

        /// <summary>
        /// Ein <c>&lt;options/&gt;</c>-IQ, wahlweise mit Formular.
        /// </summary>
        private String OptionsIq(String   id,
                                 String   art,
                                 String?  subId    = null,
                                 String?  formular = null,
                                 String?  jid      = null)

            => $"<iq type='{art}' to='bob@{Server.Domain}' id='{id}'>" +
               $"<pubsub xmlns='{PubSubNamespace}'>" +
               $"<options node='{Node}' jid='{jid ?? $"alice@{Server.Domain}"}'" +
               (subId is not null ? $" subid='{subId}'" : "") +
               (formular is null ? "/>" : $">{formular}</options>") +
               "</pubsub></iq>";

        /// <summary>Ein abgeschicktes Formular mit den angegebenen Feldern.</summary>
        private static String SubmitForm(String felder, String art = "submit")
            => $"<x xmlns='jabber:x:data' type='{art}'>" +
               "<field var='FORM_TYPE' type='hidden'>" +
               "<value>http://jabber.org/protocol/pubsub#subscribe_options</value></field>" +
               felder +
               "</x>";

        private static String DeliverField(String wert)
            => $"<field var='pubsub#deliver'><value>{wert}</value></field>";

        /// <summary>Der Wert eines Formularfeldes in einer Antwort.</summary>
        private static String? FieldValue(XElement antwort, String var)
            => antwort.Child(PubSubNamespace, "pubsub")
                     ?.Child(PubSubNamespace, "options")
                     ?.Child("jabber:x:data", "x")
                     ?.Children("jabber:x:data", "field")
                      .FirstOrDefault(f => f.Attr("var") == var)
                     ?.Child("jabber:x:data", "value")
                     ?.Value;

        /// <summary>
        /// Bob veröffentlicht - den Knoten gibt es danach.
        /// </summary>
        private async Task<XMPPClient> PublishingBobAsync(String itemId = "1", String inhalt = "sonnig")
        {

            var bob = await ConnectClientAsync("bob");

            await AskAsync(bob, $"pub-{itemId}",
                           PublishIq($"pub-{itemId}", Node, itemId,
                                     $"<wetter xmlns='urn:example:x'>{inhalt}</wetter>"));

            return bob;

        }

        #endregion


        #region Subscribing_ToAPublishedNode_IsConfirmedWithASubId()

        /// <summary>
        /// XEP-0060, Abschnitt 6.1.2: Die Zusage nennt den Knoten, den
        /// Abonnenten, eine Abonnementkennung und den Zustand.
        /// </summary>
        /// <remarks>
        /// Die <c>subid</c> ist der Teil, den ein Client sich merken muss und
        /// nicht selbst ausdenken kann: Sie kommt vom Dienst. Wer die Antwort
        /// nicht liest, hat sie nie.
        /// </remarks>
        [Test]
        public async Task Subscribing_ToAPublishedNode_IsConfirmedWithASubId()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "sub-1",
                                         PubSubBuilder.Subscribe($"bob@{Server.Domain}",
                                                                 Node,
                                                                 alice.BareJid,
                                                                 "sub-1"));

            Assert.Multiple(() =>
            {

                Assert.That(antwort.Attr("type"), Is.EqualTo("result"),
                            "Ein Knoten, den es gibt, muss abonnierbar sein.");

                var abo = SubscriptionOf(antwort);

                Assert.That(abo, Is.Not.Null, "Die Zusage fehlt.");
                Assert.That(abo!.Attr("node"),         Is.EqualTo(Node));
                Assert.That(abo!.Attr("subscription"), Is.EqualTo("subscribed"));
                Assert.That(abo!.Attr("jid"),          Is.EqualTo(alice.BareJid));
                Assert.That(abo!.Attr("subid"),        Is.Not.Null.And.Not.Empty,
                            "Ohne subid kann niemand ein Abonnement benennen.");

            });

        }

        #endregion

        #region Subscribing_ToANodeThatDoesNotExist_IsRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.1.3.12: Was es nicht gibt, kann man nicht
        /// abonnieren.
        /// </summary>
        [Test]
        public async Task Subscribing_ToANodeThatDoesNotExist_IsRejected()
        {

            await ConnectClientAsync("bob");

            var alice = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "sub-2",
                                         PubSubBuilder.Subscribe($"bob@{Server.Domain}",
                                                                 "urn:example:gibtesnicht",
                                                                 alice.BareJid,
                                                                 "sub-2"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"), Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort), Is.EqualTo("item-not-found"));
            });

        }

        #endregion

        #region Subscribing_ForSomebodyElsesJid_IsRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.1.3.1: Der <c>jid</c> muss der des Absenders
        /// sein.
        /// </summary>
        /// <remarks>
        /// <b>Das ist keine Formsache.</b> Ohne diese Prüfung könnte Alice
        /// Carol anmelden, und Carol bekäme von da an Bobs Veröffentlichungen,
        /// ohne je etwas verlangt zu haben - eine Zustellung, die sich niemand
        /// ausgesucht hat und die Carol nicht einmal zuzuordnen wüsste.
        /// </remarks>
        [Test]
        public async Task Subscribing_ForSomebodyElsesJid_IsRejected()
        {

            await PublishingBobAsync();

            Server.AddAccount("carol");

            var alice = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "sub-3",
                                         PubSubBuilder.Subscribe($"bob@{Server.Domain}",
                                                                 Node,
                                                                 $"carol@{Server.Domain}",
                                                                 "sub-3"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("bad-request"));
                Assert.That(ErrorTypeOf(antwort),       Is.EqualTo("modify"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-jid"),
                            "XEP-0060 nennt den Grund beim Namen.");
            });

        }

        #endregion

        #region ASubscriber_GetsTheNextItem_WithoutAnyPresenceSubscription()

        /// <summary>
        /// Der Kern der Sache: Ein Abonnement bringt Benachrichtigungen -
        /// auch dem, der Bobs Presence nicht sehen darf.
        /// </summary>
        /// <remarks>
        /// Vorher bekam eine PEP-Benachrichtigung genau, wer ohnehin Presence
        /// bekam. Damit war „abonnieren" nichts als ein anderes Wort für „im
        /// Roster stehen" - und für einen fremden Knoten, den niemand über
        /// Presence erreicht, gab es überhaupt keinen Weg.
        /// </remarks>
        [Test]
        public async Task ASubscriber_GetsTheNextItem_WithoutAnyPresenceSubscription()
        {

            var bob    = await PublishingBobAsync();
            var alice  = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "sub-4",
                                         PubSubBuilder.Subscribe($"bob@{Server.Domain}",
                                                                 Node,
                                                                 alice.BareJid,
                                                                 "sub-4"));

            Assert.That(antwort.Attr("type"), Is.EqualTo("result"));

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-2",
                           PublishIq("pub-2", Node, "2", "<wetter xmlns='urn:example:x'>Regen</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0,
                          "die Benachrichtigung an den Abonnenten");

            Assert.Multiple(() =>
            {
                Assert.That(ereignisse[0], Does.Contain(Node));
                Assert.That(ereignisse[0], Does.Contain("Regen"));
                Assert.That(ereignisse[0], Does.Contain($"from='bob@{Server.Domain}'"),
                            "Die Benachrichtigung kommt vom Konto und nicht vom Server.");
            });

        }

        #endregion

        #region WithoutASubscription_NothingArrives()

        /// <summary>
        /// Die Gegenprobe zum vorigen Test: Ohne Abonnement und ohne Presence
        /// bekommt Alice nichts.
        /// </summary>
        /// <remarks>
        /// Ohne sie bewiese der vorige Test nur, dass irgendetwas ankommt -
        /// nicht, dass es am Abonnement liegt.
        /// </remarks>
        [Test]
        public async Task WithoutASubscription_NothingArrives()
        {

            var bob        = await PublishingBobAsync();
            var alice      = await ConnectClientAsync("alice");
            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-2",
                           PublishIq("pub-2", Node, "2", "<wetter xmlns='urn:example:x'>Regen</wetter>"));

            await WaitAgainst(() => Count(ereignisse) > 0,
                              "eine Benachrichtigung an einen Unbeteiligten");

        }

        #endregion

        #region Unsubscribing_StopsTheEvents()

        /// <summary>
        /// XEP-0060, Abschnitt 6.2: Nach dem Abbestellen kommt nichts mehr.
        /// </summary>
        [Test]
        public async Task Unsubscribing_StopsTheEvents()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            await AskAsync(alice, "sub-5",
                           PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node, alice.BareJid, "sub-5"));

            var abbestellt = await AskAsync(alice, "unsub-5",
                                            PubSubBuilder.Unsubscribe($"bob@{Server.Domain}",
                                                                      Node,
                                                                      alice.BareJid,
                                                                      "unsub-5"));

            Assert.That(abbestellt.Attr("type"), Is.EqualTo("result"));

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-3",
                           PublishIq("pub-3", Node, "3", "<wetter xmlns='urn:example:x'>Schnee</wetter>"));

            await WaitAgainst(() => Count(ereignisse) > 0,
                              "eine Benachrichtigung nach dem Abbestellen");

        }

        #endregion

        #region Unsubscribing_WithoutASubscription_IsRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.2.3.2: Wer nicht abonniert hat, kann nicht
        /// abbestellen.
        /// </summary>
        [Test]
        public async Task Unsubscribing_WithoutASubscription_IsRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "unsub-6",
                                         PubSubBuilder.Unsubscribe($"bob@{Server.Domain}",
                                                                   Node,
                                                                   alice.BareJid,
                                                                   "unsub-6"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("unexpected-request"));
                Assert.That(ErrorTypeOf(antwort),       Is.EqualTo("cancel"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("not-subscribed"));
            });

        }

        #endregion

        #region Unsubscribing_WithAForeignSubId_IsRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.2.3.1: Eine mitgeschickte <c>subid</c>, die
        /// nicht passt, beendet nichts.
        /// </summary>
        /// <remarks>
        /// Der Fall ist selten und die Prüfung trotzdem nötig: Eine falsche
        /// Kennung durchgehen zu lassen hiesse, ein <i>anderes</i> Abonnement
        /// zu beenden als das gemeinte - und dem Absender zu bestätigen, es sei
        /// seines gewesen.
        /// </remarks>
        [Test]
        public async Task Unsubscribing_WithAForeignSubId_IsRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await AskAsync(alice, "sub-7",
                           PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node, alice.BareJid, "sub-7"));

            var antwort = await AskAsync(alice, "unsub-7",
                                         $"<iq type='set' to='bob@{Server.Domain}' id='unsub-7'>" +
                                         $"<pubsub xmlns='{PubSubNamespace}'>" +
                                         $"<unsubscribe node='{Node}' jid='{alice.BareJid}' subid='fremd'/>" +
                                         "</pubsub></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("not-acceptable"));
                Assert.That(ErrorTypeOf(antwort),       Is.EqualTo("modify"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-subid"));
            });

        }

        #endregion

        #region Unsubscribing_ForSomebodyElse_LeavesTheirSubscriptionAlone()

        /// <summary>
        /// Auch beim Abbestellen muss der <c>jid</c> der des Absenders sein.
        /// </summary>
        /// <remarks>
        /// Die Gegenrichtung zu <see cref="Subscribing_ForSomebodyElsesJid_IsRejected"/>
        /// und die gefährlichere von beiden: Ein fremdes Abonnement anzulegen
        /// ist lästig, ein fremdes zu beenden ist ein Entzug. Carol bekäme
        /// nichts mehr und wüsste nicht einmal, dass etwas fehlt - Ausbleiben
        /// sieht aus wie Ruhe.
        ///
        /// Der Test prüft deshalb beides: die Absage <b>und</b> dass Carols
        /// Abonnement noch trägt. Nur die Absage zu prüfen liesse eine
        /// Umsetzung durch, die erst abmeldet und sich dann beschwert.
        /// </remarks>
        [Test]
        public async Task Unsubscribing_ForSomebodyElse_LeavesTheirSubscriptionAlone()
        {

            var bob   = await PublishingBobAsync();
            var carol = await ConnectClientAsync("carol");
            var alice = await ConnectClientAsync("alice");

            await AskAsync(carol, "sub-11",
                           PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node, carol.BareJid, "sub-11"));

            var antwort = await AskAsync(alice, "unsub-11",
                                         PubSubBuilder.Unsubscribe($"bob@{Server.Domain}",
                                                                   Node,
                                                                   carol.BareJid,
                                                                   "unsub-11"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("bad-request"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-jid"));
            });

            var ereignisse = CollectEvents(carol);

            await AskAsync(bob, "pub-6",
                           PublishIq("pub-6", Node, "6", "<wetter xmlns='urn:example:x'>Sturm</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0,
                          "die Benachrichtigung an Carol, deren Abonnement niemand beenden durfte");

        }

        #endregion

        #region TheSubIdFromTheConfirmation_Unsubscribes()

        /// <summary>
        /// Die Gegenprobe: Mit der Kennung aus der Zusage geht es.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test prüfte der vorige nur, dass <i>irgendeine</i>
        /// subid abgewiesen wird - eine Umsetzung, die jede abweist, bestünde
        /// ihn ebenso.
        /// </remarks>
        [Test]
        public async Task TheSubIdFromTheConfirmation_Unsubscribes()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            var zusage = await AskAsync(alice, "sub-8",
                                        PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node,
                                                                alice.BareJid, "sub-8"));

            var subId = SubscriptionOf(zusage)?.Attr("subid");

            Assert.That(subId, Is.Not.Null.And.Not.Empty);

            var antwort = await AskAsync(alice, "unsub-8",
                                         $"<iq type='set' to='bob@{Server.Domain}' id='unsub-8'>" +
                                         $"<pubsub xmlns='{PubSubNamespace}'>" +
                                         $"<unsubscribe node='{Node}' jid='{alice.BareJid}' subid='{subId}'/>" +
                                         "</pubsub></iq>");

            Assert.That(antwort.Attr("type"), Is.EqualTo("result"));

        }

        #endregion

        #region ASubscriberWhoIsAlsoAContact_GetsTheEventOnlyOnce()

        /// <summary>
        /// Wer über beide Wege in Frage kommt, bekommt die Benachrichtigung
        /// trotzdem einmal.
        /// </summary>
        /// <remarks>
        /// Zwei Quellen für dieselbe Empfängerliste sind die naheliegende Art,
        /// eine Nachricht zu verdoppeln. Für einen Menschen wäre das
        /// ärgerlich; für OMEMO wäre es schlimmer, weil eine doppelt
        /// eintreffende Geräteliste zweimal beantwortet würde.
        /// </remarks>
        [Test]
        public async Task ASubscriberWhoIsAlsoAContact_GetsTheEventOnlyOnce()
        {

            MakeContacts("alice", "bob");

            var bob    = await PublishingBobAsync();
            var alice  = await ConnectClientAsync("alice");

            await AskAsync(alice, "sub-9",
                           PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node, alice.BareJid, "sub-9"));

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-4",
                           PublishIq("pub-4", Node, "4", "<wetter xmlns='urn:example:x'>Nebel</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die Benachrichtigung");

            await WaitAgainst(() => Count(ereignisse) > 1,
                              "eine zweite Benachrichtigung über dieselbe Veröffentlichung");

        }

        #endregion

        #region SubscribingTwice_YieldsTwoSubscriptions()

        /// <summary>
        /// XEP-0060, Abschnitt 6.1: Ein zweites <c>subscribe</c> ist ein
        /// zweites Abonnement, mit eigener Kennung und eigener Zustellung.
        /// </summary>
        /// <remarks>
        /// <b>Bis K3 stand hier das Gegenteil</b> - ein zweites <c>subscribe</c>
        /// gab dieselbe Kennung zurück, und die Zustellung blieb einfach. Das
        /// war nicht falsch (ein Dienst darf so verfahren), aber es machte die
        /// <c>subid</c> zur Zierde: Wo es nie zwei gibt, benennt sie nichts,
        /// was man nicht auch am Knoten erkennt.
        ///
        /// Der Fall ist nicht ausgedacht. Er entsteht von selbst, wenn ein
        /// Client neu startet und wieder abonniert, ohne seine alte Kennung zu
        /// kennen - danach hat der Dienst zwei, und von da an ist jedes
        /// Abbestellen ohne Kennung zweideutig.
        /// </remarks>
        [Test]
        public async Task SubscribingTwice_YieldsTwoSubscriptions()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            var erste  = await SubscribeAsync(alice, "sub-10a");
            var zweite = await SubscribeAsync(alice, "sub-10b");

            Assert.That(zweite, Is.Not.EqualTo(erste),
                        "Zwei Abonnements, die dieselbe Kennung tragen, sind nicht zu unterscheiden.");

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-5",
                           PublishIq("pub-5", Node, "5", "<wetter xmlns='urn:example:x'>Hagel</wetter>"));

            await WaitFor(() => Count(ereignisse) > 1, "beide Benachrichtigungen");

            Assert.That(SubIdsIn(ereignisse), Is.EquivalentTo(new[] { erste, zweite }),
                        "Jede Zustellung gehört zu genau einem Abonnement und sagt zu welchem.");

        }

        #endregion

        #region WithTwoSubscriptions_UnsubscribingWithoutASubId_IsRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.2.3.1: Wer mehrere hat, muss sagen, welches.
        /// </summary>
        /// <remarks>
        /// Der Grund ist derselbe wie bei der falschen Kennung, nur eine Stufe
        /// früher: Ein Dienst, der sich eines aussuchte, beendete vielleicht
        /// das falsche - und bestätigte dem Absender, es sei das gemeinte
        /// gewesen.
        /// </remarks>
        [Test]
        public async Task WithTwoSubscriptions_UnsubscribingWithoutASubId_IsRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-12a");
            await SubscribeAsync(alice, "sub-12b");

            var antwort = await AskAsync(alice, "unsub-12",
                                         PubSubBuilder.Unsubscribe($"bob@{Server.Domain}",
                                                                   Node,
                                                                   alice.BareJid,
                                                                   "unsub-12"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("bad-request"));
                Assert.That(ErrorTypeOf(antwort),       Is.EqualTo("modify"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("subid-required"));
            });

        }

        #endregion

        #region WithTwoSubscriptions_TheSubIdEndsExactlyOne()

        /// <summary>
        /// Und mit Kennung endet genau das benannte.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test, und die eigentliche Zusicherung:
        /// Ein Abbestellen, das beide beendete, wäre ebenso eindeutig wie
        /// falsch.
        /// </remarks>
        [Test]
        public async Task WithTwoSubscriptions_TheSubIdEndsExactlyOne()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            var erste  = await SubscribeAsync(alice, "sub-13a");
            var zweite = await SubscribeAsync(alice, "sub-13b");

            var antwort = await AskAsync(alice, "unsub-13",
                                         PubSubBuilder.Unsubscribe($"bob@{Server.Domain}", Node,
                                                                   alice.BareJid, "unsub-13", erste));

            Assert.That(antwort.Attr("type"), Is.EqualTo("result"));

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-7",
                           PublishIq("pub-7", Node, "7", "<wetter xmlns='urn:example:x'>Graupel</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die verbliebene Benachrichtigung");

            await WaitAgainst(() => Count(ereignisse) > 1,
                              "eine Benachrichtigung für das beendete Abonnement");

            Assert.That(SubIdsIn(ereignisse), Is.EqualTo(new[] { zweite }),
                        "Es blieb nicht das übrig, das bleiben sollte.");

        }

        #endregion

        #region TheOptionsForm_OffersDelivery()

        /// <summary>
        /// XEP-0060, Abschnitt 6.3.2: Das Formular sagt, was sich einstellen
        /// lässt.
        /// </summary>
        /// <remarks>
        /// <b>Es enthält genau ein Feld</b>, und das ist die Aussage: Was
        /// dieser Server nicht kann, bietet er auch nicht an. Ein Formular mit
        /// <c>pubsub#digest</c> darin, das dann nichts bewirkt, wäre eine
        /// Zusage ohne Deckung - und zwar eine, die der Abonnent nie
        /// nachprüfen kann, weil ausbleibende Zusammenfassungen wie Ruhe
        /// aussehen.
        /// </remarks>
        [Test]
        public async Task TheOptionsForm_OffersDelivery()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-20");

            var antwort = await AskAsync(alice, "opt-20", OptionsIq("opt-20", "get"));

            Assert.Multiple(() =>
            {

                Assert.That(antwort.Attr("type"), Is.EqualTo("result"));

                Assert.That(FieldValue(antwort, "FORM_TYPE"),
                            Is.EqualTo("http://jabber.org/protocol/pubsub#subscribe_options"));

                Assert.That(FieldValue(antwort, "pubsub#deliver"), Is.EqualTo("1"),
                            "Zugestellt wird, solange niemand widerspricht.");

            });

        }

        #endregion

        #region TheSubmittedForm_IsReadStrictly()

        /// <summary>
        /// Was ein abgeschicktes Formular sagt - und was es nicht sagen darf.
        /// </summary>
        /// <remarks>
        /// Ohne Server, weil es hier um das Lesen geht und nicht um den Weg.
        /// Die vier Schreibweisen aus XEP-0004, Abschnitt 3.3 stehen alle
        /// darin: Was hereinkommt, hat ein anderer geschrieben, und der darf
        /// wählen.
        /// </remarks>
        [Test]
        public void TheSubmittedForm_IsReadStrictly()
        {

            static XElement Formular(String inhalt)
                => XElement.Parse($"<x xmlns='jabber:x:data' type='submit'>{inhalt}</x>");

            static String Feld(String wert)
                => $"<field var='pubsub#deliver'><value>{wert}</value></field>";

            Assert.Multiple(() =>
            {

                foreach (var (wert, erwartet) in new[] { ("1", true), ("true", true),
                                                         ("0", false), ("false", false) })
                {
                    Assert.That(PubSubSubscriptionOptions.TryRead(Formular(Feld(wert)), out var gelesen),
                                Is.True, $"'{wert}' ist eine zulässige Schreibweise.");
                    Assert.That(gelesen!.Deliver, Is.EqualTo(erwartet), $"'{wert}'");
                }

                Assert.That(PubSubSubscriptionOptions.TryRead(Formular(Feld("vielleicht")), out _),
                            Is.False, "Alles andere ist kein Wahrheitswert.");

                Assert.That(PubSubSubscriptionOptions.TryRead(Formular(""), out var leer), Is.True);
                Assert.That(leer!.Deliver, Is.True,
                            "Ein fehlendes Feld steht auf der Vorgabe.");

                // Ein Formular für einen anderen Zweck - etwa die
                // publish-options aus XEP-0384 - trägt zufällig kein bekanntes
                // Feld und ginge sonst als leere Einstellung durch.
                Assert.That(PubSubSubscriptionOptions.TryRead(
                                Formular("<field var='FORM_TYPE' type='hidden'>" +
                                         "<value>http://jabber.org/protocol/pubsub#publish-options</value></field>"),
                                out _),
                            Is.False,
                            "Ein Formular für einen anderen Zweck ist keine Einstellung.");

            });

        }

        #endregion

        #region TurningDeliveryOff_SilencesTheSubscription()

        /// <summary>
        /// XEP-0060, Abschnitt 12.18: <c>pubsub#deliver=0</c> - das Abonnement
        /// bleibt, die Zustellung nicht.
        /// </summary>
        /// <remarks>
        /// <b>Und es fällt nicht auf die Presence-Zustellung zurück.</b> Wer
        /// gesagt hat, dass er nichts bekommen will, bekommt nichts - auch
        /// wenn er nebenbei im Roster steht. Alles andere hiesse, eine
        /// Einstellung mit einem anderen Weg zu unterlaufen.
        /// </remarks>
        [Test]
        public async Task TurningDeliveryOff_SilencesTheSubscription()
        {

            MakeContacts("alice", "bob");

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-21");

            var gesetzt = await AskAsync(alice, "opt-21",
                                         OptionsIq("opt-21", "set",
                                                   formular: SubmitForm(DeliverField("0"))));

            Assert.That(gesetzt.Attr("type"), Is.EqualTo("result"));

            var gelesen = await AskAsync(alice, "opt-21b", OptionsIq("opt-21b", "get"));

            Assert.That(FieldValue(gelesen, "pubsub#deliver"), Is.EqualTo("0"),
                        "Das Formular muss zeigen, was gilt, und nicht, was vorgesehen war.");

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-20",
                           PublishIq("pub-20", Node, "20", "<wetter xmlns='urn:example:x'>still</wetter>"));

            await WaitAgainst(() => Count(ereignisse) > 0,
                              "eine Benachrichtigung an ein stillgelegtes Abonnement");

        }

        #endregion

        #region TurningDeliveryOnAgain_ResumesIt()

        /// <summary>
        /// Die Gegenprobe: Was sich abschalten lässt, lässt sich auch wieder
        /// einschalten.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde der vorige Test auch gegen eine Umsetzung, die
        /// jede Einstellung als „nicht zustellen" liest.
        /// </remarks>
        [Test]
        public async Task TurningDeliveryOnAgain_ResumesIt()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-22");

            await AskAsync(alice, "opt-22a",
                           OptionsIq("opt-22a", "set", formular: SubmitForm(DeliverField("0"))));

            await AskAsync(alice, "opt-22b",
                           OptionsIq("opt-22b", "set", formular: SubmitForm(DeliverField("true"))));

            var gelesen = await AskAsync(alice, "opt-22c", OptionsIq("opt-22c", "get"));

            Assert.That(FieldValue(gelesen, "pubsub#deliver"), Is.EqualTo("1"),
                        "Auch 'true' ist ein Ja - XEP-0004 kennt beide Schreibweisen.");

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-21",
                           PublishIq("pub-21", Node, "21", "<wetter xmlns='urn:example:x'>wieder da</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die wieder zugestellte Benachrichtigung");

        }

        #endregion

        #region WithTwoSubscriptions_OnlyTheConfiguredOneGoesQuiet()

        /// <summary>
        /// Der Grund, aus dem sich zwei Abonnements desselben JIDs auf
        /// denselben Knoten überhaupt unterscheiden können.
        /// </summary>
        /// <remarks>
        /// Bis hierher waren zwei Abonnements zwei gleiche Dinge, und das
        /// zweite brachte nichts ein als eine zweite Zustellung. Mit der
        /// Konfiguration je Abonnement bekommen sie verschiedene Eigenschaften
        /// - und erst damit ist die <c>subid</c> nicht nur eine Kennung,
        /// sondern die Adresse einer Einstellung.
        /// </remarks>
        [Test]
        public async Task WithTwoSubscriptions_OnlyTheConfiguredOneGoesQuiet()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            var erste  = await SubscribeAsync(alice, "sub-23a");
            var zweite = await SubscribeAsync(alice, "sub-23b");

            var gesetzt = await AskAsync(alice, "opt-23",
                                         OptionsIq("opt-23", "set", erste,
                                                   SubmitForm(DeliverField("0"))));

            Assert.That(gesetzt.Attr("type"), Is.EqualTo("result"));

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-22",
                           PublishIq("pub-22", Node, "22", "<wetter xmlns='urn:example:x'>halb</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die Benachrichtigung des lauten Abonnements");

            await WaitAgainst(() => Count(ereignisse) > 1,
                              "eine Benachrichtigung des stillgelegten Abonnements");

            Assert.That(SubIdsIn(ereignisse), Is.EqualTo(new[] { zweite }),
                        "Es wurde das falsche stillgelegt.");

        }

        #endregion

        #region Options_WithoutASubId_WhenSeveralExist_AreRejected()

        /// <summary>
        /// XEP-0060, Abschnitt 6.3.3: Auch hier muss gesagt werden, welches
        /// Abonnement gemeint ist - nur mit einem anderen Fehler als beim
        /// Abbestellen.
        /// </summary>
        /// <remarks>
        /// <c>&lt;not-acceptable/&gt;</c> statt <c>&lt;bad-request/&gt;</c>,
        /// und das ist keine Willkür des XEP: Die Anfrage <i>ist</i> in Ordnung,
        /// sie lässt sich nur in dieser Lage nicht beantworten. Eine Umsetzung,
        /// die beide Stellen gleich behandelt, hat eine davon nicht gelesen.
        /// </remarks>
        [Test]
        public async Task Options_WithoutASubId_WhenSeveralExist_AreRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-24a");
            await SubscribeAsync(alice, "sub-24b");

            var antwort = await AskAsync(alice, "opt-24", OptionsIq("opt-24", "get"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("not-acceptable"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("subid-required"));
            });

        }

        #endregion

        #region Options_OfANodeNobodySubscribed_AreRejected()

        /// <summary>
        /// Ohne Abonnement gibt es nichts einzustellen.
        /// </summary>
        [Test]
        public async Task Options_OfANodeNobodySubscribed_AreRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            var antwort = await AskAsync(alice, "opt-25", OptionsIq("opt-25", "get"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("unexpected-request"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("not-subscribed"));
            });

        }

        #endregion

        #region Options_ForSomebodyElse_AreRejected()

        /// <summary>
        /// Und auch hier darf den <c>jid</c> nur setzen, wem er gehört.
        /// </summary>
        /// <remarks>
        /// Die dritte Stelle mit derselben Prüfung, und die stillste: Wer
        /// fremde Abonnements einstellen dürfte, könnte sie lautlos
        /// abschalten. Das Abonnement bliebe stehen - es käme nur nichts mehr
        /// an, und der Betroffene fände in seiner eigenen Liste nichts
        /// Auffälliges.
        /// </remarks>
        [Test]
        public async Task Options_ForSomebodyElse_AreRejected()
        {

            var bob   = await PublishingBobAsync();
            var carol = await ConnectClientAsync("carol");
            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(carol, "sub-26");

            var antwort = await AskAsync(alice, "opt-26",
                                         OptionsIq("opt-26", "set",
                                                   formular: SubmitForm(DeliverField("0")),
                                                   jid:      carol.BareJid));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-jid"));
            });

            var ereignisse = CollectEvents(carol);

            await AskAsync(bob, "pub-23",
                           PublishIq("pub-23", Node, "23", "<wetter xmlns='urn:example:x'>laut</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0,
                          "die Benachrichtigung an Carol, die niemand abschalten durfte");

        }

        #endregion

        #region AnOptionNobodyOffered_IsRejected()

        /// <summary>
        /// Ein Feld, das im Formular nicht stand, wird abgewiesen statt
        /// übergangen.
        /// </summary>
        /// <remarks>
        /// <b>Das ist strenger als üblich und Absicht.</b> Ein Dienst, der
        /// Unbekanntes stillschweigend schluckt, lässt den Abonnenten in dem
        /// Glauben, seine Einstellung gelte - und ausbleibende Wirkung sieht
        /// aus wie ein Fehler anderswo. Lieber eine Absage, die man lesen
        /// kann.
        /// </remarks>
        [Test]
        public async Task AnOptionNobodyOffered_IsRejected()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-27");

            var antwort = await AskAsync(alice, "opt-27",
                                         OptionsIq("opt-27", "set",
                                                   formular: SubmitForm(
                                                       "<field var='pubsub#digest'><value>1</value></field>")));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(ConditionOf(antwort),       Is.EqualTo("bad-request"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-options"));
            });

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-24",
                           PublishIq("pub-24", Node, "24", "<wetter xmlns='urn:example:x'>unverändert</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0,
                          "die Benachrichtigung - eine abgewiesene Einstellung ändert nichts");

        }

        #endregion

        #region ASetWithoutAForm_IsRejected()

        /// <summary>
        /// Ein <c>set</c> ohne Formular sagt nicht, was eingestellt werden
        /// soll.
        /// </summary>
        /// <remarks>
        /// Die Vorgaben einzusetzen wäre die freundliche Auslegung und die
        /// gefährliche: Aus einer unvollständigen Anfrage würde eine Änderung,
        /// die niemand verlangt hat - und sie träfe ausgerechnet den, der
        /// gerade etwas anderes eingestellt hatte.
        /// </remarks>
        [Test]
        public async Task ASetWithoutAForm_IsRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-29");

            await AskAsync(alice, "opt-29a",
                           OptionsIq("opt-29a", "set", formular: SubmitForm(DeliverField("0"))));

            var antwort = await AskAsync(alice, "opt-29b", OptionsIq("opt-29b", "set"));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-options"));
            });

            var gelesen = await AskAsync(alice, "opt-29c", OptionsIq("opt-29c", "get"));

            Assert.That(FieldValue(gelesen, "pubsub#deliver"), Is.EqualTo("0"),
                        "Eine abgewiesene Anfrage darf nichts zurückgesetzt haben.");

        }

        #endregion

        #region AFormThatIsNotSubmitted_IsRejected()

        /// <summary>
        /// XEP-0004: Was zurückkommt, muss ein <c>submit</c> sein.
        /// </summary>
        /// <remarks>
        /// Ein zurückgeschicktes <c>form</c> ist das Angebot und keine
        /// Antwort. Es anzunehmen hiesse, den Vorschlag des Dienstes für den
        /// Willen des Abonnenten zu halten.
        /// </remarks>
        [Test]
        public async Task AFormThatIsNotSubmitted_IsRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await SubscribeAsync(alice, "sub-28");

            var antwort = await AskAsync(alice, "opt-28",
                                         OptionsIq("opt-28", "set",
                                                   formular: SubmitForm(DeliverField("0"), "form")));

            Assert.Multiple(() =>
            {
                Assert.That(antwort.Attr("type"),       Is.EqualTo("error"));
                Assert.That(PubSubConditionOf(antwort), Is.EqualTo("invalid-options"));
            });

        }

        #endregion

        #region APresenceDrivenNotification_CarriesNoSubId()

        /// <summary>
        /// Wer nur über Presence benachrichtigt wird, bekommt keine Kennung -
        /// es gibt keine.
        /// </summary>
        /// <remarks>
        /// XEP-0060, Abschnitt 12.20 verlangt die Kennung, <i>wenn</i> es
        /// mehrere Abonnements gibt. Eine erfundene mitzuschicken wäre
        /// schlimmer als keine: Der Empfänger könnte danach abbestellen wollen,
        /// was nie bestellt wurde.
        /// </remarks>
        [Test]
        public async Task APresenceDrivenNotification_CarriesNoSubId()
        {

            MakeContacts("alice", "bob");

            var bob        = await PublishingBobAsync();
            var alice      = await ConnectClientAsync("alice");
            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-8",
                           PublishIq("pub-8", Node, "8", "<wetter xmlns='urn:example:x'>Dunst</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die Benachrichtigung an den Kontakt");

            Assert.That(SubIdsIn(ereignisse), Is.Empty);

        }

        #endregion

    }

}
