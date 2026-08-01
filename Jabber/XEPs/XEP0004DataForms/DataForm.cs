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
/// Was hier <b>nicht</b> steht, ist ein Formularmodell mit Feldtypen,
/// Mehrfachwerten und Prüfregeln. Es gäbe eines zu bauen; gebraucht wird es
/// nicht, und ungenutzte Fläche ist in diesem Bau kein Guthaben.
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
