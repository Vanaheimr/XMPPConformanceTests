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
    /// Der Weg zu anderen Servern - die Stelle, an der ein
    /// Server-zu-Server-Transport eingesetzt wird (RFC 6120, Abschnitt 10.4).
    /// </summary>
    /// <remarks>
    /// Bewusst nur diese eine Methode. Ob dahinter eine bestehende Verbindung
    /// liegt, ob erst eine aufgebaut wird, ob die Gegenstelle sich per
    /// Dialback (XEP-0220) oder SASL-EXTERNAL ausgewiesen hat - all das geht
    /// den Routing-Teil des Servers nichts an. Er will wissen, ob die Stanza
    /// draussen ist.
    ///
    /// Der echte Transport fehlt noch. RFC 6120 sieht für S2S TCP auf Port
    /// 5269 mit <c>jabber:server</c>-Streams vor; RFC 7395 deckt WebSocket
    /// ausdrücklich nur für Client-zu-Server ab. Welcher von beiden es wird,
    /// ist offen - deshalb steht hier eine Schnittstelle und keine
    /// Implementierung.
    /// </remarks>
    public interface IServerLinks
    {

        /// <summary>
        /// Stellt eine Stanza an eine fremde Domain zu.
        /// </summary>
        /// <param name="remoteDomain">Die Domain des Empfängers.</param>
        /// <param name="stanza">Die vollständige Stanza, bereits mit <c>from</c> gestempelt.</param>
        /// <returns>
        /// false, wenn die Domain nicht erreichbar war. Der Aufrufer erzeugt
        /// dann den Stanza-Fehler - hier zu antworten hiesse, den
        /// Fehlerpfad an jeder Implementierung zu wiederholen.
        /// </returns>
        Task<Boolean> DeliverAsync(String             remoteDomain,
                                   String             stanza,
                                   CancellationToken  cancellationToken = default);

    }

}
