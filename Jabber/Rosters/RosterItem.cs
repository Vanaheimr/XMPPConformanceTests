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
/// Repräsentiert einen Kontakt im Roster
/// </summary>
public sealed class RosterItem
{
    public string Jid { get; }
    public string? Name { get; set; }
    public SubscriptionState Subscription { get; set; } = SubscriptionState.None;
    public List<string> Groups { get; } = [];

    public PresenceState Presence { get; set; } = PresenceState.Offline;
    public string? PresenceStatus { get; set; }
    public DateTime LastSeen { get; set; }

    public RosterItem(string jid)
    {
        Jid = jid.ToLowerInvariant();
    }

    public string DisplayName => Name ?? Jid;

    public string BareJid
    {
        get
        {
            var slash = Jid.IndexOf('/');
            return slash > 0 ? Jid[..slash] : Jid;
        }
    }

    public override string ToString()
    {
        var sub = Subscription switch
        {
            SubscriptionState.Both => "↔",
            SubscriptionState.To => "→",
            SubscriptionState.From => "←",
            _ => "○"
        };

        var pres = Presence switch
        {
            PresenceState.Available => "●",
            PresenceState.Away => "◐",
            PresenceState.Dnd => "⊘",
            PresenceState.Xa => "◑",
            PresenceState.Chat => "◉",
            _ => "○"
        };

        var groups = Groups.Count > 0 ? $" [{string.Join(", ", Groups)}]" : "";
        return $"{pres} {sub} {DisplayName}{groups}";
    }
}
