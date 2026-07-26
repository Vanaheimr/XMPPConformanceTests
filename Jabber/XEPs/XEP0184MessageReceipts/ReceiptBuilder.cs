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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0184: Erzeugt und erkennt Receipt-Elemente.
/// </summary>
public static class ReceiptBuilder
{
    /// <summary>
    /// Erzeugt das XML für eine Receipt-Anfrage (in ausgehende Nachricht einfügen)
    /// </summary>
    public static string RequestXml => "<request xmlns='urn:xmpp:receipts'/>";

    /// <summary>
    /// Erzeugt eine Receipt-Antwort
    /// </summary>
    public static string CreateReceipt(string to, string originalMessageId)
    {
        return $"<message to='{XmlEscaping.Escape(to)}'>" +
               $"<received xmlns='urn:xmpp:receipts' id='{XmlEscaping.Escape(originalMessageId)}'/>" +
               $"</message>";
    }

    /// <summary>
    /// Prüft ob eine Nachricht eine Receipt-Anfrage enthält
    /// </summary>
    public static bool HasReceiptRequest(string messageXml)
    {
        return messageXml.Contains("xmlns='urn:xmpp:receipts'") &&
               messageXml.Contains("<request");
    }

    /// <summary>
    /// Extrahiert die Receipt-ID aus einer Receipt-Nachricht
    /// </summary>
    public static string? ExtractReceiptId(string messageXml)
    {
        var match = Regex.Match(messageXml, @"<received[^>]+id=['""]([^'""]+)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }
}
