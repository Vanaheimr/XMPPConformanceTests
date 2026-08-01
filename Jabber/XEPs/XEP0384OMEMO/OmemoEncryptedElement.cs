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
/// Ein Schlüssel für genau ein Gerät eines Empfängers.
/// </summary>
/// <param name="DeviceId">Die Gerätekennung (<c>rid</c>).</param>
/// <param name="Data">
/// Die durch den Ratchet verschlüsselten 48 Byte - je nachdem als
/// <c>OMEMOAuthenticatedMessage</c> oder als <c>OMEMOKeyExchange</c>.
/// </param>
/// <param name="IsKeyExchange">
/// Trägt dieser Eintrag einen Schlüsselaustausch (<c>kex='true'</c>)?
/// </param>
public sealed record OmemoKey(UInt32 DeviceId, Byte[] Data, Boolean IsKeyExchange);

/// <summary>
/// Das <c>&lt;encrypted/&gt;</c>-Element (XEP-0384, Abschnitt 4.5).
/// </summary>
/// <param name="SenderDeviceId">Das eigene Gerät (<c>sid</c>).</param>
/// <param name="Keys">
/// Je Empfänger-JID die Schlüssel für seine Geräte.
/// </param>
/// <param name="Payload">
/// Die verschlüsselte Nutzlast, oder null für eine Nachricht ohne Inhalt.
/// </param>
/// <remarks>
/// <b>Warum die Empfänger nach JID gruppiert sind.</b> Eine Nachricht geht an
/// alle Geräte aller Beteiligten - auch an die eigenen, sonst sähe der eigene
/// Rechner nicht, was das eigene Telefon geschrieben hat. Die Gruppierung
/// hält fest, <i>wessen</i> Gerät gemeint ist, und das ist mehr als Ordnung:
/// Ohne sie liesse sich ein Schlüsseleintrag für ein Gerät ausgeben, das
/// jemand ganz anderem gehört.
///
/// <b>Eine Nachricht ohne <c>&lt;payload/&gt;</c> ist kein Fehler.</b> Sie
/// heisst „ich habe die Sitzung neu aufgebaut" und trägt nichts als den
/// Schlüsselaustausch - so bekommt eine Gegenstelle eine Sitzung, ohne dass
/// ein Mensch etwas schreiben müsste.
/// </remarks>
public sealed record OmemoEncryptedElement(UInt32                                        SenderDeviceId,
                                           IReadOnlyDictionary<String, IReadOnlyList<OmemoKey>>  Keys,
                                           Byte[]?                                       Payload)
{

    /// <summary>Der Namespace von OMEMO 2.</summary>
    public const String Namespace = "urn:xmpp:omemo:2";

    #region ToXml()

    /// <summary>Das Element als XML.</summary>
    public XElement ToXml()
    {

        XNamespace ns = Namespace;

        var header = new XElement(ns + "header", new XAttribute("sid", SenderDeviceId));

        foreach (var (jid, schluessel) in Keys)
        {

            var keys = new XElement(ns + "keys", new XAttribute("jid", jid));

            foreach (var k in schluessel)
                keys.Add(new XElement(ns + "key",
                                      new XAttribute("rid", k.DeviceId),
                                      // Das Attribut steht nur da, wo es etwas
                                      // aussagt: Abschnitt 4.5 gibt ihm den
                                      // Vorgabewert 'false', und ein
                                      // ausgeschriebener Vorgabewert ist eine
                                      // Zeile, die bei jeder Nachricht mitreist,
                                      // ohne je etwas zu bedeuten.
                                      k.IsKeyExchange ? new XAttribute("kex", "true") : null,
                                      Convert.ToBase64String(k.Data)));

            header.Add(keys);

        }

        var encrypted = new XElement(ns + "encrypted", header);

        if (Payload is not null)
            encrypted.Add(new XElement(ns + "payload", Convert.ToBase64String(Payload)));

        return encrypted;

    }

    #endregion

    #region TryRead(stanza, out ...)

    /// <summary>
    /// Liest ein <c>&lt;encrypted/&gt;</c> aus einer Stanza.
    /// </summary>
    /// <remarks>
    /// <b>Nur direkte Kinder</b> - dieselbe Falle wie beim Verzugsstempel
    /// (D59) und bei der Korrektur (D60): Ein Carbon bringt in seinem
    /// <c>&lt;forwarded/&gt;</c> eine vollständige eigene Nachricht mit, und
    /// deren Verschlüsselung gehört nicht der äusseren.
    ///
    /// Was sich nicht lesen lässt, ergibt <c>false</c> und keine Ausnahme:
    /// Eine unverständliche Nachricht ist für den Empfänger dasselbe wie
    /// keine, und ein Absturz wäre die schlechtere Antwort - er liesse sich
    /// von jedem auslösen, der ein <c>&lt;key/&gt;</c> mit krummem Base64
    /// schickt.
    /// </remarks>
    public static Boolean TryRead(XElement stanza, out OmemoEncryptedElement? element)
    {

        element = null;

        var encrypted = stanza.Child(Namespace, "encrypted");

        if (encrypted is null)
            return false;

        try
        {

            var header = encrypted.Child(Namespace, "header");

            if (header is null || !UInt32.TryParse(header.Attr("sid"), out var sid))
                return false;

            var alle = new Dictionary<String, IReadOnlyList<OmemoKey>>(StringComparer.OrdinalIgnoreCase);

            foreach (var keys in header.Elements().Where(e => e.Name.LocalName == "keys"))
            {

                var jid = keys.Attr("jid");

                if (String.IsNullOrEmpty(jid))
                    return false;

                var liste = new List<OmemoKey>();

                foreach (var key in keys.Elements().Where(e => e.Name.LocalName == "key"))
                {

                    if (!UInt32.TryParse(key.Attr("rid"), out var rid))
                        return false;

                    liste.Add(new OmemoKey(rid,
                                           Convert.FromBase64String(key.Value.Trim()),
                                           key.Attr("kex") is "true" or "1"));

                }

                alle[jid] = liste;

            }

            var payload = encrypted.Child(Namespace, "payload")?.Value.Trim();

            element = new OmemoEncryptedElement(
                          sid,
                          alle,
                          String.IsNullOrEmpty(payload) ? null : Convert.FromBase64String(payload));

            return true;

        }
        catch (Exception)
        {
            return false;
        }

    }

    #endregion

    #region KeyFor(jid, deviceId)

    /// <summary>
    /// Der Eintrag für dieses Gerät dieses JIDs, oder null.
    /// </summary>
    /// <remarks>
    /// Beides zusammen und nicht nur die Gerätekennung: Zwei Konten können
    /// dieselbe Kennung tragen - sie ist eine Zufallszahl je Gerät und
    /// niemandem sonst bekannt. Wer nur nach ihr suchte, nähme unter
    /// Umständen den Eintrag, der für ein fremdes Konto bestimmt war, und
    /// scheiterte dann an einer Entschlüsselung, deren Grund er nicht sieht.
    /// </remarks>
    public OmemoKey? KeyFor(String bareJid, UInt32 deviceId)
        => Keys.TryGetValue(bareJid, out var liste)
               ? liste.FirstOrDefault(k => k.DeviceId == deviceId)
               : null;

    #endregion

}
