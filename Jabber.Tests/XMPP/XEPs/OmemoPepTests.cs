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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die PEP-Verteilung von Geräteliste und Bundles (XEP-0384, Abschnitt
    /// 5.2) - über einen echten Server.
    /// </summary>
    /// <remarks>
    /// <b>Diese Etappe ist die erste seit vier, die wieder XMPP prüft statt
    /// Kryptographie</b> - und damit die erste, bei der ein Durchlauf mehr
    /// aussagt als eine nachgerechnete Vorschrift. Der Testserver hat dafür
    /// eine PEP-Teilmenge bekommen: veröffentlichen, abrufen, benachrichtigen.
    ///
    /// Der Kern ist der Weg über die Servergrenze hinweg: Alice veröffentlicht,
    /// <b>Bob holt ab, und was er abholt, muss seine eigene Signaturprüfung
    /// bestehen.</b> Damit hängen zum ersten Mal alle bisherigen Etappen
    /// zusammen - ein Bundle, das aus dem Speicher eines Servers kommt und
    /// dessen Herkunft der Empfänger selbst nachrechnet.
    /// </remarks>
    [TestFixture]
    public class OmemoPepTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        #endregion


        #region TheDeviceList_RoundTripsThroughXml()

        /// <summary>Die Geräteliste als XML - hin und zurück.</summary>
        [Test]
        public void TheDeviceList_RoundTripsThroughXml()
        {

            var liste = new OmemoDeviceList([new OmemoDevice(31415, "Telefon"),
                                             new OmemoDevice(27182)]);

            var xml = liste.ToXml();

            Assert.That(OmemoDeviceList.TryRead(xml, out var gelesen), Is.True);

            Assert.Multiple(() =>
            {

                Assert.That(gelesen!.Devices, Has.Count.EqualTo(2));
                Assert.That(gelesen.Devices[0].Id,     Is.EqualTo(31415u));
                Assert.That(gelesen.Devices[0].Label,  Is.EqualTo("Telefon"));
                Assert.That(gelesen.Devices[1].Label,  Is.Null,
                            "Eine fehlende Bezeichnung ist keine leere Zeichenkette.");

                Assert.That(gelesen.Contains(27182u), Is.True);
                Assert.That(gelesen.Contains(1u),     Is.False);

            });

        }

        #endregion

        #region ABrokenDeviceEntry_DoesNotTakeTheOthersWithIt()

        /// <summary>
        /// Ein Gerät mit krummer Kennung wird übergangen, die Liste bleibt.
        /// </summary>
        /// <remarks>
        /// Der Grund ist die Erreichbarkeit: Wer die ganze Liste verwürfe,
        /// könnte an keines der übrigen Geräte mehr schreiben. Ein einzelner
        /// unbrauchbarer Eintrag darf nicht alle anderen mitnehmen.
        /// </remarks>
        [Test]
        public void ABrokenDeviceEntry_DoesNotTakeTheOthersWithIt()
        {

            var xml = XElement.Parse(
                          "<devices xmlns='urn:xmpp:omemo:2'>" +
                          "<device id='1'/>" +
                          "<device id='keine-zahl'/>" +
                          "<device/>" +
                          "<device id='0'/>" +
                          "<device id='2'/>" +
                          "</devices>");

            Assert.That(OmemoDeviceList.TryRead(xml, out var liste), Is.True);

            Assert.That(liste!.Devices.Select(d => d.Id), Is.EqualTo(new UInt32[] { 1, 2 }),
                        "Es blieben nicht genau die brauchbaren Einträge übrig.");

        }

        #endregion

        #region TheBundle_RoundTripsThroughXml()

        /// <summary>
        /// Das Bundle als XML - und die Signatur überlebt den Weg.
        /// </summary>
        /// <remarks>
        /// Der zweite Teil ist der wichtige: Eine Kodierung, die alle Felder
        /// richtig überträgt und dabei ein Byte verliert, fiele an einem
        /// Feldvergleich nicht auf - an der Signaturprüfung schon.
        /// </remarks>
        [Test]
        public void TheBundle_RoundTripsThroughXml()
        {

            var eigen  = OmemoIdentity.Create();
            var bundle = eigen.Bundle();

            Assert.That(OmemoPep.TryReadBundle(bundle.ToXml(), out var gelesen), Is.True);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(gelesen!.IdentityKey),            Is.EqualTo(Hex(bundle.IdentityKey)));
                Assert.That(gelesen.SignedPreKeyId,               Is.EqualTo(bundle.SignedPreKeyId));
                Assert.That(Hex(gelesen.SignedPreKey),            Is.EqualTo(Hex(bundle.SignedPreKey)));
                Assert.That(Hex(gelesen.SignedPreKeySignature),   Is.EqualTo(Hex(bundle.SignedPreKeySignature)));
                Assert.That(gelesen.PreKeys,                      Has.Count.EqualTo(bundle.PreKeys.Count));

                Assert.That(gelesen.SignatureIsValid(), Is.True,
                            "Die Signatur überlebt den Weg durch das XML nicht.");

            });

        }

        #endregion

        #region AnIncompleteBundle_IsRefused()

        /// <summary>
        /// Ein Bundle ohne IdentityKey, ohne Signed PreKey oder ohne Signatur
        /// wird abgewiesen.
        /// </summary>
        /// <remarks>
        /// Hier wird streng gelesen, anders als bei der Geräteliste: Ohne
        /// IdentityKey lässt sich die Signatur nicht prüfen, ohne Signed
        /// PreKey nichts vereinbaren. Ein halbes Bundle anzunehmen hiesse,
        /// eine Sitzung auf etwas aufzubauen, dessen Herkunft niemand geprüft
        /// hat.
        /// </remarks>
        [Test]
        public void AnIncompleteBundle_IsRefused()
        {

            var vollstaendig = OmemoIdentity.Create().Bundle().ToXml();

            Assert.Multiple(() =>
            {

                foreach (var teil in new[] { "ik", "spk", "spks" })
                {

                    var verstuemmelt = new XElement(vollstaendig);
                    verstuemmelt.Elements().First(e => e.Name.LocalName == teil).Remove();

                    Assert.That(OmemoPep.TryReadBundle(verstuemmelt, out _), Is.False, teil);

                }

                // Ein Bundle ganz ohne PreKeys ist dagegen brauchbar - die
                // Sitzung kommt auch ohne zustande, sie verliert nur eine
                // Eigenschaft.
                var ohnePreKeys = new XElement(vollstaendig);
                ohnePreKeys.Elements().First(e => e.Name.LocalName == "prekeys").Remove();

                Assert.That(OmemoPep.TryReadBundle(ohnePreKeys, out var gelesen), Is.True);
                Assert.That(gelesen!.PreKeys, Is.Empty);

            });

        }

        #endregion


        #region AnEmptyIdentityKey_IsRefused()

        /// <summary>
        /// Ein <c>&lt;ik/&gt;</c> ohne Inhalt ist kein IdentityKey.
        /// </summary>
        /// <remarks>
        /// <b>Der Unterschied zum fehlenden Element ist der ganze Punkt.</b>
        /// Ein fehlendes wirft beim Lesen und wird dadurch ohnehin abgewiesen;
        /// ein leeres liefert klaglos ein Feld von null Byte. Ohne die
        /// ausdrückliche Prüfung käme ein Bundle mit leerem IdentityKey durch,
        /// und die Signaturprüfung darauf beantwortete eine Frage über einen
        /// Schlüssel, den es nicht gibt.
        ///
        /// Aufgefallen an einer überlebenden Mutation: Sie entfernte die
        /// Prüfung, und der Test bestand weiter - er hatte nur den Fall mit
        /// dem <i>fehlenden</i> Element.
        /// </remarks>
        [Test]
        public void AnEmptyIdentityKey_IsRefused()
        {

            var vollstaendig = OmemoIdentity.Create().Bundle().ToXml();

            Assert.Multiple(() =>
            {

                foreach (var teil in new[] { "ik", "spk", "spks" })
                {

                    var leer = new XElement(vollstaendig);
                    leer.Elements().First(e => e.Name.LocalName == teil).Value = "";

                    Assert.That(OmemoPep.TryReadBundle(leer, out _), Is.False, $"<{teil}/> leer");

                    // Und - der schärfere Fall - ein Wert, der gültiges Base64
                    // ist und trotzdem kein Schlüssel: drei Byte statt
                    // zweiunddreissig.
                    //
                    // Die Prüfung auf „leer" allein erschlägt ihn nicht, und
                    // genau das haben zwei überlebende Mutationen gezeigt: Sie
                    // entfernten die Längenprüfung, und der Test blieb grün,
                    // weil er nur den leeren Fall kannte. Ein zu kurzer
                    // Schlüssel käme durch bis in die Kurvenarithmetik.
                    var zuKurz = new XElement(vollstaendig);
                    zuKurz.Elements().First(e => e.Name.LocalName == teil).Value = "AAAA";

                    Assert.That(OmemoPep.TryReadBundle(zuKurz, out _), Is.False, $"<{teil}/> zu kurz");

                }

            });

        }

        #endregion

        #region TheItemId_IsLiterallyCurrent()

        /// <summary>
        /// XEP-0384, Abschnitt 5.2: „The item id must be set to
        /// <c>current</c>."
        /// </summary>
        /// <remarks>
        /// <b>Zum fünften Mal dieselbe Vorsichtsmassnahme.</b> Die Kennung
        /// liess sich auf etwas anderes setzen, ohne dass ein Test etwas
        /// sagte - Veröffentlichen und Abrufen benutzen dieselbe Konstante und
        /// finden sich weiterhin. Erst ein fremder Client suchte vergeblich,
        /// und den gibt es hier nicht. Also steht der Wert hier wörtlich.
        /// </remarks>
        [Test]
        public void TheItemId_IsLiterallyCurrent()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OmemoDeviceList.ItemId, Is.EqualTo("current"));
                Assert.That(OmemoDeviceList.Node,   Is.EqualTo("urn:xmpp:omemo:2:devices"));
                Assert.That(OmemoPep.BundlesNode,   Is.EqualTo("urn:xmpp:omemo:2:bundles"));
            });
        }

        #endregion

        #region ABundle_TravelsFromOneAccountToAnother()

        /// <summary>
        /// Der ganze Weg: Alice veröffentlicht, Bob holt ab - und prüft die
        /// Signatur selbst.
        /// </summary>
        /// <remarks>
        /// <b>Der Test, um den es in dieser Etappe geht.</b> Er verbindet zum
        /// ersten Mal alles: das Schlüsselmaterial aus D63, die
        /// XML-Darstellung von heute, den Speicher des Servers und die
        /// Signaturprüfung beim Empfänger. Und er prüft die eigentliche
        /// Zusage von PEP - <b>Bob bekommt Alices Bundle, ohne dass Alice
        /// etwas tut.</b>
        /// </remarks>
        [Test]
        public async Task ABundle_TravelsFromOneAccountToAnother()
        {

            var alice     = await ConnectClientAsync("alice");
            var bob       = await ConnectClientAsync("bob");

            var identitaet = OmemoIdentity.Create();

            var listeVeroeffentlicht = await alice.Connection.PublishOmemoDeviceListAsync(
                                                 new OmemoDeviceList([new OmemoDevice(identitaet.DeviceId,
                                                                                      "Telefon")]));

            var bundleVeroeffentlicht = await alice.Connection.PublishOmemoBundleAsync(
                                                  identitaet.DeviceId, identitaet.Bundle());

            Assert.Multiple(() =>
            {
                Assert.That(listeVeroeffentlicht,  Is.True, "Die Geräteliste liess sich nicht veröffentlichen.");
                Assert.That(bundleVeroeffentlicht, Is.True, "Das Bundle liess sich nicht veröffentlichen.");
            });

            var liste = await bob.Connection.FetchOmemoDeviceListAsync($"alice@{Server.Domain}");

            Assert.That(liste, Is.Not.Null, "Bob findet Alices Geräteliste nicht.");
            Assert.That(liste!.Contains(identitaet.DeviceId), Is.True);

            var bundle = await bob.Connection.FetchOmemoBundleAsync($"alice@{Server.Domain}",
                                                                     identitaet.DeviceId);

            Assert.That(bundle, Is.Not.Null, "Bob bekommt Alices Bundle nicht.");

            Assert.Multiple(() =>
            {

                Assert.That(Hex(bundle!.IdentityKey), Is.EqualTo(Hex(identitaet.PublicIdentityKey)));
                Assert.That(bundle.SignatureIsValid(), Is.True);

                // Und damit lässt sich sofort eine Sitzung beginnen - der
                // eigentliche Zweck der ganzen Verteilung.
                var bobsIdentitaet = OmemoIdentity.Create();

                Assert.That(() => X3DH.Initiate(bobsIdentitaet, bundle), Throws.Nothing,
                            "Aus dem abgeholten Bundle lässt sich keine Sitzung beginnen.");

            });

        }

        #endregion

        #region TwoBundles_AreFetchedIndividually()

        /// <summary>
        /// Zwei Geräte, zwei Bundles - und jedes wird einzeln abgeholt.
        /// </summary>
        /// <remarks>
        /// <b>Der Grund für einen Eintrag je Gerät.</b> Ein Absender holt
        /// genau das Bundle, das er braucht, statt aller - und ein Gerät, das
        /// seinen PreKey verbraucht hat, schreibt nur seinen eigenen Eintrag
        /// neu.
        ///
        /// Der Test kam durch eine überlebende Mutation dazu: Wer die
        /// Eintragskennung beim Abrufen übergeht, liefert <i>alle</i> Bundles,
        /// und der Aufrufer nimmt das erste. Mit nur einem veröffentlichten
        /// Gerät ist das dasselbe Ergebnis - <b>mit zweien bekommt der
        /// Absender das falsche Gerät</b>, verschlüsselt für ein Telefon, das
        /// gar nicht mitliest, und niemand sieht einen Fehler.
        /// </remarks>
        [Test]
        public async Task TwoBundles_AreFetchedIndividually()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var telefon = OmemoIdentity.Create(1000);
            var rechner = OmemoIdentity.Create(2000);

            await alice.Connection.PublishOmemoBundleAsync(telefon.DeviceId, telefon.Bundle());
            await alice.Connection.PublishOmemoBundleAsync(rechner.DeviceId, rechner.Bundle());

            var fuerTelefon = await bob.Connection.FetchOmemoBundleAsync($"alice@{Server.Domain}", 1000);
            var fuerRechner = await bob.Connection.FetchOmemoBundleAsync($"alice@{Server.Domain}", 2000);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(fuerTelefon!.IdentityKey), Is.EqualTo(Hex(telefon.PublicIdentityKey)),
                            "Für Gerät 1000 kam ein fremdes Bundle.");

                Assert.That(Hex(fuerRechner!.IdentityKey), Is.EqualTo(Hex(rechner.PublicIdentityKey)),
                            "Für Gerät 2000 kam ein fremdes Bundle.");

                Assert.That(Hex(fuerTelefon.IdentityKey), Is.Not.EqualTo(Hex(fuerRechner.IdentityKey)));

            });

        }

        #endregion

        #region AnUnknownNode_IsAnsweredWithItemNotFound()

        /// <summary>
        /// Ein Knoten, in dem nichts steht, wird mit
        /// <c>&lt;item-not-found/&gt;</c> beantwortet - nicht mit einem leeren
        /// Ergebnis.
        /// </summary>
        /// <remarks>
        /// Für den eigenen Client wäre beides gleich: Er findet so oder so
        /// keinen Eintrag. Für einen fremden ist es der Unterschied zwischen
        /// „es gibt hier nichts" und „hier ist die Antwort, und sie ist leer" -
        /// und XEP-0060 sieht für den ersten Fall den Fehler vor.
        /// </remarks>
        [Test]
        public async Task AnUnknownNode_IsAnsweredWithItemNotFound()
        {

            var alice   = await ConnectClientAsync("alice");
            var session = Server.SessionOf(alice.FullJid)!;

            var vorher = session.Sent.Count;

            await alice.SendRawAsync(
                      "<iq type='get' id='leer-1'>" +
                      "<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
                      $"<items node='{OmemoPep.BundlesNode}'><item id='4711'/></items>" +
                      "</pubsub></iq>");

            await WaitFor(() => session.Sent.Skip(vorher)
                                       .Any(f => f.Contains("id='leer-1'", StringComparison.Ordinal)),
                          "die Antwort auf den leeren Knoten");

            var antwort = session.Sent.Skip(vorher)
                                 .First(f => f.Contains("id='leer-1'", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("item-not-found"));
            });

        }

        #endregion

        #region ARejectedPublish_IsReported()

        /// <summary>
        /// Lehnt der Server das Veröffentlichen ab, meldet der Client es -
        /// statt Erfolg zu behaupten.
        /// </summary>
        /// <remarks>
        /// <b>Das ist der Grund, warum diese Methoden überhaupt einen
        /// Rückgabewert haben</b> (siehe D38). Wer seine Geräteliste
        /// veröffentlicht und nicht erfährt, dass es misslang, ist für alle
        /// seine Kontakte unerreichbar und merkt nichts davon: Alles sieht aus
        /// wie immer, nur schreibt ihm niemand mehr verschlüsselt.
        ///
        /// Hergestellt wird der Fall über einen Server ohne PEP - dann geht
        /// die Anfrage den gewöhnlichen Weg und bekommt
        /// <c>&lt;service-unavailable/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task ARejectedPublish_IsReported()
        {

            Server.OfferPersonalEventing = false;

            var alice = await ConnectClientAsync("alice");

            var gemeldet = await alice.Connection.PublishOmemoDeviceListAsync(
                                     new OmemoDeviceList([new OmemoDevice(4711)]));

            Assert.Multiple(() =>
            {

                Assert.That(gemeldet, Is.False,
                            "Der Client meldet einen Erfolg, den es nicht gab.");

                Assert.That(Server.GetAccount($"alice@{Server.Domain}")!.PepNodes, Is.Empty);

            });

        }

        #endregion

        #region AnUnpublishedBundle_IsNotFound()

        /// <summary>
        /// Wer nichts veröffentlicht hat, hat nichts abzuholen - und ein Konto,
        /// das es nicht gibt, sieht genauso aus.
        /// </summary>
        /// <remarks>
        /// Die Gleichbehandlung ist Absicht: Sonst liesse sich über PEP
        /// herausfinden, welche Konten es auf diesem Server gibt - dieselbe
        /// Überlegung wie bei der Anmeldung (RFC 6120, Abschnitt 13.11, siehe
        /// D50).
        /// </remarks>
        [Test]
        public async Task AnUnpublishedBundle_IsNotFound()
        {

            var bob = await ConnectClientAsync("bob");

            Server.AddAccount("alice");

            var ohneListe   = await bob.Connection.FetchOmemoDeviceListAsync($"alice@{Server.Domain}");
            var ohneKonto   = await bob.Connection.FetchOmemoDeviceListAsync($"niemand@{Server.Domain}");
            var ohneBundle  = await bob.Connection.FetchOmemoBundleAsync($"alice@{Server.Domain}", 1);

            Assert.Multiple(() =>
            {
                Assert.That(ohneListe,  Is.Null, "Ein Konto ohne Geräteliste liefert eine.");
                Assert.That(ohneKonto,  Is.Null, "Ein Konto, das es nicht gibt, liefert eine Geräteliste.");
                Assert.That(ohneBundle, Is.Null);
            });

        }

        #endregion

        #region ATamperedBundle_IsRefusedOnFetching()

        /// <summary>
        /// Ein Bundle mit falscher Signatur kommt gar nicht erst beim Aufrufer
        /// an.
        /// </summary>
        /// <remarks>
        /// Der Server ist die Partei, gegen die OMEMO schützt - er hält das
        /// Bundle vorrätig und könnte es austauschen. Deshalb prüft die
        /// Verbindung die Signatur selbst und gibt ein ungültiges Bundle
        /// <b>nicht weiter</b>: Ein ungeprüftes durchzureichen hiesse, die
        /// Prüfung dem zu überlassen, der sie am ehesten vergisst.
        /// </remarks>
        [Test]
        public async Task ATamperedBundle_IsRefusedOnFetching()
        {

            var alice  = await ConnectClientAsync("alice");
            var bob    = await ConnectClientAsync("bob");

            var echt   = OmemoIdentity.Create();
            var fremd  = OmemoIdentity.Create();

            // Alices Bundle mit dem Signed PreKey eines Fremden - so sähe es
            // aus, wenn der Server ihn ausgetauscht hätte.
            var untergeschoben = echt.Bundle() with { SignedPreKey = fremd.SignedPreKey.PublicKey };

            await alice.Connection.PublishOmemoBundleAsync(echt.DeviceId, untergeschoben);

            Assert.That(await bob.Connection.FetchOmemoBundleAsync($"alice@{Server.Domain}",
                                                                    echt.DeviceId),
                        Is.Null,
                        "Ein untergeschobenes Bundle kam beim Aufrufer an.");

        }

        #endregion

        #region PublishingIntoAForeignNode_IsForbidden()

        /// <summary>
        /// In den PEP-Knoten eines anderen schreibt niemand.
        /// </summary>
        /// <remarks>
        /// Wer es dürfte, könnte fremde Bundles austauschen - und das ist
        /// genau der Angriff, gegen den die Signatur über den Signed PreKey
        /// steht. Zwei Sicherungen gegen dieselbe Sache sind hier keine
        /// Verschwendung: Die eine wirkt gegen den Server, die andere gegen
        /// jeden anderen Nutzer desselben Servers.
        /// </remarks>
        [Test]
        public async Task PublishingIntoAForeignNode_IsForbidden()
        {

            var alice   = await ConnectClientAsync("alice");
            var session = Server.SessionOf(alice.FullJid)!;

            Server.AddAccount("bob");

            var vorher = session.Sent.Count;

            await alice.SendRawAsync(
                      "<iq type='set' id='fremd-1' to='bob@" + Server.Domain + "'>" +
                      "<pubsub xmlns='http://jabber.org/protocol/pubsub'>" +
                      $"<publish node='{OmemoDeviceList.Node}'>" +
                      "<item id='current'><devices xmlns='urn:xmpp:omemo:2'>" +
                      "<device id='666'/></devices></item>" +
                      "</publish></pubsub></iq>");

            await WaitFor(() => session.Sent.Skip(vorher)
                                       .Any(f => f.Contains("id='fremd-1'", StringComparison.Ordinal)),
                          "die Antwort auf das fremde Veröffentlichen");

            var antwort = session.Sent.Skip(vorher)
                                 .First(f => f.Contains("id='fremd-1'", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {

                Assert.That(antwort, Does.Contain("type='error'"));
                Assert.That(antwort, Does.Contain("forbidden"));

                Assert.That(Server.GetAccount($"bob@{Server.Domain}")!.PepNodes, Is.Empty,
                            "Der fremde Knoten wurde trotzdem beschrieben.");

            });

        }

        #endregion

        #region ANewDeviceList_ReachesTheContacts()

        /// <summary>
        /// Veröffentlicht Alice eine neue Geräteliste, erfährt Bob davon -
        /// ohne zu fragen.
        /// </summary>
        /// <remarks>
        /// Ohne diese Benachrichtigung müsste jeder Absender vor jeder
        /// Nachricht die Liste abrufen. Mit ihr erfährt er von einem neuen
        /// Gerät in dem Augenblick, in dem es dazukommt - und das ist der
        /// Unterschied zwischen „verschlüsselt an alle Geräte" und
        /// „verschlüsselt an die, die ich zuletzt gesehen habe".
        /// </remarks>
        [Test]
        public async Task ANewDeviceList_ReachesTheContacts()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            OmemoDeviceList? empfangen = null;
            String?          vonWem    = null;

            bob.Connection.OnOmemoDeviceListChanged += (from, liste) =>
            {
                vonWem    = from;
                empfangen = liste;
            };

            await alice.Connection.PublishOmemoDeviceListAsync(
                      new OmemoDeviceList([new OmemoDevice(4711, "Telefon")]));

            await WaitFor(() => empfangen is not null, "die Benachrichtigung über Alices Geräteliste");

            Assert.Multiple(() =>
            {
                Assert.That(vonWem, Is.EqualTo($"alice@{Server.Domain}"));
                Assert.That(empfangen!.Contains(4711u), Is.True);
            });

        }

        #endregion

        #region AForeignDeviceList_DoesNotTriggerAReannounce()

        /// <summary>
        /// In der Geräteliste eines <b>anderen</b> hat das eigene Gerät nichts
        /// zu suchen.
        /// </summary>
        /// <remarks>
        /// Ohne die Prüfung, wessen Liste da eintrifft, träge sich dieser
        /// Client in <i>jede</i> Liste ein, die er zu sehen bekommt - und
        /// veröffentlichte dabei in einem fremden Knoten, was der Server
        /// zurückweist. Der Fehler wäre folgenlos und trotzdem falsch: Bei
        /// jedem Kontakt, der ein Gerät hinzufügt, ginge eine abgewiesene
        /// Anfrage hinaus.
        ///
        /// Aufgefallen an einer überlebenden Mutation - der Test davor prüfte
        /// nur die eigene Liste.
        /// </remarks>
        [Test]
        public async Task AForeignDeviceList_DoesNotTriggerAReannounce()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            bob.Connection.OmemoDeviceId = 9999;

            var empfangen = false;
            bob.Connection.OnOmemoDeviceListChanged += (_, _) => empfangen = true;

            await alice.Connection.PublishOmemoDeviceListAsync(
                      new OmemoDeviceList([new OmemoDevice(1234)]));

            await WaitFor(() => empfangen, "die Benachrichtigung über Alices Geräteliste");

            // Gefragt wird, ob Bobs Client überhaupt etwas geschickt hat - und
            // nicht, ob Alices Liste unverändert blieb.
            //
            // Die erste Fassung prüfte das Zweite und war damit wertlos: Der
            // Server weist fremde Knoten ohnehin ab, also blieb die Liste auch
            // dann sauber, wenn Bob es versuchte. Der Test bestand die
            // Mutation, die genau diesen Versuch auslöst. Geprüft werden muss
            // der Prüfling, nicht sein Nachbar.
            var bobsSitzung = Server.SessionOf(bob.FullJid)!;

            await WaitAgainst(() => bobsSitzung.Received.Any(
                                        f => f.Contains(OmemoPep.PubSubNamespace, StringComparison.Ordinal) &&
                                             f.Contains("<publish", StringComparison.Ordinal)),
                              "ein Veröffentlichen durch Bob");

            Assert.That(Server.GetAccount($"alice@{Server.Domain}")!
                              .GetPepItems(OmemoDeviceList.Node, OmemoDeviceList.ItemId)[0].Payload,
                        Does.Not.Contain("9999"),
                        "Bobs Gerät steht in Alices Geräteliste.");

        }

        #endregion

        #region AMissingOwnDevice_IsReannounced()

        /// <summary>
        /// XEP-0384, Abschnitt 5.2: Fehlt das eigene Gerät in der eigenen
        /// Liste, trägt es sich wieder ein - <b>ohne die anderen zu
        /// verdrängen</b>.
        /// </summary>
        /// <remarks>
        /// Der Fall ist nicht ausgedacht: Ein anderes Gerät desselben
        /// Menschen - oder ein aufräumender Server - schreibt die Liste neu
        /// und vergisst dieses Gerät. Von da an schreibt niemand mehr
        /// verschlüsselt an es, und <b>es merkt nichts davon</b>, weil ihm
        /// nichts fehlt: Es bekommt weiterhin alles, was unverschlüsselt
        /// kommt.
        ///
        /// Geprüft wird deshalb beides: dass es sich wieder einträgt, und dass
        /// das andere Gerät dabei stehen bleibt.
        /// </remarks>
        [Test]
        public async Task AMissingOwnDevice_IsReannounced()
        {

            var alice = await ConnectClientAsync("alice");

            // Ein zweites Gerät desselben Kontos - mit eigener Resource,
            // sonst stritten sich beide um dieselbe.
            var zweites = CreateClient("alice");
            zweites.Connection.Resource = "zweites-geraet";
            await zweites.ConnectAsync();

            alice.Connection.OmemoDeviceId = 1000;

            // Das erste Gerät trägt sich ein.
            await alice.Connection.PublishOmemoDeviceListAsync(
                      new OmemoDeviceList([new OmemoDevice(1000)]));

            // Das zweite Gerät schreibt die Liste neu - und vergisst das erste.
            await zweites.Connection.PublishOmemoDeviceListAsync(
                      new OmemoDeviceList([new OmemoDevice(2000)]));

            await WaitFor(() =>
            {

                var eintraege = Server.GetAccount($"alice@{Server.Domain}")!
                                      .GetPepItems(OmemoDeviceList.Node, OmemoDeviceList.ItemId);

                return eintraege.Count == 1 &&
                       eintraege[0].Payload.Contains("id=\"1000\"", StringComparison.Ordinal);

            },
            "den Wiedereintrag des ersten Geräts");

            var liste = Server.GetAccount($"alice@{Server.Domain}")!
                              .GetPepItems(OmemoDeviceList.Node, OmemoDeviceList.ItemId)[0].Payload;

            Assert.That(liste, Does.Contain("id=\"2000\""),
                        "Der Wiedereintrag hat das andere Gerät verdrängt.");

        }

        #endregion

    }

}
