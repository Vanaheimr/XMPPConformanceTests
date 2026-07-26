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

using org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP.Server;
using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Basis für alle XMPP-Client-Tests: startet je Test einen
    /// <see cref="FakeXMPPServer"/> und räumt Server und Clients wieder ab.
    /// </summary>
    public abstract class AXMPPTests
    {

        #region Data

        private readonly List<XMPPClient> _clients = [];

        /// <summary>Der Testserver des laufenden Tests.</summary>
        protected FakeXMPPServer Server { get; private set; } = null!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void StartServer()
        {
            Server = new FakeXMPPServer();
            Server.Start();
        }

        [TearDown]
        public async Task StopServer()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); }
                catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            await Server.DisposeAsync();

        }

        #endregion


        /// <summary>
        /// Legt ein Konto an und verbindet einen echten <see cref="XMPPClient"/>
        /// damit. Der Client wird am Testende automatisch abgeräumt.
        /// </summary>
        /// <param name="localPart">Lokalteil des JIDs, z.B. "alice".</param>
        /// <param name="createAccount">Konto anlegen, falls es noch nicht existiert.</param>
        /// <param name="keepalive">Keepalive-Intervall; null schaltet Keepalive ab.</param>
        /// <param name="reconnectDelay">Wartezeit vor dem ersten Reconnect-Versuch.</param>
        protected async Task<XMPPClient> ConnectClientAsync(String     localPart       = "alice",
                                                            Boolean    createAccount   = true,
                                                            TimeSpan?  keepalive       = null,
                                                            TimeSpan?  reconnectDelay  = null)
        {

            if (createAccount && Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client = CreateClient(localPart, keepalive, reconnectDelay);

            await client.ConnectAsync();

            return client;

        }

        /// <summary>
        /// Erstellt einen noch nicht verbundenen Client gegen den Testserver.
        /// </summary>
        protected XMPPClient CreateClient(String     localPart       = "alice",
                                          TimeSpan?  keepalive       = null,
                                          TimeSpan?  reconnectDelay  = null,
                                          String     password        = "pw")
        {

            var connection = new XMPPConnection($"{localPart}@{Server.Domain}",
                                                password,
                                                Server.Uri)
            {
                KeepaliveEnabled       = keepalive.HasValue,
                KeepaliveInterval      = keepalive ?? TimeSpan.FromSeconds(25),
                InitialReconnectDelay  = reconnectDelay ?? TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts   = 20
            };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            return client;

        }

        /// <summary>
        /// Wartet, bis die Bedingung zutrifft, und lässt den Test sonst
        /// mit einer verständlichen Meldung scheitern.
        /// </summary>
        protected static async Task WaitFor(Func<Boolean>  condition,
                                            String         what,
                                            TimeSpan?      timeout = null)
        {

            var ok = await FakeXMPPServer.WaitUntilAsync(condition, timeout);

            Assert.That(ok, Is.True, $"Zeitüberschreitung beim Warten auf: {what}");

        }

    }

}
