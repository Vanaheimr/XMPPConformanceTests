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
        public async Task Reconnect_DoesNotAccumulateKeepaliveLoops()
        {

            var client = await ConnectClientAsync(keepalive: Keepalive,
                                                  reconnectDelay: TimeSpan.FromMilliseconds(200));

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

            var before2  = session.CountReceived("urn:xmpp:ping");
            var window   = TimeSpan.FromSeconds(3);

            await Task.Delay(window);

            var pings     = session.CountReceived("urn:xmpp:ping") - before2;
            var expected  = (Int32) (window.TotalMilliseconds / Keepalive.TotalMilliseconds);
            var limit     = expected + 2;

            Assert.That(pings, Is.LessThanOrEqualTo(limit),
                        $"{pings} Pings in {window.TotalSeconds}s, erwartet höchstens {limit}. " +
                        $"Das deutet auf {Math.Round((Double) pings / expected, 1)} parallele Keepalive-Schleifen hin.");

        }

        #endregion

        #region Disconnect_StopsKeepalive()

        /// <summary>
        /// Nach dem Trennen darf kein Keepalive mehr feuern.
        /// </summary>
        [Test]
        public async Task Disconnect_StopsKeepalive()
        {

            var client   = await ConnectClientAsync(keepalive: Keepalive);
            var session  = Server.SessionOf(client.FullJid)!;

            await WaitFor(() => session.CountReceived("urn:xmpp:ping") > 0,
                          "erstes Keepalive");

            await client.DisconnectAsync();
            await Task.Delay(300);

            var afterDisconnect = session.CountReceived("urn:xmpp:ping");

            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.That(session.CountReceived("urn:xmpp:ping"), Is.EqualTo(afterDisconnect),
                        "Nach dem Trennen kamen weitere Pings an.");

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

                Assert.That(client.IsConnected, Is.False);
            });

        }

        #endregion

    }

}
