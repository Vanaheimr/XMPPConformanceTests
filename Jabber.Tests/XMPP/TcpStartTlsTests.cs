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

using System.Net;
using System.Net.Sockets;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    // Wie in XMPPServer.cs: Hermod bringt einen eigenen Typ IPAddress mit.
    // Der Alias muss innerhalb der Namespace-Deklaration stehen.
    using IPAddress = System.Net.IPAddress;

    /// <summary>
    /// Die Aushandlung selbst (RFC 6120, Abschnitt 5.4) - nicht, dass eine
    /// Nachricht ankommt, sondern dass sie unter den falschen Umständen
    /// <b>nicht</b> ankommt.
    /// </summary>
    /// <remarks>
    /// Diese Datei ist die Antwort auf eine Mutationsprobe, bei der vier von
    /// fünf Eingriffen in die Aushandlung grün blieben. Der Grund war jedesmal
    /// derselbe: die Föderationstests spielen beide Seiten korrekt, und
    /// solange sich beide an die Regeln halten, macht es keinen Unterschied,
    /// ob eine Seite sie auch <i>prüft</i>. Geprüft wird eine Regel erst durch
    /// eine Gegenstelle, die sie bricht - und die muss man eigens bauen.
    /// </remarks>
    [TestFixture]
    public class TcpStartTlsTests
    {

        #region Data

        private XMPPServer _server = null!;
        private readonly List<IAsyncDisposable> _aufraeumen = [];

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void EinServer()
        {
            _server = new XMPPServer("links.example");
            _server.Start();
        }

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var d in _aufraeumen)
            {
                try { await d.DisposeAsync(); }
                catch { /* im Teardown egal */ }
            }

            _aufraeumen.Clear();

            await _server.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Ein Server, der nach Drehbuch antwortet - für Gegenstellen, die
        /// sich nicht an RFC 6120 halten.
        /// </summary>
        private sealed class GespielterServer : IAsyncDisposable
        {

            private readonly TcpListener             _listener;
            private readonly CancellationTokenSource _cts = new();

            public Int32 Port { get; }

            public GespielterServer(Func<NetworkStream, CancellationToken, Task> drehbuch)
            {

                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();

                Port = ((IPEndPoint) _listener.LocalEndpoint).Port;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!_cts.IsCancellationRequested)
                        {
                            var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                            _ = Task.Run(async () =>
                            {
                                try { await drehbuch(client.GetStream(), _cts.Token); }
                                catch (Exception) { /* egal */ }
                                finally { try { client.Dispose(); } catch { } }
                            });
                        }
                    }
                    catch (Exception) { /* beendet */ }
                });

            }

            public async ValueTask DisposeAsync()
            {
                await _cts.CancelAsync();
                try { _listener.Stop(); } catch { }
                _cts.Dispose();
            }

        }

        private static async Task Schreibe(NetworkStream netz, String text)
            => await netz.WriteAsync(Encoding.UTF8.GetBytes(text));

        private static async Task<String> LiesBis(NetworkStream      netz,
                                                  Func<String, Boolean>  fertig,
                                                  CancellationToken  ct)
        {

            var puffer  = new Byte[8192];
            var alles   = "";

            while (!fertig(alles))
            {

                var n = await netz.ReadAsync(puffer, ct);

                if (n <= 0)
                    break;

                alles += Encoding.UTF8.GetString(puffer, 0, n);

            }

            return alles;

        }

        /// <summary>Verkabelt den Server mit einer gespielten Gegenstelle.</summary>
        private TcpServerLinks LinksZu(GespielterServer gegenstelle)
        {

            var links = new TcpServerLinks(_server, mode: TcpTlsMode.StartTls);
            _aufraeumen.Add(links);

            links.AddPeer("fremd.example",
                          IPAddress.Loopback.ToString(),
                          gegenstelle.Port,
                          TcpTlsMode.StartTls,
                          validator: (_, _, _, _) => true);

            return links;

        }

        /// <summary>
        /// Schreibt alles mit, was von jetzt an ankommt - bis die Verbindung
        /// endet.
        /// </summary>
        /// <remarks>
        /// Das ist der Unterschied zwischen "die Zustellung ist gescheitert"
        /// und "der Client hat aufgehört zu reden". Nur das Zweite belegt, dass
        /// er die Regel geprüft hat: scheitern würde die Zustellung auch dann,
        /// wenn er einfach ins Zeitlimit liefe.
        /// </remarks>
        private static async Task Mitschreiben(NetworkStream      netz,
                                               List<Byte>         ziel,
                                               CancellationToken  ct)
        {

            var puffer = new Byte[4096];

            while (true)
            {

                var n = await netz.ReadAsync(puffer, ct);

                if (n <= 0)
                    break;

                lock (ziel)
                    ziel.AddRange(puffer[..n]);

            }

        }

        private const String Stanza =
            "<message from='alice@links.example' to='bob@fremd.example'><body>hallo</body></message>";

        #endregion


        #region APeerThatDoesNotOfferStartTls_IsNotUsed()

        /// <summary>
        /// Eine Gegenstelle ohne STARTTLS im Angebot bekommt nichts - schon
        /// gar nicht im Klartext.
        /// </summary>
        /// <remarks>
        /// Ohne diese Prüfung wäre die Aushandlung eine Bitte statt einer
        /// Bedingung: ein Zwischenmann müsste nur das Angebot aus den Features
        /// streichen, und der Stream liefe unverschlüsselt weiter. Genau das
        /// ist der klassische Downgrade-Angriff auf STARTTLS.
        /// </remarks>
        [Test]
        public async Task APeerThatDoesNotOfferStartTls_IsNotUsed()
        {

            var nachDemAngebot = new List<Byte>();

            await using var gegenstelle = new GespielterServer(async (netz, ct) =>
            {
                await LiesBis(netz, t => t.Contains("<stream:stream", StringComparison.Ordinal), ct);
                await Schreibe(netz,
                    "<stream:stream xmlns='jabber:server' " +
                    "xmlns:stream='http://etherx.jabber.org/streams' " +
                    "from='fremd.example' to='links.example' id='x' version='1.0'>");
                await Schreibe(netz,
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'/>");

                await Mitschreiben(netz, nachDemAngebot, ct);
            });

            var links = LinksZu(gegenstelle);

            var zugestellt = await links.DeliverAsync("fremd.example", Stanza)
                                        .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Multiple(() =>
            {
                Assert.That(zugestellt, Is.False,
                            "Ohne STARTTLS-Angebot darf keine Stanza hinausgehen.");

                // Der eigentliche Nachweis: der Client hört auf zu reden,
                // statt bloss in ein Zeitlimit zu laufen.
                lock (nachDemAngebot)
                    Assert.That(nachDemAngebot, Is.Empty,
                                "Nach ausbleibendem STARTTLS-Angebot darf nichts mehr gesendet werden.");
            });

        }

        #endregion

        #region AFailureInsteadOfProceed_AbortsTheHandshake()

        /// <summary>
        /// Antwortet die Gegenstelle auf <c>&lt;starttls/&gt;</c> mit
        /// <c>&lt;failure/&gt;</c>, endet der Aufbau (RFC 6120, Abschnitt
        /// 5.4.2.2).
        /// </summary>
        [Test]
        public async Task AFailureInsteadOfProceed_AbortsTheHandshake()
        {

            await using var gegenstelle = new GespielterServer(async (netz, ct) =>
            {
                await LiesBis(netz, t => t.Contains("<stream:stream", StringComparison.Ordinal), ct);
                await Schreibe(netz,
                    "<stream:stream xmlns='jabber:server' " +
                    "xmlns:stream='http://etherx.jabber.org/streams' " +
                    "from='fremd.example' to='links.example' id='x' version='1.0'>");
                await Schreibe(netz,
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'><required/></starttls>" +
                    "</stream:features>");

                await LiesBis(netz, t => t.Contains("<starttls", StringComparison.Ordinal), ct);
                await Schreibe(netz, "<failure xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>");

                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            });

            var links = LinksZu(gegenstelle);

            var zugestellt = await links.DeliverAsync("fremd.example", Stanza)
                                        .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.That(zugestellt, Is.False,
                        "Nach <failure/> darf nichts hinausgehen.");

        }

        #endregion

        #region SomethingOtherThanProceed_IsNotTakenAsProceed()

        /// <summary>
        /// Und die schärfere Fassung: irgendeine Antwort ist keine Zustimmung.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bestünde der vorige auch dann, wenn der Client nur
        /// prüfte, <i>dass</i> eine Antwort kam. Hier kommt eine, sie heisst
        /// nur nicht <c>&lt;proceed/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task SomethingOtherThanProceed_IsNotTakenAsProceed()
        {

            var nachDerAntwort = new List<Byte>();

            await using var gegenstelle = new GespielterServer(async (netz, ct) =>
            {
                await LiesBis(netz, t => t.Contains("<stream:stream", StringComparison.Ordinal), ct);
                await Schreibe(netz,
                    "<stream:stream xmlns='jabber:server' " +
                    "xmlns:stream='http://etherx.jabber.org/streams' " +
                    "from='fremd.example' to='links.example' id='x' version='1.0'>");
                await Schreibe(netz,
                    "<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                    "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'><required/></starttls>" +
                    "</stream:features>");

                await LiesBis(netz, t => t.Contains("<starttls", StringComparison.Ordinal), ct);

                // Eine Antwort, aber nicht die verlangte.
                await Schreibe(netz, "<irgendwas xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>");

                await Mitschreiben(netz, nachDerAntwort, ct);
            });

            var links = LinksZu(gegenstelle);

            var zugestellt = await links.DeliverAsync("fremd.example", Stanza)
                                        .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Multiple(() =>
            {
                Assert.That(zugestellt, Is.False);

                // Hielte der Client die Antwort für eine Zustimmung, käme
                // jetzt ein TLS-ClientHello.
                lock (nachDerAntwort)
                    Assert.That(nachDerAntwort, Is.Empty,
                                "Ohne <proceed/> darf der Client nicht mit TLS anfangen.");
            });

        }

        #endregion

        #region PipelinedPlaintextAfterStartTls_GetsNoProceed()

        /// <summary>
        /// Wer hinter das <c>&lt;starttls/&gt;</c> noch Klartext schiebt,
        /// bekommt keine Zustimmung.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 5.4.3.3: nach dem <c>&lt;starttls/&gt;</c> darf
        /// im Klartext nichts mehr folgen. Steht doch etwas im Puffer, ist es
        /// entweder eine kaputte Gegenstelle oder der Versuch, Klartext in den
        /// gleich verschlüsselten Stream zu schmuggeln - beides ein Grund
        /// aufzuhören.
        /// </remarks>
        [Test]
        public async Task PipelinedPlaintextAfterStartTls_GetsNoProceed()
        {

            await using var links = new TcpServerLinks(_server, mode: TcpTlsMode.StartTls);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, links.Port);

            var netz = client.GetStream();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            await Schreibe(netz,
                "<stream:stream xmlns='jabber:server' " +
                "xmlns:stream='http://etherx.jabber.org/streams' " +
                "from='fremd.example' to='links.example' version='1.0'>");

            await LiesBis(netz,
                          t => t.Contains("urn:ietf:params:xml:ns:xmpp-tls", StringComparison.Ordinal),
                          cts.Token);

            // <starttls/> und eine Stanza in einem einzigen Schreibvorgang.
            await Schreibe(netz,
                "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>" +
                "<message from='alice@fremd.example' to='bob@links.example'><body>x</body></message>");

            var antwort = await LiesBis(netz, t => t.Length > 0, cts.Token)
                              .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(antwort, Does.Not.Contain("proceed"),
                        "Vorausgeschickter Klartext muss den Aufbau beenden.");

        }

        #endregion

        #region SomethingOtherThanStartTls_GetsFailureAndNoStream()

        /// <summary>
        /// Statt <c>&lt;starttls/&gt;</c> gleich eine Stanza: das gibt
        /// <c>&lt;failure/&gt;</c> und keinen Stream.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe dazu, dass die Aushandlung eine Bedingung ist. Ein
        /// Server, der hier weitermachte, hätte die Verschlüsselung zu einer
        /// Höflichkeit gemacht.
        /// </remarks>
        [Test]
        public async Task SomethingOtherThanStartTls_GetsFailureAndNoStream()
        {

            await using var links = new TcpServerLinks(_server, mode: TcpTlsMode.StartTls);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, links.Port);

            var netz = client.GetStream();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            await Schreibe(netz,
                "<stream:stream xmlns='jabber:server' " +
                "xmlns:stream='http://etherx.jabber.org/streams' " +
                "from='fremd.example' to='links.example' version='1.0'>");

            var begruessung = await LiesBis(netz,
                                            t => t.Contains("urn:ietf:params:xml:ns:xmpp-tls", StringComparison.Ordinal),
                                            cts.Token);

            Assert.That(begruessung, Does.Contain("<required/>"),
                        "STARTTLS muss als zwingend angekündigt werden.");

            await Schreibe(netz,
                "<message from='alice@fremd.example' to='bob@links.example'><body>x</body></message>");

            var antwort = await LiesBis(netz,
                                        t => t.Contains("failure", StringComparison.Ordinal),
                                        cts.Token)
                              .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("failure"),
                            "Auf etwas anderes als <starttls/> gehört <failure/>.");
                Assert.That(antwort, Does.Not.Contain("proceed"));
            });

        }

        #endregion

    }

}
