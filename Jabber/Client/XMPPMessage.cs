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
/// <param name="Timestamp">Zeitpunkt des Empfangs (lokale Uhr)</param>
public sealed record XMPPMessage(string    From,
                                 string    To,
                                 string    Body,
                                 string?   MessageId,
                                 DateTime  Timestamp)
{

    /// <summary>
    /// Absender ohne Resource.
    /// </summary>
    public string FromBareJid => JidUtilities.Bare(From);

}
