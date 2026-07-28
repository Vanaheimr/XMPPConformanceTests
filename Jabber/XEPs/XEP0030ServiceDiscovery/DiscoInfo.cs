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
/// XEP-0030: Ergebnis einer disco#info-Abfrage (Identitäten + Features).
/// </summary>
public sealed class DiscoInfo
{
    public string From { get; init; } = "";
    public string? Node { get; init; }
    public List<DiscoIdentity> Identities { get; } = [];
    public List<string> Features { get; } = [];

    /// <summary>
    /// Trug die Antwort ein Datenformular (XEP-0128)?
    /// </summary>
    /// <remarks>
    /// Der Inhalt wird nicht ausgewertet; gemerkt wird nur, dass er da war.
    /// Das genügt für den einen Zweck, den es hat: XEP-0115, Abschnitt 5.1
    /// lässt Datenformulare in den Verification String eingehen, und wer sie
    /// nicht mitrechnet, darf über einen Hash, der über sie geht, auch keine
    /// Aussage treffen (siehe <see cref="EntityCapsManager"/>).
    /// </remarks>
    public bool HasExtendedInfo { get; internal set; }

    public bool HasFeature(string feature) => Features.Contains(feature);
    public bool SupportsReceipts => HasFeature("urn:xmpp:receipts");
    public bool SupportsCarbons => HasFeature("urn:xmpp:carbons:2");
    public bool SupportsChatStates => HasFeature("http://jabber.org/protocol/chatstates");
    public bool SupportsChatMarkers => HasFeature("urn:xmpp:chat-markers:0");
    public bool SupportsStreamManagement => HasFeature("urn:xmpp:sm:3");
}
