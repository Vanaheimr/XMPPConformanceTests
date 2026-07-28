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
    /// Ein abgerissener Stream, der auf seinen Rückkehrer wartet (XEP-0198,
    /// Abschnitt 5).
    /// </summary>
    /// <remarks>
    /// Aufgehoben wird die Sitzung selbst und nicht eine Abschrift ihrer
    /// Werte: an ihr hängen Full-JID, Konto, Zähler, der Puffer der noch nicht
    /// bestätigten Stanzas und der Presence-Zustand. Eine Abschrift müsste
    /// jedes davon einzeln nachführen, und was dabei vergessen würde, fiele
    /// erst dem Rückkehrer auf.
    ///
    /// Ihre Verbindung ist tot; gesendet wird über sie nichts mehr
    /// (<c>SendAsync</c> bricht bei geschlossener Verbindung ab). Sie ist
    /// hier reiner Zustandsträger, bis jemand ihn übernimmt oder die Frist
    /// abläuft.
    /// </remarks>
    /// <param name="Session">Die abgerissene Sitzung samt ihrem Zustand.</param>
    /// <param name="Deadline">Wann die Zusage verfällt.</param>
    internal sealed record ParkedStream(XMPPSession     Session,
                                        DateTimeOffset  Deadline);

}
