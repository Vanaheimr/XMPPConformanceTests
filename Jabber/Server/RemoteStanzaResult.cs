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
    /// Was mit einer Stanza geschah, die von einem anderen Server kam.
    /// </summary>
    /// <remarks>
    /// Ein blosses "abgelehnt" reichte nicht mehr, sobald es einen Stream gibt:
    /// die Ablehnungen sind unterschiedlich schwer. Ein falsches
    /// <c>from</c> ist ein Angriff auf die Adressierung und beendet nach
    /// RFC 6120, Abschnitt 8.1.1.1 den ganzen Stream; ein Empfänger auf einer
    /// dritten Domain ist dagegen nur eine Stanza, die hier nichts zu suchen
    /// hat.
    /// </remarks>
    public enum RemoteStanzaResult
    {

        /// <summary>Angenommen und lokal zugestellt.</summary>
        Accepted,

        /// <summary><c>from</c> oder <c>to</c> fehlt - ohne beides ist sie nicht zustellbar.</summary>
        MissingAddress,

        /// <summary>
        /// Die Gegenstelle spricht für eine Domain, die ihr nicht gehört.
        /// </summary>
        ForeignSender,

        /// <summary>
        /// Der Empfänger liegt nicht auf dieser Domain - Weiterleiten für
        /// Dritte wäre ein offenes Relais.
        /// </summary>
        ForeignRecipient,

        /// <summary>
        /// Das Routing ist abgeschaltet (Testschalter), die Stanza wurde
        /// deshalb nicht zugestellt.
        /// </summary>
        RoutingDisabled

    }

}
