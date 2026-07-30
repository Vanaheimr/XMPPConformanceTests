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

using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6120, Abschnitt 6.4.4: Bricht der Client die SASL-Aushandlung mit
    /// <c>&lt;abort/&gt;</c> ab, antwortet der Server mit
    /// <c>&lt;failure&gt;&lt;aborted/&gt;&lt;/failure&gt;</c> — und der Stream
    /// bleibt stehen.
    /// </summary>
    /// <remarks>
    /// Seit D26 beendete ein <c>&lt;abort/&gt;</c> den Stream mit
    /// <c>&lt;unsupported-stanza-type/&gt;</c>. Wörtlich war das nicht falsch —
    /// der Server unterstützte das Element nicht —, aber es ist die schlechtere
    /// von zwei Antworten: Der Abbruch ist ein <b>vorgesehener</b> Schritt der
    /// Aushandlung, kein Protokollverstoss. Wer ihn mit dem Ende des Streams
    /// beantwortet, zwingt den Client zu einer neuen Verbindung für etwas, das
    /// der RFC ausdrücklich innerhalb der bestehenden vorsieht.
    ///
    /// Geprüft wird über einen rohen <see cref="ClientWebSocket"/> und nicht
    /// über <see cref="XMPPClient"/>: Der Abbruch gehört <b>mitten</b> in die
    /// Aushandlung, und dort führt der richtige Client sein eigenes Gespräch.
    /// Nur von Hand lässt sich ein halb begonnener SCRAM-Austausch überhaupt
    /// herstellen.
    /// </remarks>
    [TestFixture]
    public class SaslAbortTests : AXMPPTests
    {

        #region Roher Client

        /// <summary>
        /// Ein Client für die Aushandlungsphase — ohne eigene Meinung darüber,
        /// was als Nächstes zu tun ist.
        /// </summary>
        private sealed class RoherClient : IAsyncDisposable
        {

            private readonly ClientWebSocket _socket = new();

            public List<String> Empfangen { get; } = [];

            public async Task VerbindeAsync(XMPPServer server)
            {

                _socket.Options.AddSubProtocol("xmpp");
                _socket.Options.RemoteCertificateValidationCallback = server.IsOwnCertificate;

                await _socket.ConnectAsync(new Uri(server.Uri), CancellationToken.None);

                _ = LiesAsync();

            }

            public async Task SendeAsync(String rahmen)
                => await _socket.SendAsync(Encoding.UTF8.GetBytes(rahmen),
                                           WebSocketMessageType.Text, true, CancellationToken.None);

            private async Task LiesAsync()
            {

                var puffer = new Byte[16384];

                try
                {
                    while (_socket.State == WebSocketState.Open)
                    {

                        var ergebnis = await _socket.ReceiveAsync(puffer, CancellationToken.None);

                        if (ergebnis.MessageType == WebSocketMessageType.Close)
                            break;

                        lock (Empfangen)
                            Empfangen.Add(Encoding.UTF8.GetString(puffer, 0, ergebnis.Count));

                    }
                }
                catch (Exception)
                {
                    // Verbindung zu - je nach Test der erwartete Ausgang.
                }

            }

            public Boolean Sah(String text)
            {
                lock (Empfangen)
                    return Empfangen.Any(f => f.Contains(text, StringComparison.Ordinal));
            }

            public Int32 Anzahl
            {
                get { lock (Empfangen) return Empfangen.Count; }
            }

            public Boolean IstOffen
                => _socket.State == WebSocketState.Open;

            public ValueTask DisposeAsync()
            {
                try { _socket.Dispose(); } catch { /* egal */ }
                return ValueTask.CompletedTask;
            }

        }

        #endregion

        #region Hilfsfunktionen

        private const String SaslNamespace = "urn:ietf:params:xml:ns:xmpp-sasl";

        private readonly List<RoherClient> _clients = [];

        [TearDown]
        public async Task RoheAbraeumen()
        {

            foreach (var c in _clients)
                await c.DisposeAsync();

            _clients.Clear();

        }

        /// <summary>Ein verbundener roher Client mit eröffnetem Stream.</summary>
        private async Task<RoherClient> EroeffnetAsync()
        {

            Server.AddAccount("alice");

            var client = new RoherClient();
            _clients.Add(client);

            await client.VerbindeAsync(Server);

            await client.SendeAsync(
                      "<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' " +
                      $"to='{Server.Domain}' version='1.0'/>");

            await WaitFor(() => client.Sah("mechanisms"), "die Features des Servers");

            return client;

        }

        /// <summary>Der Inhalt des zuletzt empfangenen Elements dieses Namens.</summary>
        private static String Inhalt(RoherClient client, String element)
        {

            lock (client.Empfangen)
            {

                var rahmen = client.Empfangen.Last(f => f.Contains($"<{element}", StringComparison.Ordinal));

                return Regex.Match(rahmen, $@"<{element}[^>]*>([^<]*)</{element}>").Groups[1].Value;

            }

        }

        #endregion


        #region AnAbort_IsAnsweredWithAborted()

        /// <summary>
        /// Der Kern: Auf <c>&lt;abort/&gt;</c> folgt
        /// <c>&lt;failure&gt;&lt;aborted/&gt;&lt;/failure&gt;</c> und kein
        /// Stream-Fehler.
        /// </summary>
        [Test]
        public async Task AnAbort_IsAnsweredWithAborted()
        {

            var client = await EroeffnetAsync();

            var scram = new SCRAMAuthenticator("alice", "pw", SCRAMMechanism.ScramSha256);

            await client.SendeAsync(
                      $"<auth xmlns='{SaslNamespace}' mechanism='SCRAM-SHA-256'>" +
                      $"{scram.CreateClientFirstMessage()}</auth>");

            await WaitFor(() => client.Sah("<challenge"), "die Challenge des Servers");

            await client.SendeAsync($"<abort xmlns='{SaslNamespace}'/>");

            await WaitFor(() => client.Sah("<aborted"), "die Antwort auf den Abbruch");

            Assert.Multiple(() =>
            {

                Assert.That(client.Sah("<failure"), Is.True,
                            "Der Abbruch wird mit einem SASL-Fehlschlag beantwortet.");

                Assert.That(client.Sah("unsupported-stanza-type"), Is.False,
                            "Und ausdrücklich nicht mit einem Stream-Fehler.");

                Assert.That(client.IstOffen, Is.True,
                            "Der Abbruch beendet die Aushandlung, nicht den Stream.");

            });

        }

        #endregion

        #region AnAbort_DiscardsTheHalfFinishedExchange()

        /// <summary>
        /// Der abgebrochene SCRAM-Austausch ist weg — eine danach
        /// nachgereichte <c>&lt;response/&gt;</c> gehört zu nichts mehr.
        /// </summary>
        /// <remarks>
        /// Das ist der eigentliche Inhalt eines Abbruchs. Bliebe die halbe
        /// Aushandlung liegen, könnte sie mit einer später nachgeschobenen
        /// Antwort noch zu Ende geführt werden — der Abbruch wäre dann eine
        /// Höflichkeitsfloskel und keine Aussage.
        ///
        /// Die nachgeschobene Antwort ist deshalb eine <b>gültige</b>, gebaut
        /// mit dem echten <see cref="SCRAMAuthenticator"/> des Clients. Das ist
        /// der Kern dieses Tests und war zuerst falsch: Mit einer unsinnigen
        /// Antwort kommt <c>not-authorized</c> zurück, ob der Austausch nun
        /// verworfen wurde oder nicht — beide Welten geben dieselbe Antwort,
        /// und der Test prüfte nichts. Erst eine Antwort, die <b>durchginge</b>,
        /// trennt die Fälle: Sie führt entweder zu <c>&lt;success/&gt;</c> oder
        /// zu einer Absage.
        /// </remarks>
        [Test]
        public async Task AnAbort_DiscardsTheHalfFinishedExchange()
        {

            var client = await EroeffnetAsync();

            var scram = new SCRAMAuthenticator("alice", "pw", SCRAMMechanism.ScramSha256);

            await client.SendeAsync(
                      $"<auth xmlns='{SaslNamespace}' mechanism='SCRAM-SHA-256'>" +
                      $"{scram.CreateClientFirstMessage()}</auth>");

            await WaitFor(() => client.Sah("<challenge"), "die Challenge des Servers");

            var challenge = Inhalt(client, "challenge");

            await client.SendeAsync($"<abort xmlns='{SaslNamespace}'/>");

            await WaitFor(() => client.Sah("<aborted"), "die Antwort auf den Abbruch");

            var vorher = client.Anzahl;

            // Diese Antwort wäre richtig gewesen - hätte der Abbruch nicht
            // dazwischengelegen.
            await client.SendeAsync(
                      $"<response xmlns='{SaslNamespace}'>" +
                      $"{scram.ProcessServerFirstMessage(challenge)}</response>");

            await WaitFor(() => client.Anzahl > vorher, "die Antwort auf die verspätete response");

            Assert.Multiple(() =>
            {

                Assert.That(client.Sah("<success"), Is.False,
                            "Der abgebrochene Austausch darf nicht nachträglich " +
                            "zu Ende geführt werden.");

                Assert.That(client.Sah("not-authorized"), Is.True,
                            "Eine response ohne laufenden Austausch gehört zu keiner Aushandlung.");

            });

        }

        #endregion

        #region AfterAnAbort_ANewNegotiationStillWorks()

        /// <summary>
        /// Und der Stream taugt danach noch: Ein zweiter Anlauf führt zum
        /// Erfolg.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum Kern. „Kein Stream-Fehler" allein wäre auch
        /// erfüllt, wenn der Server nach dem Abbruch gar nichts mehr annähme —
        /// dann wäre der Stream formal offen und praktisch tot.
        /// </remarks>
        [Test]
        public async Task AfterAnAbort_ANewNegotiationStillWorks()
        {

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            var client = await EroeffnetAsync();

            await client.SendeAsync($"<abort xmlns='{SaslNamespace}'/>");

            await WaitFor(() => client.Sah("<aborted"), "die Antwort auf den Abbruch");

            var geheim = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0alice\0pw"));

            await client.SendeAsync(
                      $"<auth xmlns='{SaslNamespace}' mechanism='PLAIN'>{geheim}</auth>");

            await WaitFor(() => client.Sah("<success"), "die Anmeldung im zweiten Anlauf");

        }

        #endregion

    }

}
