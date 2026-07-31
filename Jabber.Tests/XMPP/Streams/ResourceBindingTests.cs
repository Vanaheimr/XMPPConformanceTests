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
    /// RFC 6120, Abschnitt 7: das Resource Binding.
    ///
    /// Der Client bat um <c>console-&lt;Prozess-ID&gt;</c> und hatte keinen
    /// Plan B. Ein Server, der eine belegte Resource ablehnt statt selbst eine
    /// freie zu vergeben - Abschnitt 7.7.2.2 lässt ihm die Wahl -, brachte
    /// damit jeden zweiten Client im selben Prozess zum Scheitern.
    /// </summary>
    [TestFixture]
    public class ResourceBindingTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Erstellt einen Client ohne Reconnect.
        /// </summary>
        /// <remarks>
        /// Ein gescheitertes Binding lässt den Client sonst bis zu zwanzigmal
        /// neu verbinden. Diese Tests fragen aber, ob das Binding
        /// <b>selbst</b> zurechtkommt - über einen Reconnect zum Ziel zu
        /// finden wäre keine Antwort darauf, nur eine langsame Wiederholung
        /// derselben Frage.
        /// </remarks>
        private XMPPClient SingleAttemptClient(String localPart = "alice")
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client = CreateClient(localPart);
            client.Connection.MaxReconnectAttempts = 0;

            return client;

        }

        #endregion


        #region ConflictingResource_IsRetriedWithoutOne()

        /// <summary>
        /// Der Kern: nach <c>&lt;conflict/&gt;</c> bindet der Client einmal
        /// ohne Wunsch neu und übernimmt, was der Server vergibt.
        /// </summary>
        [Test]
        public async Task ConflictingResource_IsRetriedWithoutOne()
        {

            Server.ConflictOnUsedResource = true;

            var erste  = await ConnectClientAsync("alice");
            var zweite = SingleAttemptClient();

            await zweite.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(zweite.IsConnected, Is.True,
                            "Die zweite Resource muss trotz Konflikt zustande kommen.");
                Assert.That(zweite.FullJid, Is.Not.EqualTo(erste.FullJid));
                Assert.That(zweite.BareJid, Is.EqualTo(erste.BareJid));
            });

        }

        #endregion

        #region ConflictingResource_KeepsBothSessionsUsable()

        /// <summary>
        /// Die neu vergebene Resource muss auch adressierbar sein - sonst wäre
        /// der Client zwar verbunden, aber unter einem JID, den der Server
        /// nicht kennt.
        /// </summary>
        [Test]
        public async Task ConflictingResource_KeepsBothSessionsUsable()
        {

            Server.ConflictOnUsedResource = true;

            await ConnectClientAsync("alice");
            var zweite = SingleAttemptClient();

            await zweite.ConnectAsync();

            await WaitFor(() => Server.SessionOf(zweite.FullJid) is not null,
                          "Serversitzung zur neu vergebenen Resource");

        }

        #endregion

        #region NonConflictRejection_IsNotRetried()

        /// <summary>
        /// Nur ein Konflikt rechtfertigt den zweiten Versuch. Wird das Binding
        /// aus einem anderen Grund abgelehnt, käme derselbe Fehler wieder -
        /// der Client bricht ab, statt es noch einmal zu probieren.
        /// </summary>
        [Test]
        public async Task NonConflictRejection_IsNotRetried()
        {

            Server.FailBind = true;

            var client  = SingleAttemptClient();
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False);
                Assert.That(errors, Is.Not.Empty);
                Assert.That(Server.AllReceived.Count(f => f.Contains("urn:ietf:params:xml:ns:xmpp-bind",
                                                                     StringComparison.Ordinal)),
                            Is.EqualTo(1),
                            "Auf eine Ablehnung ohne Konflikt gehört genau ein Versuch.");
            });

        }

        #endregion

        #region ConfiguredResource_IsRequested()

        /// <summary>
        /// <c>console-&lt;Prozess-ID&gt;</c> war fest verdrahtet - in einer
        /// Bibliothek doppelt unpassend: der Name behauptet eine Konsole, und
        /// zwei Nutzer derselben Bibliothek im selben Prozess bekamen
        /// denselben Wunsch.
        /// </summary>
        [Test]
        public async Task ConfiguredResource_IsRequested()
        {

            var client = SingleAttemptClient();
            client.Connection.Resource = "telefon";

            await client.ConnectAsync();

            Assert.That(client.FullJid, Does.EndWith("/telefon"));

        }

        #endregion

        #region NoResource_LetsTheServerChoose()

        /// <summary>
        /// Ohne Wunsch vergibt der Server (RFC 6120, Abschnitt 7.6). Das ist
        /// derselbe Weg, den der Client nach einem Konflikt geht.
        /// </summary>
        [Test]
        public async Task NoResource_LetsTheServerChoose()
        {

            var client = SingleAttemptClient();
            client.Connection.Resource = null;

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.True);
                Assert.That(client.FullJid, Does.Contain("/"));
            });

        }

        #endregion

        #region UsedResource_IsVariedByDefault()

        /// <summary>
        /// Gegenprobe zum Default: ohne den Schalter vergibt der Server selbst
        /// eine abweichende Resource, und der Client merkt vom Konflikt nichts.
        /// So verhalten sich die verbreiteten Server.
        /// </summary>
        [Test]
        public async Task UsedResource_IsVariedByDefault()
        {

            var erste  = await ConnectClientAsync("alice");
            var zweite = await ConnectClientAsync("alice");

            Assert.Multiple(() =>
            {
                Assert.That(zweite.IsConnected, Is.True);
                Assert.That(zweite.FullJid, Is.Not.EqualTo(erste.FullJid));
            });

        }

        #endregion

    }

}
