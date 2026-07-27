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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Verbindet <see cref="XMPPServer"/>-Instanzen im selben Prozess direkt
    /// miteinander, ohne Netz dazwischen.
    /// </summary>
    /// <remarks>
    /// <b>Kein Ersatz für eine echte S2S-Verbindung.</b> Es gibt keinen
    /// Stream, kein TLS, keinen Dialback und keine Authentifizierung: die
    /// Domain, für die eine Gegenstelle sprechen darf, wird hier schlicht
    /// behauptet. Für den Betrieb ist das nichts.
    ///
    /// Wofür es taugt: Routing, Adressierung und Zustellung über eine
    /// Domain-Grenze hinweg zu prüfen, ohne sich vorher auf einen Transport
    /// festgelegt zu haben. Die Absenderprüfung im Eingang von
    /// <see cref="XMPPServer.ReceiveFromRemoteAsync"/> ist deshalb trotzdem
    /// scharf - sie ist genau das, worauf ein echter Transport nach dem
    /// Dialback baut.
    /// </remarks>
    public sealed class DirectServerLinks : IServerLinks
    {

        #region Data

        private readonly XMPPServer _localServer;
        private readonly Dictionary<String, XMPPServer> _peers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _lock = new();

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt die Gegenstellenliste für einen Server an.
        /// </summary>
        public DirectServerLinks(XMPPServer localServer)
        {
            _localServer = localServer;
        }

        #endregion


        #region AddPeer(peer)

        /// <summary>
        /// Macht einen weiteren Server erreichbar - in dieser Richtung.
        /// </summary>
        public void AddPeer(XMPPServer peer)
        {
            lock (_lock)
                _peers[peer.Domain] = peer;
        }

        #endregion

        #region (static) Connect(a, b)

        /// <summary>
        /// Verbindet zwei Server in beide Richtungen und hängt die Links an
        /// ihre <see cref="XMPPServer.ServerLinks"/>.
        /// </summary>
        /// <remarks>
        /// Beide Richtungen, weil eine einseitige Verbindung eine Falle wäre:
        /// die Nachricht käme an, die Antwort nicht, und der Fehler sähe aus
        /// wie ein Zustellproblem statt wie eine halbe Verkabelung.
        /// </remarks>
        public static void Connect(XMPPServer a, XMPPServer b)
        {

            if (String.Equals(a.Domain, b.Domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                          $"Beide Server bedienen '{a.Domain}' - eine Föderation mit sich selbst ergibt nichts.",
                          nameof(b));

            LinksOf(a).AddPeer(b);
            LinksOf(b).AddPeer(a);

        }

        /// <summary>
        /// Die Gegenstellenliste eines Servers, angelegt falls nötig.
        /// </summary>
        private static DirectServerLinks LinksOf(XMPPServer server)
        {

            if (server.ServerLinks is DirectServerLinks vorhanden)
                return vorhanden;

            var links = new DirectServerLinks(server);
            server.ServerLinks = links;

            return links;

        }

        #endregion

        #region DeliverAsync(remoteDomain, stanza, cancellationToken)

        public Task<Boolean> DeliverAsync(String             remoteDomain,
                                          String             stanza,
                                          CancellationToken  cancellationToken = default)
        {

            XMPPServer? peer;

            lock (_lock)
                _peers.TryGetValue(remoteDomain, out peer);

            return peer is null
                       ? Task.FromResult(false)
                       : peer.ReceiveFromRemoteAsync(_localServer.Domain, stanza);

        }

        #endregion

    }

}
