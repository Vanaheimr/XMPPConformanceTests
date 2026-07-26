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

using System.Net.WebSockets;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Eine einzelne Client-Verbindung auf dem Testserver - nach dem Resource
    /// Binding entspricht sie genau einer Resource eines Kontos.
    /// </summary>
    public sealed class XMPPSession
    {

        #region Data

        private readonly WebSocket _webSocket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly List<String> _received = [];
        private readonly List<String> _sent = [];
        private readonly Lock _lock = new();

        #endregion

        #region Properties

        /// <summary>Laufende Nummer der Verbindung, in Reihenfolge des Verbindungsaufbaus.</summary>
        public Int32 ConnectionNumber { get; }

        /// <summary>Konto, sobald die Authentifizierung erfolgreich war.</summary>
        public XMPPAccount? Account { get; internal set; }

        /// <summary>Zugewiesene Resource, sobald das Binding erfolgt ist.</summary>
        public String? Resource { get; internal set; }

        /// <summary>Bare-JID oder null vor der Authentifizierung.</summary>
        public String? BareJid => Account?.BareJid;

        /// <summary>Full-JID oder null vor dem Binding.</summary>
        public String? FullJid => Account is not null && Resource is not null
                                      ? $"{Account.BareJid}/{Resource}"
                                      : null;

        /// <summary>XEP-0280: Hat der Client Carbons für diese Resource aktiviert?</summary>
        public Boolean CarbonsEnabled { get; internal set; }

        /// <summary>XEP-0198: Ist Stream Management für diese Sitzung ausgehandelt?</summary>
        public Boolean StreamManagementEnabled { get; private set; }

        /// <summary>
        /// XEP-0198: Anzahl zählbarer Stanzas, die der Server seit
        /// <c>&lt;enabled/&gt;</c> an den Client geschickt hat. Genau diesen
        /// Wert muss der Client in seinem <c>&lt;a h='...'/&gt;</c> melden.
        /// </summary>
        public UInt32 StanzasSentToClient { get; private set; }

        /// <summary>
        /// XEP-0198: Anzahl zählbarer Stanzas, die der Server seit
        /// <c>&lt;enabled/&gt;</c> vom Client empfangen hat. Genau diesen Wert
        /// muss der Client als eigenen Ausgangszähler führen.
        /// </summary>
        public UInt32 StanzasReceivedFromClient { get; private set; }

        /// <summary>
        /// XEP-0198: das zuletzt vom Client gemeldete <c>h</c>, oder null,
        /// solange der Client noch kein <c>&lt;a/&gt;</c> geschickt hat.
        /// </summary>
        public UInt32? LastAckFromClient { get; internal set; }

        /// <summary>Ist die Verbindung noch offen?</summary>
        public Boolean IsOpen => _webSocket.State == WebSocketState.Open;

        /// <summary>Alle vom Client empfangenen Frames, in Eingangsreihenfolge.</summary>
        public IReadOnlyList<String> Received
        {
            get { lock (_lock) return _received.ToList(); }
        }

        /// <summary>Alle an den Client gesendeten Frames, in Sendereihenfolge.</summary>
        public IReadOnlyList<String> Sent
        {
            get { lock (_lock) return _sent.ToList(); }
        }

        #endregion

        #region Constructor(s)

        internal XMPPSession(WebSocket webSocket, Int32 connectionNumber)
        {
            _webSocket        = webSocket;
            ConnectionNumber  = connectionNumber;
        }

        #endregion


        internal void RecordReceived(String frame)
        {

            lock (_lock)
            {

                _received.Add(frame);

                if (StreamManagementEnabled && IsStanza(frame))
                    StanzasReceivedFromClient++;

            }

        }

        /// <summary>
        /// XEP-0198: Zählt nur message, presence und iq - Nonzas wie
        /// <c>&lt;r/&gt;</c> oder <c>&lt;a/&gt;</c> nicht.
        ///
        /// Bewusst unabhängig vom Client implementiert: würde der Testserver
        /// dieselbe Hilfsfunktion benutzen, prüften die Tests beide Seiten mit
        /// derselben Logik und ein gemeinsamer Denkfehler bliebe unentdeckt.
        /// </summary>
        internal static Boolean IsStanza(String xml)
            => xml.StartsWith("<message",  StringComparison.Ordinal) ||
               xml.StartsWith("<presence", StringComparison.Ordinal) ||
               xml.StartsWith("<iq",       StringComparison.Ordinal);

        /// <summary>
        /// XEP-0198: Handelt Stream Management aus und setzt beide Zähler auf
        /// null, wie es Abschnitt 4 für <c>&lt;enabled/&gt;</c> verlangt.
        /// </summary>
        internal void EnableStreamManagement()
        {
            lock (_lock)
            {
                StreamManagementEnabled    = true;
                StanzasSentToClient        = 0;
                StanzasReceivedFromClient  = 0;
                LastAckFromClient          = null;
            }
        }

        /// <summary>
        /// XEP-0198: Fordert den Client auf, seinen Empfangszähler zu melden.
        /// Die Antwort landet in <see cref="LastAckFromClient"/>.
        /// </summary>
        public Task RequestAckAsync()
            => SendAsync("<r xmlns='urn:xmpp:sm:3'/>");

        /// <summary>
        /// Sendet eine Stanza an diesen Client.
        /// </summary>
        public async Task SendAsync(String xml)
        {

            await _sendLock.WaitAsync();

            try
            {
                if (_webSocket.State != WebSocketState.Open)
                    return;

                await _webSocket.SendAsync(Encoding.UTF8.GetBytes(xml),
                                           WebSocketMessageType.Text,
                                           true,
                                           CancellationToken.None);

                lock (_lock)
                {

                    _sent.Add(xml);

                    // XEP-0198: erst nach dem erfolgreichen Senden zählen.
                    if (StreamManagementEnabled && IsStanza(xml))
                        StanzasSentToClient++;

                }
            }
            catch (WebSocketException)
            {
                // Verbindung wurde zwischenzeitlich abgerissen
            }
            finally
            {
                _sendLock.Release();
            }

        }

        /// <summary>
        /// Reisst die Verbindung ohne Close-Handshake ab - simuliert einen
        /// Netzwerkausfall und löst beim Client einen Reconnect aus.
        /// </summary>
        public void Kill()
        {
            try { _webSocket.Abort(); }
            catch { /* egal */ }
        }

        /// <summary>
        /// Zählt empfangene Frames, die den angegebenen Text enthalten.
        /// </summary>
        public Int32 CountReceived(String contains)
            => Received.Count(f => f.Contains(contains, StringComparison.Ordinal));

        public override String ToString()
            => FullJid ?? BareJid ?? $"(Verbindung {ConnectionNumber}, nicht angemeldet)";

    }

}
