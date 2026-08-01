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
/// XEP-0060: Ein vom PubSub-Service zugestelltes Event.
/// </summary>
public sealed class PubSubEvent
{
    public string NodeId { get; }
    public PubSubEventType Type { get; }
    public List<PubSubItem> Items { get; } = [];
    public List<string> RetractedIds { get; } = [];

    /// <summary>
    /// Das Abonnement, zu dem diese Meldung gehört - aus der SHIM-Kopfzeile
    /// <c>SubID</c> (XEP-0060, Abschnitt 12.20), oder null.
    /// </summary>
    /// <remarks>
    /// Null heisst nicht „unbekannt", sondern meistens: Es gibt nur eines,
    /// und dann muss der Dienst die Kennung nicht mitschicken. Bei mehreren
    /// muss er - sonst wären zwei Zustellungen derselben Sache nicht
    /// auseinanderzuhalten.
    /// </remarks>
    public string? SubId { get; }

    public PubSubEvent(string nodeId, PubSubEventType type, string? subId = null)
    {
        NodeId = nodeId;
        Type = type;
        SubId = subId;
    }
}
