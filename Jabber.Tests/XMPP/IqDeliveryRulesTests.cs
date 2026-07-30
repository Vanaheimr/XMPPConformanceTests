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

using System.Collections.Concurrent;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6121, Abschnitt 8.5.2.1.3: Eine IQ-Anfrage an einen Bare-JID wird
    /// nicht zugestellt, sondern vom Server selbst beantwortet.
    /// </summary>
    /// <remarks>
    /// Der Abschnitt sagt es doppelt — „the server itself MUST reply on behalf
    /// of the user" <b>und</b> „MUST NOT deliver the IQ stanza to any of the
    /// user's available resources". Diese Verdopplung hat einen Grund, und er
    /// liegt in der Natur von IQ.
    ///
    /// IQ ist ein Frage-Antwort-Paar, über die <c>id</c> zusammengehalten, und
    /// jede empfangene Anfrage <b>muss</b> beantwortet werden (RFC 6120,
    /// Abschnitt 8.2.3, Regel 3). Wer eine Anfrage an alle Resourcen verteilt,
    /// bekommt von allen eine Antwort: Der Fragende hält drei Antworten auf eine
    /// <c>id</c> in der Hand und kann nicht entscheiden, welche gilt. Bei einer
    /// Nachricht wäre mehrfache Zustellung höchstens lästig; hier bricht sie das
    /// Verfahren.
    ///
    /// Genau das tat dieser Server: Er reichte jede IQ-Anfrage an eine fremde
    /// Adresse ins Routing, und das verteilte an einen Bare-JID an jede Sitzung,
    /// die es fand.
    /// </remarks>
    [TestFixture]
    public class IqDeliveryRulesTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Bob => $"bob@{Server.Domain}";

        /// <summary>Eine Ping-Anfrage (XEP-0199) an eine beliebige Adresse.</summary>
        private static String Anfrage(String to, String id)
            => $"<iq type='get' id='{id}' to='{to}'>" +
               "<ping xmlns='urn:xmpp:ping'/></iq>";

        /// <summary>
        /// Meldet einen weiteren Client desselben Kontos an und zählt, was für
        /// ihn hereinkommt.
        /// </summary>
        private async Task<(XMPPClient, ConcurrentQueue<String>)> ResourceAsync(String localPart,
                                                                               String resource)
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client   = CreateClient(localPart);
            var eingang  = new ConcurrentQueue<String>();

            client.Connection.Resource  = resource;
            client.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("urn:xmpp:ping", StringComparison.Ordinal))
                {
                    eingang.Enqueue(x);
                }
            };

            await client.ConnectAsync();

            return (client, eingang);

        }

        /// <summary>Sammelt die Stanza-Fehler eines Clients.</summary>
        private static ConcurrentQueue<StanzaError> Fehlerkorb(XMPPClient client)
        {

            var korb = new ConcurrentQueue<StanzaError>();
            client.Connection.OnStanzaError += (from, e) => korb.Enqueue(e);

            return korb;

        }

        /// <summary>Zählt die eingehenden IQ-Stanzas mit dieser Id.</summary>
        private static ConcurrentQueue<String> AntwortKorb(XMPPClient client, String id)
        {

            var korb = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("<iq",          StringComparison.Ordinal) &&
                    x.Contains($"id='{id}'",   StringComparison.Ordinal))
                {
                    korb.Enqueue(x);
                }
            };

            return korb;

        }

        #endregion


        #region AnIqToAnAccount_IsAnsweredOnceAndReachesNoResource()

        /// <summary>
        /// Der Kern: Eine Anfrage an den Bare-JID erreicht <b>keine</b> Resource
        /// und wird <b>einmal</b> beantwortet.
        /// </summary>
        /// <remarks>
        /// Beide Hälften in einem Test, weil sie zusammen die Aussage ergeben.
        /// „Erreicht keine Resource" allein wäre auch erfüllt, wenn die Anfrage
        /// stillschweigend verschwände — und das verstösst gegen die
        /// Antwortpflicht. „Wird beantwortet" allein wäre auch erfüllt, wenn
        /// zusätzlich alle Resourcen antworten.
        ///
        /// Zwei Resourcen und nicht eine: Mit nur einer sähe eine Antwort vom
        /// Server genauso aus wie eine von der Resource, und der eigentliche
        /// Schaden — mehrere Antworten auf eine <c>id</c> — wäre unsichtbar.
        /// </remarks>
        [Test]
        public async Task AnIqToAnAccount_IsAnsweredOnceAndReachesNoResource()
        {

            var alice = await ConnectClientAsync("alice");

            var (handy,   amHandy)   = await ResourceAsync("bob", "Handy");
            var (rechner, amRechner) = await ResourceAsync("bob", "Rechner");

            var antworten = AntwortKorb(alice, "an-das-konto");
            var fehler    = Fehlerkorb(alice);

            await alice.SendRawAsync(Anfrage(Bob, "an-das-konto"));

            await WaitFor(() => !fehler.IsEmpty, "die Antwort des Servers");

            // Den beiden Resourcen Zeit geben, die Anfrage doch zu bekommen.
            await Task.Delay(TimeSpan.FromSeconds(1));

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

                Assert.That(antworten, Has.Count.EqualTo(1),
                            "Genau eine Antwort auf eine id - sonst weiss der " +
                            "Fragende nicht, welche gilt.");

                Assert.That(amHandy,   Is.Empty, "Die Anfrage darf keine Resource erreichen.");
                Assert.That(amRechner, Is.Empty, "Auch nicht die zweite.");

                Assert.That(handy.FullJid,   Is.Not.EqualTo(rechner.FullJid),
                            "Der Aufbau taugt nur mit zwei verschiedenen Resourcen.");

            });

        }

        #endregion

        #region AnIqToAnAbsentAccount_IsAnsweredToo()

        /// <summary>
        /// Abschnitt 8.5.2.2.3 verlangt wörtlich dasselbe wie 8.5.2.1.3: Ob
        /// jemand angemeldet ist, ändert die Antwort nicht.
        /// </summary>
        /// <remarks>
        /// Der Unterschied zur Nachricht ist bemerkenswert: Dort hängt an genau
        /// dieser Frage die Offline-Ablage. Bei IQ gibt es nichts aufzubewahren —
        /// eine Frage, deren Antwort erst morgen käme, hat der Fragende längst
        /// aufgegeben. Deshalb fragt der Zustellweg gar nicht erst, ob eine
        /// Resource da ist, und dieser Test hält fest, dass das richtig ist.
        /// </remarks>
        [Test]
        public async Task AnIqToAnAbsentAccount_IsAnsweredToo()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Anfrage(Bob, "an-den-abwesenden"));

            await WaitFor(() => !fehler.IsEmpty, "die Antwort des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

        }

        #endregion

        #region AnIqToAnUnknownAccount_IsAnswered()

        /// <summary>
        /// Abschnitt 8.5.1: Für ein Konto, das es nicht gibt, <b>muss</b> eine
        /// IQ-Anfrage beantwortet werden — anders als eine Nachricht.
        /// </summary>
        /// <remarks>
        /// Der Unterschied ist kein Versehen des RFC, sondern die Folge der
        /// Antwortpflicht: Bei einer Nachricht darf der Server schweigen und
        /// verrät damit nicht, welche Konten es gibt; bei einer Anfrage muss er
        /// antworten, und die Antwort ist dieselbe wie für ein vorhandenes
        /// Konto ohne erreichbare Resource. Genau deshalb ist sie ungefährlich —
        /// <c>&lt;service-unavailable/&gt;</c> unterscheidet die beiden Fälle
        /// nicht.
        ///
        /// Das ist der zweite Teil der Aussage und der Grund, warum dieser Test
        /// neben dem vorigen steht: Wären die Antworten verschieden, hätte der
        /// Server ein Verzeichnis seiner Konten preisgegeben.
        /// </remarks>
        [Test]
        public async Task AnIqToAnUnknownAccount_IsAnswered()
        {

            var alice = await ConnectClientAsync("alice");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Anfrage($"gibtsnicht@{Server.Domain}", "an-niemanden"));

            await WaitFor(() => !fehler.IsEmpty, "die Antwort des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"),
                        "Ein unbekanntes Konto muss dieselbe Antwort geben wie ein " +
                        "bekanntes ohne erreichbare Resource.");

        }

        #endregion

        #region AnIqToAResource_IsDelivered()

        /// <summary>
        /// Die Gegenprobe: An eine passende Resource gerichtet wird die Anfrage
        /// zugestellt (Abschnitt 8.5.3.1).
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn der Server jede
        /// IQ-Anfrage an einen anderen Nutzer abwiese — und damit wäre jede
        /// Verständigung zwischen zwei Clients unmöglich. Ein Ping zwischen
        /// zwei Menschen, eine Versionsabfrage, eine Dateiübertragung: alles
        /// geht an eine Full-JID.
        /// </remarks>
        [Test]
        public async Task AnIqToAResource_IsDelivered()
        {

            var alice = await ConnectClientAsync("alice");
            var (bob, beiBob) = await ResourceAsync("bob", "Handy");

            await alice.SendRawAsync(Anfrage(bob.FullJid!, "an-die-resource"));

            await WaitFor(() => !beiBob.IsEmpty, "die Anfrage bei der Resource");

            Assert.Pass();

        }

        #endregion

        #region AnIqToAVanishedResource_IsAnswered()

        /// <summary>
        /// Abschnitt 8.5.3.2.3: Keine passende Resource, also
        /// <c>&lt;service-unavailable/&gt;</c> — hier ohne Ausnahme für die Art.
        /// </summary>
        [Test]
        public async Task AnIqToAVanishedResource_IsAnswered()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Anfrage($"{Bob}/gibtsnichtmehr", "an-die-verschwundene"));

            await WaitFor(() => !fehler.IsEmpty, "die Antwort des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

        }

        #endregion

        #region AResultToAVanishedResource_IsNotAnswered()

        /// <summary>
        /// RFC 6120, Abschnitt 8.2.3, Regel 4: Eine Antwort wird nie
        /// beantwortet.
        /// </summary>
        /// <remarks>
        /// Hier stehen zwei Vorschriften gegeneinander, und das ist der Grund
        /// für diesen Test. Abschnitt 8.5.3.2.3 verlangt für „eine IQ-Stanza"
        /// ohne passende Resource einen Fehler und unterscheidet die Art nicht;
        /// Regel 4 verbietet, eine Antwort zu beantworten. Regel 4 gewinnt: Ein
        /// Fehler auf ein <c>result</c> ginge an jemanden, der nichts gefragt
        /// hat, und trüge die <c>id</c> einer Frage, die er selbst beantwortet
        /// hat. Er kann damit nichts anfangen — und wenn er den Fehler seinerseits
        /// beantwortete, wäre es eine Schleife.
        ///
        /// Geprüft werden beide Arten von Antwort, denn nur für <c>error</c>
        /// ist das Verbot offensichtlich.
        /// </remarks>
        [Test]
        public async Task AResultToAVanishedResource_IsNotAnswered()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler     = Fehlerkorb(alice);
            var antworten  = AntwortKorb(alice, "keine-frage");

            await alice.SendRawAsync(
                      $"<iq type='result' id='keine-frage' to='{Bob}/gibtsnichtmehr'/>");

            await alice.SendRawAsync(
                      $"<iq type='error' id='auch-keine' to='{Bob}/gibtsnichtmehr'>" +
                      "<error type='cancel'>" +
                      "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                      "</error></iq>");

            await WaitAgainst(() => !fehler.IsEmpty || !antworten.IsEmpty,
                              "eine Antwort auf eine Antwort");

        }

        #endregion

        #region AResultToAnAccount_ReachesNoResource()

        /// <summary>
        /// Eine Antwort an den Bare-JID gehört niemandem: Sie wird nicht
        /// verteilt und nicht beantwortet.
        /// </summary>
        /// <remarks>
        /// Eine Antwort gehört genau der Resource, die gefragt hat — und ein
        /// Bare-JID benennt keine. Sie an alle zu verteilen wäre schlimmer als
        /// sie zu verwerfen: Jede Resource sähe eine Antwort auf eine Frage, die
        /// sie nie gestellt hat, und hielte womöglich ihre eigene offene
        /// <c>id</c> für erledigt.
        /// </remarks>
        [Test]
        public async Task AResultToAnAccount_ReachesNoResource()
        {

            var alice = await ConnectClientAsync("alice");

            var (handy,   amHandy)   = await ResourceAsync("bob", "Handy");
            var (rechner, amRechner) = await ResourceAsync("bob", "Rechner");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(
                      $"<iq type='result' id='an-alle' to='{Bob}'>" +
                      "<ping xmlns='urn:xmpp:ping'/></iq>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(amHandy,   Is.Empty);
                Assert.That(amRechner, Is.Empty);
                Assert.That(fehler,    Is.Empty, "Und beantwortet wird sie auch nicht.");
            });

        }

        #endregion

        #region TheAnswerCarriesTheRequestedAddressAsSender()

        /// <summary>
        /// Die Antwort kommt von der Adresse, an die gefragt wurde, und trägt
        /// die <c>id</c> der Frage.
        /// </summary>
        /// <remarks>
        /// Beides ist die Voraussetzung dafür, dass der Fragende sie überhaupt
        /// zuordnen kann. Die <c>id</c> hält das Paar zusammen (RFC 6120,
        /// Abschnitt 8.2.3, Regel 3: „The response MUST preserve the 'id'
        /// attribute of the request"), und der Absender beantwortet die Frage
        /// „was ist aus meiner Anfrage an Bob geworden". Käme sie vom Server,
        /// wäre es die Antwort auf eine andere Frage — und ein Client, der
        /// Antworten dem Gefragten zuordnet, fände keine Zuordnung.
        /// </remarks>
        [Test]
        public async Task TheAnswerCarriesTheRequestedAddressAsSender()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var rohe = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("mit-absender", StringComparison.Ordinal))
                {
                    rohe.Enqueue(x);
                }
            };

            await alice.SendRawAsync(Anfrage(Bob, "mit-absender"));

            await WaitFor(() => !rohe.IsEmpty, "die Antwort auf dem Draht");

            rohe.TryDequeue(out var stanza);

            Assert.Multiple(() =>
            {

                Assert.That(stanza, Does.Contain($"from='{Bob}'"),
                            "Die Antwort kommt von der Adresse, an die gefragt wurde.");

                Assert.That(stanza, Does.Contain($"to='{alice.FullJid}'"),
                            "Und geht an die Resource, die gefragt hat.");

                Assert.That(stanza, Does.Contain("id='mit-absender'"));

            });

        }

        #endregion

    }

}
