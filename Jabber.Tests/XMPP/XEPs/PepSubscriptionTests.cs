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

        #region SubscribingTwice_KeepsOneSubscription()

        /// <summary>
        /// Ein zweites <c>subscribe</c> auf denselben Knoten gibt dieselbe
        /// Kennung zurück und verdoppelt die Zustellung nicht.
        /// </summary>
        [Test]
        public async Task SubscribingTwice_KeepsOneSubscription()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");

            var erste  = await AskAsync(alice, "sub-10a",
                                        PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node,
                                                                alice.BareJid, "sub-10a"));

            var zweite = await AskAsync(alice, "sub-10b",
                                        PubSubBuilder.Subscribe($"bob@{Server.Domain}", Node,
                                                                alice.BareJid, "sub-10b"));

            Assert.That(SubscriptionOf(zweite)?.Attr("subid"),
                        Is.EqualTo(SubscriptionOf(erste)?.Attr("subid")),
                        "Zweimal dasselbe Abonnement ist ein Abonnement.");

            var ereignisse = CollectEvents(alice);

            await AskAsync(bob, "pub-5",
                           PublishIq("pub-5", Node, "5", "<wetter xmlns='urn:example:x'>Hagel</wetter>"));

            await WaitFor(() => Count(ereignisse) > 0, "die Benachrichtigung");

            await WaitAgainst(() => Count(ereignisse) > 1,
                              "eine zweite Benachrichtigung");

        }

        #endregion

    }

}
