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
    /// Fehlerbehandlung im Zusammenspiel: eine abgelehnte Anfrage darf für den
    /// Aufrufer nicht wie ein Erfolg aussehen.
    /// </summary>
    [TestFixture]
    public class ErrorHandlingTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private async Task<XMPPSession> SessionOfAsync(XMPPClient client)
        {

            await WaitFor(() => Server.SessionOf(client.FullJid) is not null,
                          "Serversitzung zum Client");

            return Server.SessionOf(client.FullJid)!;

        }

        #endregion


        #region RejectedPing_ReturnsNullInsteadOfARoundTripTime()

        /// <summary>
        /// Der deutlichste Fall: ein mit <c>iq error</c> abgelehnter Ping lief
        /// früher durch ProcessPong und lieferte eine gemessene Laufzeit. Eine
        /// Gegenstelle, die XEP-0199 gar nicht unterstützt, sah damit aus wie
        /// eine besonders schnelle.
        /// </summary>
        [Test]
        public async Task RejectedPing_ReturnsNullInsteadOfARoundTripTime()
        {

            Server.FailPings = true;

            var client = await ConnectClientAsync();

            StanzaError? reported = null;
            client.OnStanzaError += (_, error) => reported = error;

            var rtt = await client.PingAsync();

            // PingAsync kehrt zurück, sobald die Anfrage aufgelöst ist; das
            // Event wird unmittelbar danach ausgelöst.
            await WaitFor(() => reported is not null, "gemeldeter Stanza-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(rtt, Is.Null,
                            "Ein abgelehnter Ping darf keine Laufzeit liefern.");

                Assert.That(reported,            Is.Not.Null, "Der Fehler wurde nicht gemeldet.");
                Assert.That(reported!.Condition, Is.EqualTo("service-unavailable"));
                Assert.That(reported!.Type,      Is.EqualTo(StanzaErrorType.Cancel));
            });

        }

        #endregion

        #region AcceptedPing_StillMeasuresARoundTripTime()

        /// <summary>
        /// Gegenprobe: der Normalfall muss weiter funktionieren.
        /// </summary>
        [Test]
        public async Task AcceptedPing_StillMeasuresARoundTripTime()
        {

            var client = await ConnectClientAsync();

            var rtt = await client.PingAsync();

            Assert.That(rtt, Is.Not.Null);

        }

        #endregion

        #region RejectedDiscoQuery_ReturnsNullInsteadOfAnEmptyResult()

        /// <summary>
        /// Eine abgelehnte disco-Abfrage lieferte früher ein leeres, aber
        /// erfolgreiches Ergebnis - nicht zu unterscheiden von einer Entity
        /// ohne Features.
        /// </summary>
        [Test]
        public async Task RejectedDiscoQuery_ReturnsNullInsteadOfAnEmptyResult()
        {

            Server.FailDiscoInfo = true;

            var client = await ConnectClientAsync();

            StanzaError? reported = null;
            client.OnStanzaError += (_, error) => reported = error;

            var info = await client.Connection.Disco!.QueryInfoAsync(Server.Domain,
                                                                     timeout: TimeSpan.FromSeconds(5));

            await WaitFor(() => reported is not null, "gemeldeter Stanza-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(info, Is.Null,
                            "Eine abgelehnte Abfrage darf kein Ergebnis liefern.");

                Assert.That(reported,            Is.Not.Null);
                Assert.That(reported!.Condition, Is.EqualTo("item-not-found"));
                Assert.That(reported!.Type,      Is.EqualTo(StanzaErrorType.Modify));
                Assert.That(reported!.Text,      Is.EqualTo("Diesen Node gibt es hier nicht."));
            });

        }

        #endregion

        #region ErrorMessage_IsReportedAsAnErrorNotAsAMessage()

        /// <summary>
        /// Eine <c>message type='error'</c> ist die Rückmeldung, dass die
        /// eigene Nachricht nicht zugestellt wurde - und keine neue Nachricht.
        /// </summary>
        [Test]
        public async Task ErrorMessage_IsReportedAsAnErrorNotAsAMessage()
        {

            var client   = await ConnectClientAsync();
            var session  = await SessionOfAsync(client);

            StanzaError?  reported  = null;
            XMPPMessage?  asMessage = null;

            client.OnStanzaError += (_, error) => reported  = error;
            client.OnMessage     += m          => asMessage = m;

            await session.SendAsync(
                $"<message type='error' from='niemand@{Server.Domain}' to='{client.FullJid}'>" +
                "<error type='cancel'>" +
                "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error></message>");

            await WaitFor(() => reported is not null, "gemeldeter Stanza-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Condition, Is.EqualTo("service-unavailable"));
                Assert.That(asMessage, Is.Null,
                            "Eine Fehler-Stanza darf nicht als Nachricht durchgereicht werden.");
            });

        }

        #endregion

        #region ErrorPresence_DoesNotBecomeAContactState()

        /// <summary>
        /// Eine <c>presence type='error'</c> wanderte früher über
        /// <c>UpdatePresence</c> in den Roster. Weil dort nur das
        /// <c>show</c>-Element ausgewertet wird und ein Fehler keines trägt,
        /// landete der Kontakt im Zweig für "verfügbar" - eine abgeprallte
        /// Presence machte ihn also online.
        /// </summary>
        [Test]
        public async Task ErrorPresence_DoesNotMarkTheContactAsOnline()
        {

            var client   = await ConnectClientAsync();
            var session  = await SessionOfAsync(client);
            var bob      = $"bob@{Server.Domain}";

            await client.AddContactAsync(bob, "Bob");

            await WaitFor(() => client.GetContact(bob) is not null, "Kontakt im Roster");

            Assert.That(client.GetContact(bob)!.Presence, Is.EqualTo(PresenceState.Offline),
                        "Vorbedingung: Bob ist offline.");

            StanzaError? reported = null;
            client.OnStanzaError += (_, error) => reported = error;

            await session.SendAsync(
                $"<presence type='error' from='{bob}/x' to='{client.FullJid}'>" +
                "<error type='cancel'>" +
                "<remote-server-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error></presence>");

            await WaitFor(() => reported is not null, "gemeldeter Stanza-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Condition, Is.EqualTo("remote-server-not-found"));

                Assert.That(client.GetContact(bob)!.Presence, Is.EqualTo(PresenceState.Offline),
                            "Ein Presence-Fehler darf den Kontakt nicht online setzen.");
            });

        }

        #endregion

        #region FatalStreamError_IsReportedAndStopsReconnecting()

        /// <summary>
        /// RFC 6120, Abschnitt 4.9: Nach <c>conflict</c> ist der Stream
        /// endgültig verloren. Ein Reconnect liefe in dieselbe Ablehnung, also
        /// muss er unterbleiben.
        /// </summary>
        [Test]
        public async Task FatalStreamError_IsReportedAndStopsReconnecting()
        {

            var client   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(100));
            var session  = await SessionOfAsync(client);

            StreamError? reported = null;
            client.OnStreamError += error => reported = error;

            var connectionsBefore = Server.ConnectionCount;

            // Schliesst den Stream selbst (RFC 6120, Abschnitt 4.9.1.1) - hier
            // stand bis D23 ein Kill() dahinter, das genau das von Hand nachholte.
            await session.SendStreamErrorAsync("conflict", "Resource doppelt vergeben.");

            await WaitFor(() => reported is not null, "gemeldeter Stream-Fehler");

            // Dem Client Zeit geben, einen Reconnect zu versuchen - er darf keinen machen.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Condition,      Is.EqualTo("conflict"));
                Assert.That(reported!.Text,           Is.EqualTo("Resource doppelt vergeben."));
                Assert.That(reported!.IsRecoverable,  Is.False);

                Assert.That(Server.ConnectionCount, Is.EqualTo(connectionsBefore),
                            "Nach einem endgültigen Stream-Fehler darf kein Reconnect erfolgen.");
            });

        }

        #endregion

        #region RecoverableStreamError_IsReportedButAllowsReconnect()

        /// <summary>
        /// Bei <c>system-shutdown</c> lohnt der Reconnect dagegen - der Server
        /// kommt wieder.
        /// </summary>
        [Test]
        public async Task RecoverableStreamError_IsReportedButAllowsReconnect()
        {

            var client   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(100));
            var session  = await SessionOfAsync(client);

            StreamError? reported = null;
            client.OnStreamError += error => reported = error;

            var connectionsBefore = Server.ConnectionCount;

            // Schliesst den Stream selbst; der Reconnect folgt daraus und nicht
            // aus einem zusätzlichen Abriss von Hand.
            await session.SendStreamErrorAsync("system-shutdown");

            await WaitFor(() => reported is not null, "gemeldeter Stream-Fehler");

            await WaitFor(() => Server.ConnectionCount > connectionsBefore,
                          "Reconnect nach wiederholbarem Stream-Fehler");

            Assert.Multiple(() =>
            {
                Assert.That(reported!.Condition,     Is.EqualTo("system-shutdown"));
                Assert.That(reported!.IsRecoverable, Is.True);
            });

        }

        #endregion

    }

}
