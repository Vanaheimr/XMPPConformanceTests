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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die Namen der Rollen auf dem Draht (XEP-0060, Abschnitt 12.16).
/// </summary>
public static class PubSubAffiliations
{

    /// <summary>Die Rolle, wie sie im Protokoll heisst.</summary>
    public static String NameOf(PubSubAffiliation affiliation)
        => affiliation switch {
               PubSubAffiliation.Owner      => "owner",
               PubSubAffiliation.Publisher  => "publisher",
               PubSubAffiliation.Member     => "member",
               PubSubAffiliation.Outcast    => "outcast",
               _                            => "none"
           };

    /// <summary>
    /// Liest eine Rolle.
    /// </summary>
    /// <returns>
    /// false bei allem, was dieser Dienst nicht kennt - auch bei
    /// <c>publish-only</c>. <b>Eine unbekannte Rolle als „keine" zu lesen wäre
    /// hier besonders teuer:</b> Wer jemanden ausschliessen will und sich
    /// vertippt, bekäme sonst ein <c>result</c> und hielte den Ausschluss für
    /// vollzogen.
    /// </returns>
    public static Boolean TryRead(String? name, out PubSubAffiliation affiliation)
    {

        switch (name)
        {

            case "owner":      affiliation = PubSubAffiliation.Owner;      return true;
            case "publisher":  affiliation = PubSubAffiliation.Publisher;  return true;
            case "member":     affiliation = PubSubAffiliation.Member;     return true;
            case "outcast":    affiliation = PubSubAffiliation.Outcast;    return true;
            case "none":       affiliation = PubSubAffiliation.None;       return true;

            default:           affiliation = PubSubAffiliation.None;       return false;

        }

    }

}
