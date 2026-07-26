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

using System.Text.RegularExpressions;

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
    /// Prüft ob eine Nachricht markierbar ist
    /// </summary>
    public static bool IsMarkable(string xml) =>
        xml.Contains("<markable") && xml.Contains(Namespace);

    /// <summary>
    /// Extrahiert einen Marker aus einer Nachricht
    /// </summary>
    public static ChatMarker? Parse(string xml, string from)
    {
        if (!xml.Contains(Namespace))
            return null;

        var types = new[] {
            ("received", ChatMarkerType.Received),
            ("displayed", ChatMarkerType.Displayed),
            ("acknowledged", ChatMarkerType.Acknowledged)
        };

        foreach (var (name, type) in types)
        {
            var pattern = $@"<{name}\s+xmlns=['""]urn:xmpp:chat-markers:0['""]\s+id=['""]([^'""]+)['""]";
            var match = Regex.Match(xml, pattern);

            if (match.Success)
            {
                return new ChatMarker(type, from, match.Groups[1].Value, DateTime.UtcNow);
            }
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
