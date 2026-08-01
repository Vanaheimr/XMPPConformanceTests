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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die ausgehende Hälfte von XEP-0060: Der Client wartet die Antwort ab,
    /// bevor er ein Abonnement für bestehend hält.
    /// </summary>
    /// <remarks>
    /// <b>Der Fehler, um den es geht, stand seit D38 im WORKPLAN:</b>
    /// <c>PubSubSubscribeAsync</c> verschickte die Anfrage und trug das
    /// Abonnement in derselben Zeile ein - ohne dass jemand die Antwort gelesen
    /// hätte. Ein abgelehntes Abonnement stand danach als bestehendes da, und
    /// der Aufrufer erfuhr es nie.
    ///
    /// Das ist dieselbe Sorte Fehler wie die aus der OMEMO-Reihe, nur ohne
    /// Kryptographie: <b>Eine Behauptung über etwas, das man nicht nachgesehen
    /// hat.</b> Sie fällt lange nicht auf, weil sie im guten Fall stimmt.
    /// </remarks>
    [TestFixture]
    public class PubSubCorrelationTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private const String Node = "urn:example:wetter";

        private static String Payload(String inhalt)
            => $"<wetter xmlns='urn:example:x'>{inhalt}</wetter>";

        /// <summary>
        /// Bob, der veröffentlicht hat - danach gibt es den Knoten.
        /// </summary>
        private async Task<XMPPClient> PublishingBobAsync(String itemId = "1", String inhalt = "sonnig")
        {

            var bob = await ConnectClientAsync("bob");

            Assert.That(await bob.PubSubPublishAsync(Node, itemId, Payload(inhalt), bob.BareJid),
                        Is.True,
                        "Im eigenen Knoten muss Bob veröffentlichen können.");

            return bob;

        }

        private String BobsJid => $"bob@{Server.Domain}";

        /// <summary>
        /// Lässt den Testserver schweigen und antwortet stattdessen selbst -
        /// mit einer Antwort, die dieser Server so nie geben würde.
        /// </summary>
        /// <param name="anfrage">Worauf gewartet wird, z.B. <c>&lt;subscribe</c>.</param>
        /// <param name="antwort">Die Antwort; <c>{id}</c> wird durch die Kennung ersetzt.</param>
        /// <remarks>
        /// Ohne diesen Umweg bliebe ein Teil der Auswertung ungeprüft: Der
        /// eigene Server ist wohlerzogen, und gerade die Fälle, in denen ein
        /// Client vorschnell wird, kommen von einer Gegenstelle, die es nicht
        /// ist.
        ///
        /// Der Schalter gehört dazu: Antwortete der Server auch noch, hinge
        /// das Ergebnis davon ab, wer schneller ist - und ein Test, den ein
        /// Wettlauf entscheidet, misst nichts (siehe D69).
        /// </remarks>
        private void PlayTheService(String anfrage, String antwort)
        {

            Server.AnswerPepRequests = false;

            Server.OnStanzaReceived += (sitzung, frame) =>
            {
                if (frame.Contains(anfrage, StringComparison.Ordinal))
                {

                    var id = System.Text.RegularExpressions.Regex.Match(frame, @"id='([^']+)'").Groups[1].Value;

                    _ = sitzung.SendAsync(antwort.Replace("{id}", id));

                }
            };

        }

        private static String SubscriptionIq(String art, String zustand)
            => $"<iq type='{art}' id='{{id}}'>" +
               "<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
               $"<subscription node='{Node}' subid='abc123' subscription='{zustand}'/>" +
               "</pubsub></iq>";

        #endregion


        #region AConfirmedSubscription_IsRecordedWithItsSubId()

        /// <summary>
        /// Die Zusage wird gelesen, und was in ihr steht, bleibt bekannt.
        /// </summary>
        [Test]
        public async Task AConfirmedSubscription_IsRecordedWithItsSubId()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {

                Assert.That(abo,        Is.Not.Null);
                Assert.That(abo!.State, Is.EqualTo(PubSubSubscriptionState.Subscribed));
                Assert.That(abo!.NodeId, Is.EqualTo(Node));
                Assert.That(abo!.SubId, Is.Not.Null.And.Not.Empty,
                            "Die Kennung kommt vom Dienst - wer nicht hinsieht, hat sie nicht.");

                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.True);

            });

        }

        #endregion

        #region ARejectedSubscription_IsNotRecorded()

        /// <summary>
        /// Der Fehler aus D38: Ein abgelehntes Abonnement stand als
        /// bestehendes in der Buchführung.
        /// </summary>
        [Test]
        public async Task ARejectedSubscription_IsNotRecorded()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync("urn:example:gibtesnicht", BobsJid);

            Assert.Multiple(() =>
            {
                Assert.That(abo, Is.Null,
                            "Eine Absage ist kein Abonnement.");
                Assert.That(alice.Connection.PubSub!.IsSubscribed("urn:example:gibtesnicht"), Is.False,
                            "Ein abgelehntes Abonnement darf nicht als bestehendes dastehen.");
            });

        }

        #endregion

        #region AnUnansweredSubscription_IsNotRecorded()

        /// <summary>
        /// Schweigen ist keine Zusage.
        /// </summary>
        /// <remarks>
        /// Der Fall, den ein Client am ehesten falsch behandelt, weil er sich
        /// nicht meldet. Der Testserver kann dafür schweigen -
        /// <c>AnswerPepRequests</c>, wie <c>AnswerPings</c> für XEP-0199.
        ///
        /// Der Test kostet die volle Frist von zehn Sekunden. Das ist der Preis
        /// dafür, dass dieser Zweig überhaupt einmal gelaufen ist.
        /// </remarks>
        [Test]
        public async Task AnUnansweredSubscription_IsNotRecorded()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Server.AnswerPepRequests = false;

            var abo = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {
                Assert.That(abo, Is.Null);
                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);
            });

        }

        #endregion

        #region APendingSubscription_IsNotRecorded()

        /// <summary>
        /// XEP-0060, Abschnitt 6.1.4: <c>pending</c> heisst, dass noch jemand
        /// entscheidet - es ist kein Abonnement.
        /// </summary>
        /// <remarks>
        /// Der eigene Server sagt das nie; er kennt keine Genehmigungen. Ein
        /// fremder tut es, sobald ein Knoten die Zustimmung seines Besitzers
        /// verlangt - und dann ist die Verwechslung teuer: Der Client hielte
        /// sich für abonniert und wartete auf Meldungen, über die noch gar
        /// nicht entschieden ist.
        /// </remarks>
        [Test]
        public async Task APendingSubscription_IsNotRecorded()
        {

            var alice = await ConnectClientAsync("alice");

            PlayTheService("<subscribe", SubscriptionIq("result", "pending"));

            var abo = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {
                Assert.That(abo, Is.Null, "Ein pending ist keine Zusage.");
                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);
            });

        }

        #endregion

        #region AnErrorCarryingAConfirmation_IsStillARejection()

        /// <summary>
        /// Ein <c>type='error'</c> bleibt eine Absage, auch wenn eine Zusage
        /// darin steht.
        /// </summary>
        /// <remarks>
        /// <b>Warum das nicht bloss theoretisch ist:</b> Ohne die Prüfung auf
        /// den Typ hinge die Ablehnung allein daran, dass in einer
        /// Fehlerantwort zufällig keine Zusage steht. Das ist keine
        /// Entscheidung, sondern ein Zufall, der lange gutgeht - dieselbe
        /// Sorte Grundlage, auf der die fünf OMEMO-Funde standen.
        /// </remarks>
        [Test]
        public async Task AnErrorCarryingAConfirmation_IsStillARejection()
        {

            var alice = await ConnectClientAsync("alice");

            PlayTheService("<subscribe", SubscriptionIq("error", "subscribed"));

            var abo = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {
                Assert.That(abo, Is.Null);
                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);
            });

        }

        #endregion

        #region AResultWithoutAConfirmation_IsNoSubscription()

        /// <summary>
        /// Ein <c>result</c> ohne Zusage sagt nicht, dass abonniert wurde.
        /// </summary>
        /// <remarks>
        /// XEP-0060, Abschnitt 6.1.2 verlangt die Zusage; ein Dienst, der
        /// bloss quittiert, hat die Frage nicht beantwortet. Sie als Zusage zu
        /// lesen hiesse, aus dem Ausbleiben eines Fehlers auf ein Ergebnis zu
        /// schliessen.
        /// </remarks>
        [Test]
        public async Task AResultWithoutAConfirmation_IsNoSubscription()
        {

            var alice = await ConnectClientAsync("alice");

            PlayTheService("<subscribe", "<iq type='result' id='{id}'/>");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Null);
            Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);

        }

        #endregion

        #region AConfirmationWithoutANode_IsNoSubscription()

        /// <summary>
        /// Eine Zusage ohne Knoten benennt nichts.
        /// </summary>
        /// <remarks>
        /// Der Knoten ist nicht Schmuck, sondern der Schlüssel: Unter ihm
        /// steht das Abonnement in der Buchführung, und an ihm hängt später
        /// die Frage, von wem Meldungen angenommen werden. Eine Zusage ohne
        /// Knoten käme unter dem leeren Namen zu liegen - und der passt auf
        /// jedes Ereignis, dessen Knoten sich nicht lesen lässt.
        /// </remarks>
        [Test]
        public async Task AConfirmationWithoutANode_IsNoSubscription()
        {

            var alice = await ConnectClientAsync("alice");

            PlayTheService("<subscribe",
                           "<iq type='result' id='{id}'>" +
                           "<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
                           "<subscription subid='abc123' subscription='subscribed'/>" +
                           "</pubsub></iq>");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Null);
            Assert.That(alice.Connection.PubSub!.IsSubscribed(""), Is.False,
                        "Ein Abonnement unter dem leeren Namen wäre schlimmer als keines.");

        }

        #endregion

        #region AnUnknownSubscriptionState_IsNoSubscription()

        /// <summary>
        /// Ein Zustand, den dieser Client nicht kennt, gilt nicht als Zusage.
        /// </summary>
        /// <remarks>
        /// Die Vorsicht kostet nichts: Wer sich zu Unrecht für nicht abonniert
        /// hält, fragt noch einmal - wer sich zu Unrecht für abonniert hält,
        /// wartet auf etwas, das nie kommt.
        /// </remarks>
        [Test]
        public async Task AnUnknownSubscriptionState_IsNoSubscription()
        {

            var alice = await ConnectClientAsync("alice");

            PlayTheService("<subscribe", SubscriptionIq("result", "vielleicht"));

            Assert.That(PubSubSubscription.StateOf("vielleicht"),
                        Is.EqualTo(PubSubSubscriptionState.None));

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Null);
            Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);

        }

        #endregion

        #region ARejectedUnsubscribe_KeepsTheRecord()

        /// <summary>
        /// Was nicht abbestellt werden konnte, bleibt abonniert.
        /// </summary>
        /// <remarks>
        /// Die Gegenrichtung zum Eintragen, und derselbe Fehler andersherum:
        /// Wer den Eintrag vor der Antwort löscht, verwirft die Meldungen
        /// eines Abonnements, das noch besteht - und sieht dieselbe Stille wie
        /// jemand, der richtig abbestellt hat.
        /// </remarks>
        [Test]
        public async Task ARejectedUnsubscribe_KeepsTheRecord()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Not.Null);

            PlayTheService("<unsubscribe",
                           "<iq type='error' id='{id}'><error type='cancel'>" +
                           "<not-allowed xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                           "</error></iq>");

            var beendet = await alice.PubSubUnsubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {
                Assert.That(beendet, Is.False);
                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.True,
                            "Eine abgelehnte Kündigung beendet nichts.");
            });

        }

        #endregion

        #region TwoRequests_CarryTwoDifferentIds()

        /// <summary>
        /// Jede Anfrage bekommt eine eigene Kennung.
        /// </summary>
        /// <remarks>
        /// Bis D71 trugen alle <c>subscribe</c> dieselbe feste Kennung
        /// <c>pubsub-sub</c>. Solange niemand Antworten zuordnete, fiel das
        /// nicht auf - sobald jemand es tut, bekäme die zweite Anfrage die
        /// Antwort auf die erste.
        /// </remarks>
        [Test]
        public async Task TwoRequests_CarryTwoDifferentIds()
        {

            await PublishingBobAsync();

            var alice   = await ConnectClientAsync("alice");
            var sitzung = Server.SessionOf(alice.FullJid)!;

            await alice.PubSubSubscribeAsync(Node, BobsJid);
            await alice.PubSubSubscribeAsync("urn:example:gibtesnicht", BobsJid);

            var kennungen = sitzung.Received
                                   .Where (f => f.Contains("<subscribe", StringComparison.Ordinal))
                                   .Select(f => System.Text.RegularExpressions.Regex.Match(f, @"id='([^']+)'").Groups[1].Value)
                                   .ToList();

            Assert.That(kennungen, Has.Count.EqualTo(2));
            Assert.That(kennungen[0], Is.Not.EqualTo(kennungen[1]),
                        "Zwei Anfragen mit derselben Kennung sind nicht auseinanderzuhalten.");

        }

        #endregion

        #region TwoSubscriptions_AreBothRemembered()

        /// <summary>
        /// Wer zweimal abonniert, hat zwei Abonnements - und der Client weiss
        /// von beiden.
        /// </summary>
        /// <remarks>
        /// Bis K4 stand je Knoten genau eines in der Buchführung, und das
        /// zweite überschrieb das erste. Damit war die Kennung des ersten weg,
        /// und weg heisst hier: <b>Es liess sich nie wieder abbestellen</b> -
        /// der Dienst verlangt bei mehreren eine Kennung, und die kannte
        /// niemand mehr.
        /// </remarks>
        [Test]
        public async Task TwoSubscriptions_AreBothRemembered()
        {

            await PublishingBobAsync();

            var alice  = await ConnectClientAsync("alice");

            var erste  = await alice.PubSubSubscribeAsync(Node, BobsJid);
            var zweite = await alice.PubSubSubscribeAsync(Node, BobsJid);

            var abos   = alice.Connection.PubSub!.SubscriptionsOf(Node);

            Assert.Multiple(() =>
            {
                Assert.That(abos, Has.Count.EqualTo(2));
                Assert.That(abos.Select(a => a.SubId),
                            Is.EquivalentTo(new[] { erste?.SubId, zweite?.SubId }));
                Assert.That(erste?.SubId, Is.Not.EqualTo(zweite?.SubId));
            });

        }

        #endregion

        #region UnsubscribingWithoutASubId_WhenThereAreSeveral_IsRefused()

        /// <summary>
        /// Bei mehreren Abonnements wird ohne Kennung nicht einmal gefragt.
        /// </summary>
        /// <remarks>
        /// Der Dienst würde es mit <c>&lt;subid-required/&gt;</c> abweisen -
        /// der Client weiss das aber selbst und muss die Anfrage nicht erst
        /// stellen. <b>Wichtiger ist, was er nicht tut:</b> sich eines
        /// aussuchen. Das beendete vielleicht das falsche, und der Aufrufer
        /// hielte es für das gemeinte.
        /// </remarks>
        [Test]
        public async Task UnsubscribingWithoutASubId_WhenThereAreSeveral_IsRefused()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await alice.PubSubSubscribeAsync(Node, BobsJid);
            await alice.PubSubSubscribeAsync(Node, BobsJid);

            var sitzung = Server.SessionOf(alice.FullJid)!;
            var vorher  = sitzung.Received.Count(f => f.Contains("<unsubscribe", StringComparison.Ordinal));

            var beendet = await alice.PubSubUnsubscribeAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {

                Assert.That(beendet, Is.False);

                Assert.That(sitzung.Received.Count(f => f.Contains("<unsubscribe", StringComparison.Ordinal)),
                            Is.EqualTo(vorher),
                            "Eine Anfrage, die nur abgewiesen werden kann, muss nicht gestellt werden.");

                Assert.That(alice.Connection.PubSub!.SubscriptionsOf(Node), Has.Count.EqualTo(2),
                            "Es darf keines verschwinden, wenn keines beendet wurde.");

            });

        }

        #endregion

        #region UnsubscribingWithASubId_EndsOnlyThatOne()

        /// <summary>
        /// Mit Kennung endet genau das benannte, und das andere bleibt.
        /// </summary>
        [Test]
        public async Task UnsubscribingWithASubId_EndsOnlyThatOne()
        {

            await PublishingBobAsync();

            var alice  = await ConnectClientAsync("alice");

            var erste  = await alice.PubSubSubscribeAsync(Node, BobsJid);
            var zweite = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(await alice.PubSubUnsubscribeAsync(Node, BobsJid, erste!.SubId), Is.True);

            var abos = alice.Connection.PubSub!.SubscriptionsOf(Node);

            Assert.Multiple(() =>
            {
                Assert.That(abos,                    Has.Count.EqualTo(1));
                Assert.That(abos[0].SubId,           Is.EqualTo(zweite!.SubId));
                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.True);
            });

        }

        #endregion

        #region EachEvent_NamesItsSubscription()

        /// <summary>
        /// XEP-0060, Abschnitt 12.20: Die Meldung sagt, zu welchem Abonnement
        /// sie gehört.
        /// </summary>
        /// <remarks>
        /// Ohne diese Angabe wären zwei Zustellungen derselben Sache nicht
        /// auseinanderzuhalten - und ein Empfänger, der eines der beiden
        /// Abonnements beenden will, wüsste nicht, welches er gerade hört.
        /// </remarks>
        [Test]
        public async Task EachEvent_NamesItsSubscription()
        {

            var bob    = await PublishingBobAsync();
            var alice  = await ConnectClientAsync("alice");

            var erste  = await alice.PubSubSubscribeAsync(Node, BobsJid);
            var zweite = await alice.PubSubSubscribeAsync(Node, BobsJid);

            var gemeldet = new List<PubSubEvent>();
            alice.OnPubSubEvent += e => { lock (gemeldet) gemeldet.Add(e); };

            Assert.That(await bob.PubSubPublishAsync(Node, "2", Payload("Regen"), bob.BareJid), Is.True);

            await WaitFor(() => { lock (gemeldet) return gemeldet.Count > 1; },
                          "beide Meldungen");

            lock (gemeldet)
                Assert.That(gemeldet.Select(e => e.SubId),
                            Is.EquivalentTo(new[] { erste!.SubId, zweite!.SubId }));

        }

        #endregion

        #region AfterTheLastUnsubscribe_TheEventsAreRejectedAgain()

        /// <summary>
        /// Ist das letzte Abonnement beendet, ist der Absender wieder ein
        /// Fremder.
        /// </summary>
        /// <remarks>
        /// Die Erlaubnis hängt an der Buchführung; bliebe dort ein Rest
        /// stehen, bliebe auch die Erlaubnis - und der Spoofing-Schutz wäre
        /// nach dem ersten Abonnement für diesen Knoten dauerhaft offen.
        /// </remarks>
        [Test]
        public async Task AfterTheLastUnsubscribe_TheEventsAreRejectedAgain()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(await alice.PubSubUnsubscribeAsync(Node, BobsJid, abo!.SubId), Is.True);

            PubSubEvent? gemeldet = null;
            alice.OnPubSubEvent += e => gemeldet = e;

            await Server.SessionOf(alice.FullJid)!.SendAsync(
                $"<message from='{BobsJid}' type='headline' to='{alice.FullJid}'>" +
                "<event xmlns='http://jabber.org/protocol/pubsub#event'>" +
                $"<items node='{Node}'>" +
                "<item id='9'><wetter xmlns='urn:example:x'>nachträglich</wetter></item>" +
                "</items></event></message>");

            await WaitAgainst(() => gemeldet is not null,
                              "ein Ereignis für einen Knoten, der nicht mehr abonniert ist");

        }

        #endregion

        #region TheOptions_AreReadFromTheService()

        /// <summary>
        /// XEP-0060, Abschnitt 6.3.1: Was gilt, sagt der Dienst.
        /// </summary>
        /// <remarks>
        /// Nicht der Client - er kennt nur, was er selbst gesetzt hat, und das
        /// ist etwas anderes: Ein anderes Gerät desselben Kontos kann dasselbe
        /// Abonnement umgestellt haben.
        /// </remarks>
        [Test]
        public async Task TheOptions_AreReadFromTheService()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            var optionen = await alice.PubSubGetOptionsAsync(Node, BobsJid, abo!.SubId);

            Assert.Multiple(() =>
            {
                Assert.That(optionen,          Is.Not.Null);
                Assert.That(optionen!.Deliver, Is.True,
                            "Zugestellt wird, solange niemand widerspricht.");
            });

        }

        #endregion

        #region SettingTheOptions_SilencesTheSubscription()

        /// <summary>
        /// Gesetzt, bestätigt, gemerkt - und die Meldungen bleiben aus.
        /// </summary>
        [Test]
        public async Task SettingTheOptions_SilencesTheSubscription()
        {

            var bob   = await PublishingBobAsync();
            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(await alice.PubSubSetOptionsAsync(Node,
                                                          new PubSubSubscriptionOptions(Deliver: false),
                                                          BobsJid,
                                                          abo!.SubId),
                        Is.True);

            Assert.That(alice.Connection.PubSub!.SubscriptionsOf(Node)[0].Options?.Deliver,
                        Is.False,
                        "Was der Dienst bestätigt hat, gehört in die eigene Buchführung.");

            Assert.That((await alice.PubSubGetOptionsAsync(Node, BobsJid, abo.SubId))?.Deliver,
                        Is.False,
                        "Und beim Nachfragen muss dasselbe herauskommen.");

            PubSubEvent? gemeldet = null;
            alice.OnPubSubEvent += e => gemeldet = e;

            Assert.That(await bob.PubSubPublishAsync(Node, "2", Payload("still"), bob.BareJid), Is.True);

            await WaitAgainst(() => gemeldet is not null,
                              "eine Meldung an ein stillgelegtes Abonnement");

        }

        #endregion

        #region ARejectedSetting_IsNotRecorded()

        /// <summary>
        /// Eine abgelehnte Einstellung darf nicht als geltende dastehen.
        /// </summary>
        /// <remarks>
        /// Derselbe Fehler wie beim Abonnieren in D71, eine Ebene tiefer: Wer
        /// die Antwort nicht liest, hält seinen Wunsch für den Zustand. Hier
        /// lehnt der Dienst ab, weil das Abonnement einem anderen gehört.
        /// </remarks>
        [Test]
        public async Task ARejectedSetting_IsNotRecorded()
        {

            await PublishingBobAsync();

            var carol = await ConnectClientAsync("carol");
            var fremd = await carol.PubSubSubscribeAsync(Node, BobsJid);

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(await alice.PubSubSetOptionsAsync(Node,
                                                          new PubSubSubscriptionOptions(Deliver: false),
                                                          BobsJid,
                                                          fremd!.SubId),
                        Is.False,
                        "Die Kennung gehört zu Carols Abonnement.");

            Assert.That(alice.Connection.PubSub!.SubscriptionsOf(Node)[0].Options,
                        Is.Null,
                        "Was nicht angenommen wurde, ist auch nicht bekannt.");

            Assert.That(abo!.SubId, Is.Not.EqualTo(fremd!.SubId));

        }

        #endregion

        #region CreatingANode_WithItsConfiguration_IsConfirmed()

        /// <summary>
        /// Anlegen und einstellen in einem Zug - und der Client erfährt, ob
        /// es geklappt hat.
        /// </summary>
        /// <remarks>
        /// Zwei Schritte hätten eine Lücke: Zwischen dem Anlegen und dem
        /// Einstellen stünde der Knoten offen, und wer in dieser Zeit fragt,
        /// bekommt.
        /// </remarks>
        [Test]
        public async Task CreatingANode_WithItsConfiguration_IsConfirmed()
        {

            var bob = await ConnectClientAsync("bob");

            Assert.That(await bob.PubSubCreateNodeAsync("urn:example:neu",
                                                        new PubSubNodeConfiguration(PubSubAccessModel.Presence,
                                                                                    MaxItems: 3),
                                                        bob.BareJid),
                        Is.True);

            var gelesen = await bob.PubSubGetNodeConfigAsync("urn:example:neu", bob.BareJid);

            Assert.Multiple(() =>
            {
                Assert.That(gelesen,              Is.Not.Null);
                Assert.That(gelesen!.AccessModel, Is.EqualTo(PubSubAccessModel.Presence));
                Assert.That(gelesen!.MaxItems,    Is.EqualTo(3));
            });

        }

        #endregion

        #region CreatingANodeTwice_IsReported()

        /// <summary>
        /// Der zweite Versuch wird als Fehlschlag gemeldet und nicht als
        /// Erfolg.
        /// </summary>
        [Test]
        public async Task CreatingANodeTwice_IsReported()
        {

            var bob = await ConnectClientAsync("bob");

            Assert.That(await bob.PubSubCreateNodeAsync("urn:example:neu", service: bob.BareJid), Is.True);
            Assert.That(await bob.PubSubCreateNodeAsync("urn:example:neu", service: bob.BareJid), Is.False,
                        "Was es gibt, wird nicht noch einmal angelegt.");

        }

        #endregion

        #region ConfiguringSomebodyElsesNode_IsReported()

        /// <summary>
        /// Eine abgewiesene Einstellung wird als solche gemeldet.
        /// </summary>
        [Test]
        public async Task ConfiguringSomebodyElsesNode_IsReported()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubConfigureNodeAsync(Node,
                                                             new PubSubNodeConfiguration(PersistItems: false),
                                                             BobsJid),
                        Is.False);

            Assert.That(Server.GetAccount(BobsJid)!.PepNodeConfiguration(Node)!.PersistItems,
                        Is.True,
                        "Und sie darf nichts geändert haben.");

        }

        #endregion

        #region AnErrorCarryingAForm_IsStillARejection()

        /// <summary>
        /// Ein <c>type='error'</c> bleibt eine Absage, auch wenn ein
        /// vollständiges Knotenformular darin steht.
        /// </summary>
        /// <remarks>
        /// Dieselbe Stelle wie bei der Zusage in D71: Ohne die Prüfung auf den
        /// Typ hinge die Ablehnung allein daran, dass in einer Fehlerantwort
        /// zufällig kein Formular steht. Das ist keine Entscheidung, sondern
        /// ein Zufall, der lange gutgeht.
        /// </remarks>
        [Test]
        public async Task AnErrorCarryingAForm_IsStillARejection()
        {

            var bob = await PublishingBobAsync();

            PlayTheService("<configure",
                           "<iq type='error' id='{id}'>" +
                           "<pubsub xmlns='http://jabber.org/protocol/pubsub#owner'>" +
                           $"<configure node='{Node}'>" +
                           "<x xmlns='jabber:x:data' type='form'>" +
                           "<field var='FORM_TYPE' type='hidden'>" +
                           "<value>http://jabber.org/protocol/pubsub#node_config</value></field>" +
                           "<field var='pubsub#access_model' type='list-single'><value>open</value></field>" +
                           "</x></configure></pubsub>" +
                           "<error type='auth'><forbidden xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></iq>");

            Assert.That(await bob.PubSubGetNodeConfigAsync(Node, bob.BareJid), Is.Null);

        }

        #endregion

        #region AResultWithoutAForm_IsNoNodeConfiguration()

        /// <summary>
        /// Auch beim Knoten gilt: Ein <c>result</c> ohne Formular ist keine
        /// Auskunft.
        /// </summary>
        /// <remarks>
        /// Hier wäre die Vorgabe besonders irreführend, denn sie sagt
        /// <c>open</c> - der Client zeigte einen geschützten Knoten als offen
        /// an.
        /// </remarks>
        [Test]
        public async Task AResultWithoutAForm_IsNoNodeConfiguration()
        {

            var bob = await PublishingBobAsync();

            PlayTheService("<configure", "<iq type='result' id='{id}'/>");

            Assert.That(await bob.PubSubGetNodeConfigAsync(Node, bob.BareJid), Is.Null);

        }

        #endregion

        #region AResultWithoutAForm_IsNoAnswerAboutTheOptions()

        /// <summary>
        /// Ein <c>result</c> ohne Formular sagt über die Einstellungen nichts.
        /// </summary>
        /// <remarks>
        /// Dieselbe Stelle wie bei der Zusage in D71, nur eine Ebene tiefer:
        /// Aus dem Ausbleiben eines Fehlers auf einen Zustand zu schliessen ist
        /// die bequemste Art, sich etwas einzubilden. Die Vorgaben einzusetzen
        /// wäre hier besonders heikel, denn sie sagen „wird zugestellt" - der
        /// Client hielte ein stillgelegtes Abonnement für ein lautes.
        /// </remarks>
        [Test]
        public async Task AResultWithoutAForm_IsNoAnswerAboutTheOptions()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            PlayTheService("<options", "<iq type='result' id='{id}'/>");

            Assert.That(await alice.PubSubGetOptionsAsync(Node, BobsJid, abo!.SubId), Is.Null);

        }

        #endregion

        #region SettingOptions_MarksOnlyTheNamedSubscription()

        /// <summary>
        /// Die Einstellung gehört einem Abonnement, nicht dem Knoten.
        /// </summary>
        /// <remarks>
        /// Der Fehler wäre in der eigenen Buchführung und deshalb still: Der
        /// Dienst stellt das richtige ein, der Client merkt es sich am
        /// falschen mit - und von da an zeigt er einen Zustand an, den es
        /// nicht gibt.
        /// </remarks>
        [Test]
        public async Task SettingOptions_MarksOnlyTheNamedSubscription()
        {

            await PublishingBobAsync();

            var alice  = await ConnectClientAsync("alice");

            var erste  = await alice.PubSubSubscribeAsync(Node, BobsJid);
            var zweite = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(await alice.PubSubSetOptionsAsync(Node,
                                                          new PubSubSubscriptionOptions(Deliver: false),
                                                          BobsJid,
                                                          erste!.SubId),
                        Is.True);

            var abos = alice.Connection.PubSub!.SubscriptionsOf(Node);

            Assert.Multiple(() =>
            {

                Assert.That(abos.First(a => a.SubId == erste!.SubId).Options?.Deliver, Is.False);

                Assert.That(abos.First(a => a.SubId == zweite!.SubId).Options, Is.Null,
                            "Über das andere Abonnement ist nichts bekannt - und nichts zu behaupten.");

            });

        }

        #endregion

        #region TheOfferedForm_IsReadLeniently()

        /// <summary>
        /// Ein Angebot ist eine Auskunft: Was dieser Client nicht kennt,
        /// übergeht er - was nicht dasteht, erfindet er nicht.
        /// </summary>
        /// <remarks>
        /// <b>Die Gegenrichtung zu <c>TryRead</c></b>, das ein abgeschicktes
        /// Formular streng liest. Kein Widerspruch, sondern die Richtung: Ein
        /// fremder Dienst bietet ein Dutzend Felder an, von denen dieser Client
        /// nur eines setzen kann - wer daran scheitert, kann mit keinem echten
        /// Dienst sprechen. Ein übergangenes Feld in einer <i>Anweisung</i> ist
        /// dagegen eine verworfene Anweisung.
        /// </remarks>
        [Test]
        public void TheOfferedForm_IsReadLeniently()
        {

            static System.Xml.Linq.XElement Angebot(String inhalt)
                => System.Xml.Linq.XElement.Parse($"<x xmlns='jabber:x:data' type='form'>{inhalt}</x>");

            const String deliver = "<field var='pubsub#deliver' type='boolean'><value>0</value></field>";

            Assert.Multiple(() =>
            {

                Assert.That(PubSubSubscriptionOptions.TryReadForm(
                                Angebot(deliver +
                                        "<field var='pubsub#digest' type='boolean'><value>1</value></field>"),
                                out var gelesen),
                            Is.True,
                            "Ein Feld, das dieser Client nicht setzen kann, darf ihn nicht aufhalten.");

                Assert.That(gelesen!.Deliver, Is.False);

                Assert.That(PubSubSubscriptionOptions.TryReadForm(
                                Angebot("<field var='pubsub#digest' type='boolean'><value>1</value></field>"),
                                out _),
                            Is.False,
                            "Ein Angebot ohne die Zustellung sagt über sie nichts - " +
                            "die Vorgabe anzunehmen hiesse, sie zu erfinden.");

            });

        }

        #endregion

        #region OptionsWithoutASubId_WhenThereAreSeveral_AreRefused()

        /// <summary>
        /// Auch beim Einstellen sucht sich der Client keines aus.
        /// </summary>
        [Test]
        public async Task OptionsWithoutASubId_WhenThereAreSeveral_AreRefused()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            await alice.PubSubSubscribeAsync(Node, BobsJid);
            await alice.PubSubSubscribeAsync(Node, BobsJid);

            var sitzung = Server.SessionOf(alice.FullJid)!;
            var vorher  = sitzung.Received.Count(f => f.Contains("<options", StringComparison.Ordinal));

            var gelesen = await alice.PubSubGetOptionsAsync(Node, BobsJid);

            Assert.Multiple(() =>
            {

                Assert.That(gelesen, Is.Null);

                Assert.That(sitzung.Received.Count(f => f.Contains("<options", StringComparison.Ordinal)),
                            Is.EqualTo(vorher),
                            "Eine Anfrage, die nur abgewiesen werden kann, muss nicht gestellt werden.");

            });

        }

        #endregion

        #region Unsubscribing_SendsTheSubId_AndClearsTheRecord()

        /// <summary>
        /// Beim Abbestellen geht die Kennung mit, die der Dienst vergeben hat.
        /// </summary>
        [Test]
        public async Task Unsubscribing_SendsTheSubId_AndClearsTheRecord()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");
            var abo   = await alice.PubSubSubscribeAsync(Node, BobsJid);

            Assert.That(abo?.SubId, Is.Not.Null);

            var sitzung = Server.SessionOf(alice.FullJid)!;

            Assert.That(await alice.PubSubUnsubscribeAsync(Node, BobsJid), Is.True);

            Assert.Multiple(() =>
            {

                Assert.That(sitzung.Received.Any(f => f.Contains("<unsubscribe", StringComparison.Ordinal) &&
                                                      f.Contains($"subid='{abo!.SubId}'", StringComparison.Ordinal)),
                            Is.True,
                            "Die Kennung aus der Zusage gehört in das Abbestellen.");

                Assert.That(alice.Connection.PubSub!.IsSubscribed(Node), Is.False);

            });

        }

        #endregion

        #region ARejectedPublish_IsReported()

        /// <summary>
        /// In einen fremden PEP-Knoten darf niemand schreiben - und der
        /// Aufrufer erfährt es.
        /// </summary>
        [Test]
        public async Task ARejectedPublish_IsReported()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubPublishAsync(Node, "99", Payload("gefälscht"), BobsJid),
                        Is.False,
                        "Ein abgelehntes Veröffentlichen darf nicht als gelungen gelten.");

        }

        #endregion

        #region AfterSubscribing_TheEventsReachTheClient()

        /// <summary>
        /// Und damit das Abonnement etwas wert ist: Die Benachrichtigung kommt
        /// bis zum Aufrufer durch.
        /// </summary>
        /// <remarks>
        /// <b>Bis hierher tat sie das nicht.</b> Der Spoofing-Schutz verglich
        /// den Absender mit dem PubSub-Dienst der Domain - eine PEP-Meldung
        /// kommt aber vom Konto selbst (XEP-0163) und wurde deshalb jedes Mal
        /// als Fälschung verworfen. Aufgefallen ist es nie, weil bis zu diesem
        /// Punkt niemand ein Abonnement hatte, dessen Meldungen jemand
        /// erwartete.
        /// </remarks>
        [Test]
        public async Task AfterSubscribing_TheEventsReachTheClient()
        {

            var bob    = await PublishingBobAsync();
            var alice  = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Not.Null);

            PubSubEvent? gemeldet = null;
            alice.OnPubSubEvent += e => gemeldet = e;

            Assert.That(await bob.PubSubPublishAsync(Node, "2", Payload("Regen"), bob.BareJid), Is.True);

            await WaitFor(() => gemeldet is not null, "das gemeldete Ereignis");

            Assert.Multiple(() =>
            {
                Assert.That(gemeldet!.NodeId, Is.EqualTo(Node));
                Assert.That(gemeldet!.Items,  Has.Count.EqualTo(1));
                Assert.That(gemeldet!.Items[0].Payload, Does.Contain("Regen"));
            });

        }

        #endregion

        #region AnEventFromSomebodyElse_IsStillRejected()

        /// <summary>
        /// Der Spoofing-Schutz bleibt: Ein Abonnement bei Bob macht Carol nicht
        /// zur Quelle.
        /// </summary>
        [Test]
        public async Task AnEventFromSomebodyElse_IsStillRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Not.Null);

            PubSubEvent? gemeldet = null;
            alice.OnPubSubEvent += e => gemeldet = e;

            await Server.SessionOf(alice.FullJid)!.SendAsync(
                $"<message from='carol@{Server.Domain}' type='headline' to='{alice.FullJid}'>" +
                "<event xmlns='http://jabber.org/protocol/pubsub#event'>" +
                $"<items node='{Node}'>" +
                "<item id='3'><wetter xmlns='urn:example:x'>erfunden</wetter></item>" +
                "</items></event></message>");

            await WaitAgainst(() => gemeldet is not null,
                              "ein Ereignis von einem, bei dem niemand abonniert hat");

        }

        #endregion

        #region AnEventForAnotherNode_IsStillRejected()

        /// <summary>
        /// Und die Erlaubnis gilt dem Knoten, nicht dem Absender.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bestünde eine Umsetzung, die nach dem ersten
        /// Abonnement einfach alles von Bob durchliesse - er könnte dann in
        /// jeden erdachten Knoten schreiben, den dieser Client nie bestellt
        /// hat.
        /// </remarks>
        [Test]
        public async Task AnEventForAnotherNode_IsStillRejected()
        {

            await PublishingBobAsync();

            var alice = await ConnectClientAsync("alice");

            Assert.That(await alice.PubSubSubscribeAsync(Node, BobsJid), Is.Not.Null);

            PubSubEvent? gemeldet = null;
            alice.OnPubSubEvent += e => gemeldet = e;

            await Server.SessionOf(alice.FullJid)!.SendAsync(
                $"<message from='{BobsJid}' type='headline' to='{alice.FullJid}'>" +
                "<event xmlns='http://jabber.org/protocol/pubsub#event'>" +
                "<items node='urn:example:nichtbestellt'>" +
                "<item id='4'><wetter xmlns='urn:example:x'>ungefragt</wetter></item>" +
                "</items></event></message>");

            await WaitAgainst(() => gemeldet is not null,
                              "ein Ereignis für einen Knoten, den niemand abonniert hat");

        }

        #endregion

    }

}
