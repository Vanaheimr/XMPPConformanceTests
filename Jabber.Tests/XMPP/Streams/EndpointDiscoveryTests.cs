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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0156 am Verbindungsaufbau: Wer keinen Endpunkt nennt, bekommt den
    /// aus dem <c>host-meta</c> seiner Domain.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge im XEP ist ausdrücklich eine Nachrangige: „HTTPS queries
    /// for host-meta information MUST be used only as a fallback after the
    /// methods specified in RFC 6120 have been exhausted." Für diesen Client
    /// heisst das: Ein angegebener Endpunkt wird nie überstimmt. Gefragt wird
    /// nur, wenn der Aufrufer keinen genannt hat - und schlägt auch das fehl,
    /// bleibt es beim eingebauten Vorgabewert.
    /// </remarks>
    [TestFixture]
    public class EndpointDiscoveryTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>Ein Abrufer, der für jede Adresse dieselbe Antwort gibt.</summary>
        private static AltConnectionsResolver Antwortet(String? hostMeta)
            => new ((uri, ct) => Task.FromResult(hostMeta));

        #endregion


        #region TheDiscoveredEndpointIsUsed()

        /// <summary>
        /// Der Kern: ohne angegebenen Endpunkt meldet sich der Client dort an,
        /// wohin das <c>host-meta</c> zeigt.
        /// </summary>
        [Test]
        public async Task TheDiscoveredEndpointIsUsed()
        {

            Server.AddAccount("alice");

            var connection = new XMPPConnection($"alice@{Server.Domain}", "pw")
            {
                ServerCertificateValidator  = Server.IsOwnCertificate,
                EndpointDiscovery           = Antwortet(
                    "{ \"links\": [ { \"rel\": \"urn:xmpp:alt-connections:websocket\", \"href\": \"" +
                    Server.Uri + "\" } ] }")
            };

            var client = new XMPPClient(connection);

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(connection.WebSocketUri, Is.EqualTo(Server.Uri));
                Assert.That(connection.State,        Is.EqualTo(ConnectionState.Connected));
            });

            await client.DisconnectAsync();

        }

        #endregion

        #region WithoutAHostMeta_TheDefaultRemains()

        /// <summary>
        /// Findet die Discovery nichts, bleibt der eingebaute Vorgabewert
        /// stehen - und der Verbindungsaufbau scheitert dort, nicht an der
        /// Discovery.
        /// </summary>
        /// <remarks>
        /// Geprüft wird der Endpunkt und nicht der Fehlertext: Die Ausnahme
        /// kommt aus dem Transport und lautet „Unable to connect to the remote
        /// server" - sie nennt die Adresse nicht, zu der verbunden wurde. Das
        /// fällt hier zum ersten Mal ins Gewicht, weil die Adresse seit
        /// XEP-0156 nicht mehr zwingend vom Aufrufer stammt, und steht deshalb
        /// unter „Später".
        /// </remarks>
        [Test]
        public async Task WithoutAHostMeta_TheDefaultRemains()
        {

            var gefragt = 0;

            var connection = new XMPPConnection($"alice@{Server.Domain}", "pw")
            {
                EndpointDiscovery      = new AltConnectionsResolver((uri, ct) =>
                                         {
                                             gefragt++;
                                             return Task.FromResult<String?>(null);
                                         }),

                // Auf 5443 hört nichts; jeder Versuch endet sofort. Die
                // Vorgabe wären fünf davon mit wachsender Wartezeit - für eine
                // Aussage, die schon der erste trifft.
                MaxReconnectAttempts   = 1,
                InitialReconnectDelay  = TimeSpan.FromMilliseconds(50)
            };

            var client = new XMPPClient(connection);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {

                Assert.That(connection.WebSocketUri, Is.EqualTo($"wss://{Server.Domain}:5443/ws"));

                // Zwei Adressen (host-meta.json und host-meta) - aber nur
                // einmal, obwohl der Client danach noch einen zweiten
                // Verbindungsversuch macht. Wer bei jedem Versuch neu sucht,
                // wartet bei einem Server, der weg ist, jedes Mal wieder auf
                // eine HTTPS-Antwort, die es nicht gibt.
                Assert.That(gefragt, Is.EqualTo(2),
                            $"Die Discovery lief mehr als einmal an: {gefragt} Abfragen.");

            });

        }

        #endregion

        #region AGivenEndpoint_IsNeverOverruled()

        /// <summary>
        /// Wer einen Endpunkt angibt, wird nicht gefragt - die Discovery läuft
        /// gar nicht erst an.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre „immer erst nachschauen" eine bestandene
        /// Lösung. Sie wäre falsch und teuer: Ein Aufrufer, der seinen Endpunkt
        /// kennt, zahlte für jede Verbindung eine HTTPS-Abfrage, und ein
        /// fremdes <c>host-meta</c> könnte ihn woandershin schicken.
        /// </remarks>
        [Test]
        public async Task AGivenEndpoint_IsNeverOverruled()
        {

            Server.AddAccount("alice");

            var gefragt = false;

            var connection = new XMPPConnection($"alice@{Server.Domain}", "pw", Server.Uri)
            {
                ServerCertificateValidator  = Server.IsOwnCertificate,
                EndpointDiscovery           = new AltConnectionsResolver((uri, ct) =>
                                              {
                                                  gefragt = true;
                                                  return Task.FromResult<String?>(
                                                      "{ \"links\": [ { \"rel\": \"urn:xmpp:alt-connections:websocket\"," +
                                                      " \"href\": \"wss://woanders.example:443/ws\" } ] }");
                                              })
            };

            var client = new XMPPClient(connection);

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(gefragt,                 Is.False, "Der angegebene Endpunkt steht nicht zur Debatte.");
                Assert.That(connection.WebSocketUri, Is.EqualTo(Server.Uri));
            });

            await client.DisconnectAsync();

        }

        #endregion

    }

}
