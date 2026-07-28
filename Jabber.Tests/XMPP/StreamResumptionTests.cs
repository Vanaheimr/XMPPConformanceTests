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
    /// XEP-0198, Abschnitt 5: der Server hält einen abgerissenen Stream bereit,
    /// statt ihn sofort zu beerdigen.
    /// </summary>
    /// <remarks>
    /// Was hier geprüft wird, ist die Hälfte, die ohne Rückkehrer auskommt:
    /// dass ein <c>&lt;enable resume='true'/&gt;</c> beantwortet wird, dass ein
    /// Verbindungsabriss die Sitzung <b>nicht</b> beendet, und dass sie nach
    /// Ablauf der Frist doch endet. Das <c>&lt;resume/&gt;</c> selbst sitzt in
    /// der Aufbauphase des Clients - vor dem Resource Binding, nach der
    /// Anmeldung - und ist erst prüfbar, wenn der Client es schickt.
    ///
    /// Der Unterschied ist für die Kontakte sichtbar und deshalb heikel: bis
    /// jetzt erzeugte der Server beim Abriss sofort eine Abmeldung im Namen des
    /// Clients (RFC 6121, Abschnitt 4.5.2). Wer wiederkommen darf, darf nicht
    /// abgemeldet werden - sonst sähen die Kontakte ein Aus und Ein, wo in
    /// Wahrheit nichts geschehen ist. Bleibt der Rückkehrer aber aus, muss die
    /// Abmeldung nachkommen, sonst führen die Kontakte die Resource für immer
    /// als online.
    /// </remarks>
    [TestFixture]
    public class StreamResumptionTests : AXMPPTests
    {

        #region Data

        /// <summary>
        /// Kurz genug, dass der Verfall im Test abzuwarten ist, lang genug,
        /// dass er nicht mitten in den Aufbau fällt.
        /// </summary>
        private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Verbindet einen Client ohne eigenes Stream Management und handelt es
        /// danach von Hand aus - mit oder ohne Wiederaufnahme.
        /// </summary>
        /// <remarks>
        /// Von Hand, weil der Client die Wiederaufnahme noch nicht selbst
        /// anfordert. Sobald er es tut, kann das hier verschwinden.
        /// </remarks>
        private async Task<(XMPPClient Client, XMPPSession Session)> MitStreamManagementAsync(
                                                                        Boolean resume,
                                                                        String  localPart = "alice")
        {

            var client = await ConnectClientAsync(localPart,
                                                  streamManagement:     false,
                                                  maxReconnectAttempts: 0);

            await client.SendRawAsync(
                      $"<enable xmlns='urn:xmpp:sm:3'{(resume ? " resume='true'" : "")}/>");

            var session = Server.SessionOf(client.FullJid)!;

            await WaitFor(() => session.StreamManagementEnabled,
                          "ausgehandeltes Stream Management");

            return (client, session);

        }

        /// <summary>Die Antwort des Servers auf <c>&lt;enable/&gt;</c>.</summary>
        private static String? EnabledFrame(XMPPSession session)
            => session.Sent.LastOrDefault(f => f.StartsWith("<enabled", StringComparison.Ordinal));

        #endregion


        #region EnableWithResume_IsAnsweredWithAnUnguessableId()

        /// <summary>
        /// Fragt der Client nach Wiederaufnahme, bekommt er eine Kennung.
        /// </summary>
        /// <remarks>
        /// XEP-0198, Abschnitt 5.1: die Kennung ist das einzige Geheimnis, das
        /// den Rückkehrer ausweist. Wer sie kennt, kann den Stream übernehmen -
        /// deshalb darf sie nicht aus etwas Öffentlichem abzuleiten sein.
        ///
        /// Die frühere Fassung schickte <c>id='sm-{Verbindungsnummer}'</c>.
        /// Das ist eine kleine Zahl, die jeder Mitlesende mitzählen kann, und
        /// wäre mit der Wiederaufnahme zu einem Einfallstor geworden. Ohne
        /// Wiederaufnahme war die Kennung folgenlos - genau deshalb ist sie
        /// nie aufgefallen.
        /// </remarks>
        [Test]
        public async Task EnableWithResume_IsAnsweredWithAnUnguessableId()
        {

            var (_, erste)  = await MitStreamManagementAsync(resume: true, localPart: "alice");
            var (_, zweite) = await MitStreamManagementAsync(resume: true, localPart: "bob");

            Assert.Multiple(() =>
            {

                Assert.That(EnabledFrame(erste), Does.Contain("resume='true'"),
                            "Der Server hat die Wiederaufnahme nicht zugesagt.");

                Assert.That(erste.ResumptionId, Is.Not.Null.And.Length.GreaterThanOrEqualTo(22),
                            "Zu kurz, um nicht erraten zu werden.");

                Assert.That(EnabledFrame(erste), Does.Contain($"id='{erste.ResumptionId}'"));

                // Genau die frühere Form.
                Assert.That(erste.ResumptionId, Is.Not.EqualTo($"sm-{erste.ConnectionNumber}"));

                // Und zwei Sitzungen bekommen verschiedene Kennungen.
                //
                // Hier stand zuerst eine Prüfung, die Kennung dürfe die
                // Verbindungsnummer nicht als Teilzeichenkette enthalten. Das
                // sagt nichts: eine zufällige Kennung aus 22 Zeichen enthält
                // fast jede einzelne Ziffer irgendwo. Allein ausgeführt bestand
                // der Test, im vollen Lauf - mit anderer Verbindungsnummer -
                // fiel er durch.
                Assert.That(erste.ResumptionId, Is.Not.EqualTo(zweite.ResumptionId));

            });

        }

        #endregion

        #region EnableWithoutResume_PromisesNothing()

        /// <summary>
        /// Ohne Nachfrage keine Zusage - und damit auch nichts, was der Server
        /// aufheben müsste.
        /// </summary>
        [Test]
        public async Task EnableWithoutResume_PromisesNothing()
        {

            var (_, session) = await MitStreamManagementAsync(resume: false);

            Assert.Multiple(() =>
            {
                Assert.That(EnabledFrame(session), Does.Not.Contain("resume='true'"));
                Assert.That(session.ResumptionId,  Is.Null);
            });

        }

        #endregion

        #region ADroppedResumableStream_DoesNotLogTheUserOut()

        /// <summary>
        /// Reisst die Verbindung eines wiederaufnehmbaren Streams ab, bleibt
        /// die Resource für ihre Kontakte verfügbar.
        /// </summary>
        /// <remarks>
        /// Das ist der Sinn der ganzen Übung. Ohne sie erzeugt der Server beim
        /// Abriss sofort eine Abmeldung, und ein Client, der zwei Sekunden
        /// später wiederkommt, hat seinen Kontakten in der Zwischenzeit ein
        /// Verschwinden vorgeführt, das nie stattgefunden hat.
        /// </remarks>
        [Test]
        public async Task ADroppedResumableStream_DoesNotLogTheUserOut()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitAgainst(() => abmeldungen > 0,
                              "eine Abmeldung von Alice, obwohl ihr Stream aufgehoben wird");

            Assert.That(Server.ResumableStreamCount, Is.EqualTo(1),
                        "Der Stream wurde nicht aufgehoben.");

        }

        #endregion

        #region AKeptStreamExpires_AndThenTheContactsSeeIt()

        /// <summary>
        /// Kommt niemand zurück, endet die Sitzung doch - und die Abmeldung
        /// wird nachgeholt.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test, und ohne sie wäre der Zugewinn
        /// keiner: eine aufgeschobene Abmeldung, die nie kommt, ist schlimmer
        /// als eine zu frühe. Die Kontakte führten die Resource dann für immer
        /// als online, und kein Fehler wäre je sichtbar.
        /// </remarks>
        [Test]
        public async Task AKeptStreamExpires_AndThenTheContactsSeeIt()
        {

            Server.ResumptionTimeout = Frist;

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitFor(() => abmeldungen > 0,
                          "die nachgeholte Abmeldung nach Ablauf der Frist",
                          Frist + TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {

                Assert.That(abmeldungen, Is.EqualTo(1),
                            "Die Abmeldung kam mehr als einmal.");

                Assert.That(Server.ResumableStreamCount, Is.Zero,
                            "Der verfallene Stream liegt noch herum.");

            });

        }

        #endregion

        #region AStreamWithoutResume_IsAnnouncedAtOnce()

        /// <summary>
        /// Ohne zugesagte Wiederaufnahme bleibt es beim bisherigen Verhalten.
        /// </summary>
        /// <remarks>
        /// Der Test hält fest, dass die Aufschiebung an der Zusage hängt und
        /// nicht am Stream Management überhaupt. Ohne ihn liesse sich die
        /// Abmeldung versehentlich für alle aufschieben, und die
        /// Verzögerung fiele erst im Betrieb auf.
        /// </remarks>
        [Test]
        public async Task AStreamWithoutResume_IsAnnouncedAtOnce()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: false);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitFor(() => abmeldungen > 0, "die sofortige Abmeldung");

            Assert.That(Server.ResumableStreamCount, Is.Zero);

        }

        #endregion

        #region AcknowledgedStanzas_LeaveTheBuffer()

        /// <summary>
        /// Was der Client bestätigt hat, hebt der Server nicht länger auf.
        /// </summary>
        /// <remarks>
        /// Der Puffer trägt die Stanzas, die nach einer Wiederaufnahme
        /// nachzusenden wären (XEP-0198, Abschnitt 5). Er darf nur das
        /// enthalten, was noch nicht angekommen ist - sonst wüchse er ohne
        /// Ende, und der Rückkehrer bekäme alles doppelt, was er längst hat.
        /// </remarks>
        [Test]
        public async Task AcknowledgedStanzas_LeaveTheBuffer()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob               = await ConnectClientAsync("bob", createAccount: false);

            for (var i = 0; i < 3; i++)
                await bob.SendMessageAsync($"alice@{Server.Domain}", $"Nachricht {i}");

            await WaitFor(() => aliceSession.StanzasSentToClient >= 3,
                          "drei zugestellte Nachrichten");

            Assert.That(aliceSession.UnacknowledgedToClient, Is.GreaterThanOrEqualTo(3),
                        "Nichts gepuffert - dann gäbe es nach einer Wiederaufnahme nichts nachzusenden.");

            await aliceSession.RequestAckAsync();

            await WaitFor(() => aliceSession.UnacknowledgedToClient == 0,
                          "das Leeren des Puffers nach dem <a/> des Clients");

            Assert.That(aliceSession.LastAckFromClient, Is.EqualTo(aliceSession.StanzasSentToClient),
                        "Der Client hat einen anderen Stand bestätigt, als der Server gesendet hat.");

        }

        #endregion

    }

}
