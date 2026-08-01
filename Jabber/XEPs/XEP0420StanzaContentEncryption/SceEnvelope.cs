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

using System.Security.Cryptography;
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0420: Stanza Content Encryption - die Hülle, die verschlüsselt wird.
/// </summary>
/// <remarks>
/// <b>Verschlüsselt wird nicht der Text, sondern eine ganze Stanza-Hülle.</b>
/// Das ist der Unterschied zu älteren Verfahren, und er ist der Grund für
/// dieses XEP: Wer nur den <c>&lt;body/&gt;</c> verschlüsselt, lässt alles
/// andere im Klartext stehen - Chat States, Empfangsbestätigungen,
/// Korrekturvermerke - und ein Mitlesender weiss dann, wann wer tippt, was
/// ankam und was berichtigt wurde. Der Inhalt wäre geschützt, das Gespräch
/// nicht.
///
/// <b>Die Beigaben („affix elements") sind gegen Verschieben.</b> Ein
/// Geheimtext ohne sie liesse sich abfangen und an jemand anderen
/// weiterschicken - die Verschlüsselung bliebe gültig, der Empfänger sähe eine
/// Nachricht, die nie an ihn gerichtet war. Deshalb steht der Absender
/// <b>innerhalb</b> der Hülle: Aussen kann ihn jeder ändern.
///
/// <b>Und das <c>&lt;rpad/&gt;</c> ist keine Zierde.</b> Ohne es verrät die
/// Länge des Geheimtextes die Länge der Nachricht - bei „ja" und „nein" ist
/// das der ganze Inhalt. Die Polsterung hat zufällige Länge, damit sich aus
/// der Grösse nichts mehr ablesen lässt.
/// </remarks>
public sealed record SceEnvelope(IReadOnlyList<XElement>  Content,
                                 String?                  From    = null,
                                 String?                  To      = null,
                                 DateTimeOffset?          Time    = null)
{

    /// <summary>Der Namespace von XEP-0420.</summary>
    public const String Namespace = "urn:xmpp:sce:1";

    /// <summary>Die Obergrenze der Polsterung in Zeichen (XEP-0420, Abschnitt 4).</summary>
    public const Int32 MaxPadding = 200;

    #region ToXml()

    /// <summary>
    /// Die Hülle als XML, mit frisch gezogener Polsterung.
    /// </summary>
    /// <remarks>
    /// Die Polsterung wird bei jedem Aufruf neu gezogen - auch für dieselbe
    /// Nachricht. Wäre sie es nicht, hätten zwei gleiche Nachrichten wieder
    /// gleiche Länge, und die Massnahme wäre genau so weit wirkungslos, wie
    /// sie gedacht war.
    /// </remarks>
    public XElement ToXml()
    {

        XNamespace ns = Namespace;

        var envelope = new XElement(ns + "envelope",
                                    new XElement(ns + "content", Content));

        if (From is not null)
            envelope.Add(new XElement(ns + "from", new XAttribute("jid", From)));

        if (To is not null)
            envelope.Add(new XElement(ns + "to", new XAttribute("jid", To)));

        if (Time.HasValue)
            envelope.Add(new XElement(ns + "time",
                                      new XAttribute("stamp",
                                                     Time.Value.ToUniversalTime()
                                                         .ToString("yyyy-MM-ddTHH:mm:ssZ"))));

        envelope.Add(new XElement(ns + "rpad", RandomPadding()));

        return envelope;

    }

    /// <summary>
    /// Zufällige Zeichen zufälliger Länge zwischen 0 und
    /// <see cref="MaxPadding"/>.
    /// </summary>
    /// <remarks>
    /// Aus dem kryptographischen Zufallsgenerator und nicht aus
    /// <see cref="Random"/>: Eine vorhersagbare Polsterung polstert nichts.
    /// Wer die Folge kennt, zieht sie von der Länge ab und liest die
    /// Nachrichtenlänge wie zuvor.
    /// </remarks>
    private static String RandomPadding()
    {

        const String zeichen = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        var laenge = RandomNumberGenerator.GetInt32(0, MaxPadding + 1);
        var puffer = new Char[laenge];

        for (var i = 0; i < laenge; i++)
            puffer[i] = zeichen[RandomNumberGenerator.GetInt32(zeichen.Length)];

        return new String(puffer);

    }

    #endregion

    #region TryRead(xml, out envelope)

    /// <summary>
    /// Liest eine Hülle.
    /// </summary>
    /// <param name="expectedFrom">
    /// Von wem die Nachricht laut Stanza stammt. Steht in der Hülle ein
    /// anderer Absender, wird sie abgewiesen.
    /// </param>
    /// <remarks>
    /// <b>Der Abgleich ist der Zweck der Beigabe</b>, und er gehört hierhin
    /// und nicht in die Oberfläche: Eine Hülle, deren Absender niemand
    /// nachsieht, ist ein Feld, das Platz kostet. Der Angriff, den sie
    /// abwehrt, ist das Weiterreichen - jemand fängt einen Geheimtext ab und
    /// schickt ihn unter eigenem Namen weiter.
    /// </remarks>
    public static Boolean TryRead(XElement          xml,
                                  out SceEnvelope?  envelope,
                                  String?           expectedFrom = null)
    {

        envelope = null;

        if (xml.Name.LocalName != "envelope" || xml.Name.NamespaceName != Namespace)
            return false;

        var content = xml.Child(Namespace, "content");

        if (content is null)
            return false;

        var from = xml.Child(Namespace, "from")?.Attr("jid");
        var to   = xml.Child(Namespace, "to")?.Attr("jid");

        if (expectedFrom is not null &&
            from is not null &&
            !String.Equals(JidUtilities.Bare(from), JidUtilities.Bare(expectedFrom),
                           StringComparison.OrdinalIgnoreCase))
            return false;

        DateTimeOffset? zeit = null;

        if (xml.Child(Namespace, "time")?.Attr("stamp") is String stempel &&
            DateTimeOffset.TryParse(stempel, null,
                                    System.Globalization.DateTimeStyles.RoundtripKind,
                                    out var gelesen))
            zeit = gelesen;

        envelope = new SceEnvelope([.. content.Elements()], from, to, zeit);

        return true;

    }

    #endregion

}
