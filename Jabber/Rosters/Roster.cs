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
/// Roster-Manager mit Subscription-Handling
/// </summary>
public sealed class Roster
{
    private readonly Dictionary<string, RosterItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public string? Version { get; set; }

    public event Action<RosterItem>? OnItemAdded;
    public event Action<RosterItem>? OnItemUpdated;
    public event Action<string>? OnItemRemoved;
    public event Action<string, string>? OnSubscriptionRequest;

    public IReadOnlyCollection<RosterItem> Items
    {
        get { lock (_lock) return _items.Values.ToList(); }
    }

    public RosterItem? GetItem(string jid)
    {
        var bareJid = JidUtilities.Bare(jid);
        lock (_lock)
        {
            return _items.TryGetValue(bareJid, out var item) ? item : null;
        }
    }

    public void ProcessRosterItem(RosterItem newItem)
    {
        var bareJid = JidUtilities.Bare(newItem.Jid);

        lock (_lock)
        {
            if (_items.TryGetValue(bareJid, out var existing))
            {
                existing.Name = newItem.Name;
                existing.Subscription = newItem.Subscription;
                existing.Groups.Clear();
                existing.Groups.AddRange(newItem.Groups);
                OnItemUpdated?.Invoke(existing);
            }
            else
            {
                _items[bareJid] = newItem;
                OnItemAdded?.Invoke(newItem);
            }
        }
    }

    public void RemoveItem(string jid)
    {
        var bareJid = JidUtilities.Bare(jid);
        lock (_lock)
        {
            if (_items.Remove(bareJid))
            {
                OnItemRemoved?.Invoke(bareJid);
            }
        }
    }

    public void RaiseSubscriptionRequest(string from, string status)
    {
        OnSubscriptionRequest?.Invoke(from, status);
    }

    public void UpdatePresence(string from, string type, string? show, string? status)
    {
        var bareJid = JidUtilities.Bare(from);

        lock (_lock)
        {
            if (!_items.TryGetValue(bareJid, out var item))
            {
                return;
            }

            if (type == "unavailable")
            {
                item.Presence = PresenceState.Offline;
                item.PresenceStatus = null;
            }
            else
            {
                item.Presence = show switch
                {
                    "away" => PresenceState.Away,
                    "chat" => PresenceState.Chat,
                    "dnd" => PresenceState.Dnd,
                    "xa" => PresenceState.Xa,
                    _ => PresenceState.Available
                };
                item.PresenceStatus = status;
            }

            item.LastSeen = DateTime.UtcNow;
            OnItemUpdated?.Invoke(item);
        }
    }

    public IEnumerable<RosterItem> GetOnlineContacts()
    {
        lock (_lock)
        {
            return _items.Values
                .Where(i => i.Presence != PresenceState.Offline)
                .OrderBy(i => i.DisplayName)
                .ToList();
        }
    }

    public IEnumerable<RosterItem> GetByGroup(string group)
    {
        lock (_lock)
        {
            return _items.Values
                .Where(i => i.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
                .OrderBy(i => i.DisplayName)
                .ToList();
        }
    }

    public IEnumerable<string> GetGroups()
    {
        lock (_lock)
        {
            return _items.Values
                .SelectMany(i => i.Groups)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .ToList();
        }
    }
}
