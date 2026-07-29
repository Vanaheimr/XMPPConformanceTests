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

    /// <summary>
    /// RFC 6121, Abschnitt 2.1.4: Übernimmt das Ergebnis einer Roster-Anfrage
    /// als den vollständigen Roster.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zu <see cref="ProcessRosterItem"/> ist das Entfernen.
    /// Ein Roster-Ergebnis ist keine Ergänzung, sondern der Stand: Was nicht
    /// darin steht, gibt es nicht mehr.
    ///
    /// Vorher wurde es hineingemischt, und die Folge war ein Kontakt, den man
    /// nicht loswird. Wer ihn an einem anderen Gerät löscht, während dieses
    /// hier abgemeldet ist, bekommt ihn beim nächsten Anmelden zurück - der
    /// Server schickt ihn nicht mehr, aber niemand nimmt ihn heraus. Beim
    /// Löschen im laufenden Betrieb fällt das nie auf, weil dann ein Push mit
    /// <c>subscription='remove'</c> kommt.
    ///
    /// Gerufen wird das ausschliesslich für das Ergebnis, nie für einen Push.
    /// Ein Push trägt genau die geänderten Einträge; ihn so zu behandeln
    /// löschte bei jeder Änderung den ganzen übrigen Roster.
    /// </remarks>
    public void ReplaceAll(IEnumerable<RosterItem> items)
    {

        var neu       = items.ToList();
        var behalten  = new HashSet<string>(neu.Select(i => JidUtilities.Bare(i.Jid)),
                                            StringComparer.OrdinalIgnoreCase);

        List<string> entfallen;

        lock (_lock)
            entfallen = _items.Keys.Where(k => !behalten.Contains(k)).ToList();

        // Ausserhalb der Sperre: beide Aufrufe nehmen sie selbst, und die
        // Ereignisse sollen nicht unter ihr laufen.
        foreach (var item in neu)
            ProcessRosterItem(item);

        foreach (var jid in entfallen)
            RemoveItem(jid);

    }

    /// <summary>
    /// RFC 6121, Abschnitt 3: Wendet eine Subscription-Änderung an, die als
    /// Presence-Stanza hereinkommt.
    /// </summary>
    /// <remarks>
    /// Der maßgebliche Zustand kommt vom Server als Roster-Push; diese Stanzas
    /// sind die Benachrichtigung dazu. Sie hier trotzdem auszuwerten hält den
    /// Roster auch dann richtig, wenn der Push ausbleibt - vor allem aber
    /// hält es sie von <see cref="UpdatePresence"/> fern, wo alles ohne
    /// <c>type='unavailable'</c> als anwesend zählt.
    ///
    /// Ein unbekannter Kontakt wird bewusst nicht angelegt: Einträge entstehen
    /// durch den Roster-Push, nicht durch eine Presence.
    /// </remarks>
    /// <param name="from">Absender der Stanza.</param>
    /// <param name="type">subscribed, unsubscribed oder unsubscribe.</param>
    public void ProcessSubscriptionChange(string from, string type)
    {
        var bareJid = JidUtilities.Bare(from);

        lock (_lock)
        {
            if (!_items.TryGetValue(bareJid, out var item))
                return;

            item.Subscription = type switch
            {
                "subscribed"    => item.Subscription.GrantTo(),
                "unsubscribed"  => item.Subscription.RevokeTo(),
                "unsubscribe"   => item.Subscription.RevokeFrom(),
                _               => item.Subscription
            };

            // Ohne 'to' kommt keine Presence mehr herein. Was zuletzt bekannt
            // war, würde ab jetzt beliebig alt - der Kontakt gilt deshalb als
            // offline, statt auf ewig im letzten gesehenen Zustand zu stehen.
            if (type == "unsubscribed")
            {
                item.Presence        = PresenceState.Offline;
                item.PresenceStatus  = null;
            }

            OnItemUpdated?.Invoke(item);
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
