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

using System.Net.Security;
using System.Net.Sockets;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// SASL-EXTERNAL über echte Sockets: zwei Server weisen sich mit ihren
    /// Zertifikaten aus, statt sich per Dialback rückzufragen.
    /// </summary>
    /// <remarks>
    /// Der sichtbare Unterschied zu Dialback ist die <b>fehlende</b> zweite
    /// Verbindung. Dialback braucht je Richtung einen Rückruf beim autoritativen
    /// Server; SASL-EXTERNAL liest das Zertifikat, das im TLS-Handshake ohnehin
    /// vorlag. Genau daran lässt sich auch von aussen erkennen, welches
    /// Verfahren gegriffen hat - und darauf beruht der erste Test hier.
    /// </remarks>
    [TestFixture]
    public class SaslExternalTests
    {

        #region Data

        private XMPPServer _links = null!;
        private XMPPServer _rechts = null!;
        private TcpServerLinks _linksLinks = null!;
        private TcpServerLinks _rechtsLinks = null!;
        private readonly List<XMPPClient> _clients = [];
        private readonly InternalErrorGuard _guard = new();

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            // Die Wache an beide: Ein Fehler auf dem einen Server entsteht oft
            // durch eine Stanza, die der andere geschickt hat.
            _guard.Reset();

            _links   = _guard.Watched(new XMPPServer("links.example"));
            _rechts  = _guard.Watched(new XMPPServer("rechts.example"));

            _links.Start();
            _rechts.Start();

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

            if (_linksLinks  is not null) await _linksLinks.DisposeAsync();
            if (_rechtsLinks is not null) await _rechtsLinks.DisposeAsync();

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

            _guard.AssertClean();

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>Verkabelt beide Server, wahlweise mit SASL-EXTERNAL.</summary>
        private void Verkabeln(Boolean mitExternal)
        {

            TcpServerLinks.Connect(_links, _rechts,
                                   TcpTlsMode.StartTls,
                                   useSaslExternal: mitExternal);

            _linksLinks   = (TcpServerLinks) _links.ServerLinks!;
            _rechtsLinks  = (TcpServerLinks) _rechts.ServerLinks!;

        }

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


        #region MessageCrossesTheBoundaryWithoutDialback()

        /// <summary>
        /// Der Kern: die Nachricht kommt an, und die Domain wurde über das
        /// Zertifikat belegt statt über eine Rückfrage.
        /// </summary>
        /// <remarks>
        /// Gemessen wird, wie oft die Gegenstelle einen Dialback-Schlüssel
        /// nachgefragt hat - die einzige von aussen sichtbare Spur, die die
        /// beiden Verfahren unterscheidet. Die Zahl der Verbindungen taugt
        /// dafür <b>nicht</b>: über die Grenze läuft noch anderes, unter
        /// anderem Bobs automatische Empfangsbestätigung, die ihrerseits eine
        /// Verbindung in die Gegenrichtung aufbaut. Eine erste Fassung dieses
        /// Tests zählte Verbindungen und schlug genau daran fehl.
        /// </remarks>
        [Test]
        public async Task MessageCrossesTheBoundaryWithoutDialback()
        {

            Verkabeln(mitExternal: true);

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo per Zertifikat!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].Body,        Is.EqualTo("Hallo per Zertifikat!"));
                Assert.That(empfangen[0].FromBareJid, Is.EqualTo("alice@links.example"));

                Assert.That(_rechtsLinks.DialbackVerificationCount, Is.Zero,
                            "Mit SASL-EXTERNAL darf die Gegenstelle nicht zurückfragen.");
                Assert.That(_linksLinks.DialbackVerificationCount, Is.Zero);
            });

        }

        #endregion

        #region WithoutExternal_DialbackCallsBack()

        /// <summary>
        /// Die Gegenprobe: ohne SASL-EXTERNAL kommt genau die Rückfrage, die
        /// oben ausbleiben muss.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bewiese der vorige nichts: eine Null bei den
        /// eingehenden Verbindungen wäre auch dann zu sehen, wenn schlicht
        /// niemand je zurückruft.
        /// </remarks>
        [Test]
        public async Task WithoutExternal_DialbackCallsBack()
        {

            Verkabeln(mitExternal: false);

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo per Dialback!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            await WarteAuf(() => _rechtsLinks.DialbackVerificationCount > 0,
                           "die Dialback-Rückfrage von rechts.example");

        }

        #endregion

        #region TheAnswerFindsItsWayBack()

        [Test]
        public async Task TheAnswerFindsItsWayBack()
        {

            Verkabeln(mitExternal: true);

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

        #region ACertificateForAnotherDomain_GetsNothingThrough()

        /// <summary>
        /// Wer sich als <c>links.example</c> ausgibt, aber ein Zertifikat für
        /// <c>schwindler.example</c> vorlegt, kommt nicht durch.
        /// </summary>
        /// <remarks>
        /// Der Stream wird hier von Hand geführt, und das ist nötig: mit
        /// <see cref="TcpServerLinks"/> gebaut hätte der Angreifer immer ein
        /// Zertifikat, das zu seiner Domain passt - und dann wäre sein
        /// Durchkommen richtig und kein Fehler. Eine erste Fassung dieses
        /// Tests machte genau das und schlug fehl, weil sie das erlaubte
        /// Verhalten für einen Angriff hielt.
        ///
        /// Geprüft wird damit, dass der Transport die Zertifikatsprüfung
        /// wirklich verdrahtet hat. Dass die Prüfung selbst richtig
        /// entscheidet, steht in
        /// <c>S2SStreamTests.ACertificateThatDoesNotCoverTheDomain_IsRefused</c>
        /// und in <see cref="CertificateIdentityTests"/>.
        /// </remarks>
        [Test]
        public async Task ACertificateForAnotherDomain_GetsNothingThrough()
        {

            Verkabeln(mitExternal: true);

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            // Nur als Zertifikatslieferant: dieser Server heisst
            // schwindler.example, und sein Zertifikat sagt das auch.
            await using var schwindler = _guard.Watched(new XMPPServer("schwindler.example"));

            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, _rechtsLinks.Port);

            var netz    = client.GetStream();
            var puffer  = new Byte[8192];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            async Task Roh(String text)
                => await netz.WriteAsync(Encoding.UTF8.GetBytes(text), cts.Token);

            async Task<String> RohLiesBis(String was)
            {
                var alles = "";
                while (!alles.Contains(was, StringComparison.Ordinal))
                {
                    var n = await netz.ReadAsync(puffer, cts.Token);
                    if (n <= 0) break;
                    alles += Encoding.UTF8.GetString(puffer, 0, n);
                }
                return alles;
            }

            const String Kopf = "<stream:stream xmlns='jabber:server' " +
                                "xmlns:stream='http://etherx.jabber.org/streams' " +
                                "xmlns:db='jabber:server:dialback' " +
                                "from='links.example' to='rechts.example' version='1.0'>";

            await Roh(Kopf);
            await RohLiesBis("urn:ietf:params:xml:ns:xmpp-tls");
            await Roh("<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>");
            await RohLiesBis("proceed");

            await using var tls = new SslStream(netz,
                                                leaveInnerStreamOpen: false,
                                                userCertificateValidationCallback: _rechts.IsOwnCertificate);

            await tls.AuthenticateAsClientAsync(
                      new SslClientAuthenticationOptions {
                          TargetHost          = "rechts.example",
                          ClientCertificates  = [schwindler.Certificate!]
                      },
                      cts.Token);

            var gelesen = new StringBuilder();

            _ = Task.Run(async () =>
            {
                var p2 = new Byte[8192];
                try
                {
                    while (true)
                    {
                        var n = await tls.ReadAsync(p2);
                        if (n <= 0) break;
                        lock (gelesen) gelesen.Append(Encoding.UTF8.GetString(p2, 0, n));
                    }
                }
                catch (Exception) { /* Verbindung zu - erwartet */ }
            });

            async Task Sende(String text)
                => await tls.WriteAsync(Encoding.UTF8.GetBytes(text), cts.Token);

            Boolean Sah(String text)
            {
                lock (gelesen) return gelesen.ToString().Contains(text, StringComparison.Ordinal);
            }

            // Nach STARTTLS faengt der Stream von vorn an - weiterhin unter
            // dem fremden Namen.
            await Sende(Kopf);

            await WarteAuf(() => Sah("EXTERNAL"), "das SASL-Angebot");

            var authzid = Convert.ToBase64String(Encoding.UTF8.GetBytes("links.example"));

            await Sende($"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>{authzid}</auth>");

            await WarteAuf(() => Sah("failure") || Sah("success"), "die SASL-Antwort");

            await Sende($"<message from='wer@links.example' to='{bob.BareJid}' type='chat'>" +
                        "<body>Durchgerutscht?</body></message>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(Sah("not-authorized"), Is.True,
                            "Ein Zertifikat fuer eine andere Domain darf nicht ausreichen.");
                Assert.That(Sah("<success"), Is.False);
                Assert.That(empfangen, Is.Empty,
                            "Die Stanza darf den Client nicht erreichen.");
            });

        }

        #endregion

    }

}
