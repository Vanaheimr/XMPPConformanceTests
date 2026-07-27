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

using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

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

        private readonly AWebSocketServer _server;
        private readonly WebSocketServerConnection _connection;
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

        /// <summary>
        /// Der laufende SCRAM-Austausch zwischen <c>&lt;auth/&gt;</c> und
        /// <c>&lt;response/&gt;</c>, sonst null.
        /// </summary>
        internal SCRAMExchange? Scram { get; set; }

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

        /// <summary>
        /// Die zuletzt gesendete ungerichtete Presence dieser Resource, bereits
        /// mit dem Full-JID gestempelt - oder null, solange der Client noch
        /// keine geschickt hat.
        /// </summary>
        /// <remarks>
        /// Nach RFC 6121, Abschnitt 4.2.1 ist eine gebundene Resource ohne
        /// gesendete Presence noch nicht "available". Deshalb null und nicht
        /// etwa ein angenommenes <c>&lt;presence/&gt;</c>: auf eine Probe
        /// dieser Resource gibt es dann schlicht nichts zu antworten.
        /// </remarks>
        public String? LastPresence { get; private set; }

        /// <summary>
        /// Hat diese Resource ihre erste ungerichtete Presence geschickt?
        /// Genau daran hängt, wann der Server ihr den Zustand der Kontakte
        /// nachliefert (RFC 6121, Abschnitt 4.3.1).
        /// </summary>
        public Boolean HasSentInitialPresence { get; private set; }

        /// <summary>
        /// Gilt diese Resource den Kontakten gegenüber gerade als verfügbar?
        /// </summary>
        /// <remarks>
        /// Getrennt von <see cref="HasSentInitialPresence"/>, weil beide etwas
        /// anderes beantworten: ob je eine Presence kam, und ob die letzte eine
        /// verfügbare war. Am zweiten hängt, ob der Verbindungsabbau die
        /// Abmeldung noch nachholen muss - hat der Client sie selbst geschickt,
        /// käme sie sonst ein zweites Mal.
        /// </remarks>
        public Boolean IsAvailable { get; private set; }

        /// <summary>
        /// Übernimmt eine ungerichtete Presence des Clients.
        /// </summary>
        /// <returns>War es die erste dieser Sitzung?</returns>
        internal Boolean RecordPresence(String stanza, Boolean available)
        {

            lock (_lock)
            {

                var erste = !HasSentInitialPresence;

                // Eine abgemeldete Resource hat keinen Zustand zu berichten
                // (RFC 6121, Abschnitt 4.2.1). Stand hier die Abmeldung selbst,
                // lieferte der Server sie jedem Kontakt nach, der sich danach
                // anmeldete - und dem gerade abgemeldeten Kontakt gegenüber ein
                // zweites Mal, wenn dessen erste Presence erst nach der
                // Abmeldung verarbeitet wurde.
                LastPresence            = available ? stanza : null;
                HasSentInitialPresence  = true;
                IsAvailable             = available;

                return erste;

            }

        }

        /// <summary>
        /// Schaltet die Sitzung auf abgemeldet und meldet, ob <b>dieser</b>
        /// Aufruf die Umschaltung vorgenommen hat.
        /// </summary>
        /// <remarks>
        /// Die Abmeldung beim Verbindungsende darf genau einmal hinausgehen.
        /// Zuvor stand hier ein Prüfen-dann-Handeln ohne Sperre: fiel die
        /// Verbindung, während der Client seine eigene Abmeldung schickte,
        /// kamen beide Wege am Wächter vorbei und die Kontakte bekamen
        /// dieselbe Abmeldung zweimal. Im vollen Testlauf schlug das etwa in
        /// jedem zweiten Durchgang zu.
        /// </remarks>
        internal Boolean TryMarkUnavailable()
        {

            lock (_lock)
            {

                if (!IsAvailable)
                    return false;

                IsAvailable   = false;
                LastPresence  = null;

                return true;

            }

        }

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

        /// <summary>Die zugrundeliegende WebSocket-Verbindung.</summary>
        public WebSocketServerConnection Connection => _connection;

        /// <summary>Ist die Verbindung noch offen?</summary>
        public Boolean IsOpen => !_connection.IsClosed;

        /// <summary>
        /// Wie oft dieser Client bereits <c>&lt;open/&gt;</c> geschickt hat.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 6.4.6: nach erfolgreicher Authentifizierung
        /// beginnt der Client den Stream neu. Am Zähler hängt, welche Features
        /// der Server anbietet - vor der Anmeldung SASL, danach Binding.
        /// </remarks>
        internal Int32 OpenCount { get; set; }

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

        internal XMPPSession(AWebSocketServer           server,
                             WebSocketServerConnection  connection,
                             Int32                      connectionNumber)
        {
            _server           = server;
            _connection       = connection;
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
        /// RFC 6120, Abschnitt 4.9: Beendet den Stream mit einem Fehler.
        /// </summary>
        /// <param name="condition">Bedingung aus Abschnitt 4.9.3, etwa <c>conflict</c>.</param>
        /// <param name="text">Optionaler erläuternder Text.</param>
        public Task SendStreamErrorAsync(String condition, String? text = null)

            => SendAsync("<stream:error xmlns:stream='http://etherx.jabber.org/streams'>" +
                         $"<{condition} xmlns='urn:ietf:params:xml:ns:xmpp-streams'/>" +
                         (text is not null
                              ? $"<text xmlns='urn:ietf:params:xml:ns:xmpp-streams'>{text}</text>"
                              : "") +
                         "</stream:error>");

        /// <summary>
        /// Sendet eine Stanza an diesen Client.
        /// </summary>
        public async Task SendAsync(String xml)
        {

            await _sendLock.WaitAsync();

            try
            {

                if (_connection.IsClosed)
                    return;

                var status = await _server.SendTextMessage(_connection, xml);

                // Nur ein tatsächlich abgeschickter Frame zählt - sonst meldete
                // der Server dem Client ein h, das dieser nie erreichen kann.
                if (status != SentStatus.Success)
                    return;

                lock (_lock)
                {

                    _sent.Add(xml);

                    // XEP-0198: erst nach dem erfolgreichen Senden zählen.
                    if (StreamManagementEnabled && IsStanza(xml))
                        StanzasSentToClient++;

                }

            }
            catch (Exception)
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
        /// <remarks>
        /// <c>Close</c> ohne Statuscode schickt bewusst kein Close-Frame,
        /// sondern legt nur die TCP-Verbindung nieder - genau das unterscheidet
        /// einen Netzwerkausfall von einer ordentlichen Abmeldung.
        /// </remarks>
        public void Kill()
        {
            try { _connection.Close().GetAwaiter().GetResult(); }
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
