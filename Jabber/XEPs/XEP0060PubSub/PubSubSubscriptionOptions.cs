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

using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Die Einstellungen eines einzelnen Abonnements (XEP-0060, Abschnitt 6.3).
/// </summary>
/// <param name="Deliver">
/// Werden Benachrichtigungen zugestellt? <c>pubsub#deliver</c>, Abschnitt
/// 12.18.
/// </param>
/// <remarks>
/// <b>Ein Feld, und das ist die Aussage.</b> XEP-0060 kennt ein Dutzend
/// weitere - Zusammenfassungen, Ablauffristen, Tiefe, Presence-Filter. Was
/// dieser Server nicht kann, bietet er auch nicht an: Ein Formular mit
/// <c>pubsub#digest</c> darin, das dann nichts bewirkt, wäre eine Zusage ohne
/// Deckung, und zwar eine, die der Abonnent nicht nachprüfen kann - eine
/// ausbleibende Zusammenfassung sieht aus wie Ruhe.
///
/// <b>Erst hiermit unterscheiden sich zwei Abonnements.</b> Bis dahin waren
/// zwei auf denselben Knoten zwei gleiche Dinge, und das zweite brachte nichts
/// ein als eine zweite Zustellung. Jetzt ist die <c>subid</c> nicht nur eine
/// Kennung, sondern die Adresse einer Einstellung.
/// </remarks>
public sealed record PubSubSubscriptionOptions(Boolean Deliver = true)
{

    /// <summary>Der Namensraum der Datenformulare (XEP-0004).</summary>
    public const String DataFormNamespace = "jabber:x:data";

    /// <summary>Der Formulartyp dieser Einstellungen.</summary>
    public const String FormType = "http://jabber.org/protocol/pubsub#subscribe_options";

    /// <summary>Das Feld für die Zustellung.</summary>
    public const String DeliverVariable = "pubsub#deliver";

    /// <summary>
    /// Das Angebot des Dienstes (<c>type='form'</c>) - was sich einstellen
    /// lässt und was gerade gilt.
    /// </summary>
    public XElement ToForm()
    {

        XNamespace ns = DataFormNamespace;

        return new XElement(ns + "x",
                   new XAttribute("type", "form"),
                   new XElement(ns + "field",
                       new XAttribute("var",  "FORM_TYPE"),
                       new XAttribute("type", "hidden"),
                       new XElement(ns + "value", FormType)),
                   new XElement(ns + "field",
                       new XAttribute("var",   DeliverVariable),
                       new XAttribute("type",  "boolean"),
                       new XAttribute("label", "Benachrichtigungen zustellen"),
                       new XElement(ns + "value", Deliver ? "1" : "0")));

    }

    /// <summary>
    /// Die Antwort des Abonnenten (<c>type='submit'</c>).
    /// </summary>
    public XElement ToSubmit()
    {

        XNamespace ns = DataFormNamespace;

        return new XElement(ns + "x",
                   new XAttribute("type", "submit"),
                   new XElement(ns + "field",
                       new XAttribute("var",  "FORM_TYPE"),
                       new XAttribute("type", "hidden"),
                       new XElement(ns + "value", FormType)),
                   new XElement(ns + "field",
                       new XAttribute("var", DeliverVariable),
                       new XElement(ns + "value", Deliver ? "1" : "0")));

    }

    /// <summary>
    /// Liest ein abgeschicktes Formular.
    /// </summary>
    /// <returns>
    /// false, wenn es keines ist, den falschen Zweck hat oder ein Feld
    /// enthält, das hier niemand angeboten hat.
    /// </returns>
    /// <remarks>
    /// <b>Unbekannte Felder werden abgewiesen und nicht übergangen.</b> Das
    /// ist strenger, als XEP-0004 verlangt, und Absicht: Wer Unbekanntes
    /// stillschweigend schluckt, lässt den Absender in dem Glauben, seine
    /// Einstellung gelte. Eine Absage kann man lesen, eine ausbleibende
    /// Wirkung nicht.
    ///
    /// Ein fehlendes Feld ist dagegen kein Fehler: Das abgeschickte Formular
    /// ist die vollständige Einstellung, und was nicht dasteht, steht auf der
    /// Vorgabe.
    /// </remarks>
    public static Boolean TryRead(XElement x, out PubSubSubscriptionOptions? options)
    {

        options = null;

        if (x.Name.NamespaceName != DataFormNamespace ||
            x.Name.LocalName     != "x" ||
            x.Attr("type")       != "submit")
        {
            return false;
        }

        var deliver = true;

        foreach (var field in x.Children(DataFormNamespace, "field"))
        {

            var wert = field.Child(DataFormNamespace, "value")?.Value;

            switch (field.Attr("var"))
            {

                case "FORM_TYPE":
                    if (wert != FormType)
                        return false;
                    break;

                case DeliverVariable:
                    if (!TryReadBoolean(wert, out deliver))
                        return false;
                    break;

                default:
                    return false;

            }

        }

        options = new PubSubSubscriptionOptions(deliver);

        return true;

    }

    /// <summary>
    /// XEP-0004, Abschnitt 3.3: Ein Wahrheitswert steht als 0/1 oder
    /// false/true.
    /// </summary>
    /// <remarks>
    /// Beide Schreibweisen zu lesen und nur eine zu schreiben ist kein
    /// Widerspruch, sondern die übliche Vorsicht: Was hereinkommt, hat ein
    /// anderer geschrieben.
    /// </remarks>
    private static Boolean TryReadBoolean(String? wert, out Boolean ergebnis)
    {

        switch (wert)
        {

            case "1" or "true":
                ergebnis = true;
                return true;

            case "0" or "false":
                ergebnis = false;
                return true;

            default:
                ergebnis = true;
                return false;

        }

    }

}
