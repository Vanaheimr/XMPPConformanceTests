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
/// Ein Gerät in der Geräteliste.
/// </summary>
/// <param name="Id">Die Gerätekennung.</param>
/// <param name="Label">
/// Ein Name, den ein Mensch lesen kann - „Telefon", „Rechner im Büro". Er ist
/// freiwillig und unbeglaubigt.
/// </param>
public sealed record OmemoDevice(UInt32 Id, String? Label = null);

/// <summary>
/// Die Geräteliste eines Kontos (XEP-0384, Abschnitt 5.2).
/// </summary>
/// <remarks>
/// <b>Sie ist öffentlich, und das ist der Preis des Verfahrens.</b> Wer wissen
/// will, mit wie vielen Geräten jemand am Netz hängt, muss nur diesen Knoten
/// abfragen. Das lässt sich nicht vermeiden, ohne die Erreichbarkeit
/// aufzugeben: Ein Absender muss für jedes Gerät verschlüsseln, also muss er
/// jedes kennen.
///
/// Die Bezeichnung ist deshalb mit Bedacht zu wählen - „Achims Telefon" steht
/// für jeden lesbar da, der den Knoten abruft.
/// </remarks>
public sealed record OmemoDeviceList(IReadOnlyList<OmemoDevice> Devices)
{

    /// <summary>Der PEP-Knoten der Geräteliste.</summary>
    public const String Node = "urn:xmpp:omemo:2:devices";

    /// <summary>
    /// Die Kennung des einzigen Eintrags in diesem Knoten.
    /// </summary>
    /// <remarks>
    /// Ein fester Wert und keine laufende Nummer: Der Knoten trägt genau eine
    /// Liste, und ein zweiter Eintrag daneben wäre keine zweite Liste, sondern
    /// eine Unklarheit darüber, welche gilt.
    /// </remarks>
    public const String ItemId = "current";

    #region ToXml() / TryRead(xml, out list)

    /// <summary>Die Liste als XML.</summary>
    public XElement ToXml()
    {

        XNamespace ns = OmemoEncryptedElement.Namespace;

        return new XElement(ns + "devices",
                            Devices.Select(d => new XElement(ns + "device",
                                                             new XAttribute("id", d.Id),
                                                             d.Label is not null
                                                                 ? new XAttribute("label", d.Label)
                                                                 : null)));

    }

    /// <summary>
    /// Liest eine Geräteliste.
    /// </summary>
    /// <remarks>
    /// Ein Gerät ohne lesbare Kennung wird <b>übergangen</b> und nicht zum
    /// Fehler der ganzen Liste. Der Grund ist die Erreichbarkeit: Eine Liste
    /// mit einem krummen Eintrag ist immer noch eine Liste, und wer sie ganz
    /// verwürfe, könnte an keines der übrigen Geräte mehr schreiben. Ein
    /// einzelner unbrauchbarer Eintrag darf nicht alle anderen mitnehmen.
    /// </remarks>
    public static Boolean TryRead(XElement xml, out OmemoDeviceList? list)
    {

        list = null;

        if (xml.Name.LocalName     != "devices" ||
            xml.Name.NamespaceName != OmemoEncryptedElement.Namespace)
            return false;

        var geraete = new List<OmemoDevice>();

        foreach (var device in xml.Elements().Where(e => e.Name.LocalName == "device"))
            if (UInt32.TryParse(device.Attr("id"), out var id) && id > 0)
                geraete.Add(new OmemoDevice(id, device.Attr("label")));

        list = new OmemoDeviceList(geraete);

        return true;

    }

    #endregion

    #region Contains(deviceId) / With(device)

    /// <summary>Steht dieses Gerät in der Liste?</summary>
    public Boolean Contains(UInt32 deviceId)
        => Devices.Any(d => d.Id == deviceId);

