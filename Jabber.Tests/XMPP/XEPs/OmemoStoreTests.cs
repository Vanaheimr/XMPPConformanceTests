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

using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Der Speicher, der einen Neustart überdauert (XEP-0384, Etappe 6).
    /// </summary>
    /// <remarks>
    /// <b>Die Prüfung ist immer dieselbe: neu starten und weitermachen.</b>
    /// Ein Speicher, der ablegt und wieder herausgibt, ist noch keiner - er
    /// muss so viel ablegen, dass die Gegenstelle vom Neustart nichts merkt.
    /// Deshalb wird hier nicht verglichen, was gespeichert wurde, sondern
    /// geprüft, ob das Gespräch weitergeht.
    /// </remarks>
    [TestFixture]
    public class OmemoStoreTests
    {

        #region Hilfsfunktionen

        private static readonly Byte[] Beigabe = Encoding.UTF8.GetBytes("AD");

        private String _datei = "";

        [SetUp]
        public void FrischeDatei()
            => _datei = Path.Combine(Path.GetTempPath(),
                                     $"omemo-test-{Guid.NewGuid():N}.json");

        [TearDown]
        public void AufraeumenDanach()
        {
            try { if (File.Exists(_datei)) File.Delete(_datei); } catch { /* egal */ }
        }

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        private static Byte[] Text(String s)
            => Encoding.UTF8.GetBytes(s);

        /// <summary>Ein Paar Ratschen, wie es nach X3DH entsteht.</summary>
        private static (DoubleRatchet Alice, DoubleRatchet Bob) Paar()
        {

            var geheimnis  = RandomNumberGenerator.GetBytes(32);
            var bobsKey    = Curve25519.GenerateKeyPair();

            return (DoubleRatchet.InitiateAsSender(geheimnis, bobsKey.PublicKey),
                    DoubleRatchet.InitiateAsReceiver(geheimnis, bobsKey));

        }

        #endregion


        #region TheIdentity_SurvivesARestart()

        /// <summary>
        /// Nach einem Neustart ist es dasselbe Gerät - derselbe
        /// Fingerabdruck, dieselbe Kennung, dieselbe Signatur.
        /// </summary>
        /// <remarks>
        /// <b>Der Fingerabdruck ist der Punkt.</b> Ein neuer bedeutet, dass
        /// jeder Vergleich, den irgendein Mensch je angestellt hat, wertlos
        /// ist - und für seine Kontakte sieht ein Client, der bei jedem Start
        /// neue Schlüssel erzeugt, aus wie ein Angreifer. Jedes Mal.
        ///
        /// Die Signatur wird mitgenommen und nicht neu gerechnet: XEdDSA
        /// mischt Zufall in jede, die neue sähe also anders aus als die
        /// veröffentlichte, und das Bundle im PEP-Knoten wäre mit dem Gerät
        /// uneins.
        /// </remarks>
        [Test]
        public void TheIdentity_SurvivesARestart()
        {

            var speicher = new OmemoFileStore(_datei);
            var erstes   = speicher.LoadOrCreateIdentity();

            // „Neustart": eine neue Instanz auf derselben Datei.
            var zweites = new OmemoFileStore(_datei).LoadOrCreateIdentity();

            Assert.Multiple(() =>
            {

                Assert.That(zweites.Fingerprint, Is.EqualTo(erstes.Fingerprint),
                            "Der Fingerabdruck hat sich geändert.");

                Assert.That(zweites.DeviceId, Is.EqualTo(erstes.DeviceId));

                Assert.That(Hex(zweites.SignedPreKeySignature),
                            Is.EqualTo(Hex(erstes.SignedPreKeySignature)),
                            "Die Signatur wurde neu gerechnet statt mitgenommen.");

                Assert.That(zweites.AvailablePreKeys, Is.EqualTo(erstes.AvailablePreKeys));

                // Und das veröffentlichte Bundle ist Byte für Byte dasselbe.
                Assert.That(Hex(zweites.Bundle().SignedPreKey),
                            Is.EqualTo(Hex(erstes.Bundle().SignedPreKey)));

            });

        }

        #endregion

        #region AConsumedPreKey_StaysConsumedAcrossARestart()

        /// <summary>
        /// Ein verbrauchter PreKey ist nach dem Neustart immer noch
        /// verbraucht.
        /// </summary>
        /// <remarks>
        /// <b>Sonst wäre die erste Nachricht wiederholbar.</b> Wer eine alte
        /// abfängt und nach einem Neustart des Empfängers erneut einspielt,
        /// bekäme dieselbe Sitzung ein zweites Mal - und der Empfänger zeigte
        /// die Nachricht als neu an. Der Neustart ist dabei kein
        /// Sonderfall, sondern die Gelegenheit: Er passiert von selbst.
        /// </remarks>
        [Test]
        public void AConsumedPreKey_StaysConsumedAcrossARestart()
        {

            var speicher = new OmemoFileStore(_datei);
            var eigen    = speicher.LoadOrCreateIdentity();

            var verbraucht = eigen.Bundle().PreKeys[0].Id;

            Assert.That(eigen.TakePreKey(verbraucht), Is.Not.Null);

            speicher.SaveIdentity(eigen.Export());

            var nachNeustart = new OmemoFileStore(_datei).LoadOrCreateIdentity();

            Assert.Multiple(() =>
            {

                Assert.That(nachNeustart.TakePreKey(verbraucht), Is.Null,
                            "Der verbrauchte PreKey ist wieder da.");

                Assert.That(nachNeustart.AvailablePreKeys, Is.EqualTo(OmemoIdentity.PreKeyCount - 1));

            });

        }

        #endregion

        #region ASession_ContinuesAfterARestart()

        /// <summary>
        /// <b>Der Kern dieser Etappe:</b> Ein Gespräch läuft über einen
        /// Neustart hinweg weiter.
        /// </summary>
        /// <remarks>
        /// Geprüft wird nicht, ob der Zustand gleich aussieht, sondern ob die
        /// Gegenstelle vom Neustart nichts merkt. Eine neu begonnene Sitzung
        /// hätte einen anderen Wurzelschlüssel; die Gegenstelle bekäme
        /// Nachrichten, deren Prüfsumme nicht stimmt - <b>und das sieht aus
        /// wie ein Angriff, nicht wie ein Neustart</b>.
        /// </remarks>
        [Test]
        public void ASession_ContinuesAfterARestart()
        {

            var (alice, bob) = Paar();
            var speicher     = new OmemoFileStore(_datei);

            // Ein paar Nachrichten hin und her, damit beide Ratschen laufen.
            bob.Decrypt(alice.Encrypt(Text("eins"), Beigabe), Beigabe);
            alice.Decrypt(bob.Encrypt(Text("zwei"), Beigabe), Beigabe);
            bob.Decrypt(alice.Encrypt(Text("drei"), Beigabe), Beigabe);

            speicher.SaveSession("bob@example.org", 1, new OmemoSessionState(bob.Export(), Beigabe));

            // Bob startet neu.
            var bobNachher = DoubleRatchet.Import(
                                 new OmemoFileStore(_datei).LoadSession("bob@example.org", 1)!.Ratchet);

            Assert.Multiple(() =>
            {

                Assert.That(bobNachher.Decrypt(alice.Encrypt(Text("nach dem Neustart"), Beigabe), Beigabe),
                            Is.EqualTo(Text("nach dem Neustart")),
                            "Nach dem Neustart lässt sich nichts mehr lesen.");

                // Und in der Gegenrichtung ebenso.
                Assert.That(alice.Decrypt(bobNachher.Encrypt(Text("und zurück"), Beigabe), Beigabe),
                            Is.EqualTo(Text("und zurück")),
                            "Alice kann Bobs Antwort nach seinem Neustart nicht lesen.");

            });

        }

        #endregion

        #region SkippedKeys_SurviveARestart()

        /// <summary>
        /// Auch die beiseitegelegten Schlüssel überstehen den Neustart.
        /// </summary>
        /// <remarks>
        /// Ohne sie wäre jede überholte Nachricht verloren, die während des
        /// Neustarts unterwegs war - und zwar endgültig, denn ihr Schlüssel
        /// war schon ausgerechnet und dann weggeworfen. Der Absender bekäme
        /// nie einen Hinweis darauf.
        /// </remarks>
        [Test]
        public void SkippedKeys_SurviveARestart()
        {

            var (alice, bob) = Paar();
            var speicher     = new OmemoFileStore(_datei);

            var eins  = alice.Encrypt(Text("eins"),  Beigabe);
            var zwei  = alice.Encrypt(Text("zwei"),  Beigabe);
            var drei  = alice.Encrypt(Text("drei"),  Beigabe);

            // Die dritte kommt zuerst - zwei Schlüssel wandern beiseite.
            bob.Decrypt(drei, Beigabe);

            Assert.That(bob.SkippedKeys, Is.EqualTo(2));

            speicher.SaveSession("bob@example.org", 1, new OmemoSessionState(bob.Export(), Beigabe));

            var bobNachher = DoubleRatchet.Import(
                                 new OmemoFileStore(_datei).LoadSession("bob@example.org", 1)!.Ratchet);

            Assert.Multiple(() =>
            {

                Assert.That(bobNachher.SkippedKeys, Is.EqualTo(2));

                Assert.That(bobNachher.Decrypt(eins, Beigabe), Is.EqualTo(Text("eins")),
                            "Die überholte Nachricht ist über den Neustart verloren.");

                Assert.That(bobNachher.Decrypt(zwei, Beigabe), Is.EqualTo(Text("zwei")));

            });

        }

        #endregion

        #region AMessageForTheRotatedSignedPreKey_StillArrives()

        /// <summary>
        /// Eine Nachricht, die den <b>abgelösten</b> Signed PreKey nennt,
        /// kommt an - die davor nicht mehr.
        /// </summary>
        /// <remarks>
        /// In D63 war dieser Fall ausdrücklich aufgeschoben: Der abgelöste
        /// Schlüssel wurde nicht aufgehoben, und eine Nachricht, die während
        /// des Wechsels unterwegs war, ging verloren.
        ///
        /// Aufgehoben wird genau einer. <b>Jeder weitere nähme ein Stück von
        /// dem zurück, wofür es den Wechsel gibt</b> - wer ihn stiehlt, öffnet
        /// die Sitzungen, die er eröffnet hat.
        /// </remarks>
        [Test]
        public void AMessageForTheRotatedSignedPreKey_StillArrives()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();

            // Alice greift zum Bundle von jetzt.
            var beiAlice = X3DH.Initiate(alice, bob.Bundle());
            var alterSpk = bob.SignedPreKeyId;

            bob.RotateSignedPreKey();

            var beiBob = X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                     alterSpk, beiAlice.UsedPreKeyId);

            Assert.That(Hex(beiBob.SharedSecret), Is.EqualTo(Hex(beiAlice.SharedSecret)),
                        "Die Nachricht auf den abgelösten Signed PreKey kommt nicht an.");

            // Noch ein Wechsel - jetzt ist der erste endgültig fort.
            bob.RotateSignedPreKey();

            Assert.That(() => X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                          alterSpk, null),
                        Throws.TypeOf<CryptographicException>(),
                        "Der vorletzte Signed PreKey wird immer noch aufgehoben.");

        }

        #endregion

        #region TheRotatedSignedPreKey_SurvivesARestart()

        /// <summary>
        /// Auch der <b>abgelöste</b> Signed PreKey überdauert den Neustart.
        /// </summary>
        /// <remarks>
        /// Der Fall, der ihn braucht, dauert Minuten: Eine Nachricht ist
        /// unterwegs, das Gerät wechselt seinen Signed PreKey und startet neu.
        /// Fehlte der abgelöste danach, wäre die Nachricht verloren - und der
        /// Absender erführe nichts davon.
        ///
        /// Der Test kam durch eine überlebende Mutation dazu: Der abgelöste
        /// Schlüssel liess sich beim Wiederherstellen weglassen, ohne dass
        /// etwas fehlschlug. Geprüft war bis dahin nur der Wechsel selbst,
        /// nicht sein Überleben.
        /// </remarks>
        [Test]
        public void TheRotatedSignedPreKey_SurvivesARestart()
        {

            var speicher = new OmemoFileStore(_datei);
            var bob      = speicher.LoadOrCreateIdentity();
            var alice    = OmemoIdentity.Create();

            var beiAlice = X3DH.Initiate(alice, bob.Bundle());
            var alterSpk = bob.SignedPreKeyId;

            bob.RotateSignedPreKey();
            speicher.SaveIdentity(bob.Export());

            // Neustart mitten im Wechsel.
            var bobNachher = new OmemoFileStore(_datei).LoadOrCreateIdentity();

            Assert.Multiple(() =>
            {

                Assert.That(bobNachher.PreviousSignedPreKeyId, Is.EqualTo(alterSpk),
                            "Der abgelöste Signed PreKey ist über den Neustart fort.");

                var beiBob = X3DH.Accept(bobNachher, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                         alterSpk, beiAlice.UsedPreKeyId);

                Assert.That(Hex(beiBob.SharedSecret), Is.EqualTo(Hex(beiAlice.SharedSecret)),
                            "Die unterwegs gewesene Nachricht ist nach dem Neustart nicht mehr zu lesen.");

            });

        }

        #endregion

        #region ASavedSession_ReplacesTheOlderOne()

        /// <summary>
        /// Zweimal dieselbe Sitzung abgelegt heisst: die neuere gilt.
        /// </summary>
        /// <remarks>
        /// <b>Auch das kam durch eine überlebende Mutation.</b> Der Speicher
        /// liess sich so ändern, dass er die neue Fassung <i>danebenlegt</i>
        /// statt die alte zu ersetzen - und beim Laden käme die alte zuerst.
        /// Kein Test hatte je zweimal abgelegt.
        ///
        /// Der Schaden wäre der schlimmste, den dieser Speicher anrichten
        /// kann: Nach einem Neustart stünde die Ratsche auf einem alten Stand,
        /// und alles, was seitdem lief, wäre nicht mehr zu lesen - für beide
        /// Seiten, ohne erkennbaren Grund.
        /// </remarks>
        [Test]
        public void ASavedSession_ReplacesTheOlderOne()
        {

            var (alice, bob) = Paar();
            var speicher     = new OmemoFileStore(_datei);

            bob.Decrypt(alice.Encrypt(Text("eins"), Beigabe), Beigabe);
            speicher.SaveSession("bob@example.org", 1, new OmemoSessionState(bob.Export(), Beigabe));

            // Das Gespräch geht weiter, und der neue Stand wird abgelegt.
            bob.Decrypt(alice.Encrypt(Text("zwei"), Beigabe), Beigabe);
            bob.Decrypt(alice.Encrypt(Text("drei"), Beigabe), Beigabe);
            speicher.SaveSession("bob@example.org", 1, new OmemoSessionState(bob.Export(), Beigabe));

            var geladen = new OmemoFileStore(_datei).LoadSession("bob@example.org", 1)!.Ratchet;

            Assert.Multiple(() =>
            {

                Assert.That(geladen.ReceiveCount, Is.EqualTo(3u),
                            "Geladen wurde ein älterer Stand - die neue Fassung wurde danebengelegt.");

                Assert.That(DoubleRatchet.Import(geladen)
                                         .Decrypt(alice.Encrypt(Text("vier"), Beigabe), Beigabe),
                            Is.EqualTo(Text("vier")));

            });

        }

        #endregion

        #region AChangedIdentityKey_IsReported()

        /// <summary>
        /// Ein anderer IdentityKey unter derselben Gerätekennung wird
        /// <b>gemeldet</b> und nicht übernommen.
        /// </summary>
        /// <remarks>
        /// Dafür gibt es genau zwei Erklärungen: Der Mensch hat sein Gerät neu
        /// aufgesetzt - oder jemand schiebt sich dazwischen. <b>Von aussen
        /// sind die beiden nicht zu unterscheiden</b>, und deshalb ist es
        /// keine Entscheidung, die ein Programm treffen kann.
        ///
        /// Der alte Vermerk bleibt stehen, samt seiner
        /// Vertrauensentscheidung: Wer ihn überschriebe, machte aus einer
        /// bestätigten Identität eine unbestätigte, und die Warnung wäre nach
        /// dem ersten Ansehen fort.
        /// </remarks>
        [Test]
        public void AChangedIdentityKey_IsReported()
        {

            var speicher = new OmemoMemoryStore();

            var echt   = OmemoIdentity.Create().PublicIdentityKey;
            var fremd  = OmemoIdentity.Create().PublicIdentityKey;

            Assert.Multiple(() =>
            {

                Assert.That(speicher.RecordIdentity("bob@example.org", 1, echt),
                            Is.EqualTo(OmemoIdentityCheck.New));

                Assert.That(speicher.RecordIdentity("bob@example.org", 1, echt),
                            Is.EqualTo(OmemoIdentityCheck.Known));

                Assert.That(speicher.RecordIdentity("bob@example.org", 1, fremd),
                            Is.EqualTo(OmemoIdentityCheck.Changed),
                            "Der Austausch wurde nicht gemeldet.");

                // Und beim nächsten Mal wieder - die Warnung verbraucht sich
                // nicht.
                Assert.That(speicher.RecordIdentity("bob@example.org", 1, fremd),
                            Is.EqualTo(OmemoIdentityCheck.Changed),
                            "Die Warnung war nach dem ersten Mal fort.");

                Assert.That(Hex(speicher.KnownDevices().Single().IdentityKey), Is.EqualTo(Hex(echt)),
                            "Der alte Vermerk wurde überschrieben.");

            });

        }

        #endregion

        #region ATrustDecision_SurvivesAndBelongsToAKey()

        /// <summary>
        /// Die Vertrauensentscheidung überdauert den Neustart - und gilt einem
        /// Schlüssel, nicht einer Nummer.
        /// </summary>
        /// <remarks>
        /// Über ein unbekanntes Gerät lässt sich nicht entscheiden, und das
        /// ist keine Förmlichkeit: Wer im Voraus für eine Gerätekennung
        /// entschiede, hätte für den ersten Schlüssel entschieden, der unter
        /// dieser Nummer auftaucht - und das kann jeder sein.
        /// </remarks>
        [Test]
        public void ATrustDecision_SurvivesAndBelongsToAKey()
        {

            var speicher = new OmemoFileStore(_datei);
            var bob      = OmemoIdentity.Create().PublicIdentityKey;

            Assert.Multiple(() =>
            {

                Assert.That(speicher.SetTrust("bob@example.org", 1, OmemoTrust.Trusted), Is.False,
                            "Über ein unbekanntes Gerät liess sich entscheiden.");

                speicher.RecordIdentity("bob@example.org", 1, bob);

                Assert.That(speicher.TrustOf("bob@example.org", 1), Is.EqualTo(OmemoTrust.Undecided),
                            "Ein frisch gesehenes Gerät gilt als bestätigt.");

                Assert.That(speicher.SetTrust("bob@example.org", 1, OmemoTrust.Trusted), Is.True);

            });

            var nachNeustart = new OmemoFileStore(_datei);

            Assert.Multiple(() =>
            {

                Assert.That(nachNeustart.TrustOf("bob@example.org", 1), Is.EqualTo(OmemoTrust.Trusted),
                            "Die Entscheidung hat den Neustart nicht überdauert.");

                Assert.That(nachNeustart.KnownDevices().Single().Fingerprint,
                            Is.EqualTo(Convert.ToHexString(bob).ToLowerInvariant()));

                // Ein anderes Konto mit derselben Gerätekennung ist ein
                // anderes Gerät.
                Assert.That(nachNeustart.TrustOf("carol@example.org", 1), Is.EqualTo(OmemoTrust.Undecided));

            });

        }

        #endregion

        #region AnUnreadableStore_DoesNotStartFresh()

        /// <summary>
        /// Eine unlesbare Datei wirft, statt mit neuen Schlüsseln
        /// weiterzumachen.
        /// </summary>
        /// <remarks>
        /// <b>Der bequeme Weg wäre hier der gefährliche.</b> Ein Client, der
        /// nach einem Lesefehler frisch startet, hat seinen Fingerabdruck
        /// gewechselt, ohne dass jemand gefragt wurde - und die alte Datei
        /// wäre beim ersten Ablegen überschrieben. Aus einem behebbaren
        /// Lesefehler würde ein endgültiger Verlust.
        /// </remarks>
        [Test]
        public void AnUnreadableStore_DoesNotStartFresh()
        {

            File.WriteAllText(_datei, "{ das ist kein JSON");

            Assert.That(() => new OmemoFileStore(_datei),
                        Throws.InstanceOf<Exception>(),
                        "Die unlesbare Datei wurde stillschweigend ersetzt.");

            Assert.That(File.ReadAllText(_datei), Does.StartWith("{ das ist kein JSON"),
                        "Die unlesbare Datei wurde überschrieben.");

        }

        #endregion

        #region TheStore_IsWrittenAtomically()

        /// <summary>
        /// Geschrieben wird über eine Nebendatei - und die bleibt nicht
        /// liegen.
        /// </summary>
        /// <remarks>
        /// Bricht der Vorgang mittendrin ab, steht die alte Fassung noch
        /// vollständig da. Das ist hier wichtiger als beim Kontenspeicher:
        /// Eine halb geschriebene Sitzungsdatei kostet nicht einen
        /// Anmeldeversuch, sondern jede laufende Sitzung.
        /// </remarks>
        [Test]
        public void TheStore_IsWrittenAtomically()
        {

            var speicher = new OmemoFileStore(_datei);

            speicher.LoadOrCreateIdentity();
            speicher.SaveSession("bob@example.org", 1, new OmemoSessionState(Paar().Bob.Export(), Beigabe));

            Assert.Multiple(() =>
            {

                Assert.That(File.Exists(_datei), Is.True);
                Assert.That(File.Exists(_datei + ".neu"), Is.False,
                            "Die Nebendatei ist liegengeblieben.");

                // Und die Datei ist vollständiges JSON.
                Assert.That(() => System.Text.Json.JsonDocument.Parse(File.ReadAllText(_datei)),
                            Throws.Nothing);

            });

        }

        #endregion

        #region TheStoredIdentity_ContainsTheSecrets()

        /// <summary>
        /// Der Speicher enthält die geheimen Teile - und sagt es.
        /// </summary>
        /// <remarks>
        /// <b>Dieser Test steht hier als Aussage, nicht als Prüfung einer
        /// Absicht.</b> Die Datei ist nicht verschlüsselt; wer sie liest,
        /// liest die Gespräche mit. Eine Verschlüsselung mit einem Schlüssel,
        /// der danebenläge, wäre keine, und einen, den ein Mensch eingibt,
        /// gibt es in dieser Anwendung nicht.
        ///
        /// Wer das später ändert, muss diesen Test ändern - und sieht dabei,
        /// worum es geht.
        /// </remarks>
        [Test]
        public void TheStoredIdentity_ContainsTheSecrets()
        {

            var speicher = new OmemoFileStore(_datei);
            var eigen    = speicher.LoadOrCreateIdentity();

            var text = File.ReadAllText(_datei);

            Assert.That(text,
                        Does.Contain(Convert.ToBase64String(eigen.IdentityKey.PrivateKey)),
                        "Der geheime IdentityKey steht nicht im Klartext in der Datei - " +
                        "wurde die Ablage verschlüsselt? Dann gehört die Bemerkung dazu " +
                        "in OmemoFileStore berichtigt.");

        }

        #endregion

    }

}
