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
    /// Basis für alle XMPP-Client-Tests: startet je Test einen
    /// <see cref="XMPPServer"/> und räumt Server und Clients wieder ab.
    /// </summary>
    public abstract class AXMPPTests
    {

        #region Data

        private readonly List<XMPPClient> _clients = [];

        /// <summary>
        /// Die Wache gegen verschluckte Programmierfehler - sie hängt an
        /// <b>jedem</b> Test und nicht an einem eigenen. Anders wäre sie
        /// wertlos: Wo ein solcher Fehler auftritt, weiss man vorher nicht, und
        /// ein einzelner Test bewacht nur den Weg, den er selbst geht.
        /// </summary>
        private readonly InternalErrorGuard _guard = new();

        /// <summary>Der Testserver des laufenden Tests.</summary>
        protected XMPPServer Server { get; private set; } = null!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void StartServer()
        {

            _guard.Reset();

            Server = new XMPPServer();

            _guard.Watch(Server);

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

            _guard.AssertClean();

        }

        #endregion

        /// <summary>
        /// Stellt einen weiteren Server unter dieselbe Wache und gibt ihn
        /// zurück - für Tests, die neben <see cref="Server"/> noch eigene
        /// betreiben.
        /// </summary>
        protected XMPPServer Watched(XMPPServer server)
            => _guard.Watched(server);

        /// <summary>
        /// Sagt der Wache, dass dieser Test einen internen Fehler absichtlich
        /// auslöst.
        /// </summary>
        protected void ExpectInternalErrors()
            => _guard.Expect();

        /// <summary>Die gemeldeten internen Fehler dieses Tests.</summary>
        protected IReadOnlyList<String> InternalErrors
            => _guard.Errors;


        /// <summary>
        /// Legt ein Konto an und verbindet einen echten <see cref="XMPPClient"/>
        /// damit. Der Client wird am Testende automatisch abgeräumt.
        /// </summary>
        /// <param name="localPart">Lokalteil des JIDs, z.B. "alice".</param>
        /// <param name="createAccount">Konto anlegen, falls es noch nicht existiert.</param>
        /// <param name="keepalive">Keepalive-Intervall; null schaltet Keepalive ab.</param>
        /// <param name="reconnectDelay">Wartezeit vor dem ersten Reconnect-Versuch.</param>
        /// <param name="streamManagement">
        /// XEP-0198 Stream Management aushandeln? <c>null</c> lässt den
        /// Vorgabewert stehen.
        /// </param>
        protected async Task<XMPPClient> ConnectClientAsync(String     localPart             = "alice",
                                                            Boolean    createAccount         = true,
                                                            TimeSpan?  keepalive             = null,
                                                            TimeSpan?  reconnectDelay        = null,
                                                            Boolean?   streamManagement      = null,
                                                            Int32      maxReconnectAttempts  = 20)
        {

            if (createAccount && Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client = CreateClient(localPart, keepalive, reconnectDelay,
                                      streamManagement:     streamManagement,
                                      maxReconnectAttempts: maxReconnectAttempts);

            await client.ConnectAsync();

            return client;

        }

        /// <summary>
        /// Erstellt einen noch nicht verbundenen Client gegen den Testserver.
        /// </summary>
        /// <remarks>
        /// <paramref name="streamManagement"/> ist absichtlich
        /// <see cref="Nullable{T}"/> und nicht <c>false</c>: <c>null</c> lässt
        /// den Vorgabewert von <see cref="XMPPConnection"/> stehen, und damit
        /// läuft die ganze Sammlung mit dem, was ein Aufrufer ohne eigene
        /// Meinung bekommt. Stünde hier ein hartes <c>false</c>, prüfte kein
        /// einziger Test den Vorgabewert - eine Umstellung ginge geräuschlos
        /// durch.
        /// </remarks>
        protected XMPPClient CreateClient(String     localPart             = "alice",
                                          TimeSpan?  keepalive             = null,
                                          TimeSpan?  reconnectDelay        = null,
                                          String     password              = "pw",
                                          Boolean?   streamManagement      = null,
                                          Int32      maxReconnectAttempts  = 20)
        {

            var connection = new XMPPConnection($"{localPart}@{Server.Domain}",
                                                password,
                                                Server.Uri)
            {
                KeepaliveEnabled         = keepalive.HasValue,
                KeepaliveInterval        = keepalive ?? TimeSpan.FromSeconds(25),
                InitialReconnectDelay    = reconnectDelay ?? TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts     = maxReconnectAttempts,

                // Der Testserver signiert sein Zertifikat selbst; kein Rechner
                // vertraut ihm. Angeheftet wird der Fingerabdruck genau dieses
                // Servers - eine Prüfung, die alles annimmt, liesse die Tests
                // auch gegen eine fremde Gegenstelle bestehen.
                ServerCertificateValidator = Server.IsOwnCertificate
            };

            if (streamManagement.HasValue)
                connection.StreamManagementEnabled = streamManagement.Value;

            var client = new XMPPClient(connection);
            _clients.Add(client);

            return client;

        }

        /// <summary>
        /// Trägt einen Kontakt in den serverseitigen Roster eines Kontos ein.
        /// Beide Konten werden angelegt, falls es sie noch nicht gibt.
        /// </summary>
        /// <param name="localPart">Wessen Roster.</param>
        /// <param name="contact">Wer eingetragen wird.</param>
        /// <param name="subscription">none, to, from oder both.</param>
        protected void SetServerRoster(String localPart, String contact, String subscription)
        {

            var account = Server.GetAccount($"{localPart}@{Server.Domain}") ?? Server.AddAccount(localPart);

            if (Server.GetAccount($"{contact}@{Server.Domain}") is null)
                Server.AddAccount(contact);

            account.SetRosterEntry(new RosterEntry($"{contact}@{Server.Domain}", null, subscription));

        }

        /// <summary>
        /// Stellt die beidseitige Presence-Berechtigung her, wie sie nach einem
        /// vollständigen Subscription-Handshake bestünde (RFC 6121,
        /// Abschnitt 3.1).
        /// </summary>
        protected void MakeContacts(String localPartA, String localPartB)
        {
            SetServerRoster(localPartA, localPartB, "both");
            SetServerRoster(localPartB, localPartA, "both");
        }

        /// <summary>
        /// Wartet, bis die Bedingung zutrifft, und lässt den Test sonst
        /// mit einer verständlichen Meldung scheitern.
        /// </summary>
        protected static async Task WaitFor(Func<Boolean>  condition,
                                            String         what,
                                            TimeSpan?      timeout = null)
        {

            var ok = await XMPPServer.WaitUntilAsync(condition, timeout);

            Assert.That(ok, Is.True, $"Zeitüberschreitung beim Warten auf: {what}");

        }

        /// <summary>
        /// Prüft, dass die Bedingung innerhalb der Wartezeit <b>nicht</b>
        /// eintritt. Die Wartezeit ist bewusst kurz - ein negativer Nachweis
        /// kostet sie in jedem Fall vollständig.
        /// </summary>
        protected static async Task WaitAgainst(Func<Boolean>  condition,
                                                String         what,
                                                TimeSpan?      timeout = null)
        {

            var happened = await XMPPServer.WaitUntilAsync(condition,
                                                           timeout ?? TimeSpan.FromSeconds(2));

            Assert.That(happened, Is.False, $"Hätte nicht eintreten dürfen: {what}");

        }

        /// <summary>
        /// Ein Verbindungsaufbau, der scheitern <b>soll</b> — und gibt den
        /// Fehler zurück, statt ihn nach oben durchzulassen.
        /// </summary>
        /// <remarks>
        /// Seit D31 wirft <c>ConnectAsync</c> bei einem gescheiterten Aufbau.
        /// Elf Tests prüften bis dahin einen erwarteten Fehlschlag mit einem
        /// blossen <c>await</c> und den Zusicherungen danach; das ging nur, weil
        /// der Aufruf stillschweigend zurückkam.
        ///
        /// Dieser Helfer macht die Erwartung ausdrücklich, statt sie in jedem
        /// Test einzeln in ein <c>try</c> zu verpacken: <b>Hier muss es
        /// scheitern.</b> Damit prüfen die elf Tests seitdem eine Zusicherung
        /// mehr als vorher — dass der Fehlschlag überhaupt beim Aufrufer
        /// ankommt.
        /// </remarks>
        protected static async Task<Exception> FailingConnectAsync(XMPPClient client)
        {

            try
            {
                await client.ConnectAsync();
            }
            catch (Exception e)
            {
                return e;
            }

            Assert.Fail("Der Verbindungsaufbau hätte scheitern müssen, kam aber durch.");

            return null!;

        }

    }

}
