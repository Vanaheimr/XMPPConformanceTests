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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Föderation, wie sie im Betrieb aussieht: keine Gegenstelle ist von Hand
    /// eingetragen, beide Server finden einander ausschliesslich über DNS.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zu <see cref="SrvResolutionTests"/> ist nicht nur die
    /// Menge. Dort löst eine Seite auf und die andere bekommt den Rückweg
    /// eingetragen; hier gibt es überhaupt keine Liste mehr - auch die
    /// <b>Dialback-Rückfrage</b> muss ihren Weg über die Auflösung finden.
    /// Genau das ist der Fall, den XEP-0220 meint, und zugleich der, in dem
    /// die Vertrauenswurzel vom Betreiber zum DNS wandert.
    ///
    /// Die SRV-Ziele sind IP-Adressen. Das ist im Test nötig, weil ein
    /// erfundener Rechnername sich nicht auflösen liesse, und es prüft
    /// nebenbei mit, dass die Zertifikate gegen die <i>gesuchte Domain</i>
    /// geprüft werden und nicht gegen das, was im SRV-Eintrag steht - sonst
    /// käme keine der beiden Verbindungen zustande.
    /// </remarks>
    [TestFixture]
    public class DnsFederationTests
    {

        #region Data

        private DNSServer _dnsServer = null!;
        private XMPPServer _links = null!;
        private XMPPServer _rechts = null!;
        private TcpServerLinks _linksLinks = null!;
        private TcpServerLinks _rechtsLinks = null!;
        private readonly List<XMPPClient> _clients = [];
        private readonly InternalErrorGuard _guard = new();

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public async Task ZweiServerUndEinDNS()
        {

            // Die Wache an beide: Ein Fehler auf dem einen Server entsteht oft
            // durch eine Stanza, die der andere geschickt hat.
            _guard.Reset();

            _links   = _guard.Watched(new XMPPServer("links.example"));
            _rechts  = _guard.Watched(new XMPPServer("rechts.example"));

            _links.Start();
            _rechts.Start();

            // Die Ports werden vorab gewählt, weil sich sonst die Katze in den
            // Schwanz beisst: die Zonendatei braucht die Ports, und die
            // S2S-Zweige brauchen den Resolver, der ohne Zone nicht antworten
            // kann. Dasselbe Vorgehen wie überall sonst im Projekt, wo ein
            // freier Port gebraucht wird.
            var portLinks   = FreierTcpPort();
            var portRechts  = FreierTcpPort();

            var zone = new InMemoryDNSZone();

            zone.Add(SrvEintrag("_xmpp-server._tcp.links.example.",  portLinks),
                     SrvEintrag("_xmpp-server._tcp.rechts.example.", portRechts));

            var dnsPort = FreierUdpPort();

            _dnsServer = new DNSServer(
                             new AuthoritativeDNSRequestHandler(zone),
                             new DNSServerOptions {
                                 EnableUDPUnicast    = true,
                                 EnableUDPMulticast  = false,
                                 EnableTCPUnicast    = false,
                                 UDPUnicastSocket    = new IPSocket(IPv4Address.Localhost,
                                                                    IPPort.Parse(dnsPort))
                             });

            await _dnsServer.Start();

            var dnsClient = new DNSClient(IPv4Address.Localhost,
                                          IPPort.Parse(dnsPort),
                                          QueryTimeout:   TimeSpan.FromSeconds(5),
                                          UseQueryCache:  false);

            // Beide Seiten bekommen denselben Resolver und keine einzige
            // Gegenstelle von Hand.
            _linksLinks = new TcpServerLinks(_links, portLinks, TcpTlsMode.StartTls)
                          {
                              AddressResolver       = new DnsS2SAddressResolver(dnsClient),
                              DefaultPeerValidator  = _rechts.IsOwnCertificate
                          };

            _rechtsLinks = new TcpServerLinks(_rechts, portRechts, TcpTlsMode.StartTls)
                           {
                               AddressResolver       = new DnsS2SAddressResolver(dnsClient),
                               DefaultPeerValidator  = _links.IsOwnCertificate
                           };

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

            try { await _dnsServer.Stop(); }
            catch { /* im Teardown egal */ }

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

            _guard.AssertClean();

        }

        #endregion

        #region Hilfsfunktionen

        private static Int32 FreierUdpPort()
        {

            var l     = new UdpClient(0, AddressFamily.InterNetwork);
            var port  = ((System.Net.IPEndPoint) l.Client.LocalEndPoint!).Port;
            l.Close();

            return port;

        }

        private static Int32 FreierTcpPort()
        {

            var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint) l.LocalEndpoint).Port;
            l.Stop();

            return port;

        }

        private static SRV SrvEintrag(String dienstName, Int32 port)

            => new (DNSServiceName.Parse(dienstName),
                    DNSQueryClasses.IN,
                    TimeSpan.FromMinutes(5),
                    0, 0,
                    IPPort.Parse((UInt16) port),
                    DomainName.Parse(IPv4Address.Localhost.ToString()));

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


        #region TwoServersFindEachOtherThroughDnsAlone()

        /// <summary>
        /// Eine Nachricht über die Grenze, ohne dass irgendwo eine Adresse
        /// hinterlegt wäre.
        /// </summary>
        /// <remarks>
        /// Das schliesst die Dialback-Rückfrage ein: <c>rechts.example</c>
        /// muss <c>links.example</c> selbst auflösen, um den vorgelegten
        /// Schlüssel prüfen zu können.
        /// </remarks>
        [Test]
        public async Task TwoServersFindEachOtherThroughDnsAlone()
        {

            var alice = await ConnectAsync(_links,  "alice");
            var bob   = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await alice.SendMessageAsync(bob.BareJid, "Hallo über DNS!");

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht auf dem anderen Server");

            Assert.Multiple(() =>
            {
                Assert.That(empfangen[0].Body,        Is.EqualTo("Hallo über DNS!"));
                Assert.That(empfangen[0].FromBareJid, Is.EqualTo("alice@links.example"));

                Assert.That(_rechtsLinks.DialbackVerificationCount, Is.GreaterThan(0),
                            "Die Rückfrage muss stattgefunden haben - und ihren Weg über DNS gefunden haben.");
            });

        }

        #endregion

        #region TheAnswerFindsItsWayBack()

        [Test]
        public async Task TheAnswerFindsItsWayBack()
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

        #region ADomainWithoutRecords_YieldsAnError()

        /// <summary>
        /// Eine Domain, die im DNS nicht steht, führt zum
        /// <c>&lt;remote-server-not-found/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Der Rückfall aus RFC 6120 §3.2.1 greift zwar und versucht die
        /// Domain selbst - aber die gibt es nicht, und dann bleibt es beim
        /// Fehler.
        /// </remarks>
        [Test]
        public async Task ADomainWithoutRecords_YieldsAnError()
        {

            var alice   = await ConnectAsync(_links, "alice");
            var fehler  = new List<StanzaError>();

            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync("wer@niemand.example", "Hallo?");

            await WarteAuf(() => fehler.Count > 0, "den Fehler zur unbekannten Domain");

            Assert.That(fehler[0].Condition, Is.EqualTo("remote-server-not-found"));

        }

        #endregion

    }

}
