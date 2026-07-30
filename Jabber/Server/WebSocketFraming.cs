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
    /// Die Rahmung nach RFC 7395: ein WebSocket-Frame ist genau ein Element,
    /// der Stream wird mit <c>&lt;open/&gt;</c> geöffnet und mit
    /// <c>&lt;close/&gt;</c> geschlossen.
    /// </summary>
    /// <remarks>
    /// Jeder Rahmen steht für sich und trägt seine Namensräume selbst - es
    /// gibt kein Wurzelelement, das etwas vererben könnte.
    /// </remarks>
    public sealed class WebSocketFraming : IS2SFraming
    {

        #region Properties

        /// <summary>Es gibt nichts zu unterscheiden - eine Instanz genügt.</summary>
        public static readonly WebSocketFraming Instance = new();

        /// <summary>Der Namensraum der Rahmung (RFC 7395, Abschnitt 3.1).</summary>
        public const String Namespace = "urn:ietf:params:xml:ns:xmpp-framing";

        #endregion

        private WebSocketFraming()
        { }


        #region IS2SFraming

        public String StreamOpen(String from, String? to, String? id)

            => $"<open xmlns='{Namespace}' " +
               $"from='{XmlEscaping.Escape(from)}'" +
               (to is not null ? $" to='{XmlEscaping.Escape(to)}'" : "") +
               (id is not null ? $" id='{XmlEscaping.Escape(id)}'" : "") +
               " version='1.0'/>";

        public String StreamClose()
            => $"<close xmlns='{Namespace}'/>";

        // Am Elementnamen und nicht am Präfix: <opencast/> ist keine
        // Stream-Eröffnung, <closet/> kein Abschied.
        public Boolean IsStreamOpen(String frame)
            => StanzaElement.Is(frame, "open");

        public Boolean IsStreamClose(String frame)
            => StanzaElement.Is(frame, "close");

        #endregion

    }

}
