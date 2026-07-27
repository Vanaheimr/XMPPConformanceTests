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
    /// <param name="PendingIn">
    /// Von diesem Kontakt liegt eine unbeantwortete Anfrage vor.
    /// </param>
    /// <remarks>
    /// <paramref name="Ask"/> und <paramref name="PendingIn"/> sind die beiden
    /// Richtungen derselben Frage und werden getrennt geführt: die eine hält
    /// fest, dass <i>wir</i> gefragt haben, die andere, dass <i>gefragt
    /// wurde</i>. RFC 6121 nennt die Kombinationen ausgeschrieben ("None +
    /// Pending Out+In"); ohne beide Angaben liesse sich Abschnitt 3.4 gar nicht
    /// umsetzen, weil dort die Behandlung eines <c>subscribed</c> davon
    /// abhängt, ob eine Anfrage offen ist.
    /// </remarks>
    public sealed record RosterEntry(String   Jid,
                                         String?  Name          = null,
                                         String   Subscription  = "both",
                                         String?  Ask           = null,
                                         Boolean  Approved      = false,
                                         Boolean  PendingIn     = false);

}