    /// <summary>
    /// Die Liste mit diesem Gerät - unverändert, wenn es schon darin steht.
    /// </summary>
    /// <remarks>
    /// Ergänzt und ersetzt nicht: Abschnitt 5.2 verlangt vom Client, sich
    /// wieder einzutragen, wenn er aus der Liste verschwunden ist - <b>ohne
    /// die anderen zu entfernen</b>. Wer hier eine neue Liste mit nur dem
    /// eigenen Gerät veröffentlichte, machte aus einem Wiedereintrag eine
    /// Verdrängung aller anderen Geräte des Menschen.
    /// </remarks>
    public OmemoDeviceList With(OmemoDevice device)
        => Contains(device.Id)
               ? this
               : new OmemoDeviceList([.. Devices, device]);

    #endregion

}

/// <summary>
/// Die PEP-Seite von OMEMO: Geräteliste und Bundles (XEP-0384, Abschnitt 5.2).
/// </summary>
/// <remarks>
/// <b>Warum die Bundles je Gerät einen eigenen Eintrag bekommen.</b> Der
/// Knoten <c>urn:xmpp:omemo:2:bundles</c> trägt einen Eintrag pro Gerät, mit
/// der Gerätekennung als Eintragskennung. So holt ein Absender genau das
/// Bundle, das er braucht, statt aller - und ein Gerät, das seinen PreKey
/// verbraucht hat, schreibt nur seinen eigenen Eintrag neu und stört die
/// anderen nicht.
/// </remarks>
public static class OmemoPep
{

    /// <summary>Der PEP-Knoten der Bundles.</summary>
    public const String BundlesNode = "urn:xmpp:omemo:2:bundles";

    /// <summary>Der Namespace von XEP-0060.</summary>
    public const String PubSubNamespace = "http://jabber.org/protocol/pubsub";

    #region Das Bundle als XML

    /// <summary>Ein Bundle als XML (Abschnitt 5.2).</summary>
    public static XElement ToXml(this OmemoBundle bundle)
    {

        XNamespace ns = OmemoEncryptedElement.Namespace;

        return new XElement(ns + "bundle",
                            new XElement(ns + "spk",
                                         new XAttribute("id", bundle.SignedPreKeyId),
                                         Convert.ToBase64String(bundle.SignedPreKey)),
                            new XElement(ns + "spks",
                                         Convert.ToBase64String(bundle.SignedPreKeySignature)),
                            new XElement(ns + "ik",
                                         Convert.ToBase64String(bundle.IdentityKey)),
                            new XElement(ns + "prekeys",
                                         bundle.PreKeys.Select(p =>
                                             new XElement(ns + "pk",
                                                          new XAttribute("id", p.Id),
                                                          Convert.ToBase64String(p.PublicKey)))));

    }

