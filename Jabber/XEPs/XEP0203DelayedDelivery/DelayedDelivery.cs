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

#region Usings

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0203: Der Vermerk, dass eine Stanza <b>nicht jetzt</b> entstanden ist.
/// </summary>
/// <remarks>
/// Der Server setzt ihn, wenn er etwas nachreicht, das er aufgehoben hatte -
/// eine Nachricht aus der Offline-Ablage etwa. Ohne ihn wäre die Ablage nicht
/// zu erkennen: Was beim Anmelden hereinkommt, sähe aus wie eben geschrieben.
///
/// <b>Dieser Server schreibt den Stempel seit jeher und hat ihn selbst nie
/// gelesen.</b> Die Folge war eine Lüge mit Uhrzeit: Eine Nachricht von
/// gestern Abend erschien nach dem Anmelden mit der Uhrzeit von jetzt. Der
/// Absender hatte gesagt, wann sie wirklich geschrieben wurde; der Empfänger
/// hat nicht zugehört (siehe D59).
/// </remarks>
public static class DelayedDelivery
{

    /// <summary>Der Namespace von XEP-0203.</summary>
    public const string Namespace = "urn:xmpp:delay";

    /// <summary>
    /// Liest den Stempel einer Stanza.
    /// </summary>
    /// <param name="stanza">Die Stanza.</param>
    /// <param name="stamp">Wann sie entstanden ist.</param>
    /// <param name="by">
    /// Wer sie aufgehoben hat - der Server, ein Raum. Freiwillig nach
    /// Abschnitt 4.
    /// </param>
    /// <returns>false, wenn sie keinen trägt oder er unlesbar ist.</returns>
    /// <remarks>
    /// <b>Nur direkte Kinder.</b> Ein Carbon (XEP-0280) oder eine
    /// weitergeleitete Nachricht (XEP-0297) bringt in ihrem
    /// <c>&lt;forwarded/&gt;</c> einen eigenen Stempel mit - den der
    /// <i>inneren</i> Nachricht. Wer die ganze Stanza durchsucht, datiert die
    /// äussere auf die Zeit der inneren und liegt genau dann falsch, wenn es
    /// darauf ankommt.
    ///
    /// Ein unlesbarer Stempel gilt wie keiner. Er kommt von der Gegenstelle,
    /// und was von dort kommt, darf hier nichts umwerfen; die Nachricht ist
    /// dann eben so alt, wie sie angekommen ist.
    /// </remarks>
    public static bool TryRead(XElement stanza, out DateTimeOffset stamp, out string? by)
    {

        stamp  = default;
        by     = null;

        var delay = stanza.Child(Namespace, "delay");

        if (delay is null)
            return false;

        var wert = delay.Attribute("stamp")?.Value;

        if (string.IsNullOrEmpty(wert))
            return false;

        // XEP-0203, Abschnitt 3 verlangt die Form aus XEP-0082, also RFC 3339
        // in UTC - und damit eine Zonenangabe. Ohne sie ist der Stempel nicht
        // auszuwerten: Eine Uhrzeit von einem fremden Rechner, von dem man die
        // Zone nicht kennt, ist keine Uhrzeit. Sie als hiesige zu lesen wäre
        // die schlechteste Wahl - dann verschiebt sich die Nachricht um genau
        // den Zonenunterschied, und zwar unbemerkt.
        if (!Regex.IsMatch(wert, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase))
            return false;

        // RoundtripKind hält den Zonenteil fest, statt ihn zu deuten. Was
        // gemeint ist, steht nach der Prüfung oben in jedem Fall in der
        // Zeichenkette und nicht in der Umgebung.
        if (!DateTimeOffset.TryParse(wert,
                                     CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind,
                                     out stamp))
        {
            return false;
        }

        by = delay.Attribute("from")?.Value;

        return true;

    }

}
