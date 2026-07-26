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
/// XEP-0085: Serialisierung und Parsing von Chat States.
/// </summary>
public static class ChatStateExtensions
{

    /// <summary>Der Namespace von XEP-0085.</summary>
    public const string Namespace = "http://jabber.org/protocol/chatstates";

    public static string ToXml(this ChatState state) => state switch
    {
        ChatState.Active    => $"<active xmlns='{Namespace}'/>",
        ChatState.Composing => $"<composing xmlns='{Namespace}'/>",
        ChatState.Paused    => $"<paused xmlns='{Namespace}'/>",
        ChatState.Inactive  => $"<inactive xmlns='{Namespace}'/>",
        ChatState.Gone      => $"<gone xmlns='{Namespace}'/>",
        _ => ""
    };

    /// <summary>
    /// Liest den Chat State aus einer Nachricht.
    ///
    /// Gesucht wird nur unter den direkten Kindelementen und nur im Namespace
    /// von XEP-0085. Die frühere Prüfung <c>Contains("&lt;composing")</c> tat
    /// beides nicht: sie meldete jedes gleichnamige Element aus einer
    /// beliebigen Erweiterung als Chat State, und der Zustand einer nach
    /// XEP-0297 weitergeleiteten Nachricht wirkte nach aussen.
    /// </summary>
    public static ChatState? ParseChatState(XElement message)
    {

        foreach (var child in message.Elements().Where(e => e.Name.NamespaceName == Namespace))
        {

            switch (child.Name.LocalName)
            {
                case "active":     return ChatState.Active;
                case "composing":  return ChatState.Composing;
                case "paused":     return ChatState.Paused;
                case "inactive":   return ChatState.Inactive;
                case "gone":       return ChatState.Gone;
            }

        }

        return null;

    }

}
