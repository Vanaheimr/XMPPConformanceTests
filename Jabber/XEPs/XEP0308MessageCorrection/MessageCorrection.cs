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
/// XEP-0308: „Ich meinte: morgen." - eine Nachricht ersetzt die vorige.
/// </summary>
/// <remarks>
/// Die Korrektur ist eine gewöhnliche Nachricht mit eigener <c>id</c> und
/// vollständigem <c>&lt;body/&gt;</c>; das <c>&lt;replace/&gt;</c> nennt nur,
/// welche sie ablöst. <b>Das ist Absicht:</b> Ein Empfänger, der die
/// Erweiterung nicht kennt, zeigt sie als neue Nachricht an - unschön, aber
/// vollständig. Wer stattdessen nur den geänderten Teil schickte, hinterliesse
/// bei ihm eine leere Zeile.
///
/// <b>Nur die letzte.</b> Abschnitt 5 lässt eine Korrektur nur für die zuletzt
/// an denselben Empfänger geschickte Nachricht zu. Der Grund ist die
/// Verlässlichkeit: Ohne Grenze müsste jeder Empfänger seinen ganzen Verlauf
/// vorhalten und dürfte nie etwas als endgültig ansehen. Diese Seite hält sich
/// beim Senden daran; beim Empfangen wird die Ersetzung gemeldet und die
/// Entscheidung der Oberfläche überlassen - eine Konsole kann Geschriebenes
/// ohnehin nicht zurücknehmen.
/// </remarks>
public static class MessageCorrection
{

    /// <summary>Der Namespace von XEP-0308.</summary>
    public const string Namespace = "urn:xmpp:message-correct:0";

    /// <summary>
    /// Das <c>&lt;replace/&gt;</c> für eine Korrektur.
    /// </summary>
    /// <param name="replacesId">Die <c>id</c> der Nachricht, die ersetzt wird.</param>
    public static string ReplaceXml(string replacesId)
        => $"<replace id='{XmlEscaping.Escape(replacesId)}' xmlns='{Namespace}'/>";

    /// <summary>
    /// Welche Nachricht diese hier ersetzt, oder null.
    /// </summary>
    /// <remarks>
    /// <b>Nur direkte Kinder</b> - aus demselben Grund wie beim Verzugsstempel
    /// (XEP-0203, siehe D59): Ein Carbon bringt in seinem
    /// <c>&lt;forwarded/&gt;</c> eine eigene Nachricht mit, und deren
    /// Korrekturvermerk gehört nicht der äusseren.
    ///
    /// Eine leere <c>id</c> gilt wie keine. Sie zeigt auf nichts, und eine
    /// Ersetzung ohne Ziel ist keine.
    /// </remarks>
    public static string? ReplacedId(XElement message)
    {

        var replace = message.Child(Namespace, "replace");

        if (replace is null)
            return null;

        var id = replace.Attribute("id")?.Value;

        return string.IsNullOrEmpty(id) ? null : id;

    }

}
