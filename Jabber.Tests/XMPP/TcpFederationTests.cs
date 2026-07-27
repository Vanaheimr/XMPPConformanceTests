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

using System.Net.Sockets;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Dasselbe Zielbild wie in <see cref="FederationTests"/> und
    /// <see cref="WebSocketFederationTests"/>, diesmal über die klassische
    /// Rahmung: TCP, <c>jabber:server</c>-Streams (RFC 6120).
    /// </summary>
    /// <remarks>
    /// Der Sinn dieser Datei ist der Vergleich. Sie prüft dieselben Dinge wie
    /// die WebSocket-Fassung, und sie tut es mit derselben Protokollschicht -
    /// gewechselt hat nur, was darunter liegt. Bliebe hier etwas rot, das dort
    /// grün ist, wäre die Trennung aus S4b-1 an genau der Stelle nicht sauber
    /// gewesen.
    /// </remarks>
    [TestFixture]
    public class TcpFederationTests
    {

        #region Data

        private XMPPServer _links = null!;
        private XMPPServer _rechts = null!;
        private TcpServerLinks _linksLinks = null!;
        private TcpServerLinks _rechtsLinks = null!;
        private readonly List<XMPPClient> _clients = [];

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            _links.Start();
            _rechts.Start();

            TcpServerLinks.Connect(_links, _rechts);

            _linksLinks   = (TcpServerLinks) _links.ServerLinks!;
            _rechtsLinks  = (TcpServerLinks) _rechts.ServerLinks!;

        }

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); }
                catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            await _linksLinks.DisposeAsync();
            await _rechtsLinks.DisposeAsync();

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        private async Task<XMPPClient> ConnectAsync(XMPPServer server, String localPart)
        {

            if (server.GetAccount($"{localPart}@{server.Domain}") is null)
                server.AddAccount(localPart);

            var connection = new XMPPConnection($"{localPart}@{server.Domain}",
                                                "pw",
                                                server.Uri)
            {
                KeepaliveEnabled            = false,
                MaxReconnectAttempts        = 0,
                ServerCertificateValidator  = server.IsOwnCertificate
            };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            return client;

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        #endregion


        #region MessageCrossesTheDomainBoundaryOverTcp()

        /// <summary>
        /// Der Kern: eine Nachricht über einen echten
        /// <c>jabber:server</c>-Stream.
        /// </summary>
        [Test]
        public async Task MessageCrossesTheDomainBoundaryOverTcp()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo über TCP!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].Body,         Is.EqualTo("Hallo über TCP!"));
                Assert.That(empfangen[0].FromBareJid,  Is.EqualTo("alice@links.example"));
            });

        }

        #endregion

        #region TheAnswerFindsItsWayBackOverTcp()

        [Test]
        public async Task TheAnswerFindsItsWayBackOverTcp()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var beiBob    = new List<XMPPMessage>();
            var beiAlice  = new List<XMPPMessage>();

            bob.OnMessage    += m => beiBob.Add(m);
            alice.OnMessage  += m => beiAlice.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Frage");
            await WarteAuf(() => beiBob.Count > 0, "die Frage bei Bob");

            await bob.SendMessageAsync(beiBob[0].FromBareJid, "Antwort");
            await WarteAuf(() => beiAlice.Count > 0, "die Antwort bei Alice");

            Assert.That(beiAlice[0].Body, Is.EqualTo("Antwort"));

        }

        #endregion

        #region SeveralMessagesReuseTheSameConnection()

        [Test]
        public async Task SeveralMessagesReuseTheSameConnection()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "eins");
            await WarteAuf(() => empfangen.Count == 1, "die erste Nachricht");

            await Task.Delay(TimeSpan.FromSeconds(1));

            var nachDemAufbau = _rechtsLinks.InboundConnectionCount;

            await alice.SendMessageAsync(bob.BareJid, "zwei");
            await alice.SendMessageAsync(bob.BareJid, "drei");
            await WarteAuf(() => empfangen.Count == 3, "alle drei Nachrichten");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(nachDemAufbau, Is.GreaterThan(0));
                Assert.That(_rechtsLinks.InboundConnectionCount, Is.EqualTo(nachDemAufbau),
                            "Weitere Nachrichten dürfen keine neue Verbindung aufbauen.");
            });

        }

        #endregion

        #region ALongMessageSurvivesBeingSplitAcrossPackets()

        /// <summary>
        /// Eine Stanza, die nicht in ein TCP-Paket passt.
        /// </summary>
        /// <remarks>
        /// Über WebSocket gibt es diese Frage nicht - ein Frame ist ein
        /// Element. Über TCP ist sie <b>die</b> Frage, und sie fällt bei
        /// kurzen Testnachrichten über localhost nie auf. Deshalb hier ein
        /// Rumpf, der jede übliche Paketgrösse überschreitet.
        /// </remarks>
        [Test]
        public async Task ALongMessageSurvivesBeingSplitAcrossPackets()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            var lang = String.Concat(Enumerable.Repeat("Lange Nachricht. ", 4000));

            await alice.SendMessageAsync(bob.BareJid, lang);

            await WarteAuf(() => empfangen.Count > 0, "die lange Nachricht");

            Assert.That(empfangen[0].Body, Is.EqualTo(lang));

        }

        #endregion

        #region ADomainWithoutAPeer_StillYieldsAnError()

        [Test]
        public async Task ADomainWithoutAPeer_StillYieldsAnError()
        {

            var alice   = await ConnectAsync(_links, "alice");
            var fehler  = new List<StanzaError>();

            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync("wer@ganzwoanders.example", "Hallo?");

            await WarteAuf(() => fehler.Count > 0, "den Fehler zur unbekannten Domain");

            Assert.That(fehler[0].Condition, Is.EqualTo("remote-server-not-found"));

        }

        #endregion

        #region AnImpostorWithoutTheSecret_FailsDialbackOverTcp()

        /// <summary>
        /// Dialback über TCP - wer die Domain nur behauptet, kommt nicht
        /// durch.
        /// </summary>
        /// <remarks>
        /// Der Hochstapler baut eine rohe TLS-Verbindung auf und schreibt den
        /// Stream von Hand. Das ist zugleich die Gegenprobe dazu, dass die
        /// Rahmung wirklich RFC 6120 ist und nicht doch noch etwas von
        /// RFC 7395 durchscheint: ein Stream-Kopf im falschen Format käme hier
        /// gar nicht erst an.
        /// </remarks>
        [Test]
        public Task AnImpostorWithoutTheSecret_FailsDialbackOverTcp()
            => HochstaplerScheitert("links.example");

        #endregion

        #region AnImpostorForAnUnknownDomain_CannotBeVerifiedAtAll()

        /// <summary>
        /// Für eine Domain ohne hinterlegte Adresse kann niemand gefragt
        /// werden - also wird auch nichts angenommen.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test: dort scheitert der Schlüssel, hier
        /// scheitert schon die Möglichkeit zu prüfen. Ohne diesen Fall bliebe
        /// die Zeile, die eine unbekannte Domain ablehnt, ungeprüft - und die
        /// unbekannte Domain wäre der bequemere Weg hinein als die bekannte.
        /// </remarks>
        [Test]
        public Task AnImpostorForAnUnknownDomain_CannotBeVerifiedAtAll()
            => HochstaplerScheitert("niemand.example");

        #endregion

        #region (Hilfsfunktion) HochstaplerScheitert(behaupteteDomain)

        /// <summary>
        /// Verbindet sich von Hand mit dem TCP-S2S-Eingang, behauptet eine
        /// fremde Domain, legt einen erfundenen Dialback-Schlüssel vor und
        /// versucht danach zuzustellen.
        /// </summary>
        private async Task HochstaplerScheitert(String behaupteteDomain)
        {

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, _rechtsLinks.Port);

            await using var tls = new System.Net.Security.SslStream(
                                      client.GetStream(),
                                      leaveInnerStreamOpen: false,
                                      userCertificateValidationCallback: _rechts.IsOwnCertificate);

            await tls.AuthenticateAsClientAsync("rechts.example");

            var gelesen = new StringBuilder();

            _ = Task.Run(async () =>
            {
                var puffer = new Byte[8192];
                try
                {
                    while (true)
                    {
                        var n = await tls.ReadAsync(puffer);
                        if (n <= 0) break;
                        lock (gelesen) gelesen.Append(Encoding.UTF8.GetString(puffer, 0, n));
                    }
                }
                catch (Exception) { /* Verbindung zu - erwartet */ }
            });

            async Task Sende(String text)
                => await tls.WriteAsync(Encoding.UTF8.GetBytes(text));

            Boolean Sah(String text)
            {
                lock (gelesen) return gelesen.ToString().Contains(text, StringComparison.Ordinal);
            }

            await Sende("<stream:stream xmlns='jabber:server' " +
                        "xmlns:stream='http://etherx.jabber.org/streams' " +
                        "xmlns:db='jabber:server:dialback' " +
                        $"from='{behaupteteDomain}' to='rechts.example' version='1.0'>");

            await WarteAuf(() => Sah("<stream:stream"), "den Stream-Kopf der Gegenstelle");

            await Sende($"<db:result from='{behaupteteDomain}' to='rechts.example'>" +
                        "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff" +
                        "</db:result>");

            await WarteAuf(() => Sah("db:result") && Sah("type="), "die Dialback-Antwort");

            await Sende($"<message from='wer@{behaupteteDomain}' to='{bob.BareJid}' type='chat'>" +
                        "<body>Durchgerutscht?</body></message>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(Sah("type='invalid'"), Is.True,
                            "Der erfundene Schlüssel muss als ungültig zurückkommen.");
                Assert.That(Sah("type='valid'"),   Is.False);
                Assert.That(empfangen, Is.Empty,
                            "Ohne bestandenes Dialback darf keine Stanza zugestellt werden.");
            });

        }

        #endregion

    }

}
