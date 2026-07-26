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
/// XEP-0333: Chat Markers - erzeugt und erkennt received/displayed/acknowledged.
/// </summary>
public static class ChatMarkers
{
    public const string Namespace = "urn:xmpp:chat-markers:0";

    /// <summary>
    /// Erzeugt <c>&lt;markable/&gt;</c> Element für ausgehende Nachrichten
    /// </summary>
    public static string Markable => $"<markable xmlns='{Namespace}'/>";

    /// <summary>
    /// Erzeugt eine Marker-Nachricht
    /// </summary>
    public static string CreateMarker(string to, string refId, ChatMarkerType type)
    {
        var element = type switch
        {
            ChatMarkerType.Received => "received",
            ChatMarkerType.Displayed => "displayed",
            ChatMarkerType.Acknowledged => "acknowledged",
            _ => "received"
        };

        return $"<message to='{XmlEscaping.Escape(to)}'>" +
               $"<{element} xmlns='{Namespace}' id='{XmlEscaping.Escape(refId)}'/>" +
               $"</message>";
    }

    /// <summary>
    /// Prüft, ob eine Nachricht markierbar ist.
    /// </summary>
    public static bool IsMarkable(XElement message)
        => message.Elements()
                  .Any(child => child.Name.NamespaceName == Namespace &&
                                child.Name.LocalName     == "markable");

    /// <summary>
    /// Extrahiert einen Marker aus einer Nachricht.
    ///
    /// Das frühere Muster verlangte <c>xmlns</c> vor <c>id</c>; XML kennt aber
    /// keine Attributreihenfolge, und ein Server, der sie andersherum schreibt,
    /// wurde still ignoriert. Die Namespace-Prüfung ist hier besonders wichtig:
    /// <c>&lt;received/&gt;</c> gibt es in XEP-0333 und in XEP-0184, und ohne
    /// sie sind die beiden nicht auseinanderzuhalten.
    /// </summary>
    public static ChatMarker? Parse(XElement message, string from)
    {

        foreach (var child in message.Elements().Where(e => e.Name.NamespaceName == Namespace))
        {

            ChatMarkerType type;

            switch (child.Name.LocalName)
            {
                case "received":      type = ChatMarkerType.Received;     break;
                case "displayed":     type = ChatMarkerType.Displayed;    break;
                case "acknowledged":  type = ChatMarkerType.Acknowledged; break;
                default:              continue;
            }

            var id = child.Attr("id");

            if (id is not null)
                return new ChatMarker(type, from, id, DateTime.UtcNow);

        }

        return null;

    }

    /// <summary>
    /// Symbol für Marker-Typ
    /// </summary>
    public static string GetSymbol(ChatMarkerType type) => type switch
    {
        ChatMarkerType.Received => "✓",
        ChatMarkerType.Displayed => "👁",
        ChatMarkerType.Acknowledged => "✓✓",
        _ => "?"
    };
}
