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
/// RFC 6120, Abschnitt 4.9: Ein Stream-Fehler ist immer endgültig für den
/// Stream - der Server schliesst ihn unmittelbar danach.
/// </summary>
/// <param name="Condition">
/// Die definierte Bedingung aus Abschnitt 4.9.3, etwa <c>conflict</c> oder
/// <c>system-shutdown</c>.
/// </param>
/// <param name="Text">Optionaler, für Menschen gedachter Text.</param>
public sealed record StreamError(string   Condition,
                                 string?  Text = null)
{

    /// <summary>Der Namespace der definierten Bedingungen.</summary>
    public const string Namespace = "urn:ietf:params:xml:ns:xmpp-streams";

    /// <summary>
    /// Lohnt ein erneuter Verbindungsversuch?
    ///
    /// Nur bei Bedingungen, die eine vorübergehende Lage beschreiben. Bei allem
    /// anderen - falsche Zugangsdaten, verdrängte Resource, unbekannter Host,
    /// Richtlinienverstoss - würde ein Reconnect denselben Fehler erneut
    /// erzeugen und den Server sinnlos belasten.
    ///
    /// <c>see-other-host</c> gilt hier bewusst als nicht wiederholbar: der
    /// Server nennt eine andere Adresse, und solange die nicht ausgewertet wird
    /// (RFC 6120, Abschnitt 4.9.3.16), liefe ein Reconnect gegen dieselbe
    /// Adresse in eine Schleife.
    /// </summary>
    public bool IsRecoverable
        => Condition is "connection-timeout"
                     or "internal-server-error"
                     or "remote-connection-failed"
                     or "reset"
                     or "resource-constraint"
                     or "system-shutdown"
                     or "undefined-condition";

    /// <summary>
    /// Liest einen <c>&lt;stream:error/&gt;</c>-Rahmen.
    /// </summary>
    /// <returns>False, wenn die Stanza kein Stream-Fehler ist.</returns>
    public static bool TryParse(string stanza, out StreamError? error)
    {

        error = null;

        // Das Präfix ist nicht vorgeschrieben - üblich ist stream:, möglich ist
        // aber jedes an den Streams-Namespace gebundene Präfix.
        if (!Regex.IsMatch(stanza, @"^\s*<(?:[a-zA-Z][\w\-]*:)?error\b"))
            return false;

        var condition = "undefined-condition";

        foreach (Match m in Regex.Matches(stanza,
                                          @"<([a-zA-Z][\w\-]*)\s[^>]*xmlns\s*=\s*['""]" +
                                          Regex.Escape(Namespace) + @"['""]"))
        {
            if (m.Groups[1].Value != "text")
            {
                condition = m.Groups[1].Value;
                break;
            }
        }

        var textMatch = Regex.Match(stanza, @"<text\b[^>]*>(.*?)</text\s*>", RegexOptions.Singleline);
        var text      = textMatch.Success ? textMatch.Groups[1].Value.Trim() : null;

        error = new StreamError(condition, string.IsNullOrEmpty(text) ? null : text);

        return true;

    }

    public override string ToString()
        => Text is null
               ? Condition
               : $"{Condition}: {Text}";

}
