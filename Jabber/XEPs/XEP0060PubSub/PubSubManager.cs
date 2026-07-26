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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0060: Verwaltet PubSub-Abonnements und verarbeitet eingehende Events.
/// </summary>
public sealed class PubSubManager
{
    private readonly HashSet<string> _subscribedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _pubsubService;
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public event Action<PubSubEvent>? OnEvent;
    public event Action<string, bool, string?>? OnSubscriptionResult; // nodeId, success, error

    public PubSubManager(string pubsubService = "pubsub", ILogger? logger = null)
    {
        _pubsubService = pubsubService;
        _logger = logger ?? NullLogger.Instance;
    }

    public string PubSubService => _pubsubService;

    /// <summary>
    /// Verarbeitet eine eingehende PubSub-Event-Nachricht mit Spoofing-Schutz
    /// </summary>
    public bool ProcessEvent(string messageXml, string from, string expectedPubSubJid)
    {
        var bareFrom = JidUtilities.Bare(from);
        var expectedBare = JidUtilities.Bare(expectedPubSubJid);

        // SPOOFING-SCHUTZ: Events dürfen nur vom PubSub-Service kommen
        if (!string.Equals(bareFrom, expectedBare, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PubSub-Spoofing erkannt! Von: {From}, erwartet: {Expected}",
                               from, expectedPubSubJid);
            return false;
        }

        // Parse event
        var eventMatch = Regex.Match(messageXml,
            @"<event\s+xmlns='http://jabber\.org/protocol/pubsub#event'[^>]*>(.*?)</event>",
            RegexOptions.Singleline);

        if (!eventMatch.Success)
        {
            return false;
        }

        var eventXml = eventMatch.Groups[1].Value;

        // Items Event
        var itemsMatch = Regex.Match(eventXml, @"<items\s+node=['""]([^'""]+)['""][^>]*>(.*?)</items>", RegexOptions.Singleline);
        if (itemsMatch.Success)
        {
            var nodeId = itemsMatch.Groups[1].Value;
            var itemsXml = itemsMatch.Groups[2].Value;

            var pubsubEvent = new PubSubEvent(nodeId, PubSubEventType.Items);

            var items = Regex.Matches(itemsXml, @"<item\s+id=['""]([^'""]+)['""][^>]*>(.*?)</item>", RegexOptions.Singleline);
            foreach (Match item in items)
            {
                pubsubEvent.Items.Add(new PubSubItem(
                    item.Groups[1].Value,
                    nodeId,
                    item.Groups[2].Value
                ));
            }

            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Retract Event
        var retractMatch = Regex.Match(eventXml, @"<items\s+node=['""]([^'""]+)['""][^>]*>.*?<retract\s+id=['""]([^'""]+)['""]", RegexOptions.Singleline);
        if (retractMatch.Success)
        {
            var nodeId = retractMatch.Groups[1].Value;
            var retractId = retractMatch.Groups[2].Value;

            var pubsubEvent = new PubSubEvent(nodeId, PubSubEventType.Retract);
            pubsubEvent.RetractedIds.Add(retractId);

            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Purge Event
        var purgeMatch = Regex.Match(eventXml, @"<purge\s+node=['""]([^'""]+)['""]");
        if (purgeMatch.Success)
        {
            var pubsubEvent = new PubSubEvent(purgeMatch.Groups[1].Value, PubSubEventType.Purge);
            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        // Delete Event
        var deleteMatch = Regex.Match(eventXml, @"<delete\s+node=['""]([^'""]+)['""]");
        if (deleteMatch.Success)
        {
            var pubsubEvent = new PubSubEvent(deleteMatch.Groups[1].Value, PubSubEventType.Delete);
            OnEvent?.Invoke(pubsubEvent);
            return true;
        }

        return false;
    }

    public void AddSubscription(string nodeId)
    {
        lock (_lock) _subscribedNodes.Add(nodeId);
    }

    public void RemoveSubscription(string nodeId)
    {
        lock (_lock) _subscribedNodes.Remove(nodeId);
    }

    public bool IsSubscribed(string nodeId)
    {
        lock (_lock) return _subscribedNodes.Contains(nodeId);
    }
}
