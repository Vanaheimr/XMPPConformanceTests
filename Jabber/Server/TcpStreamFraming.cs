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
    /// Die klassische Rahmung nach RFC 6120: ein einziges, nie geschlossenes
    /// <c>&lt;stream:stream&gt;</c>-Element, dessen Kinder die Stanzas sind.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zu RFC 7395 ist grösser, als er aussieht. Der
    /// Stream-Kopf ist ein <b>offenes</b> Tag: für sich genommen ist er kein
    /// wohlgeformtes XML, und alles, was danach kommt, hängt für seine
    /// Namensräume an ihm. Deshalb werden hier gleich drei deklariert -
    /// <c>jabber:server</c> als Vorgabe für die Stanzas,
    /// <c>stream</c> für die Stream-Ebene und <c>db</c> für Dialback.
    ///
    /// Genau daran zahlt sich eine Entscheidung aus S4b-3 aus: die
    /// Dialback-Elemente werden über einen regulären Ausdruck gelesen und
    /// nicht über einen XML-Parser. Ein <c>&lt;db:result/&gt;</c> über TCP
    /// wäre allein betrachtet nicht wohlgeformt, weil sein Präfix am
    /// Wurzelelement hängt - ein Parser, der jeden Rahmen für sich nimmt,
    /// müsste daran scheitern.
    ///
    /// Wo RFC 7395 vom Transport fertige Rahmen bekommt, muss hier erst
    /// zerlegt werden; das erledigt <see cref="XmlStreamSplitter"/>.
    /// </remarks>
    public sealed class TcpStreamFraming : IS2SFraming
    {

        #region Properties

        /// <summary>Es gibt nichts zu unterscheiden - eine Instanz genügt.</summary>
        public static readonly TcpStreamFraming Instance = new();

        /// <summary>Der Vorgabe-Namensraum der Stanzas auf einer S2S-Strecke (RFC 6120, Abschnitt 4.8.2).</summary>
        public const String ContentNamespace = "jabber:server";

        /// <summary>Der Namensraum der Stream-Ebene.</summary>
        public const String StreamNamespace = S2SStream.StreamNamespace;

        /// <summary>Der voreingestellte Port für S2S (RFC 6120, Abschnitt 3.2.1).</summary>
        public const Int32 DefaultPort = 5269;

        #endregion

        private TcpStreamFraming()
        { }


        #region IS2SFraming

        public String StreamOpen(String from, String? to, String? id)

            => "<stream:stream " +
               $"xmlns='{ContentNamespace}' " +
               $"xmlns:stream='{StreamNamespace}' " +
               $"xmlns:db='{DialbackKey.Namespace}' " +
               $"from='{XmlEscaping.Escape(from)}'" +
               (to is not null ? $" to='{XmlEscaping.Escape(to)}'" : "") +
               (id is not null ? $" id='{XmlEscaping.Escape(id)}'" : "") +
               " version='1.0'>";

        public String StreamClose()
            => "</stream:stream>";

        public Boolean IsStreamOpen(String frame)
            => frame.StartsWith("<stream:stream", StringComparison.Ordinal);

        public Boolean IsStreamClose(String frame)
            => frame.StartsWith("</stream:stream", StringComparison.Ordinal);

        #endregion

    }

}
