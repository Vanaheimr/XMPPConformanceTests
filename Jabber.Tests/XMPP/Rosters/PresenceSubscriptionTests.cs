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
    /// RFC 6121, Abschnitt 4: Presence ist keine Rundsendung.
    ///
    /// Wer sie bekommt, entscheidet der Subscription-Zustand im Roster des
    /// Absenders: nur Kontakte mit <c>from</c> oder <c>both</c>, dazu die
    /// eigenen weiteren Resourcen. Der Testserver hat sie bis hierher an alle
    /// verteilt - jede Sitzung erfuhr damit, wer sonst noch online ist.
    /// </summary>
    [TestFixture]
    public class PresenceSubscriptionTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Verbindet einen Client und sammelt ab sofort alle Presence-Meldungen
        /// als <c>jid|typ</c>.
        /// </summary>
        private async Task<(XMPPClient Client, ConcurrentQueue<String> Presences)> WatcherAsync(String localPart)
        {

            var client     = await ConnectClientAsync(localPart);
            var presences  = new ConcurrentQueue<String>();

            client.OnPresenceChanged += (from, type) => presences.Enqueue($"{from}|{type}");

            return (client, presences);

        }

        private static Boolean Saw(ConcurrentQueue<String> presences, XMPPClient who)
            => presences.Any(p => p.StartsWith(who.BareJid, StringComparison.OrdinalIgnoreCase));

        #endregion


        #region Presence_ReachesAContactWithSubscriptionFrom()

        /// <summary>
        /// Die Grundlage: wer <c>from</c> oder <c>both</c> hat, bekommt sie.
        /// </summary>
        [Test]
        public async Task Presence_ReachesAContactWithSubscriptionFrom()
        {

            MakeContacts("alice", "bob");

            var alice           = await ConnectClientAsync("alice");
            var (bob, atBobs)   = await WatcherAsync("bob");

            await alice.SetPresenceAsync("away", "Bin gleich zurück");

            await WaitFor(() => Saw(atBobs, alice), "Presence von Alice bei Bob");

        }

        #endregion

        #region Presence_DoesNotReachANonContact()

        /// <summary>
        /// Der Kern: Carol steht in keinem Roster und darf nicht erfahren, dass
        /// Alice online ist. Bisher bekam sie es mit, weil die Verteilung an
        /// alle Sitzungen ging - eine Sitzung auf dem Server genügte, um die
        /// Anwesenheit aller anderen mitzulesen.
        /// </summary>
        [Test]
        public async Task Presence_DoesNotReachANonContact()
        {

            MakeContacts("alice", "bob");

            var alice             = await ConnectClientAsync("alice");
            var (_, atCarols)     = await WatcherAsync("carol");

            await alice.SetPresenceAsync("away");

            await WaitAgainst(() => Saw(atCarols, alice), "Presence von Alice bei Carol");

        }

        #endregion

        #region Presence_DoesNotReachAContactWithSubscriptionToOnly()

        /// <summary>
        /// Subscriptions sind gerichtet. Steht Bob in Alices Roster nur mit
        /// <c>to</c>, dann sieht <b>Alice</b> die Presence von Bob - nicht
        /// umgekehrt.
        /// </summary>
        [Test]
        public async Task Presence_DoesNotReachAContactWithSubscriptionToOnly()
        {

            SetServerRoster("alice", "bob", "to");
            SetServerRoster("bob", "alice", "from");

            var alice          = await ConnectClientAsync("alice");
            var (_, atBobs)    = await WatcherAsync("bob");

            await alice.SetPresenceAsync("away");

            await WaitAgainst(() => Saw(atBobs, alice), "Presence von Alice bei Bob");

        }

        #endregion

        #region Presence_ReachesTheOwnOtherResources()

        /// <summary>
        /// RFC 6121, Abschnitt 4.4.2: die weiteren Resourcen des eigenen Kontos
        /// bekommen sie immer, ganz ohne Roster-Eintrag.
        ///
        /// Das galt schon vorher - der Test steht als Regressionsschutz für die
        /// Filterung, nicht als Beleg für einen behobenen Fehler.
        /// </summary>
        [Test]
        public async Task Presence_ReachesTheOwnOtherResources()
        {

            var erste           = await ConnectClientAsync("alice");
            var (_, atZweiter)  = await WatcherAsync("alice");

            await erste.SetPresenceAsync("dnd");

            await WaitFor(() => Saw(atZweiter, erste), "Presence der ersten Resource bei der zweiten");

        }

        #endregion

        #region NewlyOnlineClient_LearnsAboutContactsAlreadyOnline()

        /// <summary>
        /// RFC 6121, Abschnitt 4.3.1: Beim Anmelden fragt der Server für den
        /// Client den Zustand seiner Kontakte ab. Ohne das erfährt ein Client
        /// nur von Kontakten, die sich <b>nach</b> ihm anmelden - wer schon
        /// online war, blieb für ihn unsichtbar, bis er von sich aus etwas
        /// schickte.
        /// </summary>
        [Test]
        public async Task NewlyOnlineClient_LearnsAboutContactsAlreadyOnline()
        {

            MakeContacts("alice", "bob");

            var bob = await ConnectClientAsync("bob");
            await bob.SetPresenceAsync("away", "Schon länger da");

            var (_, atAlices) = await WatcherAsync("alice");

            await WaitFor(() => Saw(atAlices, bob), "Presence des bereits angemeldeten Bob bei Alice");

        }

        #endregion

        #region NewlyOnlineClient_LearnsNothingAboutNonContacts()

        /// <summary>
        /// Die Gegenprobe: derselbe Weg darf keine Auskunft über Fremde geben.
        /// </summary>
        [Test]
        public async Task NewlyOnlineClient_LearnsNothingAboutNonContacts()
        {

            var bob = await ConnectClientAsync("bob");
            await bob.SetPresenceAsync("away");

            var (_, atCarols) = await WatcherAsync("carol");

            await WaitAgainst(() => Saw(atCarols, bob), "Presence von Bob bei der fremden Carol");

        }

        #endregion

        #region NewlyOnlineClient_LearnsNothingAboutAnUnavailableResource()

        /// <summary>
        /// Eine bereits abgemeldete Resource darf einem sich anmeldenden
        /// Kontakt nicht nachgeliefert werden.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.2.1: eine Resource, die sich abgemeldet hat,
        /// hat keinen Zustand zu berichten. Der Server merkte sich die
        /// Abmeldung aber als letzte Presence und lieferte sie jedem Kontakt
        /// nach, der sich danach anmeldete.
        ///
        /// Das war zugleich die Ursache eines Fehlschlags, der etwa jeden
        /// zweiten vollen Testlauf traf: verarbeitete der Server die erste
        /// Presence eines Kontakts erst <b>nach</b> der Abmeldung, bekam
        /// dieser Kontakt sie zweimal - einmal aus der Verteilung, einmal aus
        /// dem Nachliefern. Welche Reihenfolge eintrat, hing an der Last.
        /// </remarks>
        [Test]
        public async Task NewlyOnlineClient_LearnsNothingAboutAnUnavailableResource()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");

            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => Server.SessionOf(alice.FullJid)?.IsAvailable == false,
                          "die Abmeldung von Alice auf dem Server");

            var (_, atBobs) = await WatcherAsync("bob");

            await WaitAgainst(() => atBobs.Any(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                              "eine nachgelieferte Abmeldung der bereits abgemeldeten Alice");

        }

        #endregion

        #region Probe_FromASubscriber_IsAnswered()

        /// <summary>
        /// Eine ausdrückliche Probe (RFC 6121, Abschnitt 4.3) beantwortet der
        /// Server mit dem aktuellen Zustand - sofern der Fragende ihn sehen
        /// darf.
        /// </summary>
        [Test]
        public async Task Probe_FromASubscriber_IsAnswered()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice");
            await alice.SetPresenceAsync("dnd", "Nicht stören");

            var (bob, atBobs) = await WatcherAsync("bob");

            // Erst abwarten, was die Anmeldung selbst mitbringt (Abschnitt
            // 4.3.1), und *dann* leeren. Nur zu leeren ist ein Wettlauf: Kommt
            // die Zustellung der Anmeldung erst danach an, zählt sie als Antwort
            // auf die Probe — und der Test bestünde auch bei einem Server, der
            // Proben überhaupt nicht beantwortet.
            await WaitFor(() => Saw(atBobs, alice), "Alices Zustand nach der Anmeldung");

            atBobs.Clear();

            await bob.SendRawAsync($"<presence type='probe' to='{alice.BareJid}'/>");

            await WaitFor(() => Saw(atBobs, alice), "Antwort auf die Presence-Probe");

        }

        #endregion

        #region Probe_FromANonSubscriber_IsIgnored()

        /// <summary>
        /// Ohne Berechtigung bleibt die Probe unbeantwortet. RFC 6121,
        /// Abschnitt 4.3.2 stellt dem Server <c>&lt;unsubscribed/&gt;</c> und
        /// Schweigen frei; Schweigen verrät nicht einmal, ob es das Konto gibt.
        /// </summary>
        [Test]
        public async Task Probe_FromANonSubscriber_IsIgnored()
        {

            var alice = await ConnectClientAsync("alice");
            await alice.SetPresenceAsync("dnd");

            var (carol, atCarols) = await WatcherAsync("carol");

            atCarols.Clear();

            await carol.SendRawAsync($"<presence type='probe' to='{alice.BareJid}'/>");

            await WaitAgainst(() => Saw(atCarols, alice), "Antwort auf die unberechtigte Probe");

        }

        #endregion

        #region Disconnect_MakesTheResourceUnavailable()

        /// <summary>
        /// RFC 6121, Abschnitt 4.5.2: Endet die Verbindung, ohne dass der
        /// Client selbst <c>&lt;presence type='unavailable'/&gt;</c> geschickt
        /// hat, erzeugt der Server sie in seinem Namen. Ohne das führen die
        /// Kontakte die Resource für immer als online.
        /// </summary>
        [Test]
        public async Task Disconnect_MakesTheResourceUnavailable()
        {

            MakeContacts("alice", "bob");

            var alice          = await ConnectClientAsync("alice");
            var (_, atBobs)    = await WatcherAsync("bob");

            await alice.DisconnectAsync();

            await WaitFor(() => atBobs.Any(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                          "unavailable für die getrennte Resource");

        }

        #endregion

        #region LostConnection_MakesTheResourceUnavailable()

        /// <summary>
        /// Derselbe Fall, aber unsanft: die Sitzung wird abgerissen, ohne dass
        /// der Client etwas dazu sagen kann. Genau dafür gibt es die Regel.
        /// </summary>
        /// <remarks>
        /// Seit XEP-0198 Abschnitt 5 kommt die Abmeldung nicht mehr im selben
        /// Atemzug: ein abgerissener Stream wird zunächst aufgehoben, weil
        /// sein Client wiederkommen darf, und erst wenn er ausbleibt, wird die
        /// Abmeldung nachgeholt. Die Regel aus RFC 6121 gilt unverändert - nur
        /// nach Ablauf der Frist.
        ///
        /// Deshalb hier eine kurze Frist. Die Vorgabe von einer Minute ist für
        /// den Betrieb richtig und für einen Test unbrauchbar.
        /// </remarks>
        [Test]
        public async Task LostConnection_MakesTheResourceUnavailable()
        {

            Server.ResumptionTimeout = TimeSpan.FromMilliseconds(1);

            MakeContacts("alice", "bob");

            var alice          = await ConnectClientAsync("alice");
            var (_, atBobs)    = await WatcherAsync("bob");

            Server.SessionOf(alice.FullJid)!.Kill();

            await WaitFor(() => atBobs.Any(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                          "unavailable für die abgerissene Resource",
                          TimeSpan.FromSeconds(20));

        }

        #endregion

        #region LostConnection_TellsOnlyTheSubscribers()

        /// <summary>
        /// Auch die Abmeldung ist eine Presence-Auskunft und darf Fremde nicht
        /// erreichen - sonst verriete gerade das Ende einer Sitzung, was ihr
        /// Beginn verschweigt.
        /// </summary>
        [Test]
        public async Task LostConnection_TellsOnlyTheSubscribers()
        {

            MakeContacts("alice", "bob");

            var alice           = await ConnectClientAsync("alice");
            var (_, atCarols)   = await WatcherAsync("carol");

            Server.SessionOf(alice.FullJid)!.Kill();

            await WaitAgainst(() => atCarols.Any(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                              "unavailable bei der fremden Carol");

        }

        #endregion

        #region OwnUnavailable_IsNotRepeatedByTheServer()

        /// <summary>
        /// Hat der Client sich ordentlich abgemeldet, ist die Sache erledigt -
        /// der Verbindungsabbau darf die Abmeldung nicht ein zweites Mal
        /// verschicken.
        /// </summary>
        [Test]
        public async Task OwnUnavailable_IsNotRepeatedByTheServer()
        {

            MakeContacts("alice", "bob");

            var alice          = await ConnectClientAsync("alice");
            var (_, atBobs)    = await WatcherAsync("bob");

            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => atBobs.Any(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                          "eigene Abmeldung von Alice");

            await alice.DisconnectAsync();

            // Der Gegenprobe ihre Zeit lassen: eine zweite Abmeldung käme
            // unmittelbar nach dem Verbindungsabbau.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(atBobs.Count(p => p.EndsWith("|unavailable", StringComparison.Ordinal)),
                        Is.EqualTo(1),
                        "Die Abmeldung darf genau einmal ankommen.");

        }

        #endregion

        #region IsPresenceSubscriber_ReadsTheSubscriptionState()

        /// <summary>
        /// Die Richtung, um die es geht: <c>from</c> und <c>both</c> heissen
        /// "der Kontakt sieht mich". Ein <c>to</c> heisst das Gegenteil, und
        /// eine Verwechslung der beiden gäbe die Presence genau an die falsche
        /// Hälfte des Rosters.
        /// </summary>
        [TestCase("both",   true)]
        [TestCase("from",   true)]
        [TestCase("to",     false)]
        [TestCase("none",   false)]
        [TestCase("remove", false)]
        public void IsPresenceSubscriber_ReadsTheSubscriptionState(String subscription, Boolean expected)
        {

            var account = new XMPPAccount("alice@localhost", "pw");
            account.SetRosterEntry(new RosterEntry("bob@localhost", null, subscription));

            Assert.That(account.IsPresenceSubscriber("bob@localhost"), Is.EqualTo(expected));

        }

        #endregion

        #region IsPresenceSubscriber_IsFalseForAnUnknownContact()

        /// <summary>Wer gar nicht im Roster steht, sieht nichts.</summary>
        [Test]
        public void IsPresenceSubscriber_IsFalseForAnUnknownContact()
        {

            var account = new XMPPAccount("alice@localhost", "pw");

            Assert.That(account.IsPresenceSubscriber("fremd@localhost"), Is.False);

        }

        #endregion

    }

}
