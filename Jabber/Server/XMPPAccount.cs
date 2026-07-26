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

        /// <summary>Erwartetes Passwort für die SASL-Authentifizierung.</summary>
        public String Password { get; }

        /// <summary>Momentaufnahme des serverseitigen Rosters.</summary>
        public IReadOnlyList<RosterEntry> Roster
        {
            get { lock (_lock) return _roster.ToList(); }
        }

        #endregion

        #region Constructor(s)

        public XMPPAccount(String bareJid, String password)
        {
            BareJid   = bareJid;
            Password  = password;
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
        }

        /// <summary>
        /// Entfernt einen Roster-Eintrag.
        /// </summary>
        public void RemoveRosterEntry(String jid)
        {
            lock (_lock)
                _roster.RemoveAll(e => String.Equals(e.Jid, jid, StringComparison.OrdinalIgnoreCase));
        }

    }

}
