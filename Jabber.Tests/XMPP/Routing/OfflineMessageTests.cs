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
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Offline-Ablage nach RFC 6121, Abschnitt 8.5.2.2.1 und XEP-0160:
    /// Wer gerade keine erreichbare Resource hat, verliert seine Nachrichten
    /// nicht.
    /// </summary>
    /// <remarks>
    /// Der Abschnitt lässt dem Server zwei Wege und verbietet den dritten: Er
    /// darf die Nachricht ablegen oder dem Absender
    /// <c>&lt;service-unavailable/&gt;</c> antworten - stillschweigend
    /// verwerfen darf er sie nicht. Genau das tat dieser Server bis hierher,
    /// und es ist der schlechteste denkbare Ausgang: Der Absender hält seine
    /// Nachricht für zugestellt, der Empfänger hat nie erfahren, dass es sie
    /// gab, und niemand kann den Verlust bemerken.
    ///
    /// Beide erlaubten Wege sind hier umgesetzt, weil sie sich gegenseitig
    /// begrenzen: Ohne die Ablage wäre die Ablehnung der Regelfall, und ohne
    /// die Ablehnung hätte eine voll gelaufene Ablage keinen Ausweg mehr.
    /// </remarks>
    [TestFixture]
    public class OfflineMessageTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Alice => $"alice@{Server.Domain}";
        private String Bob   => $"bob@{Server.Domain}";

        /// <summary>
        /// Ein noch nicht verbundener Client mit angehängtem Eingangskorb.
        /// </summary>
        /// <remarks>
        /// Der Korb wird <b>vor</b> dem Verbinden angehängt: Eine nachgereichte
        /// Nachricht kommt unmittelbar nach der ersten Presence, und ein erst
        /// danach angemeldeter Empfänger verpasst sie je nach Zeitverlauf. Ein
        /// Test, der so scheitert, sieht aus wie ein Fehler im Server.
        /// </remarks>
        private (XMPPClient, ConcurrentQueue<XMPPMessage>) VorbereiteterClient(String   localPart,
                                                                              Int32?   priority = null)
        {

            var client   = CreateClient(localPart);
            var eingang  = new ConcurrentQueue<XMPPMessage>();

            client.Connection.PresencePriority  = priority;
            client.OnMessage                   += m => eingang.Enqueue(m);

            return (client, eingang);

        }

        /// <summary>Meldet einen Client ab und wartet, bis der Server das sieht.</summary>
        private async Task AbmeldenAsync(XMPPClient client, String bareJid)
        {
            await client.DisconnectAsync();
            await WaitFor(() => Server.SessionsOf(bareJid).Count == 0,
                          $"das Ende der Sitzung von {bareJid}");
        }

        /// <summary>Wartet, bis so viele Nachrichten für ein Konto abgelegt sind.</summary>
        private async Task WarteAufAblage(String bareJid, Int32 count)
            => await WaitFor(() => Server.GetAccount(bareJid)?.OfflineMessages.Count == count,
                             $"{count} abgelegte Nachricht(en) für {bareJid}");

        #endregion


        #region AChatToAnOfflineAccount_ArrivesAtTheNextLogin()

        /// <summary>
        /// Der Kern: Bob ist nicht da, als Alice schreibt - und liest es beim
        /// nächsten Anmelden, in der Reihenfolge des Eingangs.
        /// </summary>
        /// <remarks>
        /// Zwei Nachrichten und nicht eine, weil die Reihenfolge zur Sache
        /// gehört. Ein Gespräch, das verdreht nachgereicht wird, ist schwerer
        /// zu lesen als eines, das ganz fehlt: Der Leser hält die Antwort für
        /// die Frage.
        /// </remarks>
        [Test]
        public async Task AChatToAnOfflineAccount_ArrivesAtTheNextLogin()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Erste");
            await alice.SendMessageAsync(Bob, "Zweite");

            await WarteAufAblage(Bob, 2);

            // Erst jetzt kommt Bob - die Nachrichten liegen schon eine Weile.
            var (bob, eingang) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => eingang.Count == 2, "die nachgereichten Nachrichten");

            var gelesen = eingang.ToArray();

            Assert.Multiple(() =>
            {

                Assert.That(gelesen[0].Body,        Is.EqualTo("Erste"));
                Assert.That(gelesen[1].Body,        Is.EqualTo("Zweite"));
                Assert.That(gelesen[0].FromBareJid, Is.EqualTo(Alice),
                            "Nachgereicht wird mit dem ursprünglichen Absender.");

                Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Is.Empty,
                            "Zugestellt ist erledigt - die Ablage bleibt nicht stehen.");

            });

        }

        #endregion

        #region AMessageWithoutAType_IsAlsoStored()

        /// <summary>
        /// Abschnitt 8.5.2.2.1 nennt <c>normal</c> und <c>chat</c> - und eine
        /// Nachricht ohne <c>type</c> ist nach Abschnitt 5.2.2 eine
        /// <c>normal</c>.
        /// </summary>
        /// <remarks>
        /// Der Fall ist nicht ausgedacht: Eine Nachricht ohne <c>type</c> ist
        /// das, was ein Absender schickt, der kein Gespräch führt, sondern
        /// etwas hinterlässt - genau die Art, für die eine Ablage gemacht ist.
        /// </remarks>
        [Test]
        public async Task AMessageWithoutAType_IsAlsoStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync($"<message to='{Bob}' id='ohne-art'><body>Hinterlassen</body></message>");

            await WarteAufAblage(Bob, 1);

            var (bob, eingang) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => !eingang.IsEmpty, "die nachgereichte Nachricht");

            eingang.TryDequeue(out var nachricht);

            Assert.That(nachricht!.Type, Is.EqualTo(MessageType.Normal));

        }

        #endregion

        #region AHeadlineToAnOfflineAccount_IsNotStored()

        /// <summary>
        /// Die Gegenprobe: <c>headline</c> wird nicht abgelegt, sondern
        /// stillschweigend verworfen.
        /// </summary>
        /// <remarks>
        /// Abschnitt 8.5.2.2.1 verlangt das ausdrücklich, und XEP-0160 nennt
        /// den Grund: Eine Meldung ist zeitgebunden. Der Kurs von gestern beim
        /// Anmelden nachgereicht zu bekommen, ist nicht besser als ihn zu
        /// verpassen, sondern schlechter - er sieht aus wie der von heute.
        ///
        /// Ohne diese Gegenprobe bestünde die Sammlung auch dann, wenn schlicht
        /// alles abgelegt würde, was nicht zustellbar war.
        /// </remarks>
        [Test]
        public async Task AHeadlineToAnOfflineAccount_IsNotStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='headline' id='meldung'><body>Kurs gefallen</body></message>");

            // Und danach eine, die abgelegt werden muss - sie ist der Beweis,
            // dass die Ablage überhaupt lief, während die Meldung durchfiel.
            await alice.SendMessageAsync(Bob, "Und das bleibt liegen");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("Und das bleibt liegen"));

        }

        #endregion

        #region AChatStateOnly_IsNotStored()

        /// <summary>
        /// XEP-0160, Abschnitt 3: Ein <c>chat</c>, der nur einen Tippstatus
        /// trägt, wird nicht abgelegt - „such messages SHOULD NOT be stored
        /// offline".
        /// </summary>
        /// <remarks>
        /// Ein Tippstatus ist eine Aussage über <i>jetzt</i>. Beim Anmelden
        /// nachgereicht sagt er, jemand tippe gerade - und das stimmt dann
        /// nicht mehr. Zehn davon in der Ablage verdrängen ausserdem die
        /// Nachrichten, für die sie gedacht ist.
        ///
        /// <b>Und der Absender bekommt hier keinen Fehler</b>, obwohl D14 das
        /// stillschweigende Verwerfen sonst ausdrücklich ausschliesst. Der
        /// Unterschied liegt in der Erwartung: Wer eine Nachricht schickt, will
        /// wissen, ob sie ankam; wer einen Tippstatus schickt, hat nichts
        /// verloren, wenn er verfällt. Ein Fehler dafür wäre Lärm - und einer,
        /// der bei jedem Tastendruck neu käme.
        /// </remarks>
        [Test]
        public async Task AChatStateOnly_IsNotStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='tippt'>" +
                      "<composing xmlns='http://jabber.org/protocol/chatstates'/></message>");

            // Danach eine, die abgelegt werden muss - sie ist der Beweis, dass
            // die Ablage lief, während der Tippstatus durchfiel.
            await alice.SendMessageAsync(Bob, "Und das bleibt liegen");

            await WarteAufAblage(Bob, 1);

            Assert.Multiple(() =>
            {

                Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                            Does.Contain("Und das bleibt liegen"));

                Assert.That(fehler, Is.Empty,
                            "Ein verfallener Tippstatus ist kein Fehler.");

            });

        }

        #endregion

        #region AChatStateWithABody_IsStored()

        /// <summary>
        /// Die Gegenprobe: Trägt dieselbe Nachricht auch einen Text, wird sie
        /// abgelegt.
        /// </summary>
        /// <remarks>
        /// XEP-0085, Abschnitt 5.3 lässt den Tippstatus ausdrücklich an einer
        /// gewöhnlichen Nachricht mitreisen. Die Ausnahme aus XEP-0160 gilt
        /// nur, wenn <b>nichts anderes</b> darin steht - ohne diese Gegenprobe
        /// wäre „alles wegwerfen, was einen Tippstatus enthält" eine bestandene
        /// Lösung, und sie verlöre echte Nachrichten.
        /// </remarks>
        [Test]
        public async Task AChatStateWithABody_IsStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='mit-text'>" +
                      "<body>Bin gleich da</body>" +
                      "<active xmlns='http://jabber.org/protocol/chatstates'/></message>");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("Bin gleich da"));

        }

        #endregion

        #region WhatIsNotAChatState_IsStored()

        /// <summary>
        /// Die zweite Gegenprobe: Eine Nachricht ohne Text ist deshalb noch
        /// lange kein Tippstatus.
        /// </summary>
        /// <remarks>
        /// Die naheliegende Abkürzung wäre „ohne <c>&lt;body/&gt;</c> nicht
        /// ablegen". Sie wäre falsch: Eine Empfangsbestätigung (XEP-0184) und
        /// ein Lesevermerk (XEP-0333) haben keinen Text und sollen den Empfänger
        /// trotzdem erreichen. Auch ein <c>thread</c> allein macht aus einer
        /// Nachricht keinen Tippstatus.
        /// </remarks>
        [Test]
        public async Task WhatIsNotAChatState_IsStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='quittung'>" +
                      "<received xmlns='urn:xmpp:receipts' id='vorher'/></message>");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("urn:xmpp:receipts"));

        }

        #endregion

        #region AChatStateWithAThread_IsAlsoNotStored()

        /// <summary>
        /// Ein <c>thread</c> neben dem Tippstatus ändert nichts: Er ist eine
        /// Kennung, kein Inhalt.
        /// </summary>
        /// <remarks>
        /// XEP-0085, Abschnitt 5.3 führt genau diese Form vor. Wer den Thread
        /// als Inhalt zählte, legte den Tippstatus doch ab - und zwar in genau
        /// der Schreibweise, die das XEP empfiehlt.
        /// </remarks>
        [Test]
        public async Task AChatStateWithAThread_IsAlsoNotStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='tippt-im-faden'>" +
                      "<composing xmlns='http://jabber.org/protocol/chatstates'/>" +
                      "<thread>abcd</thread></message>");

            await alice.SendMessageAsync(Bob, "Und das bleibt liegen");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("Und das bleibt liegen"));

        }

        #endregion

        #region ANormalMessageWithOnlyAChatState_IsStored()

        /// <summary>
        /// Die Ausnahme steht bei <c>chat</c> und nirgends sonst - ein
        /// <c>normal</c> mit demselben Inhalt wird abgelegt.
        /// </summary>
        /// <remarks>
        /// Das ist der Buchstabe von XEP-0160, Abschnitt 3: Für <c>normal</c>
        /// steht dort „SHOULD be stored offline" ohne jede Einschränkung, für
        /// <c>chat</c> mit. Die Regel weiter zu ziehen als geschrieben hiesse,
        /// eine eigene Vorschrift zu erfinden und sie fremd zu nennen.
        /// </remarks>
        [Test]
        public async Task ANormalMessageWithOnlyAChatState_IsStored()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='normal' id='normal-tippt'>" +
                      "<composing xmlns='http://jabber.org/protocol/chatstates'/></message>");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("chatstates"));

        }

        #endregion

        #region WhatCountsAsAChatStateOnlyMessage()

        /// <summary>
        /// Die Regel selbst, ohne Netz - samt der beiden Fälle, die über die
        /// Ablage nicht erreichbar sind.
        /// </summary>
        /// <remarks>
        /// Eine Nachricht ohne Kinder und eine, die sich nicht lesen lässt,
        /// kommen im laufenden Betrieb nicht bis hierher - die Rahmung siebt sie
        /// vorher aus. Die Regel muss trotzdem für sich stimmen: Sie
        /// entscheidet, ob eine Nachricht verworfen wird, und wer sie in eine
        /// andere Umgebung trägt, bringt die Siebe nicht mit.
        ///
        /// <b>Im Zweifel gilt „ist eine Nachricht":</b> Was sich nicht als
        /// Tippstatus nachweisen lässt, wird abgelegt. Der umgekehrte Irrtum
        /// verlöre eine Nachricht.
        /// </remarks>
        [Test]
        public void WhatCountsAsAChatStateOnlyMessage()
        {

            const String Tippstatus = "<composing xmlns='http://jabber.org/protocol/chatstates'/>";

            Assert.Multiple(() =>
            {

                Assert.That(XMPPServer.IsChatStateOnly($"<message>{Tippstatus}</message>"),
                            Is.True);

                Assert.That(XMPPServer.IsChatStateOnly($"<message>{Tippstatus}<thread>a</thread></message>"),
                            Is.True);

                // Nicht nur <composing/>: XEP-0085 kennt fünf Zustände, und der
                // Namensraum entscheidet, nicht der Name.
                Assert.That(XMPPServer.IsChatStateOnly("<message><active xmlns='http://jabber.org/protocol/chatstates'/></message>"),
                            Is.True);

                Assert.That(XMPPServer.IsChatStateOnly("<message><thread>a</thread></message>"),
                            Is.False, "Ein thread allein ist kein Tippstatus.");

                Assert.That(XMPPServer.IsChatStateOnly("<message/>"),
                            Is.False, "Eine Nachricht ohne Kinder auch nicht.");

                Assert.That(XMPPServer.IsChatStateOnly("<message><composing/></message>"),
                            Is.False, "Ohne den Namensraum ist es irgendein Element.");

                Assert.That(XMPPServer.IsChatStateOnly("<message><composing"),
                            Is.False, "Und was sich nicht lesen lässt, ist eine Nachricht.");

            });

        }

        #endregion

        #region AStoredMessage_IsDeliveredOnlyOnce()

        /// <summary>
        /// Nachgereicht wird einmal. Danach ist die Ablage leer.
        /// </summary>
        /// <remarks>
        /// Der Unterschied zur aufbewahrten Subscription-Anfrage, die bei
        /// <i>jeder</i> Anmeldung wieder vorgelegt wird, bis sie beantwortet
        /// ist. Bei einer Nachricht gibt es kein Beantworten, an dem sich das
        /// Ende festmachen liesse - wer sie bei jeder Anmeldung erneut bekäme,
        /// könnte sie nie loswerden.
        /// </remarks>
        [Test]
        public async Task AStoredMessage_IsDeliveredOnlyOnce()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Einmal");
            await WarteAufAblage(Bob, 1);

            var (erste, ersterEingang) = VorbereiteterClient("bob");
            await erste.ConnectAsync();
            await WaitFor(() => !ersterEingang.IsEmpty, "die Nachricht bei der ersten Anmeldung");

            await AbmeldenAsync(erste, Bob);

            var (zweite, zweiterEingang) = VorbereiteterClient("bob");
            await zweite.ConnectAsync();

            await WaitAgainst(() => !zweiterEingang.IsEmpty,
                              "eine bereits zugestellte Nachricht noch einmal");

        }

        #endregion

        #region ANegativePriority_DoesNotEmptyTheStore()

        /// <summary>
        /// XEP-0160: Nachgereicht wird, sobald der Empfänger
        /// „non-negative available presence" schickt - eine Resource mit
        /// negativer Priorität leert die Ablage nicht.
        /// </summary>
        /// <remarks>
        /// Sie ist derselbe Wunsch, den Abschnitt 8.5 für den laufenden Betrieb
        /// achtet: Dieses Gerät soll nichts abbekommen, was bloss an das Konto
        /// ging. Eine Ablage, die sich beim ersten Lebenszeichen irgendeiner
        /// Resource entleert, hebelte ihn aus - und zwar an der empfindlichsten
        /// Stelle, weil die Nachrichten dann auf einem Gerät liegen, das der
        /// Nutzer gerade nicht ansieht.
        ///
        /// Die zweite Hälfte gehört dazu und prüft mehr als die Umkehrung:
        /// Dieselbe Resource hebt ihre Priorität, ohne sich neu anzumelden.
        /// Nachgereicht wird also bei jeder nicht-negativen verfügbaren
        /// Presence und nicht nur beim Verfügbar<i>werden</i> - sonst bliebe
        /// die Ablage bis zur nächsten Anmeldung liegen, obwohl der Nutzer
        /// gerade eben gesagt hat, dass er wieder hinsieht.
        /// </remarks>
        [Test]
        public async Task ANegativePriority_DoesNotEmptyTheStore()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Für den Menschen, nicht für das Gerät");
            await WarteAufAblage(Bob, 1);

            var (bob, eingang) = VorbereiteterClient("bob", priority: -1);
            await bob.ConnectAsync();

            await WaitFor(() => Server.SessionOf(bob.FullJid!)?.PresencePriority == -1,
                          "die negative Priorität am Server");

            await WaitAgainst(() => !eingang.IsEmpty,
                              "die Ablage auf einem Gerät, das sie nicht will");

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Has.Count.EqualTo(1),
                        "Die Nachricht muss weiterhin liegen.");

            // Nun sieht der Nutzer doch hin - dieselbe Resource, neue Priorität.
            bob.Connection.PresencePriority = 0;
            await bob.Connection.SendPresenceAsync();

            await WaitFor(() => !eingang.IsEmpty,
                          "die Ablage, sobald die Resource sie wieder annimmt");

        }

        #endregion

        #region AnUnavailablePresence_DoesNotEmptyTheStore()

        /// <summary>
        /// Nachgereicht wird an eine <i>verfügbare</i> Resource - eine
        /// Abmeldung ist keine.
        /// </summary>
        /// <remarks>
        /// Der Fall ist unscheinbar und die Falle scharf: Eine Abmeldung setzt
        /// die Priorität der Sitzung auf 0 zurück, denn eine abgemeldete
        /// Resource hat keinen Zustand zu berichten. Wer nur nach der Priorität
        /// fragt, sieht in genau diesem Moment eine 0 und leert die Ablage in
        /// einen Stream, der sich gerade verabschiedet - die Nachrichten sind
        /// dann weg, ohne jemals gelesen worden zu sein.
        ///
        /// Der Zustand davor ist absichtlich negativ: Mit der üblichen 0 wäre
        /// die Ablage bereits beim Anmelden geleert, und der Test prüfte nichts
        /// mehr.
        /// </remarks>
        [Test]
        public async Task AnUnavailablePresence_DoesNotEmptyTheStore()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Bleibt liegen");
            await WarteAufAblage(Bob, 1);

            var (bob, eingang) = VorbereiteterClient("bob", priority: -1);
            await bob.ConnectAsync();

            await WaitFor(() => Server.SessionOf(bob.FullJid!)?.PresencePriority == -1,
                          "die negative Priorität am Server");

            await bob.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)?.IsAvailable == false,
                          "die Abmeldung am Server");

            await WaitAgainst(() => Server.GetAccount(Bob)!.OfflineMessages.Count == 0,
                              "eine Ablage, die sich in eine Abmeldung entleert");

            Assert.That(eingang, Is.Empty);

        }

        #endregion

        #region WithoutPresenceBroadcast_TheStoreIsStillDelivered()

        /// <summary>
        /// Das Nachreichen hängt nicht am Verteilen von Presence.
        /// </summary>
        /// <remarks>
        /// Die beiden haben nichts miteinander zu tun, stehen aber am selben
        /// Ort: Beide beginnen mit der ersten Presence einer Resource. Wer das
        /// Verteilen abschaltet - etwa um einen Test auf einen Aspekt zu
        /// verengen -, will damit nicht die Post des Nutzers stillegen. Und der
        /// Verlust wäre still: Die Nachricht bleibt in der Ablage und käme erst
        /// bei einer Anmeldung heraus, die man wieder eingeschaltet hat.
        /// </remarks>
        [Test]
        public async Task WithoutPresenceBroadcast_TheStoreIsStillDelivered()
        {

            Server.BroadcastPresence = false;

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Trotzdem");
            await WarteAufAblage(Bob, 1);

            var (bob, eingang) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => !eingang.IsEmpty, "die nachgereichte Nachricht");

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Is.Empty);

        }

        #endregion

        #region AStoredMessage_KeepsTheTimeItWasWritten()

        /// <summary>
        /// Der Empfänger zeigt die Zeit, zu der die Nachricht geschrieben
        /// wurde - nicht die, zu der sie ihn erreicht.
        /// </summary>
        /// <remarks>
        /// Die andere Hälfte von <see cref="AStoredMessage_CarriesADelayStamp"/>.
        /// Der Server hat den Stempel seit jeher geschrieben und der Client ihn
        /// nie gelesen: <c>XMPPMessage.Timestamp</c> war der Zeitpunkt des
        /// Empfangs, und eine Nachricht von gestern Abend erschien nach dem
        /// Anmelden mit der Uhrzeit von jetzt. <b>Eine Uhrzeit, die dasteht und
        /// nicht stimmt, ist schlimmer als keine</b> - sie lädt dazu ein, auf
        /// eine Frage zu antworten, die sich längst erledigt hat.
        ///
        /// Geprüft wird gegen ein Zeitfenster um das Senden herum. Die genaue
        /// Zahl kennt der Test nicht - sie kommt vom Server -, wohl aber die
        /// Grenzen: vor dem Senden kann sie nicht liegen und nach dem
        /// Wiederanmelden auch nicht.
        /// </remarks>
        [Test]
        public async Task AStoredMessage_KeepsTheTimeItWasWritten()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var vorDemSenden = DateTime.Now.AddSeconds(-1);

            await alice.SendMessageAsync(Bob, "Von gestern");

            await WarteAufAblage(Bob, 1);

            var nachDemSenden = DateTime.Now.AddSeconds(1);

            var (bob, eingang) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => !eingang.IsEmpty, "die nachgereichte Nachricht");

            eingang.TryDequeue(out var nachricht);

            Assert.Multiple(() =>
            {

                Assert.That(nachricht!.IsDelayed, Is.True,
                            "Eine nachgereichte Nachricht muss als solche erkennbar sein.");

                Assert.That(nachricht.Timestamp, Is.InRange(vorDemSenden, nachDemSenden),
                            "Angezeigt wird die Zeit des Empfangs statt der des Schreibens.");

                Assert.That(nachricht.ReceivedAt, Is.GreaterThanOrEqualTo(nachricht.Timestamp),
                            "Angekommen ist sie nach dem Schreiben, nicht davor.");

                Assert.That(nachricht.DelayedBy, Is.EqualTo(Server.Domain),
                            "XEP-0203, Abschnitt 4: aufgehoben hat sie der Server.");

            });

        }

        #endregion

        #region ALiveMessage_IsNotDelayed()

        /// <summary>
        /// Die Gegenprobe: Eine Nachricht an einen anwesenden Empfänger gilt
        /// nicht als nachgereicht.
        /// </summary>
        /// <remarks>
        /// Ohne sie wäre „immer nachgereicht" eine bestandene Lösung, und jede
        /// laufende Unterhaltung trüge den Vermerk. Zugleich hält der Test
        /// fest, dass die angezeigte Zeit im Normalfall weiterhin die des
        /// Empfangs ist - für alles Laufende sind die beiden dasselbe.
        /// </remarks>
        [Test]
        public async Task ALiveMessage_IsNotDelayed()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var (bob, eingang) = VorbereiteterClient("bob");

            await bob.ConnectAsync();

            var vorher = DateTime.Now.AddSeconds(-1);

            await alice.SendMessageAsync(Bob, "Jetzt gerade");

            await WaitFor(() => !eingang.IsEmpty, "die zugestellte Nachricht");

            eingang.TryDequeue(out var nachricht);

            Assert.Multiple(() =>
            {

                Assert.That(nachricht!.IsDelayed, Is.False);

                Assert.That(nachricht.Timestamp,  Is.InRange(vorher, DateTime.Now.AddSeconds(1)));

                Assert.That(nachricht.DelayedBy,  Is.Null);

            });

        }

        #endregion

        #region AStoredMessage_CarriesADelayStamp()

        /// <summary>
        /// XEP-0160 und XEP-0203: Eine nachgereichte Nachricht trägt ein
        /// <c>&lt;delay/&gt;</c> mit dem Zeitpunkt des Eingangs.
        /// </summary>
        /// <remarks>
        /// Ohne den Stempel behauptet eine Nachricht von gestern, sie sei von
        /// jetzt - der Empfänger kann den Unterschied nicht sehen und antwortet
        /// auf eine Frage, die sich längst erledigt hat. Der Stempel ist damit
        /// nicht Zierde, sondern der einzige Weg, den Verzug überhaupt
        /// mitzuteilen.
        /// </remarks>
        [Test]
        public async Task AStoredMessage_CarriesADelayStamp()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='mit-stempel'><body>Von gestern</body></message>");

            await WarteAufAblage(Bob, 1);

            var eingegangen  = new ConcurrentQueue<String>();
            var bob          = CreateClient("bob");

            bob.Connection.OnRawXml += xml =>
            {
                if (xml.Contains("mit-stempel", StringComparison.Ordinal))
                    eingegangen.Enqueue(xml);
            };

            await bob.ConnectAsync();

            await WaitFor(() => !eingegangen.IsEmpty, "die nachgereichte Nachricht auf dem Draht");

            eingegangen.TryDequeue(out var stanza);

            Assert.Multiple(() =>
            {

                Assert.That(stanza, Does.Contain("urn:xmpp:delay"),
                            "Eine nachgereichte Nachricht muss ihren Verzug mitteilen.");

                Assert.That(stanza, Does.Contain($"from='{Server.Domain}'"),
                            "XEP-0203: Den Stempel setzt der Server, nicht der Absender.");

                Assert.That(stanza, Does.Match(@"stamp='\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z'"),
                            "XEP-0082 verlangt eine UTC-Zeitangabe.");

            });

        }

        #endregion

        #region WithoutTheStore_TheSenderIsTold()

        /// <summary>
        /// Der zweite von Abschnitt 8.5.2.2.1 erlaubte Weg: kein Ablegen,
        /// sondern <c>&lt;service-unavailable/&gt;</c> an den Absender.
        /// </summary>
        /// <remarks>
        /// Er ist nicht der schlechtere - er ist nur unbequemer. Was der
        /// Abschnitt beiden gemeinsam abverlangt, ist die Ehrlichkeit: Der
        /// Absender erfährt in jedem Fall, woran er ist. Ohne diesen Test wäre
        /// nur der bequeme Weg belegt, und ein Server ohne Ablage fiele
        /// stillschweigend auf das Verwerfen zurück.
        /// </remarks>
        [Test]
        public async Task WithoutTheStore_TheSenderIsTold()
        {

            Server.StoreOfflineMessages = false;

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendMessageAsync(Bob, "Kommt nicht an");

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung beim Absender");

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

                Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Is.Empty,
                            "Ohne Ablage wird nichts abgelegt.");

            });

        }

        #endregion

        #region TheLimit_RefusesTheNewMessageAndKeepsTheStoredOnes()

        /// <summary>
        /// Ist die Ablage voll, wird die neue Nachricht abgewiesen - und keine
        /// bereits abgelegte verdrängt.
        /// </summary>
        /// <remarks>
        /// Geprüft wird nicht nur, <i>dass</i> die Grenze greift, sondern in
        /// welche Richtung. Beide Richtungen verlieren eine Nachricht, aber nur
        /// eine davon sagt es jemandem: Wer abweist, antwortet dem Absender;
        /// wer verdrängt, wirft eine Nachricht weg, von der der Absender
        /// annimmt, sie liege bereit, und der Empfänger nie erfährt, dass es
        /// sie gab. Eine Grenze, die verdrängt, wäre ausserdem selbst der
        /// Angriff - wer die Ablage vollschreibt, löschte damit fremde Post.
        /// </remarks>
        [Test]
        public async Task TheLimit_RefusesTheNewMessageAndKeepsTheStoredOnes()
        {

            Server.MaxStoredOfflineMessages = 1;

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendMessageAsync(Bob, "Die erste");
            await WarteAufAblage(Bob, 1);

            await alice.SendMessageAsync(Bob, "Die zweite");
            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung der zweiten");

            Assert.Multiple(() =>
            {

                Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Has.Count.EqualTo(1));

                Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                            Does.Contain("Die erste"),
                            "Die zuerst abgelegte Nachricht bleibt stehen.");

            });

        }

        #endregion

        #region AnAbsentRecipient_DoesNotSuppressTheSentCarbon()

        /// <summary>
        /// XEP-0280: Die anderen Geräte des <i>Absenders</i> bekommen ihre
        /// sent-Kopie auch dann, wenn die Nachricht in der Ablage landet.
        /// </summary>
        /// <remarks>
        /// Der Carbon sagt nichts über die Zustellung, sondern über das
        /// Schreiben: „Das hast du geschickt." Das bleibt wahr, ob der
        /// Empfänger da war oder nicht. Bliebe er hier aus, hätte der Nutzer
        /// eine Gesprächsspur, in der seine eigenen Nachrichten an Abwesende
        /// fehlen - und zwar genau die, deren Verbleib ihn interessiert.
        ///
        /// Der Test hält eine Zeile fest, die vorher unbemerkt mitlief: Bis
        /// hierher fiel dieser Fall durch denselben Code wie die gelungene
        /// Zustellung, weil nichts ihn davor abfing. Nun tut es der
        /// Offline-Zweig, und was er nicht ausdrücklich mitnimmt, ginge
        /// stillschweigend verloren.
        /// </remarks>
        [Test]
        public async Task AnAbsentRecipient_DoesNotSuppressTheSentCarbon()
        {

            var handy    = await ConnectClientAsync("alice");
            var rechner  = await ConnectClientAsync("alice");

            Server.AddAccount("bob");

            await WaitFor(() => Server.SessionsOf(handy.BareJid).All(s => s.CarbonsEnabled),
                          "die Carbons auf beiden Resourcen");

            var carbons = new ConcurrentQueue<CarbonMessage>();
            rechner.OnCarbonMessage += c => carbons.Enqueue(c);

            await handy.SendMessageAsync(Bob, "Für später");

            await WarteAufAblage(Bob, 1);

            await WaitFor(() => !carbons.IsEmpty, "die sent-Kopie auf dem anderen Gerät");

            carbons.TryDequeue(out var carbon);

            Assert.Multiple(() =>
            {
                Assert.That(carbon!.IsSent, Is.True);
                Assert.That(carbon.Body,    Is.EqualTo("Für später"));
            });

        }

        #endregion

        #region AChatToAnUnknownResource_IsHandledLikeTheAccount()

        /// <summary>
        /// Abschnitt 8.5.3.2.1: Ein <c>chat</c> an eine Resource, die es nicht
        /// gibt, wird behandelt, als wäre er an das Konto gegangen - abgelegt,
        /// wenn niemand da ist, und zugestellt, wenn doch.
        /// </summary>
        /// <remarks>
        /// Beide Hälften gehören in denselben Test. Nur die Ablage umzusetzen
        /// wäre schlimmer als der bisherige Zustand: Die Nachricht landete in
        /// der Ablage, während der Empfänger mit einer anderen Resource
        /// daneben sitzt und wartet.
        ///
        /// Der Fall ist alltäglich. Ein Client antwortet auf die Full-JID, die
        /// er zuletzt gesehen hat; wechselt der Gesprächspartner in der
        /// Zwischenzeit das Gerät, ist diese Resource weg. Genau deshalb macht
        /// der Abschnitt für <c>chat</c> eine Ausnahme von der Regel, dass eine
        /// nicht passende Resource das Ende ist.
        /// </remarks>
        [Test]
        public async Task AChatToAnUnknownResource_IsHandledLikeTheAccount()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var verschwunden = $"{Bob}/gibtsnichtmehr";

            await alice.SendRawAsync(
                      $"<message to='{verschwunden}' type='chat' id='abgelegt'><body>Bist du weg?</body></message>");

            await WarteAufAblage(Bob, 1);

            // Zweite Hälfte: Bob ist da, nur unter einem anderen Namen.
            var (bob, eingang) = VorbereiteterClient("bob");
            bob.Connection.Resource = "Neu";
            await bob.ConnectAsync();

            await WaitFor(() => eingang.Any(m => m.MessageId == "abgelegt"),
                          "die nachgereichte Nachricht");

            await alice.SendRawAsync(
                      $"<message to='{verschwunden}' type='chat' id='zugestellt'><body>Doch nicht</body></message>");

            await WaitFor(() => eingang.Any(m => m.MessageId == "zugestellt"),
                          "die Zustellung an die erreichbare Resource");

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages, Is.Empty,
                        "Solange eine Resource erreichbar ist, wird nichts abgelegt.");

        }

        #endregion

        #region AnUnknownResource_StoresNothingForTheOtherTypes()

        /// <summary>
        /// Die Ausnahme gilt nur für <c>chat</c>. Ein <c>normal</c> an eine
        /// Resource, die es nicht gibt, wird nach Abschnitt 8.5.3.2.1
        /// stillschweigend verworfen.
        /// </summary>
        /// <remarks>
        /// Der Unterschied sieht schrullig aus und hat einen Grund: Wer eine
        /// Full-JID anschreibt, meint diese Resource. Bei einem Gespräch ist
        /// das eine Abkürzung für „mein Gegenüber", bei allem anderen eine
        /// Angabe, die der Absender so gewollt hat.
        ///
        /// Ohne diesen Test bestünde die Sammlung auch dann, wenn die Ausnahme
        /// für jede Art gälte - und die Ablage füllte sich mit Nachrichten an
        /// Adressen, die es nie gab.
        /// </remarks>
        [Test]
        public async Task AnUnknownResource_StoresNothingForTheOtherTypes()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var verschwunden = $"{Bob}/gibtsnichtmehr";

            await alice.SendRawAsync(
                      $"<message to='{verschwunden}' id='ohne-art'><body>An genau diese Resource</body></message>");

            await alice.SendRawAsync(
                      $"<message to='{verschwunden}' type='headline' id='meldung'><body>Kurs gefallen</body></message>");

            // Danach ein chat - er muss abgelegt werden und belegt damit, dass
            // die Ablage lief, während die beiden anderen durchfielen.
            await alice.SendRawAsync(
                      $"<message to='{verschwunden}' type='chat' id='gespraech'><body>Bist du weg?</body></message>");

            await WarteAufAblage(Bob, 1);

            Assert.That(Server.GetAccount(Bob)!.OfflineMessages[0].Stanza,
                        Does.Contain("gespraech"));

        }

        #endregion

        #region TheDelayStampIsAppendedToBothFormsOfAMessage()

        /// <summary>
        /// Der Stempel wird angehängt, und zwar auch dann, wenn die Nachricht
        /// als leeres Element daherkommt.
        /// </summary>
        /// <remarks>
        /// <c>&lt;message .../&gt;</c> ist kein ausgedachter Fall: Ein Client
        /// darf eine Nachricht ohne Kindelemente schicken, sie ist ein
        /// <c>chat</c> wie jeder andere und wird deshalb abgelegt. Ohne das
        /// Auflösen des leeren Elements landete der Stempel hinter dem Ende der
        /// Stanza - und wäre damit kein Teil der Nachricht mehr.
        ///
        /// Der Zeitpunkt trägt absichtlich einen Versatz gegen UTC. Mit
        /// <c>TimeSpan.Zero</c> wäre die Ortszeit dieselbe Zahl wie die
        /// Weltzeit, und ein Stempel in Ortszeit - den XEP-0082 nicht zulässt -
        /// fiele nicht auf.
        /// </remarks>
        [Test]
        public void TheDelayStampIsAppendedToBothFormsOfAMessage()
        {

            var zeitpunkt = new DateTimeOffset(2026, 7, 29, 16, 5, 9, TimeSpan.FromHours(2));

            var mitInhalt = XMPPServer.WithDelay(
                                new OfflineMessage("<message to='bob@localhost'><body>Hallo</body></message>",
                                                   zeitpunkt),
                                "localhost");

            var leer      = XMPPServer.WithDelay(
                                new OfflineMessage("<message to='bob@localhost' type='chat'/>",
                                                   zeitpunkt),
                                "localhost");

            Assert.Multiple(() =>
            {

                Assert.That(mitInhalt,
                            Is.EqualTo("<message to='bob@localhost'><body>Hallo</body>" +
                                       "<delay xmlns='urn:xmpp:delay' from='localhost' " +
                                       "stamp='2026-07-29T14:05:09Z'>Offline Storage</delay></message>"));

                Assert.That(leer,
                            Is.EqualTo("<message to='bob@localhost' type='chat'>" +
                                       "<delay xmlns='urn:xmpp:delay' from='localhost' " +
                                       "stamp='2026-07-29T14:05:09Z'>Offline Storage</delay></message>"));

            });

        }

        #endregion

        #region TheStoreIsAnnouncedInDiscoInfo()

        /// <summary>
        /// XEP-0160, Abschnitt 4: Ein Server mit Offline-Ablage kündigt
        /// <c>msgoffline</c> in disco#info an.
        /// </summary>
        /// <remarks>
        /// Für den Client ist das der Unterschied zwischen „liegt bereit" und
        /// „ist weg": Ohne die Ankündigung müsste er aus dem Ausbleiben eines
        /// Fehlers schliessen, dass abgelegt wurde - und ein Fehler kann sich
        /// verspäten.
        ///
        /// Geprüft wird auch die Gegenprobe. Eine Ankündigung, die immer da
        /// ist, sagt nichts; sie wäre dann sogar falsch, denn ein Server ohne
        /// Ablage verspräche etwas, das er nicht tut.
        /// </remarks>
        [Test]
        public async Task TheStoreIsAnnouncedInDiscoInfo()
        {

            var mitAblage  = await FrageDiscoInfoAsync(true);
            var ohneAblage = await FrageDiscoInfoAsync(false);

            Assert.Multiple(() =>
            {

                Assert.That(mitAblage,  Does.Contain("msgoffline"));

                Assert.That(ohneAblage, Does.Not.Contain("msgoffline"),
                            "Ohne Ablage darf der Server sie nicht ankündigen.");

            });

        }

        /// <summary>Fragt den Server nach seinen Merkmalen.</summary>
        private async Task<String> FrageDiscoInfoAsync(Boolean storeOfflineMessages)
        {

            Server.StoreOfflineMessages = storeOfflineMessages;

            var client    = await ConnectClientAsync("alice");
            var antworten = new ConcurrentQueue<String>();
            var id        = $"disco-{storeOfflineMessages}";

            client.Connection.OnRawXml += xml =>
            {
                if (xml.Contains("<<<", StringComparison.Ordinal) &&
                    xml.Contains(id,    StringComparison.Ordinal))
                {
                    antworten.Enqueue(xml);
                }
            };

            await client.SendRawAsync(
                      $"<iq type='get' id='{id}' to='{Server.Domain}'>" +
                      "<query xmlns='http://jabber.org/protocol/disco#info'/></iq>");

            await WaitFor(() => !antworten.IsEmpty, "die disco#info-Antwort des Servers");

            antworten.TryDequeue(out var antwort);

            await client.DisconnectAsync();

            return antwort!;

        }

        #endregion

    }

}
