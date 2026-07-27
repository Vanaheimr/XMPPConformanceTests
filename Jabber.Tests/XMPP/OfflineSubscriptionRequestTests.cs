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
    /// Aufbewahrte Subscription-Anfragen nach RFC 6121, Abschnitt 3.1.3,
    /// Regel 4: wer gerade nicht verbunden ist, soll seine Anfragen trotzdem
    /// bekommen.
    /// </summary>
    /// <remarks>
    /// Ohne das Aufbewahren geht eine Anfrage an ein abgemeldetes Konto
    /// ersatzlos verloren - und zwar unbemerkt auf beiden Seiten. Der
    /// Antragsteller sieht ein <c>ask='subscribe'</c> in seinem Roster und
    /// wartet auf eine Antwort; der Kontakt hat nie erfahren, dass er gefragt
    /// wurde. Genau das war hier bis zuletzt der Fall.
    ///
    /// Der Abschnitt verlangt mehr als einmaliges Nachreichen: aufbewahrt wird
    /// die <b>vollständige</b> Stanza, und zugestellt wird sie bei
    /// <b>jeder</b> neu verfügbaren Resource, bis der Kontakt zustimmt oder
    /// ablehnt.
    /// </remarks>
    [TestFixture]
    public class OfflineSubscriptionRequestTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Alice => $"alice@{Server.Domain}";
        private String Bob   => $"bob@{Server.Domain}";

        /// <summary>
        /// Ein noch nicht verbundener Client mit angehängtem Zähler für
        /// eingehende Anfragen.
        /// </summary>
        /// <remarks>
        /// Der Zähler wird <b>vor</b> dem Verbinden angehängt, nicht danach:
        /// eine nachgereichte Anfrage kommt unmittelbar nach der ersten
        /// Presence, und ein erst danach angemeldeter Empfänger verpasst sie
        /// je nach Zeitverlauf. Ein Test, der so scheitert, sieht aus wie ein
        /// Fehler im Server.
        /// </remarks>
        private (XMPPClient, List<(String From, String? Status)>) VorbereiteterClient(String localPart)
        {

            var client     = CreateClient(localPart);
            var eingegangen = new List<(String, String?)>();

            client.OnSubscriptionRequest += (from, status) => eingegangen.Add((from, status));

            return (client, eingegangen);

        }

        /// <summary>Meldet einen Client ab und wartet, bis der Server das sieht.</summary>
        private async Task AbmeldenAsync(XMPPClient client, String bareJid)
        {
            await client.DisconnectAsync();
            await WaitFor(() => Server.SessionsOf(bareJid).Count == 0,
                          $"das Ende der Sitzung von {bareJid}");
        }

        #endregion


        #region ARequestToAnOfflineAccount_ArrivesAtTheNextLogin()

        /// <summary>
        /// Der Kern von Regel 4: Bob ist nicht da, als Alice fragt - und
        /// erfährt es beim nächsten Anmelden.
        /// </summary>
        [Test]
        public async Task ARequestToAnOfflineAccount_ArrivesAtTheNextLogin()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");

            // Erst jetzt kommt Bob - die Anfrage liegt schon eine Weile.
            var (bob, anfragen) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => anfragen.Count > 0, "die nachgereichte Anfrage");

            Assert.That(anfragen[0].From, Is.EqualTo(Alice));

        }

        #endregion

        #region NothingIsAddedToTheRosterBeforeApproval()

        /// <summary>
        /// Abschnitt 3.1.3, Security Warning: "the contact's server MUST NOT
        /// add an item for the user to the contact's roster" - solange nicht
        /// zugestimmt wurde.
        /// </summary>
        /// <remarks>
        /// Die Warnung hat einen handfesten Grund: ein Eintrag im Roster ist
        /// für den Kontakt sichtbar und überdauert die Anfrage. Wer beliebige
        /// Fremde in fremde Roster schreiben kann, kann sie vollschreiben.
        /// Aufbewahrt wird die Anfrage deshalb neben dem Roster, nicht darin.
        /// </remarks>
        [Test]
        public async Task NothingIsAddedToTheRosterBeforeApproval()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");

            var (bob, anfragen) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => anfragen.Count > 0, "die nachgereichte Anfrage");

            Assert.That(Server.GetAccount(Bob)!.Roster, Is.Empty,
                        "Vor der Zustimmung gehört der Antragsteller nicht in Bobs Roster.");

        }

        #endregion

        #region TheCompleteStanzaIsKept()

        /// <summary>
        /// Regel 4 verlangt die vollständige Stanza, "including any extended
        /// content contained therein".
        /// </summary>
        /// <remarks>
        /// Der erweiterte Inhalt ist hier keine Formalie: das
        /// <c>&lt;status/&gt;</c> einer Anfrage ist die Begründung, mit der ein
        /// Mensch entscheidet, ob er zustimmt. Eine Anfrage ohne sie ist eine
        /// andere Anfrage.
        /// </remarks>
        [Test]
        public async Task TheCompleteStanzaIsKept()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.Connection.SendRawAsync(
                      $"<presence to='{Bob}' type='subscribe'><status>Wir kennen uns vom Bahnsteig</status></presence>");

            var (bob, anfragen) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => anfragen.Count > 0, "die nachgereichte Anfrage");

            Assert.That(anfragen[0].Status, Is.EqualTo("Wir kennen uns vom Bahnsteig"));

        }

        #endregion

        #region TheRequestIsRepeatedUntilItIsAnswered()

        /// <summary>
        /// "The contact's server MUST continue to deliver the subscription
        /// request whenever the contact creates an available resource, until
        /// the contact either approves or denies the request."
        /// </summary>
        /// <remarks>
        /// Einmaliges Nachreichen genügt nicht. Wer sich anmeldet, die Anfrage
        /// übersieht und sich wieder abmeldet, hätte sie sonst für immer
        /// verloren - und der Antragsteller wartete auf eine Antwort, die
        /// niemand mehr geben kann.
        /// </remarks>
        [Test]
        public async Task TheRequestIsRepeatedUntilItIsAnswered()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");

            var (ersteAnmeldung, ersteAnfragen) = VorbereiteterClient("bob");
            await ersteAnmeldung.ConnectAsync();
            await WaitFor(() => ersteAnfragen.Count > 0, "die Anfrage bei der ersten Anmeldung");

            // Ohne zu antworten wieder weg.
            await AbmeldenAsync(ersteAnmeldung, Bob);

            var (zweiteAnmeldung, zweiteAnfragen) = VorbereiteterClient("bob");
            await zweiteAnmeldung.ConnectAsync();

            await WaitFor(() => zweiteAnfragen.Count > 0, "die Anfrage bei der zweiten Anmeldung");

            Assert.That(zweiteAnfragen[0].From, Is.EqualTo(Alice));

        }

        #endregion

        #region AnApprovedRequest_IsNotRepeated()

        /// <summary>
        /// "... until the contact either approves or denies the request."
        /// Zustimmung beendet das Nachreichen.
        /// </summary>
        [Test]
        public async Task AnApprovedRequest_IsNotRepeated()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");

            var (ersteAnmeldung, ersteAnfragen) = VorbereiteterClient("bob");
            await ersteAnmeldung.ConnectAsync();
            await WaitFor(() => ersteAnfragen.Count > 0, "die Anfrage bei der ersten Anmeldung");

            await ersteAnmeldung.AcceptSubscriptionAsync(Alice);
            await WaitFor(() => Server.GetAccount(Bob)?.SubscriptionOf(Alice) == "from",
                          "die Zustimmung");

            await AbmeldenAsync(ersteAnmeldung, Bob);

            var (zweiteAnmeldung, zweiteAnfragen) = VorbereiteterClient("bob");
            await zweiteAnmeldung.ConnectAsync();

            await WaitAgainst(() => zweiteAnfragen.Count > 0,
                              "eine bereits beantwortete Anfrage noch einmal");

        }

        #endregion

        #region ADeniedRequest_IsNotRepeated()

        /// <summary>
        /// Ablehnung ebenso - sonst käme dieselbe Anfrage bei jeder Anmeldung
        /// wieder, und Ablehnen wäre wirkungslos.
        /// </summary>
        [Test]
        public async Task ADeniedRequest_IsNotRepeated()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");

            var (ersteAnmeldung, ersteAnfragen) = VorbereiteterClient("bob");
            await ersteAnmeldung.ConnectAsync();
            await WaitFor(() => ersteAnfragen.Count > 0, "die Anfrage bei der ersten Anmeldung");

            await ersteAnmeldung.DenySubscriptionAsync(Alice);
            await WaitFor(() => Server.GetAccount(Alice)?.SubscriptionOf(Bob) is null or "none",
                          "die Ablehnung");

            await AbmeldenAsync(ersteAnmeldung, Bob);

            var (zweiteAnmeldung, zweiteAnfragen) = VorbereiteterClient("bob");
            await zweiteAnmeldung.ConnectAsync();

            await WaitAgainst(() => zweiteAnfragen.Count > 0,
                              "eine bereits abgelehnte Anfrage noch einmal");

        }

        #endregion

        #region RepeatedRequests_AreStoredOnlyOnce()

        /// <summary>
        /// Regel 4: "MUST deliver only one of the requests when the contact
        /// next has an available resource; ... this helps to prevent
        /// 'subscription request spam'".
        /// </summary>
        /// <remarks>
        /// Ohne diese Grenze wäre das Aufbewahren selbst die Schwachstelle:
        /// wer eine Anfrage hundertmal schickt, während der Kontakt weg ist,
        /// überschüttete ihn beim Anmelden mit hundert Anfragen.
        /// </remarks>
        [Test]
        public async Task RepeatedRequests_AreStoredOnlyOnce()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            for (var i = 0; i < 5; i++)
                await alice.Connection.SendRawAsync($"<presence to='{Bob}' type='subscribe'/>");

            var (bob, anfragen) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => anfragen.Count > 0, "die nachgereichte Anfrage");

            // Die weiteren hätten inzwischen ankommen können.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(anfragen, Has.Count.EqualTo(1));

        }

        #endregion

        #region AFurtherRequest_IsNotDeliveredAgain()

        /// <summary>
        /// Anhang A, Tabelle 6: liegt bereits eine Anfrage vor, <i>soll</i> ein
        /// weiteres <c>subscribe</c> desselben Absenders nicht noch einmal
        /// zugestellt werden.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zu <see cref="RepeatedRequests_AreStoredOnlyOnce"/>,
        /// und die schärfere: dort ist der Kontakt abgemeldet, und ob eine
        /// wiederholte Anfrage abgewiesen wird oder die aufbewahrte ersetzt,
        /// sieht am Ende gleich aus - einmal zugestellt. Hier ist er verbunden,
        /// und der Unterschied wird sichtbar, weil jede angenommene Anfrage
        /// sofort hinausgeht.
        ///
        /// Geprüft wird deshalb auch der Inhalt: aufbewahrt und zugestellt
        /// bleibt die <b>erste</b>. Bestimmte die letzte, könnte jemand die
        /// Begründung, mit der er einmal gefragt hat, beliebig oft gegen eine
        /// andere austauschen.
        /// </remarks>
        [Test]
        public async Task AFurtherRequest_IsNotDeliveredAgain()
        {

            var alice = await ConnectClientAsync("alice");

            var (bob, anfragen) = VorbereiteterClient("bob");
            Server.AddAccount("bob");
            await bob.ConnectAsync();

            await alice.Connection.SendRawAsync(
                      $"<presence to='{Bob}' type='subscribe'><status>erste</status></presence>");
            await WaitFor(() => anfragen.Count > 0, "die erste Anfrage");

            await alice.Connection.SendRawAsync(
                      $"<presence to='{Bob}' type='subscribe'><status>zweite</status></presence>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(anfragen, Has.Count.EqualTo(1));
                Assert.That(anfragen[0].Status, Is.EqualTo("erste"),
                            "Aufbewahrt bleibt die zuerst gestellte Anfrage.");
            });

        }

        #endregion

        #region AStatusChange_DoesNotRepeatTheRequest()

        /// <summary>
        /// Nachgereicht wird beim Verfügbar<i>werden</i>, nicht bei jeder
        /// Presence.
        /// </summary>
        /// <remarks>
        /// Der Unterschied ist leicht zu übersehen und im Betrieb sofort
        /// spürbar: ein Client schickt bei jedem Wechsel auf "abwesend" oder
        /// "beschäftigt" eine neue Presence. Hinge das Nachreichen daran,
        /// bekäme der Nutzer dieselbe unbeantwortete Anfrage jedes Mal wieder
        /// vorgelegt.
        /// </remarks>
        [Test]
        public async Task AStatusChange_DoesNotRepeatTheRequest()
        {

            var alice = await ConnectClientAsync("alice");

            var (bob, anfragen) = VorbereiteterClient("bob");
            Server.AddAccount("bob");
            await bob.ConnectAsync();

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => anfragen.Count > 0, "die Anfrage");

            // Dieselbe Sitzung, neue Presence - die Anfrage ist weiterhin
            // unbeantwortet und liegt weiterhin aufbewahrt.
            await bob.SetPresenceAsync("away", "Mittagspause");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(anfragen, Has.Count.EqualTo(1));

        }

        #endregion

        #region TheLimit_DropsFurtherRequestsInsteadOfTheStoredOnes()

        /// <summary>
        /// Security Warning zu Abschnitt 3.1.3: wer Anfragen aufhebt, hebt
        /// auf, was Fremde schicken - deshalb eine Obergrenze.
        /// </summary>
        /// <remarks>
        /// Geprüft wird nicht nur, <i>dass</i> die Grenze greift, sondern in
        /// welche Richtung: die neue Anfrage wird verworfen, die bereits
        /// aufbewahrte bleibt. Andersherum könnte ein Angreifer die echte
        /// Anfrage eines Bekannten gezielt hinausdrängen - eine Grenze, die
        /// verdrängt statt abzuweisen, wäre selbst der Angriff.
        /// </remarks>
        [Test]
        public async Task TheLimit_DropsFurtherRequestsInsteadOfTheStoredOnes()
        {

            Server.MaxStoredSubscriptionRequests = 1;

            var alice = await ConnectClientAsync("alice");
            var carol = await ConnectClientAsync("carol");
            Server.AddAccount("bob");

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => Server.GetAccount(Bob)!.PendingSubscriptionRequests.Count == 1,
                          "Alices aufbewahrte Anfrage");

            await carol.AddContactAsync(Bob, "Bob");

            var (bob, anfragen) = VorbereiteterClient("bob");
            await bob.ConnectAsync();

            await WaitFor(() => anfragen.Count > 0, "die nachgereichte Anfrage");
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(anfragen, Has.Count.EqualTo(1));
                Assert.That(anfragen[0].From, Is.EqualTo(Alice),
                            "Die zuerst aufbewahrte Anfrage bleibt stehen.");
            });

        }

        #endregion

        #region ASecondResource_AlsoGetsTheRequest()

        /// <summary>
        /// "... whenever the contact creates an available resource": auch eine
        /// zweite Resource neben einer bereits angemeldeten.
        /// </summary>
        /// <remarks>
        /// Der Fall sieht künstlich aus, ist aber der Regelfall: Telefon und
        /// Rechner gleichzeitig. Beantwortet wird die Anfrage dort, wo der
        /// Mensch gerade sitzt - und das ist nicht notwendig die Resource, die
        /// zuerst da war.
        /// </remarks>
        [Test]
        public async Task ASecondResource_AlsoGetsTheRequest()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var (erste, ersteAnfragen) = VorbereiteterClient("bob");
            await erste.ConnectAsync();

            await alice.AddContactAsync(Bob, "Bob");
            await WaitFor(() => ersteAnfragen.Count > 0, "die Anfrage bei der ersten Resource");

            var (zweite, zweiteAnfragen) = VorbereiteterClient("bob");
            await zweite.ConnectAsync();

            await WaitFor(() => zweiteAnfragen.Count > 0, "die Anfrage bei der zweiten Resource");

            Assert.That(zweiteAnfragen[0].From, Is.EqualTo(Alice));

        }

        #endregion

    }

}
