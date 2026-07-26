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
/// Builder für Roster-IQ-Stanzas
/// </summary>
public static class RosterStanzaBuilder
{
    public static string GetRoster(string? version = null)
    {
        var ver = version != null ? $" ver='{version}'" : "";
        return $"<iq type='get' id='roster1'>" +
               $"<query xmlns='jabber:iq:roster'{ver}/>" +
               $"</iq>";
    }

    public static string SetItem(string jid, string? name = null, IEnumerable<string>? groups = null)
    {
        var nameAttr = name != null ? $" name='{XmlEscaping.Escape(name)}'" : "";
        var groupsXml = groups != null
            ? string.Join("", groups.Select(g => $"<group>{XmlEscaping.Escape(g)}</group>"))
            : "";

        return $"<iq type='set' id='roster-set-{Guid.NewGuid():N}'>" +
               $"<query xmlns='jabber:iq:roster'>" +
               $"<item jid='{XmlEscaping.Escape(jid)}'{nameAttr}>{groupsXml}</item>" +
               $"</query></iq>";
    }

    public static string RemoveItem(string jid)
    {
        return $"<iq type='set' id='roster-remove-{Guid.NewGuid():N}'>" +
               $"<query xmlns='jabber:iq:roster'>" +
               $"<item jid='{XmlEscaping.Escape(jid)}' subscription='remove'/>" +
               $"</query></iq>";
    }

    public static string Subscribe(string jid) =>
        $"<presence to='{XmlEscaping.Escape(jid)}' type='subscribe'/>";

    public static string Subscribed(string jid) =>
        $"<presence to='{XmlEscaping.Escape(jid)}' type='subscribed'/>";

    public static string Unsubscribed(string jid) =>
        $"<presence to='{XmlEscaping.Escape(jid)}' type='unsubscribed'/>";

    public static string Unsubscribe(string jid) =>
        $"<presence to='{XmlEscaping.Escape(jid)}' type='unsubscribe'/>";
}
