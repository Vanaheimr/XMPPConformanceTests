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
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die DNS-Auflösung selbst - gegen einen echten DNS-Server, über echte
    /// DNS-Pakete.
    /// </summary>
    /// <remarks>
    /// Hermod bringt einen DNS-Server mit, also gibt es keinen Grund, hier
    /// mit einer nachgebauten Antwort zu arbeiten. Der Unterschied ist nicht
    /// kosmetisch: geprüft wird damit auch, ob der Abfragename stimmt
    /// (<c>_xmpp-server._tcp.&lt;domain&gt;</c>), ob die SRV-Felder in der
    /// richtigen Reihenfolge ankommen und ob eine Domain ohne Eintrag
    /// tatsächlich als "nichts gefunden" durchschlägt. Ein selbstgebauter
    /// Resolver hätte all das bestätigt, weil er dieselben Annahmen gemacht
    /// hätte wie der Code, den er prüfen soll.
    ///
    /// Der Server horcht auf dem Loopback und auf einem freien Port; weder
    /// Multicast noch TCP werden angeschaltet, damit der Testlauf keine
    /// Firewall-Rückfrage auslöst.
    /// </remarks>
    [TestFixture]
    public class DnsS2SAddressResolverTests
    {

        #region Data

        private DNSServer? _dnsServer;
        private DNSClient? _dnsClient;

        #endregion

        #region SetUp / TearDown

        [TearDown]
        public async Task Abraeumen()
        {

            if (_dnsServer is not null)
            {
                try { await _dnsServer.Stop(); }
                catch { /* im Teardown egal */ }
            }

            _dnsServer = null;
            _dnsClient = null;

        }

        #endregion

        #region Hilfsfunktionen

        private static Int32 FreierPort()
        {

            var l = new UdpClient(0, AddressFamily.InterNetwork);
            var port = ((System.Net.IPEndPoint) l.Client.LocalEndPoint!).Port;
            l.Close();

            return port;

        }

        /// <summary>
        /// Startet einen autoritativen DNS-Server mit den angegebenen
        /// Einträgen und liefert einen Client, der ihn befragt.
        /// </summary>
        private async Task<DnsS2SAddressResolver> ServerMit(params IDNSResourceRecord[] eintraege)
        {

            var zone = new InMemoryDNSZone();
            zone.Add(eintraege);

            var port = FreierPort();

            _dnsServer = new DNSServer(
                             new AuthoritativeDNSRequestHandler(zone),
                             new DNSServerOptions {
                                 EnableUDPUnicast    = true,
                                 EnableUDPMulticast  = false,
                                 EnableTCPUnicast    = false,
                                 UDPUnicastSocket    = new IPSocket(IPv4Address.Localhost,
                                                                    IPPort.Parse(port))
                             });

            await _dnsServer.Start();

            _dnsClient = new DNSClient(IPv4Address.Localhost,
                                       IPPort.Parse(port),
                                       QueryTimeout:   TimeSpan.FromSeconds(5),
                                       UseQueryCache:  false);

            return new DnsS2SAddressResolver(_dnsClient);

        }

        private static SRV Eintrag(String  dienstName,
                                   UInt16  prioritaet,
                                   UInt16  gewicht,
                                   String  ziel,
                                   UInt16  port)

            // Voll qualifiziert, mit Punkt am Ende: die Zone schlägt über den
            // Namen aus der Frage nach, und der kommt vom Client so an.
            => new (DNSServiceName.Parse(dienstName.TrimEnd('.') + "."),
                    DNSQueryClasses.IN,
                    TimeSpan.FromMinutes(5),
                    prioritaet,
                    gewicht,
                    IPPort.Parse(port),
                    DomainName.Parse(ziel == "." ? "." : ziel.TrimEnd('.') + "."));

        #endregion


        #region ASrvRecord_IsFoundAndTranslated()

        /// <summary>
        /// Der Normalfall: ein SRV-Eintrag wird gefunden, und alle vier Felder
        /// kommen richtig an.
        /// </summary>
        [Test]
        public async Task ASrvRecord_IsFoundAndTranslated()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-server._tcp.rechts.example", 10, 20, "im.rechts.example", 5269));

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.Multiple(() =>
            {
                Assert.That(ziele,             Has.Count.EqualTo(1));
                Assert.That(ziele[0].Host,     Is.EqualTo("im.rechts.example"));
                Assert.That(ziele[0].Port,     Is.EqualTo(5269));
                Assert.That(ziele[0].Priority, Is.EqualTo(10));
                Assert.That(ziele[0].Weight,   Is.EqualTo(20));
            });

        }

        #endregion

        #region ThePortComesFromTheRecord_NotFromTheDefault()

        /// <summary>
        /// Der Port stammt aus dem Eintrag - sonst wäre die halbe Aussage
        /// eines SRV-Eintrags verschenkt.
        /// </summary>
        [Test]
        public async Task ThePortComesFromTheRecord_NotFromTheDefault()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-server._tcp.rechts.example", 0, 0, "im.rechts.example", 15269));

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.That(ziele[0].Port, Is.EqualTo(15269));

        }

        #endregion

        #region TheQueryUsesTheServicePrefix()

        /// <summary>
        /// Gefragt wird nach <c>_xmpp-server._tcp.&lt;domain&gt;</c> und nicht
        /// nach der Domain selbst.
        /// </summary>
        /// <remarks>
        /// Der Eintrag liegt hier unter einem anderen Dienstnamen. Fragte der
        /// Resolver falsch, fände er ihn - und der Test würde es merken.
        /// </remarks>
        [Test]
        public async Task TheQueryUsesTheServicePrefix()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-client._tcp.rechts.example", 0, 0, "falsch.rechts.example", 5222));

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.Multiple(() =>
            {
                Assert.That(ziele, Has.Count.EqualTo(1));
                Assert.That(ziele[0].Host, Is.EqualTo("rechts.example"),
                            "Ohne passenden SRV-Eintrag gilt der Rückfall auf die Domain selbst.");
                Assert.That(ziele[0].Host, Is.Not.EqualTo("falsch.rechts.example"));
            });

        }

        #endregion

        #region SeveralRecords_ComeBackInPriorityOrder()

        /// <summary>
        /// Mehrere Einträge kommen in der Reihenfolge zurück, in der sie
        /// versucht werden sollen.
        /// </summary>
        [Test]
        public async Task SeveralRecords_ComeBackInPriorityOrder()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-server._tcp.rechts.example", 30, 0, "drittens.example", 5269),
                               Eintrag("_xmpp-server._tcp.rechts.example", 10, 0, "erstens.example",  5269),
                               Eintrag("_xmpp-server._tcp.rechts.example", 20, 0, "zweitens.example", 5269));

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.That(ziele.Select(z => z.Host),
                        Is.EqualTo(new[] { "erstens.example", "zweitens.example", "drittens.example" }));

        }

        #endregion

        #region WithoutAnyRecord_TheDomainItselfIsTried()

        /// <summary>
        /// Ohne SRV-Eintrag gilt der Rückfall aus RFC 6120, Abschnitt 3.2.1:
        /// die Domain selbst auf Port 5269.
        /// </summary>
        [Test]
        public async Task WithoutAnyRecord_TheDomainItselfIsTried()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-server._tcp.woanders.example", 0, 0, "egal.example", 5269));

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.Multiple(() =>
            {
                Assert.That(ziele,         Has.Count.EqualTo(1));
                Assert.That(ziele[0].Host, Is.EqualTo("rechts.example"));
                Assert.That(ziele[0].Port, Is.EqualTo(5269));
            });

        }

        #endregion

        #region WithTheFallbackTurnedOff_NothingIsTried()

        /// <summary>
        /// Wer den Rückfall abschaltet, bekommt ohne SRV-Eintrag nichts.
        /// </summary>
        /// <remarks>
        /// Gedacht für Betreiber, die ausschliesslich über SRV
        /// veröffentlichte Ziele erlauben wollen - sonst würde stillschweigend
        /// woandershin verbunden.
        /// </remarks>
        [Test]
        public async Task WithTheFallbackTurnedOff_NothingIsTried()
        {

            await ServerMit(
                Eintrag("_xmpp-server._tcp.woanders.example", 0, 0, "egal.example", 5269));

            var streng = new DnsS2SAddressResolver(_dnsClient!) { FallBackToDomain = false };

            Assert.That(await streng.ResolveAsync("rechts.example"), Is.Empty);

        }

        #endregion

        #region ADotTarget_MeansTheServiceIsNotOffered()

        /// <summary>
        /// Ein Ziel "." ist eine Absage und kein fehlender Eintrag - der
        /// Rückfall greift dann <b>nicht</b>.
        /// </summary>
        /// <remarks>
        /// Der Unterschied ist der ganze Sinn dieser Schreibweise. Wer sie als
        /// Schweigen liest, verbindet sich zu einer Domain, die ausdrücklich
        /// gesagt hat, dass sie den Dienst nicht anbietet.
        /// </remarks>
        [Test]
        public async Task ADotTarget_MeansTheServiceIsNotOffered()
        {

            var resolver = await ServerMit(
                               Eintrag("_xmpp-server._tcp.rechts.example", 0, 0, ".", 0));

            Assert.That(await resolver.ResolveAsync("rechts.example"), Is.Empty);

        }

        #endregion

        #region AnUnreachableDnsServer_YieldsTheFallback()

        /// <summary>
        /// Antwortet gar kein DNS-Server, bleibt der Rückfall auf die Domain.
        /// </summary>
        /// <remarks>
        /// Eine ausbleibende Antwort ist nicht dasselbe wie eine Absage, und
        /// eine Ausnahme darf sie schon gar nicht werden - der Aufrufer würde
        /// sonst je nach Zustand des Netzes verschieden scheitern.
        /// </remarks>
        [Test]
        public async Task AnUnreachableDnsServer_YieldsTheFallback()
        {

            var totesZiel = new DNSClient(IPv4Address.Localhost,
                                          IPPort.Parse(FreierPort()),
                                          QueryTimeout:   TimeSpan.FromSeconds(1),
                                          UseQueryCache:  false);

            var resolver = new DnsS2SAddressResolver(totesZiel)
                           {
                               Timeout = TimeSpan.FromSeconds(1)
                           };

            var ziele = await resolver.ResolveAsync("rechts.example");

            Assert.Multiple(() =>
            {
                Assert.That(ziele,         Has.Count.EqualTo(1));
                Assert.That(ziele[0].Host, Is.EqualTo("rechts.example"));
            });

        }

        #endregion

    }

}
