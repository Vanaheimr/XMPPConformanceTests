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
    /// Die beiden Schlüssel, die ein Server je Konto und SCRAM-Mechanismus
    /// aufbewahrt (RFC 5802, Abschnitt 3).
    /// </summary>
    /// <remarks>
    /// Bewusst nicht das Passwort und auch nicht der <c>ClientKey</c>: aus
    /// <see cref="StoredKey"/> lässt sich der <c>ClientKey</c> nicht
    /// zurückrechnen, wohl aber prüfen, ob der Client ihn kennt. Wer die
    /// Datenbank eines Servers erbeutet, kann sich damit also nicht ohne
    /// weiteres als der Nutzer anmelden - genau das ist der Sinn der
    /// Konstruktion.
    ///
    /// Der <see cref="ServerKey"/> muss dagegen aufbewahrt werden, weil der
    /// Server dem Client mit ihm beweist, dass er das Passwort ebenfalls
    /// kennt (Abschnitt 5, <c>ServerSignature</c>).
    /// </remarks>
    /// <param name="StoredKey">H(HMAC(SaltedPassword, "Client Key")).</param>
    /// <param name="ServerKey">HMAC(SaltedPassword, "Server Key").</param>
    public sealed record SCRAMKeys(Byte[] StoredKey,
                                   Byte[] ServerKey);

}
