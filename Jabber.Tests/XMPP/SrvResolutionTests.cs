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
    /// Gegenstellen über die Auflösung finden statt über die Liste
    /// (RFC 6120, Abschnitt 3.2).
    /// </summary>
    /// <remarks>
    /// Der Resolver wird hier eingesetzt und nicht befragt. Ein Test, der
    /// echtes DNS benutzt, prüft die Welt statt den Code: er hängt an einer
    /// Netzverbindung, an fremden Zonendateien und an Antwortzeiten, und wenn
    /// er rot wird, weiss niemand, woran es lag.
    /// </remarks>
    [TestFixture]
    public class SrvResolutionTests
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

            // Nur die Empfängerseite wird verkabelt; die Senderseite soll
            // ihre Adresse auflösen.
            _rechtsLinks = new TcpServerLinks(_rechts, mode: TcpTlsMode.StartTls);

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

        /// <summary>Ein Resolver, der eine feste Antwort gibt und mitzählt.</summary>
        private sealed class FesterResolver : IS2SAddressResolver
        {

            private readonly Func<String, IReadOnlyList<SrvTarget>> _antwort;

            public List<String> Gefragt { get; } = [];

            public FesterResolver(Func<String, IReadOnlyList<SrvTarget>> antwort)
            {
                _antwort = antwort;
            }

            public Task<IReadOnlyList<SrvTarget>> ResolveAsync(String             domain,
                                                               CancellationToken  cancellationToken = default)
            {
                lock (Gefragt) Gefragt.Add(domain);
                return Task.FromResult(_antwort(domain));
            }

        }

        /// <summary>Verkabelt die Senderseite über einen Resolver.</summary>
        private FesterResolver SenderMitResolver(Func<String, IReadOnlyList<SrvTarget>> antwort)
        {

            var resolver = new FesterResolver(antwort);

            _linksLinks = new TcpServerLinks(_links, mode: TcpTlsMode.StartTls)
                          {
                              AddressResolver       = resolver,
                              DefaultPeerValidator  = _rechts.IsOwnCertificate
                          };

            RueckwegEintragen();

            return resolver;

        }

        /// <summary>
        /// Trägt bei der Empfängerseite den Weg zurück ein.
        /// </summary>
        /// <remarks>
        /// Gehört nicht zu dem, was hier geprüft wird, ist aber nötig: die
        /// Dialback-Rückfrage geht von <c>rechts.example</c> aus, und ohne
        /// Adresse für <c>links.example</c> könnte sie niemanden fragen. In
        /// den übrigen Föderationstests erledigt das
        /// <c>TcpServerLinks.Connect</c> für beide Richtungen; hier wird nur
        /// eine Seite über den Resolver verkabelt, also muss die andere von
        /// Hand nachgezogen werden.
        /// </remarks>
        private void RueckwegEintragen()

            => _rechtsLinks.AddPeer("links.example",
                                    System.Net.IPAddress.Loopback.ToString(),
                                    _linksLinks.Port,
                                    TcpTlsMode.StartTls,
                                    validator: _links.IsOwnCertificate);

        private SrvTarget ZielAufRechts(UInt16 prioritaet = 0, UInt16 gewicht = 0)
            => new (prioritaet, gewicht, System.Net.IPAddress.Loopback.ToString(), _rechtsLinks.Port);

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

        private const String Stanza =
            "<message from='alice@links.example' to='bob@rechts.example'><body>aufgeloest</body></message>";

        #endregion


        #region AResolvedTarget_IsUsed()

        /// <summary>
        /// Eine Domain ohne Eintrag von Hand wird aufgelöst und erreicht.
        /// </summary>
        [Test]
        public async Task AResolvedTarget_IsUsed()
        {

            var resolver = SenderMitResolver(_ => [ZielAufRechts()]);

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            var zugestellt = await _linksLinks.DeliverAsync("rechts.example", Stanza)
                                              .WaitAsync(TimeSpan.FromSeconds(25));

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht über das aufgelöste Ziel");

            Assert.Multiple(() =>
            {
                Assert.That(zugestellt, Is.True);
                Assert.That(resolver.Gefragt, Is.EquivalentTo(new[] { "rechts.example" }));
            });

        }

        #endregion

        #region AManualEntry_WinsOverTheResolver()

        /// <summary>
        /// Ein Eintrag von Hand geht vor - der Resolver wird gar nicht erst
        /// gefragt.
        /// </summary>
        /// <remarks>
        /// Eine Entscheidung des Betreibers wiegt schwerer als eine Auskunft
        /// aus dem Netz. Ohne diesen Vorrang liesse sich eine hinterlegte
        /// Adresse durch eine gefälschte DNS-Antwort umgehen.
        /// </remarks>
        [Test]
        public async Task AManualEntry_WinsOverTheResolver()
        {

            var resolver = SenderMitResolver(_ => throw new InvalidOperationException(
                                                      "Der Resolver darf hier nicht gefragt werden."));

            _linksLinks.AddPeer("rechts.example",
                                System.Net.IPAddress.Loopback.ToString(),
                                _rechtsLinks.Port,
                                TcpTlsMode.StartTls,
                                validator: _rechts.IsOwnCertificate);

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await _linksLinks.DeliverAsync("rechts.example", Stanza)
                             .WaitAsync(TimeSpan.FromSeconds(25));

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht über den Eintrag von Hand");

            Assert.That(resolver.Gefragt, Is.Empty);

        }

        #endregion

        #region AnUnreachableFirstTarget_FallsThroughToTheNext()

        /// <summary>
        /// Ist das erste Ziel nicht erreichbar, wird das nächste versucht.
        /// </summary>
        /// <remarks>
        /// SRV-Einträge nennen Ausweichrechner. Sie aufzulisten und dann nur
        /// den ersten anzuwählen wäre eine halbe Umsetzung - und eine, die
        /// erst auffällt, wenn ein Rechner ausfällt.
        /// </remarks>
        [Test]
        public async Task AnUnreachableFirstTarget_FallsThroughToTheNext()
        {

            // Ein sicher toter Port zuerst, das echte Ziel danach.
            var totesZiel = new SrvTarget(10, 0, System.Net.IPAddress.Loopback.ToString(), 1);

            SenderMitResolver(_ => [totesZiel, ZielAufRechts(prioritaet: 20)]);

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            var zugestellt = await _linksLinks.DeliverAsync("rechts.example", Stanza)
                                              .WaitAsync(TimeSpan.FromSeconds(30));

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht über das zweite Ziel");

            Assert.That(zugestellt, Is.True);

        }

        #endregion

        #region ADomainThatResolvesToNothing_YieldsAnError()

        /// <summary>
        /// Löst eine Domain auf nichts auf, bleibt es beim
        /// <c>&lt;remote-server-not-found/&gt;</c>.
        /// </summary>
        [Test]
        public async Task ADomainThatResolvesToNothing_YieldsAnError()
        {

            SenderMitResolver(_ => []);

            var alice   = await ConnectAsync(_links, "alice");
            var fehler  = new List<StanzaError>();

            alice.OnStanzaError += (_, e) => fehler.Add(e);

            await alice.SendMessageAsync("niemand.example", "Hallo?");

            await WarteAuf(() => fehler.Count > 0, "den Fehler zur unauflösbaren Domain");

            Assert.That(fehler[0].Condition, Is.EqualTo("remote-server-not-found"));

        }

        #endregion

        #region TheCertificateIsCheckedAgainstTheDomain_NotTheSrvTarget()

        /// <summary>
        /// Geprüft wird gegen die <b>gesuchte Domain</b>, nicht gegen den
        /// Rechnernamen aus dem SRV-Eintrag (RFC 6120, Abschnitt 13.7.2.1).
        /// </summary>
        /// <remarks>
        /// Der wichtigste Test dieser Datei. Ohne DNSSEC ist ein SRV-Eintrag
        /// nicht beglaubigt; wer ihn fälschen kann, gibt das Ziel vor. Würde
        /// die Zertifikatsprüfung dem Ziel folgen, brächte der Angreifer den
        /// Massstab gleich mit, an dem er gemessen wird.
        ///
        /// Geprüft wird das hier daran, dass die Angabe im SRV-Eintrag eine
        /// nackte IP-Adresse ist, die in keinem Zertifikat als Domain steht.
        /// Die Verbindung gelingt trotzdem - also stammt der Massstab nicht
        /// von dort. Die Gegenprobe steht in
        /// <see cref="SaslExternalTests"/>: ein Zertifikat, das die gesuchte
        /// Domain nicht deckt, kommt nicht durch.
        /// </remarks>
        [Test]
        public async Task TheCertificateIsCheckedAgainstTheDomain_NotTheSrvTarget()
        {

            var gepruefteNamen = new List<String>();

            var resolver = new FesterResolver(_ => [ZielAufRechts()]);

            _linksLinks = new TcpServerLinks(_links, mode: TcpTlsMode.StartTls)
                          {
                              AddressResolver       = resolver,
                              DefaultPeerValidator  = (sender, cert, chain, fehler) =>
                                                      {
                                                          if (sender is System.Net.Security.SslStream s)
                                                              lock (gepruefteNamen)
                                                                  gepruefteNamen.Add(s.TargetHostName);

                                                          return _rechts.IsOwnCertificate(sender, cert, chain, fehler);
                                                      }
                          };

            RueckwegEintragen();

            var bob = await ConnectAsync(_rechts, "bob");

            var empfangen = new List<XMPPMessage>();
            bob.OnMessage += m => empfangen.Add(m);

            await _linksLinks.DeliverAsync("rechts.example", Stanza)
                             .WaitAsync(TimeSpan.FromSeconds(25));

            await WarteAuf(() => empfangen.Count > 0, "die Nachricht");

            lock (gepruefteNamen)
                Assert.That(gepruefteNamen, Has.All.EqualTo("rechts.example"),
                            "TLS muss gegen die gesuchte Domain laufen, nicht gegen das SRV-Ziel.");

        }

        #endregion

    }

}
