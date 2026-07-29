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
/// Die Art einer Nachricht (RFC 6121, Abschnitt 5.2.2).
/// </summary>
/// <remarks>
/// Der Unterschied ist keine Verzierung: Er entscheidet, wohin eine Nachricht
/// in der Oberfläche gehört und ob überhaupt eine Antwort erwartet wird. Bis
/// hierher kam alles gleich an, und der Empfänger konnte den Zuruf einer
/// Nachrichtenagentur nicht von der Zeile eines Bekannten unterscheiden.
/// </remarks>
public enum MessageType
{

    /// <summary>
    /// Eine einzelne Nachricht ausserhalb eines Gesprächs - und die Vorgabe,
    /// wenn das Attribut fehlt oder unbekannt ist.
    /// </summary>
    Normal,

    /// <summary>Teil eines Gesprächs unter vier Augen.</summary>
    Chat,

    /// <summary>Aus einem Mehrpersonenraum (XEP-0045).</summary>
    GroupChat,

    /// <summary>
    /// Ein Zuruf: Meldung, Benachrichtigung, Kurs - „no reply is expected".
    /// </summary>
    Headline,

    /// <summary>
    /// Die Antwort auf eine Nachricht, die die Gegenstelle nicht verarbeiten
    /// konnte. Sie trägt keine Nutzlast, sondern die Begründung.
    /// </summary>
    Error

}
