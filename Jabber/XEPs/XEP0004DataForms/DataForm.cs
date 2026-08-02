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
/// XEP-0004: die paar Handgriffe an einem Datenformular, die hier gebraucht
/// werden.
/// </summary>
/// <remarks>
/// <b>Kein Formularmodell, nur die gemeinsamen Stellen.</b> Zwei Formulare gibt
/// es in diesem Haus - die Einstellungen eines Abonnements und die eines
/// Knotens -, und beide bauen dieselben Felder, lesen denselben Wahrheitswert
/// und stolpern über dieselben Schreibweisen. Zweimal dasselbe zu schreiben
/// heisst, es einmal zu ändern und einmal zu vergessen.
///
/// Was hier <b>nicht</b> steht, ist ein Formularmodell mit Feldtypen und
/// Prüfregeln. Es gäbe eines zu bauen; gebraucht wird es nicht, und ungenutzte
/// Fläche ist in diesem Bau kein Guthaben.
///
/// <b>Mehrfachwerte standen bis D92 in derselben Zeile</b> - auch sie wurden
/// nicht gebraucht. Mit <c>pubsub#roster_groups_allowed</c> gibt es das erste
/// Feld, das mehrere trägt; ein <c>list-multi</c>, von dem nur der erste Wert
/// gelesen würde, wäre genau die stille Verkürzung, gegen die dieses Haus
/// sonst schreibt.
/// </remarks>
internal static class DataForm
{

    /// <summary>Der Namensraum der Datenformulare.</summary>
    public const String Namespace = "jabber:x:data";

    /// <summary>
    /// Ist das ein Formular dieser Art - <c>form</c>, <c>submit</c>?
    /// </summary>
    public static Boolean Is(XElement x, String type)
        => x.Name.NamespaceName == Namespace &&
           x.Name.LocalName     == "x" &&
           x.Attr("type")       == type;

    /// <summary>Die Felder eines Formulars.</summary>
    public static IEnumerable<XElement> Fields(XElement x)
        => x.Children(Namespace, "field");

    /// <summary>
    /// Der erste Wert eines Feldes, oder null.
    /// </summary>
    public static String? ValueOf(XElement field)
        => field.Child(Namespace, "value")?.Value;

    /// <summary>
    /// Alle Werte eines Feldes - für <c>list-multi</c>, wo jeder Wert ein
    /// eigenes <c>&lt;value/&gt;</c> ist.
    /// </summary>
    /// <remarks>
    /// Ein Feld ohne Werte gibt eine leere Liste. Bei einem Mehrfachfeld ist
    /// das eine Aussage und keine Lücke: <b>keine Auswahl</b>.
    /// </remarks>
    public static IReadOnlyList<String> ValuesOf(XElement field)
        => [.. field.Children(Namespace, "value").Select(v => v.Value)];

    /// <summary>
    /// XEP-0004, Abschnitt 3.3: Ein Wahrheitswert steht als 0/1 oder
    /// false/true.
    /// </summary>
    /// <remarks>
    /// Beide Schreibweisen zu lesen und nur eine zu schreiben ist kein
    /// Widerspruch, sondern die übliche Vorsicht: Was hereinkommt, hat ein
    /// anderer geschrieben.
    /// </remarks>
    public static Boolean TryBoolean(String? value, out Boolean result)
    {

        switch (value)
        {

            case "1" or "true":
                result = true;
                return true;

            case "0" or "false":
                result = false;
                return true;

            default:
                result = true;
                return false;

        }

    }

    /// <summary>Ein Wahrheitswert, wie er geschrieben wird.</summary>
    public static String Boolean(Boolean value)
        => value ? "1" : "0";

    /// <summary>Ein Feld mit einem Wert.</summary>
    public static XElement Field(String var, String? type, String? label, String value)
    {

        XNamespace ns = Namespace;

        var field = new XElement(ns + "field", new XAttribute("var", var));

        if (type is not null)
            field.Add(new XAttribute("type", type));

        if (label is not null)
            field.Add(new XAttribute("label", label));

        field.Add(new XElement(ns + "value", value));

        return field;

    }

    /// <summary>
    /// Ein Feld mit beliebig vielen Werten - auch mit keinem.
    /// </summary>
    /// <remarks>
    /// Kein Wert heisst hier „nichts ausgewählt" und nicht „Feld fehlt": Das
    /// Feld steht im Formular, es ist nur leer. Wer es stattdessen wegliesse,
    /// sagte „diese Einstellung gibt es nicht" - etwas ganz anderes.
    /// </remarks>
    public static XElement MultiField(String var, String? type, String? label, IEnumerable<String> values)
    {

        XNamespace ns = Namespace;

        var field = new XElement(ns + "field", new XAttribute("var", var));

        if (type is not null)
            field.Add(new XAttribute("type", type));

        if (label is not null)
            field.Add(new XAttribute("label", label));

        foreach (var value in values)
            field.Add(new XElement(ns + "value", value));

        return field;

    }

    /// <summary>
    /// Ein Formular mit seinem <c>FORM_TYPE</c> und den angegebenen Feldern.
    /// </summary>
    public static XElement Form(String type, String formType, params XElement[] fields)
    {

        XNamespace ns = Namespace;

        return new XElement(ns + "x",
                   new XAttribute("type", type),
                   Field("FORM_TYPE", "hidden", null, formType),
                   fields);

    }

}
