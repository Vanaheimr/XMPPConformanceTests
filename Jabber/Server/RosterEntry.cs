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
    /// Ein Roster-Eintrag im Testserver.
    /// </summary>
    /// <param name="Jid">Bare-JID des Kontakts.</param>
    /// <param name="Name">Anzeigename oder null.</param>
    /// <param name="Subscription">none, to, from oder both.</param>
    /// <param name="Ask">
    /// <c>subscribe</c>, solange eine gestellte Anfrage noch unbeantwortet ist,
    /// sonst null (RFC 6121, Abschnitt 3.1.2). Der Zustand hängt nicht an
    /// <paramref name="Subscription"/>: eine offene Anfrage lässt die
    /// Subscription bei <c>none</c> stehen.
    /// </param>
    /// <param name="Approved">
    /// Der Kontakt ist im Voraus zugelassen (RFC 6121, Abschnitt 3.4): stellt
    /// er künftig eine Anfrage, beantwortet der Server sie selbst.
    /// </param>
    /// <remarks>
    /// Die Gegenrichtung von <paramref name="Ask"/> - dass <i>gefragt
    /// wurde</i> - steht hier bewusst nicht. RFC 6121 kennt den Zustand
    /// ("None + Pending In"), aber Abschnitt 3.1.3 untersagt im selben Atemzug
    /// einen Roster-Eintrag für einen Antragsteller, dem noch nicht zugestimmt
    /// wurde. Die offene Anfrage liegt deshalb neben dem Roster, in
    /// <see cref="XMPPAccount.PendingSubscriptionRequests"/> - und dort
    /// vollständig, samt erweitertem Inhalt, statt als blosses Ja/Nein.
    /// </remarks>
    /// <param name="Groups">
    /// Die Gruppen, in die der Eigentümer diesen Kontakt gesteckt hat (RFC
    /// 6121, Abschnitt 2.1.2.4).
    /// </param>
    /// <remarks>
    /// <b>Die Gruppen fehlten hier bis D91</b>, und der Kommentar in der
    /// Roster-Behandlung behauptete seit jeher, ein Set ändere „Name und
    /// Gruppen". Gelesen wurden sie nie: Ein Client schickte eine Gruppe, bekam
    /// ein <c>result</c> und im Push denselben Eintrag ohne sie zurück - womit
    /// sie auch bei ihm verschwand, denn ein Push ersetzt die Gruppen eines
    /// Eintrags vollständig.
    /// </remarks>
    public sealed record RosterEntry(String                 Jid,
                                     String?                Name          = null,
                                     String                 Subscription  = "both",
                                     String?                Ask           = null,
                                     Boolean                Approved      = false,
                                     IReadOnlyList<String>? Groups        = null)
    {

        /// <summary>
        /// Die Gruppen, nie null - „keine Gruppe" ist eine leere Liste und
        /// nichts Fehlendes.
        /// </summary>
        public IReadOnlyList<String> Groups { get; init; } = Groups ?? [];

    }

}
