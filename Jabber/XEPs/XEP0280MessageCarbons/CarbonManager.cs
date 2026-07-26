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
/// XEP-0280: Message Carbons - spiegelt Nachrichten auf alle eigenen Geräte.
/// </summary>
public sealed class CarbonManager
{
    private readonly string _myBareJid;
    private bool _enabled;

    public bool IsEnabled => _enabled;

    public event Action<CarbonMessage>? OnCarbonReceived;
    public event Action<string>? OnParseError;

    public CarbonManager(string myBareJid)
    {
        _myBareJid = JidUtilities.Bare(myBareJid);
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>
    /// Verarbeitet eine Carbon-Nachricht mit Spoofing-Schutz
    /// </summary>
    public CarbonResult ProcessCarbon(string messageXml, string from)
    {
        var bareFrom = JidUtilities.Bare(from);

        // KRITISCHER SPOOFING-SCHUTZ:
        // Carbons dürfen NUR vom eigenen Bare-JID kommen (= vom Server)!
        if (!string.Equals(bareFrom, _myBareJid, StringComparison.OrdinalIgnoreCase))
        {
            return CarbonResult.SpoofingDetected;
        }

        // Prüfe ob sent oder received (mit Single- oder Double-Quotes)
        var isSent = messageXml.Contains("<sent") &&
                     messageXml.Contains("urn:xmpp:carbons:2");
        var isReceived = messageXml.Contains("<received") &&
                         messageXml.Contains("urn:xmpp:carbons:2") &&
                         !messageXml.Contains("urn:xmpp:receipts"); // Nicht mit XEP-0184 verwechseln!

        if (!isSent && !isReceived)
        {
            return CarbonResult.NotACarbon;
        }

        // Extrahiere die forwarded Message - flexiblerer Regex
        // Akzeptiert beide Quote-Styles und optionale Whitespace
        var forwardedMatch = Regex.Match(messageXml,
            @"<forwarded[^>]*>(.*)</forwarded>",
            RegexOptions.Singleline);

        if (!forwardedMatch.Success)
        {
            // Eventuell unvollständige Nachricht - versuche trotzdem zu parsen
            // Suche nach der inneren <message>
            var innerMsgStart = messageXml.IndexOf("<message",
                messageXml.IndexOf("<forwarded", StringComparison.Ordinal) + 1,
                StringComparison.Ordinal);

            if (innerMsgStart < 0)
            {
                OnParseError?.Invoke($"Kein </forwarded> gefunden, XML möglicherweise unvollständig");
                return CarbonResult.ParseError;
            }

            // Parse ab der inneren Message
            var forwardedXml = messageXml[innerMsgStart..];
            return ExtractAndFireCarbon(forwardedXml, isSent);
        }

        return ExtractAndFireCarbon(forwardedMatch.Groups[1].Value, isSent);
    }

    private CarbonResult ExtractAndFireCarbon(string forwardedXml, bool isSent)
    {
        // Extrahiere Attribute aus der inneren Message
        var originalFrom = ExtractAttribute(forwardedXml, "from");
        var originalTo = ExtractAttribute(forwardedXml, "to");
        var msgId = ExtractAttribute(forwardedXml, "id");

        if (originalFrom == null && originalTo == null)
        {
            OnParseError?.Invoke("Konnte from/to nicht aus Carbon extrahieren");
            return CarbonResult.ParseError;
        }

        // Body extrahieren
        var body = ExtractElement(forwardedXml, "body");

        var carbon = new CarbonMessage(
            isSent,
            originalFrom ?? "",
            originalTo ?? "",
            body,
            msgId);

        OnCarbonReceived?.Invoke(carbon);
        return CarbonResult.Success;
    }

    /// <summary>
    /// Erzeugt das IQ zum Aktivieren von Carbons
    /// </summary>
    public static string EnableIq(string id = "carbons-enable")
    {
        return $"<iq type='set' id='{id}'>" +
               $"<enable xmlns='urn:xmpp:carbons:2'/>" +
               $"</iq>";
    }

    /// <summary>
    /// Erzeugt das IQ zum Deaktivieren von Carbons
    /// </summary>
    public static string DisableIq(string id = "carbons-disable")
    {
        return $"<iq type='set' id='{id}'>" +
               $"<disable xmlns='urn:xmpp:carbons:2'/>" +
               $"</iq>";
    }

    private static string? ExtractAttribute(string xml, string name)
    {
        // Akzeptiert sowohl single als auch double quotes
        var match = Regex.Match(xml, $@"{name}\s*=\s*['""]([^'""]*)['""]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractElement(string xml, string name)
    {
        var match = Regex.Match(xml, $@"<{name}[^>]*>([^<]*)</{name}>");
        return match.Success ? match.Groups[1].Value : null;
    }
}
