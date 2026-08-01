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
    /// Ob sich zu einer Anfrage das gemeinte Abonnement finden liess.
    /// </summary>
    /// <remarks>
    /// Dieselbe Frage stellt sich beim Abbestellen (XEP-0060, Abschnitt 6.2)
    /// und beim Einstellen (Abschnitt 6.3), und sie wird beide Male gleich
    /// beantwortet. <b>Der Fehler dazu ist es nicht:</b> Fehlt bei mehreren
    /// die Kennung, verlangt das XEP beim Abbestellen ein
    /// <c>&lt;bad-request/&gt;</c> und beim Einstellen ein
    /// <c>&lt;not-acceptable/&gt;</c>. Das ist keine Willkür - dort ist die
    /// Anfrage unvollständig, hier ist sie in Ordnung und nur in dieser Lage
    /// nicht zu beantworten.
    ///
    /// Deshalb steht hier der Befund und nicht die Antwort. Wer beide Stellen
    /// dieselbe Fehlermeldung bauen liesse, hätte eine von beiden nicht
    /// gelesen.
    /// </remarks>
    public enum PepSubscriptionResult
    {

        /// <summary>
        /// Gefunden - und bei einer Änderung auch ausgeführt.
        /// </summary>
        Ok,

        /// <summary>
        /// Dieser JID hat auf diesen Knoten kein Abonnement -
        /// <c>&lt;unexpected-request/&gt;</c> mit
        /// <c>&lt;not-subscribed/&gt;</c>.
        /// </summary>
        NotSubscribed,

        /// <summary>
        /// Die mitgeschickte <c>subid</c> gehört zu keinem seiner Abonnements -
        /// <c>&lt;not-acceptable/&gt;</c> mit <c>&lt;invalid-subid/&gt;</c>.
        /// </summary>
        WrongSubId,

        /// <summary>
        /// Es gibt mehrere, und keine Kennung sagt, welches gemeint ist.
        /// </summary>
        /// <remarks>
        /// Sich eines auszusuchen wäre die bequeme Antwort und die falsche:
        /// Der Dienst träfe vielleicht das andere und bestätigte dem Absender,
        /// es sei seines gewesen.
        /// </remarks>
        SubIdRequired

    }

}
