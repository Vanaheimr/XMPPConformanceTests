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
    /// Wo ein Server seine Konten und deren Roster aufbewahrt.
    /// </summary>
    /// <remarks>
    /// Absichtlich klein: Laden beim Start, Speichern bei jeder Änderung,
    /// Löschen. Kein Suchen, kein Blättern, keine Abfragesprache - der Server
    /// hält seine Konten ohnehin im Speicher, und alles Weitere wäre erfunden,
    /// bevor jemand es braucht.
    ///
    /// Aufbewahrt wird nie ein Klartextpasswort, sondern nur
    /// <see cref="XMPPCredentials"/>: Salt, Iterationszahl und die
    /// abgeleiteten Schlüssel aus RFC 5802.
    /// </remarks>
    public interface IXMPPAccountStore
    {

        /// <summary>
        /// Liest alle vorhandenen Konten. Wird einmal beim Start gerufen.
        /// </summary>
        IEnumerable<XMPPAccount> Load();

        /// <summary>
        /// Legt ein Konto an oder schreibt seine Änderungen fort - auch
        /// Roster-Änderungen laufen hier durch.
        /// </summary>
        void Save(XMPPAccount account);

        /// <summary>
        /// Entfernt ein Konto. Ein unbekannter JID ist kein Fehler.
        /// </summary>
        void Delete(String bareJid);

    }

}
