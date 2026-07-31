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
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6120, Abschnitt 8.3.3.8: Steht im <c>to</c> kein JID, antwortet der
    /// Server mit <c>&lt;jid-malformed/&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Die Prüfung selbst gibt es seit D42 bis D45 vollständig - RFC 7622 mit
    /// PRECIS, IDNA2008, Bidi-Regel und den kontextabhängigen Regeln aus
    /// Anhang A. <b>Der Server hat sie nie gefragt.</b> Was ankam, ging in die
    /// Zustellung, und ein unmöglicher Empfänger sah dort aus wie ein
    /// abwesender: Der Absender bekam Schweigen oder eine Ablage, aus der ihn
    /// nie jemand abholt.
    ///
    /// Eine geprüfte Regel ohne Aufrufer ist keine halbe Regel, sondern keine.
    /// Dieselbe Lücke stand in D43 (die IDNA-Prüfung war fertig und im JID
    /// nicht verdrahtet) und in D45 - deshalb prüft hier jeder Test den Weg
    /// über die Leitung und nicht die Funktion.
    /// </remarks>
    [TestFixture]
    public class MalformedRecipientTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Schickt eine Stanza und gibt die Antwort zurück, die der Server
        /// darauf geschickt hat.
        /// </summary>
        private async Task<String> AntwortAufAsync(XMPPClient client, String stanza)
        {

            var session = Server.SessionOf(client.FullJid)!;
            var vorher  = session.Sent.Count;

            await client.SendRawAsync(stanza);

            await WaitFor(() => Neu(session, vorher).Any(f => f.Contains("type='error'", StringComparison.Ordinal)),
                          $"die Abweisung des Servers auf: {stanza}");

            return Neu(session, vorher).First(f => f.Contains("type='error'", StringComparison.Ordinal));

        }

        /// <summary>Was der Server seit diesem Stand geschickt hat.</summary>
        private static IEnumerable<String> Neu(XMPPSession session, Int32 stand)
            => session.Sent.Skip(stand);

        #endregion


        #region AMessageToANonJid_IsRefused(...)

        /// <summary>
        /// Fünf Adressen, die keine sind - und jede aus einem anderen Grund.
        /// </summary>
        /// <remarks>
        /// Eine einzige unmögliche Adresse liesse offen, wie weit die Prüfung
        /// reicht: <c>alice@</c> fällt schon einem Vergleich auf zwei leere
        /// Zeichenketten auf, <c>alice@-localhost</c> erst der Labelregel aus
        /// RFC 5891, und das Leerzeichen im Localpart nur der
        /// PRECIS-IdentifierClass. Fünf Gründe, damit ein Test nicht bestehen
        /// kann, indem er den einfachsten davon abdeckt.
        /// </remarks>
        [TestCase("@localhost",         TestName = "Ohne Localpart")]
        [TestCase("alice@",             TestName = "Ohne Domainpart")]
        [TestCase("alice@localhost/",   TestName = "Mit leerer Resource")]
        [TestCase("al ice@localhost",   TestName = "Mit Leerzeichen im Localpart")]
        [TestCase("alice@-localhost",   TestName = "Mit Bindestrich am Labelanfang")]
        public async Task AMessageToANonJid_IsRefused(String empfaenger)
        {

            var alice = await ConnectClientAsync();

            var antwort = await AntwortAufAsync(
                              alice,
                              $"<message to='{empfaenger}' type='chat'><body>Hallo</body></message>");

            Assert.Multiple(() =>
            {

                // Auf eine Nachricht antwortet eine Nachricht. Zwischen
                // Elementnamen und Typ steht der Namensraum: Jede Stanza an
                // einen Client trägt jabber:client.
                Assert.That(antwort, Does.StartWith("<message"));
                Assert.That(antwort, Does.Contain("type='error'"));

                Assert.That(antwort, Does.Contain("jid-malformed"));

                Assert.That(antwort, Does.Contain("type='modify'"),
                            "RFC 6120, Abschnitt 8.3.3.8: die Fehlerart ist 'modify'.");

                // Nicht der gemeinte Empfänger, wie bei service-unavailable:
                // Dort hat der Server für jemanden geantwortet, hier für
                // niemanden - die Adresse ist keine.
                Assert.That(antwort, Does.Contain($"from='{Server.Domain}'"));

            });

        }

        #endregion

        #region AnIqToANonJid_KeepsItsId()

        /// <summary>
        /// Die Ablehnung einer Anfrage trägt deren <c>id</c> - sonst weiss ein
        /// Frager mit mehreren offenen Anfragen nur, dass eine gescheitert ist.
        /// </summary>
        [Test]
        public async Task AnIqToANonJid_KeepsItsId()
        {

            var alice = await ConnectClientAsync();

            var antwort = await AntwortAufAsync(
                              alice,
                              "<iq type='get' id='frage-1' to='alice@@localhost'>" +
                              "<query xmlns='jabber:iq:version'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.StartWith("<iq"));
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("id='frage-1'"));
                Assert.That(antwort, Does.Contain("jid-malformed"));
            });

        }

        #endregion

        #region APresenceToANonJid_IsRefusedAsWell()

        /// <summary>
        /// Auch gerichtete Presence, und mit demselben Element.
        /// </summary>
        /// <remarks>
        /// Ungerichtete Presence trägt kein <c>to</c> und darf davon nicht
        /// getroffen werden - das prüft jeder andere Test der Sammlung
        /// mit, denn ohne sie gilt keine Sitzung als verfügbar.
        /// </remarks>
        [Test]
        public async Task APresenceToANonJid_IsRefusedAsWell()
        {

            var alice = await ConnectClientAsync();

            var antwort = await AntwortAufAsync(alice, "<presence to='alice@localhost/'/>");

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.StartWith("<presence"));
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("jid-malformed"));
            });

        }

        #endregion

        #region AnErrorStanza_IsNotAnsweredWithAnError()

        /// <summary>
        /// Auf eine Fehler-Stanza folgt kein Fehler - verworfen wird sie
        /// trotzdem.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 8.3.1. Ohne diese Ausnahme könnten zwei Server
        /// sich gegenseitig Meldungen zuschieben, bis einer aufgibt: Der eine
        /// antwortet auf die unmögliche Adresse, der andere auf die Antwort.
        /// </remarks>
        [Test]
        public async Task AnErrorStanza_IsNotAnsweredWithAnError()
        {

            var alice   = await ConnectClientAsync();
            var session = Server.SessionOf(alice.FullJid)!;
            var vorher  = session.Sent.Count;

            await alice.SendRawAsync(
                      "<message to='@localhost' type='error'>" +
                      "<error type='cancel'><gone xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>" +
                      "</message>");

            await WaitAgainst(() => Neu(session, vorher).Any(f => f.Contains("jid-malformed", StringComparison.Ordinal)),
                              "eine Antwort auf eine Fehler-Stanza");

        }

        #endregion

        #region ARefusedStanza_IsNotDeliveredAnyway()

        /// <summary>
        /// Die abgewiesene Stanza endet auch wirklich - sie wird nicht
        /// zusätzlich zugestellt.
        /// </summary>
        /// <remarks>
        /// Die Adresse ist mit Absicht so gewählt, dass ein Weiterreichen
        /// auffiele: <c>bob@…/</c> ist kein JID (eine leere Resource gibt es
        /// nicht), aber der Teil davor gehört einem angemeldeten Konto. Eine
        /// Prüfung, die zwar antwortet und danach trotzdem zustellt, käme über
        /// den Weg für Bare-JIDs bei Bob an - und ohne diesen Test wäre sie
        /// von der richtigen nicht zu unterscheiden.
        /// </remarks>
        [Test]
        public async Task ARefusedStanza_IsNotDeliveredAnyway()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync();
            var bob   = await ConnectClientAsync("bob", createAccount: false);

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            await alice.SendRawAsync(
                      $"<message to='bob@{Server.Domain}/' type='chat'><body>Trotzdem</body></message>");

            await WaitAgainst(() => { lock (angekommen) return angekommen.Contains("Trotzdem"); },
                              "die Zustellung einer abgewiesenen Nachricht");

        }

        #endregion

        #region AnUnusualButValidJid_IsDelivered()

        /// <summary>
        /// Ein JID, der ungewöhnlich aussieht und trotzdem einer ist, kommt
        /// durch.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe, ohne die „lehne alles ab" eine bestandene Lösung
        /// wäre. Sie prüft zugleich, dass hier wirklich RFC 7622 arbeitet und
        /// nicht eine Handvoll Sonderzeichen: Der Localpart trägt Umlaute, die
        /// Resource ein Leerzeichen - im Localpart wäre es verboten
        /// (IdentifierClass), in der Resource erlaubt (FreeformClass).
        /// </remarks>
        [Test]
        public async Task AnUnusualButValidJid_IsDelivered()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync();
            var bob   = await ConnectClientAsync("bob", createAccount: false);

            Server.AddAccount("bäcker");

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            var session = Server.SessionOf(alice.FullJid)!;
            var vorher  = session.Sent.Count;

            // Erst die ungewöhnliche Adresse: Sie darf nicht als unmöglich
            // gelten. Zugestellt wird sie niemandem - es sitzt niemand dort -,
            // und die Antwort darauf ist ein anderer Fehler als jid-malformed.
            await alice.SendRawAsync(
                      $"<message to='bäcker@{Server.Domain}/Büro 1' type='chat'><body>Brötchen</body></message>");

            // Und dann eine gewöhnliche, die ankommen muss.
            await alice.SendMessageAsync($"bob@{Server.Domain}", "Und Kaffee");

            await WaitFor(() => { lock (angekommen) return angekommen.Contains("Und Kaffee"); },
                          "die gewöhnliche Nachricht");

            Assert.That(Neu(session, vorher).Any(f => f.Contains("jid-malformed", StringComparison.Ordinal)),
                        Is.False,
                        "Ein gültiger JID wurde als unmöglich abgewiesen.");

        }

        #endregion

    }

}
