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

using System.Diagnostics;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Beim Reconnect muss die vorige Verbindung vollständig abgebaut werden.
    /// Wird die alte CancellationTokenSource nur überschrieben statt
    /// abgebrochen, laufen Empfangs- und Keepalive-Schleife weiter und
    /// summieren sich mit jedem Reconnect auf.
    /// </summary>
    [TestFixture]
    public class ReconnectTests : AXMPPTests
    {

        #region Data

        private static readonly TimeSpan Keepalive = TimeSpan.FromMilliseconds(500);

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Zählt, was die Keepalive-Schleife tatsächlich verschickt.
        /// </summary>
        /// <remarks>
        /// Die Schleife wählt ihr Mittel nach Lage: ist XEP-0198 ausgehandelt,
        /// schickt sie ein <c>&lt;r/&gt;</c>, sonst einen XEP-0199-Ping. Diese
        /// beiden Tests zählten bis zuletzt nur Pings - und als Stream
        /// Management zum Vorgabewert wurde, zählten sie nichts mehr.
        ///
        /// Der eine Test wurde davon rot, der andere <b>grün</b>: „null Pings
        /// sind höchstens sieben Pings" trifft zu, und ein Test, der nichts
        /// mehr misst, sagt das nicht von selbst. Deshalb hier beide Verfahren
        /// und beide Tests über beide.
        /// </remarks>
        private static Int32 KeepaliveCount(XMPPSession session, Boolean streamManagement)

            => streamManagement
                   ? session.CountReceived("<r xmlns='urn:xmpp:sm:3'/>")
                   : session.CountReceived("urn:xmpp:ping");

        #endregion

        #region Reconnect_EstablishesExactlyOneNewConnection()

        /// <summary>
        /// Jeder Verbindungsabriss führt zu genau einer neuen Server-Verbindung.
        /// </summary>
        [Test]
        public async Task Reconnect_EstablishesExactlyOneNewConnection()
        {

            var client = await ConnectClientAsync(keepalive: Keepalive,
                                                  reconnectDelay: TimeSpan.FromMilliseconds(200));

            Assert.That(client.IsConnected, Is.True);

            const Int32 kills = 3;

            for (var i = 0; i < kills; i++)
            {

                var before = Server.ConnectionCount;
                Server.KillAllSessions();

                await WaitFor(() => Server.ConnectionCount > before && client.IsConnected,
                              $"Reconnect {i + 1} von {kills}",
                              TimeSpan.FromSeconds(20));

            }

            Assert.That(Server.ConnectionCount, Is.EqualTo(kills + 1),
                        "Der Server hat mehr oder weniger Verbindungen gesehen als erwartet.");

        }

        #endregion

        #region Reconnect_DoesNotAccumulateKeepaliveLoops()

        /// <summary>
        /// Nach mehreren Reconnects darf weiterhin nur eine Keepalive-Schleife
        /// laufen. Mit dem früheren Leak feuerten nach drei Abrissen vier
        /// Schleifen parallel - gemessen 17 statt 6 Pings in drei Sekunden.
        /// </summary>
        [Test]
        [TestCase(true,  TestName = "Reconnect_DoesNotAccumulateKeepaliveLoops(Stream Management)")]
        [TestCase(false, TestName = "Reconnect_DoesNotAccumulateKeepaliveLoops(Ping)")]
        public async Task Reconnect_DoesNotAccumulateKeepaliveLoops(Boolean streamManagement)
        {

            var client = await ConnectClientAsync(keepalive: Keepalive,
                                                  reconnectDelay: TimeSpan.FromMilliseconds(200),
                                                  streamManagement: streamManagement);

            const Int32 kills = 3;

            for (var i = 0; i < kills; i++)
            {

                var before = Server.ConnectionCount;
                Server.KillAllSessions();

                await WaitFor(() => Server.ConnectionCount > before && client.IsConnected,
                              $"Reconnect {i + 1} von {kills}",
                              TimeSpan.FromSeconds(20));

            }

            // Messfenster: nur die aktuelle Sitzung zählen
            var session = Server.SessionOf(client.FullJid)!;
            await Task.Delay(300);

            var before2  = KeepaliveCount(session, streamManagement);
            var window   = TimeSpan.FromSeconds(3);

            await Task.Delay(window);

            var gezaehlt  = KeepaliveCount(session, streamManagement) - before2;
            var expected  = (Int32) (window.TotalMilliseconds / Keepalive.TotalMilliseconds);
            var limit     = expected + 2;

            Assert.Multiple(() =>
            {

                Assert.That(gezaehlt, Is.LessThanOrEqualTo(limit),
                            $"{gezaehlt} Keepalives in {window.TotalSeconds}s, erwartet höchstens {limit}. " +
                            $"Das deutet auf {Math.Round((Double) gezaehlt / expected, 1)} parallele Keepalive-Schleifen hin.");

                // Die Untergrenze ist der eigentliche Zugewinn: ohne sie besteht
                // dieser Test auch dann, wenn gar kein Keepalive mehr feuert -
                // und genau das war er eine Zeitlang.
                Assert.That(gezaehlt, Is.GreaterThan(0),
                            "Kein einziges Keepalive im Messfenster - dann prüft dieser Test nichts.");

            });

        }

        #endregion

        #region Disconnect_StopsKeepalive()

        /// <summary>
        /// Nach dem Trennen darf kein Keepalive mehr feuern.
        /// </summary>
        [Test]
        [TestCase(true,  TestName = "Disconnect_StopsKeepalive(Stream Management)")]
        [TestCase(false, TestName = "Disconnect_StopsKeepalive(Ping)")]
        public async Task Disconnect_StopsKeepalive(Boolean streamManagement)
        {

            var client   = await ConnectClientAsync(keepalive: Keepalive,
                                                    streamManagement: streamManagement);
            var session  = Server.SessionOf(client.FullJid)!;

            await WaitFor(() => KeepaliveCount(session, streamManagement) > 0,
                          "erstes Keepalive");

            await client.DisconnectAsync();
            await Task.Delay(300);

            var nachTrennung = KeepaliveCount(session, streamManagement);

            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.That(KeepaliveCount(session, streamManagement), Is.EqualTo(nachTrennung),
                        "Nach dem Trennen kamen weitere Keepalives an.");

        }

        #endregion

        #region Disconnect_WithSilentServer_ReturnsWithinCloseTimeout()

        /// <summary>
        /// Beantwortet der Server das Close-Frame nicht, darf DisconnectAsync
        /// trotzdem zügig zurückkehren - der Close-Handshake ist auf drei
        /// Sekunden begrenzt, danach wird der Socket abgebrochen.
        /// </summary>
        [Test]
        public async Task Disconnect_WithSilentServer_ReturnsWithinCloseTimeout()
        {

            Server.CompleteCloseHandshake = false;

            var client = await ConnectClientAsync();
            Assert.That(client.IsConnected, Is.True);

            var sw = Stopwatch.StartNew();
            await client.DisconnectAsync();
            sw.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
                            $"DisconnectAsync hing {sw.Elapsed.TotalSeconds:F1}s.");

                // Ohne Untergrenze bestünde der Test auch dann, wenn der Server
                // gar nicht schweigt, sondern die Verbindung abreisst - der
                // Client kehrt dann sofort zurück und das Zeitlimit hat nie
                // gegriffen. Genau so ist er beim Umbau des Transports einmal
                // durchgelaufen.
                Assert.That(sw.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(2)),
                            $"DisconnectAsync kehrte nach {sw.Elapsed.TotalSeconds:F1}s zurück - " +
                            "das Zeitlimit des Close-Handshakes kann nicht gegriffen haben.");

                Assert.That(client.IsConnected, Is.False);
            });

        }

        #endregion

    }

}
