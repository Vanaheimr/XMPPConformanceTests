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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Zerlegt den Zeichenstrom eines XMPP-Streams (RFC 6120) in einzelne
    /// Rahmen: den Stream-Kopf, dann jede Stanza, zuletzt das Stream-Ende.
    /// </summary>
    /// <remarks>
    /// Über WebSocket ist das umsonst zu haben - ein Frame ist ein Element.
    /// Über TCP kommt ein Strom an, in dem eine Stanza über beliebig viele
    /// Lesevorgänge verteilt sein kann und mehrere Stanzas in einem stecken
    /// dürfen. Ohne diese Zerlegung <b>funktioniert TCP scheinbar</b>, solange
    /// die Pakete zufällig auf Elementgrenzen fallen - also im Test auf
    /// localhost fast immer, und im Betrieb dann nicht mehr. Deshalb steht das
    /// hier als eigener, für sich geprüfter Baustein und nicht als Handgriff
    /// in der Empfangsschleife.
    ///
    /// Bewusst <b>kein</b> XML-Parser: der Stream-Kopf ist ein offenes Tag und
    /// wäre für sich genommen nicht wohlgeformt, und ein Parser über den
    /// gesamten Strom müsste den ganzen Stream als ein Dokument aufbauen. Was
    /// hier gebraucht wird, ist nur die Kunst, Elementgrenzen zu finden -
    /// samt der Fallen, die dabei zählen: Anführungszeichen, in denen ein
    /// <c>&gt;</c> stehen darf, CDATA, Kommentare.
    ///
    /// Diese Klasse hält keinen Zustand über die Wohlgeformtheit; sie prüft
    /// nicht, ob Tags zueinander passen. Ein Strom mit falsch verschachtelten
    /// Namen wird zerteilt, nicht abgelehnt - das zu beurteilen ist Sache der
    /// Schicht darüber.
    /// </remarks>
    public sealed class XmlStreamSplitter
    {

        #region Data

        private String   rest      = "";
        private Boolean  rootSeen;

        #endregion

        #region Push(text)

        /// <summary>
        /// Nimmt das nächste Stück des Stroms entgegen und liefert alle
        /// Rahmen, die damit vollständig geworden sind.
        /// </summary>
        /// <remarks>
        /// Der erste gelieferte Rahmen ist der Stream-Kopf - also das
        /// <b>offene</b> <c>&lt;stream:stream ...&gt;</c>-Tag ohne seine
        /// Kinder. Danach folgt je ein Rahmen pro Stanza, zuletzt
        /// <c>&lt;/stream:stream&gt;</c>.
        /// </remarks>
        public IReadOnlyList<String> Push(String text)
        {

            rest += text;

            var frames = new List<String>();

            while (true)
            {

                var start = SkipProlog(rest);

                if (start >= rest.Length)
                {
                    rest = "";
                    break;
                }

                var end = ScanOne(rest, start, stopAfterOpenTag: !rootSeen);

                if (end < 0)
                {
                    // Noch unvollständig - das bereits Übersprungene darf weg.
                    rest = rest[start..];
                    break;
                }

                frames.Add(rest[start..end]);
                rest      = rest[end..];
                rootSeen  = true;

            }

            return frames;

        }

        #endregion

        #region (private static) SkipProlog(s)

        /// <summary>
        /// Überspringt Leerraum, XML-Deklarationen und Kommentare zwischen
        /// zwei Elementen.
        /// </summary>
        /// <remarks>
        /// Beide sind auf oberster Ebene erlaubt und für das Protokoll ohne
        /// Bedeutung. Würden sie als Rahmen durchgereicht, hielte die Schicht
        /// darüber die XML-Deklaration für den Stream-Kopf.
        /// </remarks>
        private static Int32 SkipProlog(String s)
        {

            var i = 0;

            while (i < s.Length)
            {

                while (i < s.Length && Char.IsWhiteSpace(s[i]))
                    i++;

                if (Match(s, i, "<?"))
                {

                    var e = s.IndexOf("?>", i + 2, StringComparison.Ordinal);

                    if (e < 0)
                        return i;

                    i = e + 2;
                    continue;

                }

                if (Match(s, i, "<!--"))
                {

                    var e = s.IndexOf("-->", i + 4, StringComparison.Ordinal);

                    if (e < 0)
                        return i;

                    i = e + 3;
                    continue;

                }

                break;

            }

            return i;

        }

        #endregion

        #region (private static) ScanOne(s, start, stopAfterOpenTag)

        /// <summary>
        /// Sucht das Ende genau eines Elements ab <paramref name="start"/>.
        /// </summary>
        /// <param name="stopAfterOpenTag">
        /// Für den Stream-Kopf: nach dem öffnenden Tag aufhören, statt auf
        /// sein schliessendes zu warten. Das Wurzelelement wird erst am Ende
        /// des Streams geschlossen - darauf zu warten hiesse, nie einen Rahmen
        /// zu liefern.
        /// </param>
        /// <returns>Der Index hinter dem Element, oder -1 wenn noch unvollständig.</returns>
        private static Int32 ScanOne(String s, Int32 start, Boolean stopAfterOpenTag)
        {

            var i      = start;
            var depth  = 0;

            while (i < s.Length)
            {

                if (s[i] != '<')
                {
                    i++;
                    continue;
                }

                if (Match(s, i, "<!--"))
                {

                    var e = s.IndexOf("-->", i + 4, StringComparison.Ordinal);

                    if (e < 0)
                        return -1;

                    i = e + 3;
                    continue;

                }

                // In CDATA darf alles stehen, auch '<' und '>'.
                if (Match(s, i, "<![CDATA["))
                {

                    var e = s.IndexOf("]]>", i + 9, StringComparison.Ordinal);

                    if (e < 0)
                        return -1;

                    i = e + 3;
                    continue;

                }

                if (Match(s, i, "<?"))
                {

                    var e = s.IndexOf("?>", i + 2, StringComparison.Ordinal);

                    if (e < 0)
                        return -1;

                    i = e + 2;
                    continue;

                }

                var schliessend = Match(s, i, "</");
                var j           = i + 1;
                var quote       = '\0';
                var leer        = false;

                while (j < s.Length)
                {

                    var c = s[j];

                    if (quote != '\0')
                    {
                        if (c == quote)
                            quote = '\0';
                        j++;
                        continue;
                    }

                    if (c is '\'' or '"')
                    {
                        quote = c;
                        j++;
                        continue;
                    }

                    // Ein '>' innerhalb eines Attributwerts ist gültiges XML
                    // und beendet das Tag nicht - deshalb erst hier, ausserhalb
                    // der Anführungszeichen.
                    if (c == '>')
                    {

                        var k = j - 1;

                        while (k > i && Char.IsWhiteSpace(s[k]))
                            k--;

                        leer = s[k] == '/';
                        break;

                    }

                    j++;

                }

                // Das Tag ist noch nicht zu Ende gelesen.
                if (j >= s.Length)
                    return -1;

                i = j + 1;

                if (schliessend)
                {

                    depth--;

                    // Auch </stream:stream> als erstes Element landet hier:
                    // die Tiefe wird negativ, und der Rahmen ist vollständig.
                    if (depth <= 0)
                        return i;

                }

                else if (leer)
                {
                    if (depth == 0)
                        return i;
                }

                else
                {

                    depth++;

                    if (stopAfterOpenTag)
                        return i;

                }

            }

            return -1;

        }

        #endregion

        #region (private static) Match(s, i, text)

        private static Boolean Match(String s, Int32 i, String text)
            => i + text.Length <= s.Length &&
               String.CompareOrdinal(s, i, text, 0, text.Length) == 0;

        #endregion

    }

}
