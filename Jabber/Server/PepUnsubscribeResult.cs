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
    /// Was aus einem <c>&lt;unsubscribe/&gt;</c> wurde (XEP-0060,
    /// Abschnitt 6.2.3).
    /// </summary>
    /// <remarks>
    /// Ein <see cref="Boolean"/> täte es nicht: XEP-0060 unterscheidet die
    /// beiden Fehlschläge ausdrücklich, und sie sagen Verschiedenes. „Nicht
    /// abonniert" heisst, der Absender hat sich geirrt; „falsche Kennung"
    /// heisst, er meint ein Abonnement, das es gibt - nur nicht seines.
    /// </remarks>
    public enum PepUnsubscribeResult
    {

        /// <summary>Das Abonnement ist beendet.</summary>
        Removed,

        /// <summary>
        /// Es gab keines - <c>&lt;unexpected-request/&gt;</c> mit
        /// <c>&lt;not-subscribed/&gt;</c>.
        /// </summary>
        NotSubscribed,

        /// <summary>
        /// Die mitgeschickte <c>subid</c> gehört nicht zu diesem Abonnement -
        /// <c>&lt;not-acceptable/&gt;</c> mit <c>&lt;invalid-subid/&gt;</c>.
        /// </summary>
        WrongSubId,

        /// <summary>
        /// Es gibt mehrere, und keine Kennung sagt, welches gemeint ist -
        /// <c>&lt;bad-request/&gt;</c> mit <c>&lt;subid-required/&gt;</c>
        /// (XEP-0060, Abschnitt 6.2.3.1).
        /// </summary>
        /// <remarks>
        /// Sich eines auszusuchen wäre die bequeme Antwort und die falsche:
        /// Der Dienst beendete vielleicht das andere und bestätigte dem
        /// Absender, es sei seines gewesen.
        /// </remarks>
        SubIdRequired

    }

}
