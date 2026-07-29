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

using System.Security.Cryptography;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Ein Konto auf dem Testserver: Zugangsdaten und serverseitiger Roster.
    /// </summary>
    public sealed class XMPPAccount
    {

        #region Data

        private readonly List<RosterEntry> _roster = [];
        private readonly Dictionary<String, String> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _lock = new();

        #endregion

        #region Properties

        /// <summary>Bare-JID des Kontos, z.B. alice@localhost.</summary>
        public String BareJid { get; }

        /// <summary>
        /// Die Zugangsdaten für die SASL-Authentifizierung - abgeleitet, nicht
        /// im Klartext.
        /// </summary>
        public XMPPCredentials Credentials { get; }

        /// <summary>Momentaufnahme des serverseitigen Rosters.</summary>
        public IReadOnlyList<RosterEntry> Roster
        {
            get { lock (_lock) return _roster.ToList(); }
        }

        /// <summary>
        /// RFC 6121, Abschnitt 2.6: Die Fassung des Rosters - eine
        /// undurchsichtige Zeichenkette, die sich mit jeder Änderung ändert.
        /// </summary>
        /// <remarks>
        /// Gerechnet statt gezählt. Ein Zähler wäre die naheliegende Wahl,
        /// müsste aber mit dem Konto gespeichert werden und überstünde einen
        /// Neustart nur, wenn jemand daran denkt. Ein Streuwert über den Inhalt
        /// braucht keinen Speicher, ist nach einem Neustart derselbe und bleibt
        /// auch dann richtig, wenn jemand den Roster an der Datei vorbei
        /// ändert.
        ///
        /// Er hat eine Eigenschaft, die ein Zähler nicht hat: Geht der Roster
        /// von A nach B und wieder zurück nach A, ist die Fassung wieder die
        /// alte. Das ist kein Mangel, sondern richtig - der Zwischenstand eines
        /// Clients, der A zwischengespeichert hat, stimmt ja wieder.
        ///
        /// Die Trennzeichen sind Steuerzeichen, die in keinem Feld vorkommen
        /// können. Ohne sie ergäben ein Kontakt „ab" ohne Namen und ein Kontakt
        /// „a" mit dem Namen „b" dieselbe Zeichenfolge.
        /// </remarks>
        public String RosterVersion
        {
            get
            {

                var sb = new StringBuilder();

                foreach (var e in Roster.OrderBy(e => e.Jid, StringComparer.Ordinal))
                    sb.Append(e.Jid).         Append('\u001F').
                       Append(e.Name).        Append('\u001F').
                       Append(e.Subscription).Append('\u001F').
                       Append(e.Ask).         Append('\u001F').
                       Append(e.Approved).    Append('\u001E');

                return Convert.ToBase64String(
                           SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))
                       )[..16];

            }
        }

        /// <summary>
        /// Unbeantwortete Subscription-Anfragen, nach dem Bare-JID des
        /// Antragstellers (RFC 6121, Abschnitt 3.1.3, Regel 4).
        /// </summary>
        /// <remarks>
        /// Aufbewahrt wird die vollständige Stanza und nicht bloss der
        /// Absender: der Abschnitt verlangt das ausdrücklich, weil eine
        /// Anfrage erweiterten Inhalt tragen darf - vor allem das
        /// <c>&lt;status/&gt;</c>, mit dem ein Mensch begründet, warum er
        /// fragt. Wer sich nur den Absender merkt, stellt beim nächsten
        /// Anmelden eine andere Anfrage zu als die, die gestellt wurde.
        ///
        /// Neben dem Roster und nicht darin: die Security Warning desselben
        /// Abschnitts untersagt einen Roster-Eintrag, solange nicht
        /// zugestimmt wurde.
        /// </remarks>
        public IReadOnlyDictionary<String, String> PendingSubscriptionRequests
        {
            get
            {
                lock (_lock)
                    return new Dictionary<String, String>(_pendingRequests, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Wird nach jeder Roster-Änderung gerufen; der Server hängt daran
        /// seinen Kontenspeicher.
        /// </summary>
        /// <remarks>
        /// Hier und nicht an den Aufrufstellen im Server: der Roster lässt
        /// sich auch direkt am Konto ändern - Testhilfen tun genau das -, und
        /// eine Liste von Stellen, an denen man das Speichern nicht vergessen
        /// darf, wird über kurz oder lang unvollständig.
        /// </remarks>
        internal Action<XMPPAccount>? OnChanged { get; set; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt ein Konto mit einem Klartextpasswort an, das sofort abgeleitet
        /// und danach verworfen wird.
        /// </summary>
        public XMPPAccount(String bareJid, String password)
            : this(bareJid, XMPPCredentials.FromPassword(password))
        { }

        /// <summary>
        /// Legt ein Konto mit bereits abgeleiteten Zugangsdaten an - der Weg,
        /// auf dem ein Kontenspeicher sie wieder einliest.
        /// </summary>
        public XMPPAccount(String bareJid, XMPPCredentials credentials)
        {
            BareJid      = bareJid;
            Credentials  = credentials;
        }

        #endregion


        /// <summary>
        /// Legt einen Roster-Eintrag an oder aktualisiert ihn.
        /// </summary>
        public void SetRosterEntry(RosterEntry entry)
        {

            lock (_lock)
            {
                _roster.RemoveAll(e => String.Equals(e.Jid, entry.Jid, StringComparison.OrdinalIgnoreCase));
                _roster.Add(entry);
            }

            // Ausserhalb der Sperre: der Speicher schreibt womöglich eine
            // Datei, und darauf soll niemand warten, der nur den Roster lesen
            // will.
            OnChanged?.Invoke(this);

        }

        /// <summary>
        /// Entfernt einen Roster-Eintrag.
        /// </summary>
        public void RemoveRosterEntry(String jid)
        {

            lock (_lock)
                _roster.RemoveAll(e => String.Equals(e.Jid, jid, StringComparison.OrdinalIgnoreCase));

            OnChanged?.Invoke(this);

        }

        /// <summary>
        /// Bewahrt eine Subscription-Anfrage auf, bis sie beantwortet ist.
        /// </summary>
        /// <param name="maxStored">
        /// Obergrenze für die Zahl aufbewahrter Anfragen. Ist sie erreicht,
        /// wird die neue verworfen statt eine bereits aufbewahrte zu
        /// verdrängen - sonst könnte ein Angreifer die echte Anfrage eines
        /// Bekannten gezielt hinausdrängen.
        /// </param>
        /// <returns>
        /// false, wenn nichts aufbewahrt wurde: entweder liegt von diesem
        /// Absender bereits eine Anfrage vor, oder die Grenze ist erreicht.
        /// In beiden Fällen soll auch nichts zugestellt werden.
        /// </returns>
        /// <remarks>
        /// Abschnitt 3.1.3 stellt frei, ob die erste oder die letzte Anfrage
        /// eines Absenders aufbewahrt wird, verlangt aber, dass es genau eine
        /// bleibt ("this helps to prevent 'subscription request spam'"). Hier
        /// bleibt die erste stehen: sonst bestimmte derjenige, der zuletzt
        /// fragt, den Inhalt dessen, was der Kontakt beim nächsten Anmelden zu
        /// sehen bekommt, und könnte ihn beliebig oft austauschen.
        /// </remarks>
        public Boolean RememberSubscriptionRequest(String   fromBareJid,
                                                   String   stanza,
                                                   Int32    maxStored = Int32.MaxValue)
        {

            lock (_lock)
            {

                if (_pendingRequests.ContainsKey(fromBareJid) ||
                    _pendingRequests.Count >= maxStored)
                {
                    return false;
                }

                _pendingRequests[fromBareJid] = stanza;

            }

            OnChanged?.Invoke(this);

            return true;

        }

        /// <summary>
        /// Vergisst eine aufbewahrte Anfrage - sie ist beantwortet.
        /// </summary>
        /// <returns>true, wenn eine vorlag.</returns>
        public Boolean ForgetSubscriptionRequest(String fromBareJid)
        {

            Boolean entfernt;

            lock (_lock)
                entfernt = _pendingRequests.Remove(fromBareJid);

            if (entfernt)
                OnChanged?.Invoke(this);

            return entfernt;

        }

        /// <summary>
        /// Liegt von diesem Kontakt eine unbeantwortete Anfrage vor?
        /// </summary>
        /// <remarks>
        /// Die Frage, an der nach Abschnitt 3.4.2 hängt, ob ein
        /// <c>&lt;presence type='subscribed'/&gt;</c> eine Zustimmung ist oder
        /// eine Vormerkung.
        /// </remarks>
        public Boolean HasPendingRequestFrom(String bareJid)
        {
            lock (_lock)
                return _pendingRequests.ContainsKey(bareJid);
        }

        /// <summary>
        /// Darf dieser Kontakt die Presence dieses Kontos sehen?
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.2.2: das ist genau bei <c>from</c> und
        /// <c>both</c> der Fall. Die Richtung ist leicht zu verwechseln - ein
        /// <c>to</c> heisst, dass <b>dieses Konto</b> die Presence des
        /// Kontakts sieht, und gäbe die eigene an genau die falsche Hälfte des
        /// Rosters.
        /// </remarks>
        public Boolean IsPresenceSubscriber(String bareJid)
            => SubscriptionOf(bareJid) is "from" or "both";

        /// <summary>
        /// Bekommt dieses Konto die Presence des Kontakts - also <c>to</c> oder
        /// <c>both</c>?
        /// </summary>
        public Boolean ReceivesPresenceFrom(String bareJid)
            => SubscriptionOf(bareJid) is "to" or "both";

        /// <summary>
        /// Der Subscription-Zustand zu diesem Kontakt, oder null, wenn er nicht
        /// im Roster steht.
        /// </summary>
        public String? SubscriptionOf(String bareJid)
        {
            lock (_lock)
                return _roster.FirstOrDefault(e => String.Equals(e.Jid, bareJid, StringComparison.OrdinalIgnoreCase))
                             ?.Subscription;
        }

    }

}
