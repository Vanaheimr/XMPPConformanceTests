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
    /// Ein Abonnement auf einem PEP-Knoten (XEP-0060, Abschnitt 6.1).
    /// </summary>
    /// <param name="Jid">Der Bare-JID des Abonnenten.</param>
    /// <param name="SubId">
    /// Die Kennung, die dieser Server vergeben hat. Sie unterscheidet zwei
    /// Abonnements desselben JIDs auf denselben Knoten - und benennt seit der
    /// Konfiguration je Abonnement auch, welche Einstellung gemeint ist.
    /// </param>
    /// <param name="Options">Die Einstellungen dieses Abonnements.</param>
    /// <param name="State">
    /// Zugesagt oder erst beantragt (XEP-0060, Abschnitt 12.19).
    /// </param>
    /// <remarks>
    /// <b>Der Zustand stand bis D93 nur auf dem Papier.</b> Ohne
    /// <c>authorize</c> war jedes eingetragene Abonnement ein zugesagtes, und
    /// der Server schrieb <c>subscription='subscribed'</c> als feste
    /// Zeichenkette hin - richtig, solange es nichts anderes gab. Mit dem
    /// Genehmigungsvorgang gibt es etwas anderes, und ein Abonnement, das man
    /// nicht von einem beantragten unterscheiden kann, ist genau die Zusage
    /// ohne Deckung, gegen die diese Reihe schreibt.
    /// </remarks>
    public sealed record PepSubscription(String                     Jid,
                                         String                     SubId,
                                         PubSubSubscriptionOptions  Options,
                                         PubSubSubscriptionState    State = PubSubSubscriptionState.Subscribed);

}
