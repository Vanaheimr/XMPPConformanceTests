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
/// Der Namensraum, in dem eine Stanza steht - und wie er beim Übergang von
/// einem Stream auf einen anderen mitwandert.
/// </summary>
/// <remarks>
/// RFC 6120, Abschnitt 4.8.1 gibt jedem Stream einen Content-Namensraum:
/// <c>jabber:client</c> auf der Client-Verbindung, <c>jabber:server</c>
/// zwischen Servern. Über TCP steht er einmal am
/// <c>&lt;stream:stream&gt;</c> und gilt für alles darin; eine Stanza selbst
/// trägt ihn dort nie.
///
/// Zwei Stellen brechen diese Bequemlichkeit auf:
///
/// <list type="bullet">
///   <item>
///     <b>WebSocket</b> (RFC 7395, Abschnitt 3.3.3) kennt kein umschliessendes
///     Element. Jeder Rahmen muss für sich lesbar sein, „complete with all
///     relevant namespace and language declarations" - eine Stanza ohne eigene
///     Deklaration steht dort in <i>keinem</i> Namensraum.
///   </item>
///   <item>
///     <b>Die Domain-Grenze.</b> Was von einem Client hereinkommt, steht in
///     <c>jabber:client</c>; hinaus geht es auf einem Stream, der
///     <c>jabber:server</c> spricht. Trägt die Stanza ihren alten Namensraum
///     mit, ist sie auf dem neuen Stream keine gültige Stanza mehr.
///   </item>
/// </list>
///
/// Beides fiel gegen den eigenen Server nie auf, weil er Stanzas am lokalen
/// Namen erkennt und den Namensraum gar nicht ansieht. Prosody sieht ihn an:
/// ein Bind-IQ ohne Namensraum beantwortete es mit
/// <c>&lt;unsupported-stanza-type/&gt;</c>, ein <c>jabber:client</c>-IQ auf
/// dem S2S-Stream mit einem Fehler.
/// </remarks>
internal static class StanzaNamespace
{

    /// <summary>Der Content-Namensraum der Client-Verbindung.</summary>
    public const String Client = "jabber:client";

    /// <summary>Der Content-Namensraum zwischen Servern.</summary>
    public const String Server = "jabber:server";


    #region Apply(xml, ns)

    /// <summary>
    /// Setzt den Namensraum einer Stanza auf <paramref name="ns"/>.
    /// </summary>
    /// <remarks>
    /// Angefasst werden nur <c>message</c>, <c>presence</c> und <c>iq</c>.
    /// Nonzas bringen ihren Namensraum selbst mit und behalten ihn - ein
    /// <c>&lt;enable/&gt;</c> nach <c>jabber:client</c> umzuhängen machte es
    /// unlesbar.
    ///
    /// Angesehen wird ausschliesslich das Start-Tag des Wurzelelements. Ein
    /// blosses „steht irgendwo ein xmlns" fiele auf das erste Kindelement
    /// herein - beim Bind-IQ etwa auf
    /// <c>&lt;bind xmlns='…xmpp-bind'/&gt;</c>, und genau die Stanza bliebe
    /// dann unbehandelt.
    ///
    /// Steht der gewünschte Namensraum schon da, kommt die Zeichenkette
    /// unverändert zurück - auch in ihrer Schreibweise der Anführungszeichen.
    /// </remarks>
    public static String Apply(String xml, String ns)
    {

        if (String.IsNullOrEmpty(xml))
            return xml;

        var i = 0;
        while (i < xml.Length && Char.IsWhiteSpace(xml[i]))
            i++;

        if (i >= xml.Length || xml[i] != '<')
            return xml;

        i++;
        var nameAnfang = i;

        while (i < xml.Length &&
               (Char.IsLetterOrDigit(xml[i]) || xml[i] == '-' || xml[i] == '_' || xml[i] == ':'))
            i++;

        var nameEnde = i;

        if (xml[nameAnfang..nameEnde] is not ("message" or "presence" or "iq"))
            return xml;

        // Das Start-Tag durchgehen und dabei Anführungszeichen beachten: ein
        // Attributwert darf ein '>' enthalten.
        var quote           = '\0';
        var attributAnfang  = -1;
        var attributEnde    = -1;
        var wertAnfang      = -1;
        var wertEnde        = -1;

        while (i < xml.Length)
        {

            var c = xml[i];

            if (quote != '\0')
            {

                if (c == quote)
                {

                    quote = '\0';

                    if (attributAnfang >= 0 && wertEnde < 0)
                    {
                        wertEnde      = i;
                        attributEnde  = i + 1;
                        break;
                    }

                }

            }

            else if (c is '\'' or '"')
            {

                quote = c;

                if (attributAnfang >= 0 && wertAnfang < 0)
                    wertAnfang = i + 1;

            }

            else if (c == '>')
                break;

            // Genau "xmlns", nicht "xmlns:irgendwas" - ein Präfix deklariert
            // keinen Standard-Namensraum und geht diese Funktion nichts an.
            else if (c == 'x' &&
                     attributAnfang < 0 &&
                     Char.IsWhiteSpace(xml[i - 1]) &&
                     xml.AsSpan(i).StartsWith("xmlns", StringComparison.Ordinal) &&
                     i + 5 < xml.Length &&
                     (xml[i + 5] == '=' || Char.IsWhiteSpace(xml[i + 5])))
            {
                attributAnfang = i;
            }

            i++;

        }

        // Kein Namensraum vorhanden: hinter dem Elementnamen einsetzen.
        if (attributAnfang < 0 || wertAnfang < 0 || wertEnde < 0)
            return String.Concat(xml.AsSpan(0, nameEnde),
                                 $" xmlns='{ns}'",
                                 xml.AsSpan(nameEnde));

        // Schon der richtige: nichts anfassen, auch nicht die Anführungszeichen.
        if (xml.AsSpan(wertAnfang, wertEnde - wertAnfang).SequenceEqual(ns))
            return xml;

        return String.Concat(xml.AsSpan(0, attributAnfang),
                             $"xmlns='{ns}'",
                             xml.AsSpan(attributEnde));

    }

    #endregion

}
