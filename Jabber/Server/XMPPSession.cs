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

using System.Text.RegularExpressions;

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
        private readonly HashSet<String> _directedPresence = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _lock = new();

        /// <summary>
        /// XEP-0198, Abschnitt 5: was an den Client ging und noch nicht
        /// bestätigt ist. Nach einer Wiederaufnahme geht genau das nach.
        /// </summary>
        private readonly Queue<(UInt32 Seq, String Stanza)> _unackedToClient = new();

        private UInt32? _lastAckFromClient;

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
        /// Die Priorität aus der letzten ungerichteten Presence (RFC 6121,
        /// Abschnitt 4.7.2.3) - Vorgabe 0.
        /// </summary>
        /// <remarks>
        /// Sie ist keine Zierde: Nach Abschnitt 8.5.2.1.1 darf der Server an
        /// eine Resource mit <b>negativer</b> Priorität überhaupt keine
        /// Nachricht zustellen. Genau dafür setzt ein Client sie - das Gerät
        /// bleibt erreichbar für gerichtete Nachrichten an seine Full-JID,
        /// bekommt aber nichts mehr ab, was nur an den Bare-JID ging.
        /// </remarks>
        public Int32 PresencePriority { get; private set; }

        /// <summary>
        /// Die Entitäten, denen diese Resource gerichtete Presence geschickt hat
        /// (RFC 6121, Abschnitt 4.6) - nach Bare-JID.
        /// </summary>
        /// <remarks>
        /// Abschnitt 4.6.1 beschreibt genau diese Liste: „keeping a list of the
        /// entities (bare JIDs or full JIDs) to which a user has sent directed
        /// presence during the user's current session for a given resource (full
        /// JID), then clearing the list when the user goes offline".
        ///
        /// Sie hängt deshalb an der Sitzung und nicht am Konto: Gerichtete
        /// Presence ist eine Zusage dieser einen Resource und endet mit ihr.
        ///
        /// Aufgehoben wird der Bare-JID des Empfängers, auch wenn eine Full-JID
        /// angeschrieben wurde. Wer einer Resource seine Anwesenheit zeigt, zeigt
        /// sie einem Menschen, und dessen anderes Gerät weiss es im nächsten
        /// Augenblick ohnehin. Feiner zu unterscheiden hiesse, dieselbe Person je
        /// nach Gerät verschieden zu behandeln - und das Roster, an dem dieselbe
        /// Frage sonst hängt, kennt ebenfalls nur Bare-JIDs.
        /// </remarks>
        public IReadOnlyCollection<String> DirectedPresenceTargets
        {
            get { lock (_lock) return _directedPresence.ToList(); }
        }

        /// <summary>
        /// Darf diese Entität die Presence dieser Resource sehen, weil ihr
        /// gerichtete Presence geschickt wurde?
        /// </summary>
        public Boolean HasDirectedPresenceTo(String bareJid)
        {
            lock (_lock)
                return _directedPresence.Contains(bareJid);
        }

        /// <summary>
        /// Vermerkt eine gerichtete Presence oder nimmt sie zurück
        /// (RFC 6121, Abschnitt 4.6.1).
        /// </summary>
        /// <param name="bareJid">Der Empfänger, ohne Resource.</param>
        /// <param name="available">
        /// true für eine verfügbare Presence, false für <c>unavailable</c>.
        /// </param>
        /// <remarks>
        /// Das Zurücknehmen ist ein MUSS des Abschnitts: „The server MUST remove
        /// from the directed presence list (or its functional equivalent) any
        /// entity to which the user sends directed unavailable presence." Ohne
        /// es bliebe die Zusage stehen, nachdem der Nutzer sie ausdrücklich
        /// widerrufen hat - und daran hängt nach Abschnitt 8.5.3.1, wer ihn
        /// überhaupt etwas fragen darf.
        /// </remarks>
        internal void RecordDirectedPresence(String bareJid, Boolean available)
        {

            lock (_lock)
            {

                if (available)
                    _directedPresence.Add(bareJid);

                else
                    _directedPresence.Remove(bareJid);

            }

        }

        /// <summary>
        /// Gibt die Empfänger gerichteter Presence heraus und leert die Liste -
        /// aufzurufen, wenn diese Resource unverfügbar wird.
        /// </summary>
        /// <remarks>
        /// Zwei Aufgaben in einem Schritt, und das ist Absicht. Abschnitt 4.6.1
        /// verlangt das Leeren beim Abmelden, Abschnitt 4.6.3, Regel 2 verlangt,
        /// die Abmeldung vorher an genau diese Empfänger zu schicken. Wären es
        /// zwei Aufrufe, liesse sich der zweite vergessen - und dann bliebe
        /// entweder die Liste über das Ende der Anwesenheit hinaus stehen oder
        /// die Abmeldung unterwegs. So kommt niemand an die Empfänger, ohne die
        /// Liste zu leeren, und niemand leert sie, ohne sie in der Hand zu
        /// halten.
        ///
        /// Herausgegeben werden Bare-JIDs; ob ein Empfänger noch eine Abmeldung
        /// braucht, entscheidet der Server - wer im Roster mit <c>from</c> oder
        /// <c>both</c> steht, bekommt sie schon über die gewöhnliche Verteilung.
        /// </remarks>
        internal IReadOnlyCollection<String> TakeDirectedPresenceTargets()
        {

            lock (_lock)
            {

                if (_directedPresence.Count == 0)
                    return [];

                var entnommen = _directedPresence.ToList();
                _directedPresence.Clear();

                return entnommen;

            }

        }

        /// <summary>
        /// Übernimmt eine ungerichtete Presence des Clients.
        /// </summary>
        /// <returns>War es die erste dieser Sitzung?</returns>
        /// <summary>
        /// Liest die Priorität aus einer Presence-Stanza.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.7.2.3: eine ganze Zahl von -128 bis +127.
        /// Fehlt sie oder ist sie unbrauchbar, gilt 0 - und nicht etwa ein
        /// Fehler: Die Zahl ist ein Wunsch des Clients, kein Vertrag, und eine
        /// unlesbare darf keine Zustellung verhindern.
        /// </remarks>
        internal static Int32 ReadPriority(String stanza)
        {

            var m = Regex.Match(stanza, @"<priority[^>]*>\s*(-?\d+)\s*</priority>");

            return m.Success && Int32.TryParse(m.Groups[1].Value, out var wert)
                       ? Math.Clamp(wert, -128, 127)
                       : 0;

        }

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
                PresencePriority        = available ? ReadPriority(stanza) : 0;

                // Das Leeren der Liste gerichteter Presence steht nicht hier,
                // sondern in TakeDirectedPresenceTargets: Abschnitt 4.6.3,
                // Regel 2 verlangt, die Abmeldung vorher an genau diese
                // Empfänger zu schicken, und wer hier leerte, nähme dem
                // Aufrufer die Liste weg, die er dafür braucht.
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
        public UInt32? LastAckFromClient
        {
            get { lock (_lock) return _lastAckFromClient; }
            internal set => AcknowledgeToClient(value);
        }

        /// <summary>
        /// XEP-0198, Abschnitt 5: die Kennung, mit der dieser Stream wieder
        /// aufgenommen werden kann - oder null, wenn der Client nicht danach
        /// gefragt hat.
        /// </summary>
        /// <remarks>
        /// Sie ist das einzige Geheimnis, das einen Rückkehrer ausweist: wer
        /// sie kennt, übernimmt den Stream samt Full-JID. Deshalb kommt sie
        /// aus <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
        /// und nicht aus der Verbindungsnummer, wie es die frühere Fassung tat
        /// - dort war sie folgenlos, hier wäre sie ein Einfallstor.
        /// </remarks>
        public String? ResumptionId { get; private set; }

        /// <summary>
        /// XEP-0198, Abschnitt 5: die noch nicht bestätigten Stanzas an den
        /// Client, die nach einer Wiederaufnahme nachzusenden wären.
        /// </summary>
        public Int32 UnacknowledgedToClient
        {
            get { lock (_lock) return _unackedToClient.Count; }
        }

        /// <summary>
        /// Die noch nicht bestätigten Stanzas mit ihrer Sequenznummer, in
        /// Sendereihenfolge.
        /// </summary>
        internal IReadOnlyList<(UInt32 Seq, String Stanza)> PendingToClient
        {
            get { lock (_lock) return [.. _unackedToClient]; }
        }

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
        /// <param name="resumable">
        /// Hat der Client <c>resume='true'</c> verlangt? Dann bekommt die
        /// Sitzung eine Kennung, unter der sie wieder aufgenommen werden kann.
        /// </param>
        internal void EnableStreamManagement(Boolean resumable = false)
        {
            lock (_lock)
            {

                StreamManagementEnabled    = true;
                StanzasSentToClient        = 0;
                StanzasReceivedFromClient  = 0;
                _lastAckFromClient         = null;

                _unackedToClient.Clear();

                // 128 Bit aus dem Zufallsgenerator, Base64 ohne Polsterung -
                // 22 Zeichen. Kürzer wäre ratbar, und was hier zu raten ist,
                // ist eine fremde Sitzung.
                ResumptionId = resumable
                                   ? Convert.ToBase64String(
                                         System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
                                            .TrimEnd('=')
                                   : null;

            }
        }

        /// <summary>
        /// XEP-0198, Abschnitt 5: Übernimmt einen aufgehobenen Stream.
        /// </summary>
        /// <remarks>
        /// Übernommen wird alles, woran der Stream für seine Umgebung hängt:
        /// die Resource - und damit die Full-JID, unter der Kontakte ihn
        /// adressieren -, beide Zähler, die Kennung, der Presence-Zustand und
        /// die Carbons-Einstellung. Was hier vergessen würde, fiele nicht dem
        /// Rückkehrer auf, sondern seinen Gesprächspartnern.
        ///
        /// Der alte Sitzungsobjekt bleibt zurück; seine Verbindung ist tot und
        /// <see cref="IsOpen"/> filtert es aus allem heraus.
        /// </remarks>
        /// <returns>
        /// Die noch offenen Stanzas des alten Streams, damit der Aufrufer sie
        /// nachsenden kann.
        /// </returns>
        internal IReadOnlyList<(UInt32 Seq, String Stanza)> AdoptResumed(XMPPSession vorher)
        {

            var offen = vorher.PendingToClient;

            lock (_lock)
            {

                Resource                   = vorher.Resource;
                CarbonsEnabled             = vorher.CarbonsEnabled;

                StreamManagementEnabled    = true;
                ResumptionId               = vorher.ResumptionId;
                StanzasSentToClient        = vorher.StanzasSentToClient;
                StanzasReceivedFromClient  = vorher.StanzasReceivedFromClient;
                _lastAckFromClient         = vorher.LastAckFromClient;

                _unackedToClient.Clear();
                foreach (var e in offen)
                    _unackedToClient.Enqueue(e);

                // Ohne diese beiden gälte die Resource als frisch gebunden und
                // damit als nicht verfügbar (RFC 6121, 4.2.1) - der Server
                // hätte sie den Kontakten gegenüber nie abgemeldet und würde
                // sie ihnen jetzt trotzdem nicht mehr als anwesend melden.
                HasSentInitialPresence     = vorher.HasSentInitialPresence;
                IsAvailable                = vorher.IsAvailable;
                LastPresence               = vorher.LastPresence;

            }

            vorher.EndResumption();

            return offen;

        }

        /// <summary>
        /// XEP-0198, Abschnitt 5: Nimmt die Zusage der Wiederaufnahme zurück.
        /// </summary>
        /// <remarks>
        /// Nach Ablauf der Frist ist der Stream endgültig zu Ende. Ohne diesen
        /// Schritt sähe die Abmeldung, die jetzt nachzuholen ist, wieder einen
        /// wiederaufnehmbaren Stream vor sich und schöbe sich selbst erneut
        /// auf.
        /// </remarks>
        internal void EndResumption()
        {
            lock (_lock)
            {
                ResumptionId = null;
                _unackedToClient.Clear();
            }
        }

        /// <summary>
        /// XEP-0198: Der Client hat gemeldet, wie viel er empfangen hat -
        /// alles bis dahin darf aus dem Puffer.
        /// </summary>
        internal void AcknowledgeToClient(UInt32? h)
        {

            lock (_lock)
            {

                _lastAckFromClient = h;

                if (h is null)
                    return;

                // Modulo-Arithmetik wie auf der Client-Seite: der Zähler läuft
                // nach 2^32-1 auf 0 über (Abschnitt 4), und ein schlichtes
                // Seq <= h liesse die offenen Stanzas danach für immer liegen.
                while (_unackedToClient.Count > 0 &&
                       unchecked(h.Value - _unackedToClient.Peek().Seq) < 0x8000_0000u)
                    _unackedToClient.Dequeue();

            }

        }

        /// <summary>
        /// XEP-0198, Abschnitt 4: Schaltet Stream Management ein und bestätigt
        /// es in einem Zug.
        /// </summary>
        /// <remarks>
        /// Beides unter der Sperre, die auch das Senden hält. Sonst kann
        /// zwischen dem Nullsetzen der Zähler und dem <c>&lt;enabled/&gt;</c>
        /// eine Stanza hinausgehen: der Server zählt sie, der Client nicht -
        /// denn der setzt seinen Zähler erst beim <c>&lt;enabled/&gt;</c>
        /// zurück -, und die beiden Stände bleiben für den Rest der Sitzung um
        /// genau diese eine auseinander. Danach bestätigt jedes
        /// <c>&lt;a h='…'/&gt;</c> eine Stanza zu wenig, und der Puffer der
        /// unbestätigten läuft nie mehr leer.
        ///
        /// Das Fenster ist schmal und trifft nur, wer <c>&lt;enable/&gt;</c>
        /// nicht in der Aufbauphase schickt - im vollen Testlauf reichte es.
        /// </remarks>
        /// <param name="resumable">Hat der Client <c>resume='true'</c> verlangt?</param>
        /// <param name="antwort">Baut das <c>&lt;enabled/&gt;</c> aus der frisch gesetzten Kennung.</param>
        internal async Task EnableStreamManagementAsync(Boolean                    resumable,
                                                        Func<XMPPSession, String>  antwort)
        {

            await _sendLock.WaitAsync();

            try
            {

                EnableStreamManagement(resumable);

                if (_connection.IsClosed)
                    return;

                var xml = antwort(this);

                if (await _server.SendTextMessage(_connection, xml) == SentStatus.Success)
                    lock (_lock)
                        _sent.Add(xml);

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
        /// XEP-0198: Fordert den Client auf, seinen Empfangszähler zu melden.
        /// Die Antwort landet in <see cref="LastAckFromClient"/>.
        /// </summary>
        public Task RequestAckAsync()
            => SendAsync("<r xmlns='urn:xmpp:sm:3'/>");

        /// <summary>
        /// RFC 6120, Abschnitt 4.9: Beendet den Stream mit einem Fehler - Fehler
        /// schicken, Stream schliessen, Verbindung niederlegen.
        /// </summary>
        /// <param name="condition">Bedingung aus Abschnitt 4.9.3, etwa <c>conflict</c>.</param>
        /// <param name="text">Optionaler erläuternder Text.</param>
        /// <remarks>
        /// Beides in einem Aufruf, weil Abschnitt 4.9.1.1 keine Wahl lässt:
        /// „Stream-level errors are unrecoverable. Therefore, if an error occurs
        /// at the level of the stream, the entity that detects the error MUST
        /// send an &lt;error/&gt; element ... and then <b>immediately close the
        /// stream</b>." Ein Stream, der nach einem Stream-Fehler weiterläuft, ist
        /// keiner mehr: Beide Seiten haben verschiedene Vorstellungen davon, was
        /// noch gilt.
        ///
        /// Es waren bis D23 zwei Methoden - diese ohne Abschluss und ein
        /// <c>FailStreamAsync</c> mit. Die Trennung hatte keinen Aufrufer, der sie
        /// brauchte: Beide Nutzer waren Tests, und beide holten das Schliessen
        /// unmittelbar danach von Hand nach. Eine Wahl, die es nicht gibt, sollte
        /// die Schnittstelle auch nicht anbieten.
        ///
        /// Drei Schritte, und der mittlere ist der, den man über WebSocket
        /// leicht vergisst: Nach RFC 7395, Abschnitt 3.6 steht
        /// <c>&lt;close/&gt;</c> für das <c>&lt;/stream:stream&gt;</c> - ohne es
        /// sieht der Client einen Socket, der ohne Abschied zufällt, und das ist
        /// ein Netzwerkausfall und kein Stream-Fehler.
        ///
        /// <see cref="S2SStream.SendStreamErrorAsync"/> tut dasselbe für einen
        /// Server-zu-Server-Stream und hiess von Anfang an so; dass die
        /// gleichnamige Methode hier etwas anderes tat, war die eigentliche
        /// Falle.
        /// </remarks>
        public async Task SendStreamErrorAsync(String condition, String? text = null)
        {

            await SendAsync("<stream:error xmlns:stream='http://etherx.jabber.org/streams'>" +
                            $"<{condition} xmlns='urn:ietf:params:xml:ns:xmpp-streams'/>" +
                            (text is not null
                                 ? $"<text xmlns='urn:ietf:params:xml:ns:xmpp-streams'>{text}</text>"
                                 : "") +
                            "</stream:error>");

            await SendAsync(WebSocketFraming.Instance.StreamClose());

            Kill();

        }

        /// <summary>
        /// Sendet eine Stanza an diesen Client.
        /// </summary>
        public async Task SendAsync(String xml)
        {

            // RFC 6120, Abschnitt 4.8.1 und RFC 7395, Abschnitt 3.3.3: auf der
            // Client-Verbindung steht jede Stanza in jabber:client, und über
            // WebSocket muss sie ihn selbst tragen - es gibt kein
            // umschliessendes <stream:stream>, von dem sie ihn erben könnte.
            //
            // Zwei Fälle laufen hier zusammen. Was der Server selbst erzeugt,
            // trug bisher gar keinen Namensraum; was von einem fremden Server
            // hereinkam, trug jabber:server und wurde damit unverändert
            // weitergereicht. Beides ist auf diesem Stream falsch, und beides
            // ist nie aufgefallen, weil unser eigener Client Stanzas am
            // lokalen Namen erkennt und den Namensraum gar nicht ansieht -
            // dieselbe Nachsicht, die den umgekehrten Fehler auf der
            // Client-Seite jahrelang verdeckt hat.
            //
            // Hier und nicht an den Aufrufern, aus demselben Grund, aus dem
            // hier auch gezählt wird: das ist die einzige Stelle, durch die
            // jeder Rahmen an einen Client läuft.
            xml = StanzaNamespace.Apply(xml, StanzaNamespace.Client);

            await _sendLock.WaitAsync();

            try
            {

                if (_connection.IsClosed)
                {

                    // XEP-0198, Abschnitt 5: einem aufgehobenen Stream geht
                    // trotzdem etwas zu - er wartet ja gerade auf seinen
                    // Rückkehrer, und dann bekommt er es nachgeliefert.
                    //
                    // Ohne das wäre die Wiederaufnahme fast wertlos: gerettet
                    // würde nur, was in der letzten Zehntelsekunde vor dem
                    // Abriss unterwegs war. Alles, was während der Störung
                    // ankommt - und das ist der Fall, um den es geht -, wäre
                    // verworfen, ohne dass Absender oder Empfänger davon
                    // erführen.
                    if (ResumptionId is not null && IsStanza(xml))
                        lock (_lock)
                        {
                            StanzasSentToClient++;
                            _unackedToClient.Enqueue((StanzasSentToClient, xml));
                        }

                    return;

                }

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
                    {

                        StanzasSentToClient++;

                        // Und aufheben, solange der Client sie nicht bestätigt
                        // hat - nur mit zugesagter Wiederaufnahme, sonst wäre
                        // es ein Puffer, aus dem nie jemand liest.
                        if (ResumptionId is not null)
                            _unackedToClient.Enqueue((StanzasSentToClient, xml));

                    }

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
