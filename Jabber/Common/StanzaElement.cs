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
/// Der Name des äussersten Elements eines Rahmens.
/// </summary>
/// <remarks>
/// Klingt nach einer Kleinigkeit und ist der Unterschied zwischen einer Weiche,
/// die entscheidet, und einer, die rät. Ein Vergleich mit
/// <c>StartsWith("&lt;iq")</c> trifft auch <c>&lt;iqbogus/&gt;</c>,
/// <c>StartsWith("&lt;presence")</c> auch <c>&lt;presence-probe/&gt;</c> und
/// <c>StartsWith("&lt;open")</c> auch <c>&lt;opencast/&gt;</c>. Der Name endet
/// beim ersten Zeichen, das nicht mehr zu ihm gehört, und genau bis dorthin ist
/// zu lesen.
///
/// Diese Lesung stand schon im Haus — in
/// <c>StreamManagementManager.IsCountableStanza</c>, wo sie beantwortet, ob ein
/// Rahmen für XEP-0198 mitzählt. Dass die Weiche daneben riet, lag nicht an
/// fehlendem Wissen, sondern daran, dass es an der falschen Stelle lag.
/// </remarks>
public static class StanzaElement
{

    /// <summary>
    /// Der Name des äussersten Elements, ohne Namensraum-Präfix — oder
    /// <c>null</c>, wenn der Rahmen mit keinem Element beginnt.
    /// </summary>
    /// <remarks>
    /// Das Präfix fällt weg, weil es den Typ nicht ändert: RFC 6120, Abschnitt
    /// 4.8.1 legt den Namensraum fest und nicht die Abkürzung, unter der er
    /// angesprochen wird. <c>&lt;client:iq/&gt;</c> ist ein <c>iq</c>.
    /// </remarks>
    public static String? NameOf(String xml)
    {

        if (String.IsNullOrEmpty(xml))
            return null;

        var i = 0;

        while (i < xml.Length && Char.IsWhiteSpace(xml[i]))
            i++;

        if (i >= xml.Length || xml[i] != '<')
            return null;

        i++;

        var start = i;

        while (i < xml.Length &&
               (Char.IsLetterOrDigit(xml[i]) || xml[i] == '-' || xml[i] == '_' || xml[i] == ':'))
        {
            i++;
        }

        if (i == start)
            return null;

        var name    = xml[start..i];
        var doppel  = name.LastIndexOf(':');

        return doppel >= 0
                   ? name[(doppel + 1)..]
                   : name;

    }

    /// <summary>
    /// Heisst das äusserste Element so?
    /// </summary>
    public static Boolean Is(String xml, String name)

        => String.Equals(NameOf(xml), name, StringComparison.Ordinal);

    /// <summary>
    /// Ist das eine der drei Stanzas aus RFC 6120, Abschnitt 8.1 —
    /// <c>message</c>, <c>presence</c> oder <c>iq</c>?
    /// </summary>
    public static Boolean IsStanza(String xml)

        => NameOf(xml) is "message" or "presence" or "iq";

}
