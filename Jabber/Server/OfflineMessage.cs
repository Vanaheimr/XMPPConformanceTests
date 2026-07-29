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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Eine aufbewahrte Nachricht für ein Konto, das gerade keine erreichbare
    /// Resource hat (RFC 6121, Abschnitt 8.5.2.2.1).
    /// </summary>
    /// <param name="Stanza">
    /// Die vollständige Stanza, wie sie zugestellt worden wäre - mit gesetztem
    /// <c>from</c>.
    /// </param>
    /// <param name="StoredAt">
    /// Wann sie hereinkam. Der Zeitpunkt gehört zur Nachricht und nicht zur
    /// Zustellung: er wird beim Nachreichen als XEP-0203
    /// <c>&lt;delay/&gt;</c> mitgegeben, damit der Empfänger eine Nachricht von
    /// gestern nicht für eine von jetzt hält.
    /// </param>
    public sealed record OfflineMessage(String          Stanza,
                                        DateTimeOffset  StoredAt);

}
