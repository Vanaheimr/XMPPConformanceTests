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
/// Der <c>type</c> einer Nachricht, gelesen und geschrieben.
/// </summary>
public static class MessageTypeExtensions
{

    #region Parse(Value)

    /// <summary>
    /// Liest das <c>type</c>-Attribut einer Nachricht.
    /// </summary>
    /// <remarks>
    /// RFC 6121, Abschnitt 5.2.2 ist an dieser Stelle ungewöhnlich deutlich:
    /// Fehlt das Attribut <b>oder versteht der Empfänger seinen Wert nicht</b>,
    /// MUSS die Nachricht als <c>normal</c> gelten. Ein unbekannter Wert ist
    /// also kein Fehler und darf die Nachricht nicht verschwinden lassen - eine
    /// spätere Erweiterung soll bei alten Empfängern schlicht als gewöhnliche
    /// Nachricht ankommen.
    /// </remarks>
    public static MessageType Parse(String? Value)

        => Value switch {
               "chat"       => MessageType.Chat,
               "groupchat"  => MessageType.GroupChat,
               "headline"   => MessageType.Headline,
               "error"      => MessageType.Error,
               _            => MessageType.Normal
           };

    #endregion

    #region AsAttribute(Type)

    /// <summary>
    /// Der Wert für das <c>type</c>-Attribut, oder null für
    /// <see cref="MessageType.Normal"/> - der Vorgabewert wird nicht
    /// geschrieben.
    /// </summary>
    public static String? AsAttribute(this MessageType Type)

        => Type switch {
               MessageType.Chat       => "chat",
               MessageType.GroupChat  => "groupchat",
               MessageType.Headline   => "headline",
               MessageType.Error      => "error",
               _                      => null
           };

    #endregion

    #region ExpectsAReply(Type)

    /// <summary>
    /// Darf auf eine Nachricht dieser Art von selbst geantwortet werden -
    /// Empfangsbestätigung (XEP-0184) oder Marker (XEP-0333)?
    /// </summary>
    /// <remarks>
    /// Bei <see cref="MessageType.Headline"/> sagt es RFC 6121,
    /// Abschnitt 5.2.2 selbst: „no reply is expected". Eine
    /// Empfangsbestätigung an eine Nachrichtenquelle ist im besten Fall
    /// nutzlos.
    ///
    /// Bei <see cref="MessageType.GroupChat"/> ist der Grund handfester: Der
    /// Absender ist der Raum, nicht ein Mensch. Eine Bestätigung ginge an den
    /// Raum, und der reicht sie an alle darin weiter - aus einer stillen
    /// Quittung würde eine Wortmeldung vor Publikum, und zwar von jedem
    /// Anwesenden für jede Nachricht.
    /// </remarks>
    public static Boolean ExpectsAReply(this MessageType Type)

        => Type is MessageType.Normal or MessageType.Chat;

    #endregion

}
