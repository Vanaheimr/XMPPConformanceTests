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

#region Usings

using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0184: Erzeugt und erkennt Receipt-Elemente.
/// </summary>
public static class ReceiptBuilder
{

    /// <summary>Der Namespace von XEP-0184.</summary>
    public const string Namespace = "urn:xmpp:receipts";

    /// <summary>
    /// Erzeugt das XML für eine Receipt-Anfrage (in ausgehende Nachricht einfügen)
    /// </summary>
    public static string RequestXml => $"<request xmlns='{Namespace}'/>";

    /// <summary>
    /// Erzeugt eine Receipt-Antwort
    /// </summary>
    public static string CreateReceipt(string to, string originalMessageId)
    {
        return $"<message to='{XmlEscaping.Escape(to)}'>" +
               $"<received xmlns='{Namespace}' id='{XmlEscaping.Escape(originalMessageId)}'/>" +
               $"</message>";
    }

    /// <summary>
    /// Prüft, ob eine Nachricht um eine Quittung bittet.
    ///
    /// Die frühere Prüfung suchte wörtlich nach
    /// <c>xmlns='urn:xmpp:receipts'</c>, also nur mit einfachen
    /// Anführungszeichen - gegen einen Server, der doppelte benutzt, blieb
    /// jede Quittung aus. Ausserdem zählte ein <c>&lt;request/&gt;</c>
    /// irgendwo in der Nachricht, also auch eines in einer weitergeleiteten.
    /// </summary>
    public static bool HasReceiptRequest(XElement message)
        => message.Elements()
                  .Any(child => child.Name.NamespaceName == Namespace &&
                                child.Name.LocalName     == "request");

    /// <summary>
    /// Extrahiert die Quittungs-ID aus einer Quittung.
    ///
    /// Die Namespace-Prüfung trennt sie vom gleichnamigen
    /// <c>&lt;received/&gt;</c> aus XEP-0333.
    /// </summary>
    public static string? ExtractReceiptId(XElement message)
        => message.Elements()
                  .FirstOrDefault(child => child.Name.NamespaceName == Namespace &&
                                           child.Name.LocalName     == "received")
                  ?.Attr("id");
}
