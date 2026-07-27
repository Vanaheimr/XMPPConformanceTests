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
    /// Ein Konto auf dem Testserver: Zugangsdaten und serverseitiger Roster.
    /// </summary>
    public sealed class XMPPAccount
    {

        #region Data

        private readonly List<RosterEntry> _roster = [];
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
