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
/// XEP-0085: Serialisierung und Parsing von Chat States.
/// </summary>
public static class ChatStateExtensions
{
    public static string ToXml(this ChatState state) => state switch
    {
        ChatState.Active    => "<active xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Composing => "<composing xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Paused    => "<paused xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Inactive  => "<inactive xmlns='http://jabber.org/protocol/chatstates'/>",
        ChatState.Gone      => "<gone xmlns='http://jabber.org/protocol/chatstates'/>",
        _ => ""
    };

    public static ChatState? ParseChatState(string xml)
    {
        if (xml.Contains("<active"))    return ChatState.Active;
        if (xml.Contains("<composing")) return ChatState.Composing;
        if (xml.Contains("<paused"))    return ChatState.Paused;
        if (xml.Contains("<inactive"))  return ChatState.Inactive;
        if (xml.Contains("<gone"))      return ChatState.Gone;
        return null;
    }
}
