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
/// Eine empfangene Chat-Nachricht.
/// </summary>
/// <param name="From">Absender (Full-JID)</param>
/// <param name="To">Empfänger (i. d. R. der eigene Full-JID)</param>
/// <param name="Body">Nachrichtentext</param>
/// <param name="MessageId">Stanza-ID, falls der Absender eine gesetzt hat</param>
/// <param name="Timestamp">
/// Wann die Nachricht <b>entstanden</b> ist, auf der lokalen Uhr: der Stempel
/// aus XEP-0203, wenn sie einen trägt, sonst der Zeitpunkt des Empfangs.
///
/// Hier stand bis D59 immer der Empfang. Für alles Laufende ist das dasselbe;
/// für eine nachgereichte Nachricht war es falsch, und zwar auf die
/// unangenehmste Art: Die Uhrzeit stand da und stimmte nicht.
/// </param>
/// <param name="Type">
/// Die Art der Nachricht (RFC 6121, Abschnitt 5.2.2). Ohne sie liesse sich
/// die Zeile aus einem Raum nicht von der eines Bekannten unterscheiden -
/// und beim Raum ist der Absender nicht einmal ein Mensch, sondern der Raum
/// selbst.
/// </param>
/// <param name="ReceivedAt">
/// Wann sie hier angekommen ist. Weicht sie von <paramref name="Timestamp"/>
/// ab, war sie unterwegs aufgehoben.
/// </param>
/// <param name="DelayedBy">
/// Wer sie aufgehoben hat, wenn er es gesagt hat (XEP-0203, Abschnitt 4) -
/// der Server, ein Raum. Freiwillig, deshalb oft null, auch bei einer
/// nachgereichten Nachricht.
/// </param>
public sealed record XMPPMessage(string       From,
                                 string       To,
                                 string       Body,
                                 string?      MessageId,
                                 DateTime     Timestamp,
                                 MessageType  Type        = MessageType.Normal,
                                 DateTime?    ReceivedAt  = null,
                                 string?      DelayedBy   = null)
{

    /// <summary>
    /// Absender ohne Resource.
    /// </summary>
    public string FromBareJid => JidUtilities.Bare(From);

    /// <summary>
    /// Wurde diese Nachricht aufgehoben und nachgereicht?
    /// </summary>
    /// <remarks>
    /// Am Zeitunterschied und nicht an <see cref="DelayedBy"/>: Das
    /// <c>from</c> des Stempels ist freiwillig, seine Abwesenheit sagt also
    /// nichts. Der Vergleich ist der einzige Beleg, den es immer gibt.
    /// </remarks>
    public bool IsDelayed
        => ReceivedAt.HasValue && ReceivedAt.Value != Timestamp;

}
