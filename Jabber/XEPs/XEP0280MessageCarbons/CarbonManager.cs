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
/// XEP-0280: Message Carbons - spiegelt Nachrichten auf alle eigenen Geräte.
/// </summary>
public sealed class CarbonManager
{

    /// <summary>Der Namespace von XEP-0280.</summary>
    public const string Namespace = "urn:xmpp:carbons:2";

    /// <summary>Der Namespace von XEP-0297, in dem die Nachricht steckt.</summary>
    public const string ForwardNamespace = "urn:xmpp:forward:0";

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
    /// Verarbeitet eine Carbon-Nachricht mit Spoofing-Schutz.
    ///
    /// Die Unterscheidung von XEP-0184 lief früher über einen Ausschluss
    /// (<c>!messageXml.Contains("urn:xmpp:receipts")</c>), weil beide
    /// Erweiterungen ein <c>&lt;received/&gt;</c> kennen. Mit dem Namespace am
    /// Element ist die Unterscheidung direkt und ohne Nebenwirkungen möglich.
    /// </summary>
    public CarbonResult ProcessCarbon(XElement message, string from)
    {

        var bareFrom = JidUtilities.Bare(from);

        // KRITISCHER SPOOFING-SCHUTZ:
        // Carbons dürfen NUR vom eigenen Bare-JID kommen (= vom Server)!
        if (!string.Equals(bareFrom, _myBareJid, StringComparison.OrdinalIgnoreCase))
            return CarbonResult.SpoofingDetected;

        var carbonElement = message.Elements()
                                   .FirstOrDefault(child => child.Name.NamespaceName == Namespace &&
                                                            (child.Name.LocalName == "sent" ||
                                                             child.Name.LocalName == "received"));

        if (carbonElement is null)
            return CarbonResult.NotACarbon;

        var isSent = carbonElement.Name.LocalName == "sent";

        var inner = carbonElement.Elements()
                                 .FirstOrDefault(child => child.Name.NamespaceName == ForwardNamespace &&
                                                          child.Name.LocalName     == "forwarded")
                                ?.Elements()
                                 .FirstOrDefault(child => child.Name.LocalName == "message");

        if (inner is null)
        {
            OnParseError?.Invoke("Carbon ohne eingebettete Nachricht");
            return CarbonResult.ParseError;
        }

        var originalFrom  = inner.Attr("from");
        var originalTo    = inner.Attr("to");

        if (originalFrom is null && originalTo is null)
        {
            OnParseError?.Invoke("Konnte from/to nicht aus Carbon extrahieren");
            return CarbonResult.ParseError;
        }

        OnCarbonReceived?.Invoke(new CarbonMessage(isSent,
                                                   originalFrom ?? "",
                                                   originalTo   ?? "",
                                                   inner.ChildValue("body"),
                                                   inner.Attr("id")));

        return CarbonResult.Success;

    }

    /// <summary>
    /// Erzeugt das IQ zum Aktivieren von Carbons
    /// </summary>
    public static string EnableIq(string id = "carbons-enable")
    {
        return $"<iq type='set' id='{id}'>" +
               $"<enable xmlns='{Namespace}'/>" +
               $"</iq>";
    }

}