    /// <summary>
    /// Liest ein Bundle.
    /// </summary>
    /// <remarks>
    /// <b>Hier wird streng gelesen, anders als bei der Geräteliste.</b> Ein
    /// Bundle mit einem fehlenden Teil ist unbrauchbar - ohne IdentityKey
    /// lässt sich die Signatur nicht prüfen, ohne Signed PreKey nichts
    /// vereinbaren. Ein halbes Bundle anzunehmen hiesse, eine Sitzung auf
    /// etwas aufzubauen, dessen Herkunft niemand geprüft hat.
    ///
    /// Ein einzelner unlesbarer PreKey nimmt allerdings nicht das ganze Bundle
    /// mit: Von hundert genügt einer, und die Sitzung kommt sogar ganz ohne
    /// zustande.
    /// </remarks>
    public static Boolean TryReadBundle(XElement xml, out OmemoBundle? bundle)
    {

        bundle = null;

        if (xml.Name.LocalName     != "bundle" ||
            xml.Name.NamespaceName != OmemoEncryptedElement.Namespace)
            return false;

        var ns = OmemoEncryptedElement.Namespace;

        try
        {

            var spk   = xml.Child(ns, "spk");
            var spks  = xml.Child(ns, "spks")?.Value.Trim();
            var ik    = xml.Child(ns, "ik")?.Value.Trim();

            if (spk is null || String.IsNullOrEmpty(spk.Value.Trim()) ||
                String.IsNullOrEmpty(spks) || String.IsNullOrEmpty(ik) ||
                !UInt32.TryParse(spk.Attr("id"), out var spkId))
                return false;

            var preKeys = new List<OmemoPreKey>();

            foreach (var pk in xml.Child(ns, "prekeys")?.Elements()
                                                        .Where(e => e.Name.LocalName == "pk")
                                   ?? [])
            {

                if (!UInt32.TryParse(pk.Attr("id"), out var pkId))
                    continue;

                try
                {
                    preKeys.Add(new OmemoPreKey(pkId, Convert.FromBase64String(pk.Value.Trim())));
                }
                catch (FormatException)
                {
                    // Ein krummer PreKey unter hundert nimmt nicht die
                    // anderen mit.
                }

            }

            var gelesen = new OmemoBundle(Convert.FromBase64String(ik),
                                          spkId,
                                          Convert.FromBase64String(spk.Value.Trim()),
                                          Convert.FromBase64String(spks),
                                          preKeys);

            // Die Längen gehören hierhin und nicht zum Aufrufer.
            //
            // Ein leeres <spk/> ist gültiges Base64 und ergibt ein Feld von
            // null Byte - das kam durch, bis eine überlebende Mutation den
            // Test dafür erzwang. Weiter unten wäre daraus eine Ausnahme aus
            // der Kurvenarithmetik geworden, mit einer Meldung, die niemandem
            // sagt, dass ein Bundle unbrauchbar war.
            if (gelesen.IdentityKey.Length            != Curve25519.KeyLength ||
                gelesen.SignedPreKey.Length           != Curve25519.KeyLength ||
                gelesen.SignedPreKeySignature.Length  != Curve25519.SignatureLength)
                return false;

            bundle = gelesen;

            return true;

        }
        catch (Exception)
        {
            return false;
        }

    }

    #endregion

    #region Die IQs

    /// <summary>Veröffentlicht einen Eintrag in einem eigenen PEP-Knoten.</summary>
    public static String PublishIq(String id, String node, String itemId, XElement payload)
        => $"<iq type='set' id='{XmlEscaping.Escape(id)}'>" +
           $"<pubsub xmlns='{PubSubNamespace}'>" +
           $"<publish node='{XmlEscaping.Escape(node)}'>" +
           $"<item id='{XmlEscaping.Escape(itemId)}'>{payload}</item>" +
           "</publish>" +

           // Abschnitt 5.2 verlangt ein offenes Zugriffsmodell: Wer
           // verschlüsselt schreiben will, muss das Bundle lesen können, und
           // das ist im Zweifel jemand, der noch in keinem Roster steht.
           $"<publish-options><x xmlns='jabber:x:data' type='submit'>" +
           "<field var='FORM_TYPE' type='hidden'>" +
           "<value>http://jabber.org/protocol/pubsub#publish-options</value></field>" +
           "<field var='pubsub#access_model'><value>open</value></field>" +
           "</x></publish-options>" +

           "</pubsub></iq>";

    /// <summary>Holt einen Eintrag aus dem PEP-Knoten eines anderen.</summary>
    /// <param name="itemId">
    /// Welcher Eintrag; ohne Angabe der zuletzt veröffentlichte.
    /// </param>
    public static String FetchIq(String id, String to, String node, String? itemId = null)
        => $"<iq type='get' id='{XmlEscaping.Escape(id)}' to='{XmlEscaping.Escape(to)}'>" +
           $"<pubsub xmlns='{PubSubNamespace}'>" +
           $"<items node='{XmlEscaping.Escape(node)}'" +
           (itemId is null ? " max_items='1'>" : $"><item id='{XmlEscaping.Escape(itemId)}'/>") +
           "</items>" +
           "</pubsub></iq>";

    #endregion

}
