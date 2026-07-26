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

using System.Text.RegularExpressions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// RFC 6120, Abschnitt 8.3: Der Inhalt eines <c>&lt;error/&gt;</c>-Elements
/// aus einer Stanza vom Typ <c>error</c>.
/// </summary>
/// <param name="Type">Die Fehlerart; bestimmt, ob und wie wiederholt werden darf.</param>
/// <param name="Condition">
/// Die definierte Bedingung aus Abschnitt 8.3.3, etwa <c>service-unavailable</c>
/// oder <c>item-not-found</c>. Bleibt als Zeichenkette erhalten, damit auch
/// anwendungsspezifische und künftige Bedingungen unverfälscht durchkommen.
/// </param>
/// <param name="Text">Optionaler, für Menschen gedachter Text.</param>
/// <param name="By">
/// Optional: wer den Fehler erzeugt hat. Bei einem Fehler von einem Server
/// im Zustellweg ist das nicht zwingend der ursprüngliche Empfänger.
/// </param>
public sealed record StanzaError(StanzaErrorType  Type,
                                 string           Condition,
                                 string?          Text  = null,
                                 string?          By    = null)
{

    /// <summary>Der Namespace der definierten Bedingungen.</summary>
    public const string Namespace = "urn:ietf:params:xml:ns:xmpp-stanzas";

    /// <summary>
    /// Liest das <c>&lt;error/&gt;</c>-Element aus einer Stanza.
    /// </summary>
    /// <returns>False, wenn die Stanza kein error-Element enthält.</returns>
    public static bool TryParse(string stanza, out StanzaError? error)
    {

        error = null;

        var errorElement = Regex.Match(stanza,
                                       @"<error\b[^>]*>.*?</error\s*>|<error\b[^>]*/>",
                                       RegexOptions.Singleline);

        if (!errorElement.Success)
            return false;

        var xml = errorElement.Value;

        // RFC 6120, 8.3.2: Das type-Attribut ist Pflicht. Fehlt es oder ist es
        // unbekannt, wird 'cancel' angenommen - die vorsichtigste Annahme, denn
        // sie führt nicht zu einem Wiederholungsversuch.
        var type = ParseType(Attribute(xml, "type"));

        error = new StanzaError(type,
                                ParseCondition(xml),
                                ParseText(xml),
                                Attribute(xml, "by"));

        return true;

    }

    private static StanzaErrorType ParseType(string? value)
        => value switch {
               "auth"      => StanzaErrorType.Auth,
               "continue"  => StanzaErrorType.Continue,
               "modify"    => StanzaErrorType.Modify,
               "wait"      => StanzaErrorType.Wait,
               _           => StanzaErrorType.Cancel
           };

    /// <summary>
    /// Die definierte Bedingung ist das erste Kindelement im Stanzas-Namespace,
    /// das nicht <c>text</c> heisst.
    /// </summary>
    private static string ParseCondition(string errorXml)
    {

        // Regulärer Fall: die Bedingung trägt den Namespace selbst.
        foreach (Match m in Regex.Matches(errorXml,
                                          @"<([a-zA-Z][\w\-]*)\s[^>]*xmlns\s*=\s*['""]" +
                                          Regex.Escape(Namespace) + @"['""]"))
        {
            if (m.Groups[1].Value != "text")
                return m.Groups[1].Value;
        }

        // Rückfall für Server, die den Namespace am error-Element setzen:
        // das erste Kindelement, das nicht 'text' ist.
        foreach (Match m in Regex.Matches(errorXml, @"<([a-zA-Z][\w\-]*)[\s/>]"))
        {
            var name = m.Groups[1].Value;
            if (name != "error" && name != "text")
                return name;
        }

        // RFC 6120, 8.3.3: 'undefined-condition' ist der vorgesehene Rückfall.
        return "undefined-condition";

    }

    private static string? ParseText(string errorXml)
    {

        var m = Regex.Match(errorXml, @"<text\b[^>]*>(.*?)</text\s*>", RegexOptions.Singleline);

        if (!m.Success)
            return null;

        var text = m.Groups[1].Value.Trim();

        return text.Length > 0 ? text : null;

    }

    private static string? Attribute(string xml, string name)
    {
        var m = Regex.Match(xml, @"^<error\b[^>]*?\s" + name + @"\s*=\s*['""]([^'""]*)['""]");
        return m.Success ? m.Groups[1].Value : null;
    }

    public override string ToString()
        => Text is null
               ? $"{Condition} ({Type.ToString().ToLowerInvariant()})"
               : $"{Condition} ({Type.ToString().ToLowerInvariant()}): {Text}";

}
