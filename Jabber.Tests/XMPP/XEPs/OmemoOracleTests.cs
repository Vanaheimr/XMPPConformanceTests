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

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// OMEMO gegen die Referenzimplementierung - python-omemo (Syndace),
    /// dieselbe Fassung `urn:xmpp:omemo:2`.
    /// </summary>
    /// <remarks>
    /// <b>Diese Sammlung kann eine Klasse von Fehlern grundsätzlich nicht
    /// finden.</b> Sind beide Seiten derselbe Code, kommen sie auch dann
    /// überein, wenn beide gleich falsch rechnen. In D62 bis D65 war das
    /// fünfmal der Befund - ein Info-String, eine Reihenfolge, eine
    /// Einbettung. Jedes Mal hätten sich zwei Clients dieses Hauses bestens
    /// verstanden und kein einziger fremder.
    ///
    /// Dagegen hilft nur eine Gegenstelle, die niemand hier geschrieben hat.
    ///
    /// <b>Diese Tests überspringen sich selbst</b>, wenn das Orakel nicht
    /// erreichbar ist - wie die Tests gegen Prosody und ejabberd. Ein Lauf
    /// ohne WSL soll nicht rot sein, nur weniger aussagen. Wie viele
    /// übersprungen sind, sagt hinterher, was gemessen wurde.
    ///
    /// Geprüft wird alles von der Nutzlast abwärts: Bundle-Format, X3DH,
    /// Ratchet-Anfang, Drahtformat. Die SCE-Hülle bleibt aussen vor -
    /// python-omemo überlässt sie der Anwendung, die es benutzt.
    /// </remarks>
    [TestFixture]
    public class OmemoOracleTests
    {

        #region Das Orakel aufrufen

        private const String LibPfad     = "/tmp/omemo-oracle/lib";
        private const String SkriptPfad  = "Jabber.Tests/XMPP/XEPs/Orakel/omemo_orakel.py";

        private static String? _grundFuerUeberspringen;

        [OneTimeSetUp]
        public void OrakelPruefen()
        {

            var (code, _, fehler) = Rufe("bundle", null, pruefen: false);

            if (code != 0)
                _grundFuerUeberspringen =
                    "Das Orakel ist nicht erreichbar (python-omemo in WSL unter " +
                    $"{LibPfad}): {fehler.Split('\n').LastOrDefault(z => z.Trim().Length > 0)?.Trim()}";

        }

        [SetUp]
        public void UeberspringenWennNoetig()
        {
            if (_grundFuerUeberspringen is not null)
                Assert.Ignore(_grundFuerUeberspringen);
        }

        /// <summary>
        /// Startet das Orakel in WSL und gibt zurück, was es gesagt hat.
        /// </summary>
        /// <remarks>
        /// Der Auftrag geht über eine Datei und nicht über die Befehlszeile:
        /// Ein Bundle mit hundert PreKeys sprengt jede Zeilenlänge, und
        /// base64 in Anführungszeichen über zwei Betriebssystem-Grenzen
        /// hinweg ist eine Fehlerquelle, die niemand braucht.
        /// </remarks>
        private static (Int32 Code, String Ausgabe, String Fehler) Rufe(String            modus,
                                                                        Object?           auftrag,
                                                                        Boolean           pruefen = true)
        {

            var wurzel = new DirectoryInfo(AppContext.BaseDirectory);

            while (wurzel is not null && !File.Exists(Path.Combine(wurzel.FullName, "WORKPLAN.md")))
                wurzel = wurzel.Parent;

            Assert.That(wurzel, Is.Not.Null, "Das Wurzelverzeichnis des Repositories ist nicht zu finden.");

            String? auftragsdatei = null;

            if (auftrag is not null)
            {
                auftragsdatei = Path.Combine(Path.GetTempPath(), $"orakel-{Guid.NewGuid():N}.json");
                File.WriteAllText(auftragsdatei, JsonSerializer.Serialize(auftrag));
            }

            var befehl = $"PYTHONPATH={LibPfad} python3 '{WslPfad(Path.Combine(wurzel!.FullName, SkriptPfad))}'" +
                         $" {modus}" +
                         (auftragsdatei is not null ? $" '{WslPfad(auftragsdatei)}'" : "");

            var start = new ProcessStartInfo("wsl", $"-d Debian -- bash -c \"{befehl}\"") {
                RedirectStandardOutput  = true,
                RedirectStandardError   = true,
                UseShellExecute         = false
            };

            using var prozess = Process.Start(start)!;

            var ausgabe = prozess.StandardOutput.ReadToEnd();
            var fehler  = prozess.StandardError.ReadToEnd();

            prozess.WaitForExit(120_000);

            if (auftragsdatei is not null)
                try { File.Delete(auftragsdatei); } catch { /* egal */ }

            if (pruefen && prozess.ExitCode != 0)
                Assert.Fail($"Das Orakel scheiterte im Modus '{modus}':\n{fehler}");

            return (prozess.ExitCode, ausgabe, fehler);

        }

        private static String WslPfad(String windowsPfad)
            => "/mnt/" + Char.ToLowerInvariant(windowsPfad[0]) +
               windowsPfad[2..].Replace('\\', '/');

        private static JsonElement Antwort(String ausgabe)
            => JsonDocument.Parse(ausgabe.Trim()).RootElement;

        private static String B64(Byte[] daten)
            => Convert.ToBase64String(daten);

        #endregion

        #region Unser Bundle als Auftrag

        /// <summary>
        /// Unser Bundle in der Gestalt, die das Orakel erwartet.
        /// </summary>
        private static Object AlsAuftrag(OmemoIdentity eigen, String jid, String? plaintext = null)
        {

            var bundle = eigen.Bundle();

            return new Dictionary<String, Object?> {
                ["jid"]                 = jid,
                ["device_id"]           = eigen.DeviceId,
                ["identity_key"]        = B64(bundle.IdentityKey),
                ["signed_pre_key_id"]   = bundle.SignedPreKeyId,
                ["signed_pre_key"]      = B64(bundle.SignedPreKey),
                ["signed_pre_key_sig"]  = B64(bundle.SignedPreKeySignature),
                ["pre_keys"]            = bundle.PreKeys
                                                .Take(10)
                                                .Select(p => new Dictionary<String, Object> {
                                                            ["id"]   = p.Id,
                                                            ["key"]  = B64(p.PublicKey)
                                                        })
                                                .ToList(),
                ["plaintext"]           = plaintext
            };

        }

        #endregion


        #region TheReferenceAcceptsOurBundle()

        /// <summary>
        /// Die Referenzimplementierung nimmt unser Bundle an - <b>und prüft
        /// dabei die Signatur über den Signed PreKey selbst</b>.
        /// </summary>
        /// <remarks>
        /// Das war eine ungeprüfte Annahme aus D63, ausdrücklich als solche
        /// vermerkt: Unterschrieben wird der Signed PreKey in Montgomery-Form.
        /// Abschnitt 5.3.2 sagt nur „the signed PreKey signature" und lässt
        /// die Kodierung offen. <b>Hier entscheidet sich, ob die Lesart
        /// stimmt</b> - eine fremde Bibliothek prüft die Signatur mit ihrer
        /// eigenen Vorstellung davon, worüber sie geht.
        /// </remarks>
        [Test]
        public void TheReferenceAcceptsOurBundle()
        {

            var eigen = OmemoIdentity.Create();

            var (code, ausgabe, fehler) = Rufe("encrypt",
                                               AlsAuftrag(eigen, "wir@example.org", "Probe"),
                                               pruefen: false);

            Assert.That(code, Is.EqualTo(0),
                        "Die Referenzimplementierung hat unser Bundle abgelehnt. Wenn hier von " +
                        "einer ungültigen Signatur die Rede ist, unterschreiben wir den Signed " +
                        "PreKey über etwas anderes als sie erwartet - die ungeprüfte Annahme aus " +
                        $"D63:\n{fehler}");

            Assert.That(Antwort(ausgabe).GetProperty("key").GetString(), Is.Not.Empty);

        }

        #endregion

        #region WeCanReadWhatTheReferenceWrote()

        /// <summary>
        /// <b>Der Test, für den es diese Etappe gibt:</b> Die
        /// Referenzimplementierung verschlüsselt, wir entschlüsseln.
        /// </summary>
        /// <remarks>
        /// Was hier alles zugleich geprüft wird, und zwar gegen fremden Code:
        /// die Kodierung des Bundles, die Reihenfolge der vier
        /// Diffie-Hellman, der Info-String von X3DH, der 0xFF-Vorspann, die
        /// Beigabe aus beiden IdentityKeys, der Anfang der Ratsche, die
        /// Info-Strings der Wurzelkette und des Nachrichtenschlüssels, die
        /// Konstanten 0x01/0x02, die Protobuf-Feldnummern, die Einbettung des
        /// Geheimtexts in die Nachricht, die Kürzung des HMAC und die
        /// Ableitung der Nutzlast.
        ///
        /// <b>Jeder einzelne dieser Punkte war in D62 bis D65 eine überlebende
        /// Mutation oder ein Fund beim Lesen.</b> Dieser eine Test hätte sie
        /// alle gefunden.
        /// </remarks>
        [Test]
        public void WeCanReadWhatTheReferenceWrote()
        {

            const String geheim = "Von der Referenzimplementierung geschrieben";

            var eigen  = OmemoIdentity.Create();
            var jid    = "wir@example.org";

            var (_, ausgabe, _) = Rufe("encrypt", AlsAuftrag(eigen, jid, geheim));
            var antwort         = Antwort(ausgabe);

            // Geprüft wird auf der Schicht, die das Orakel abdeckt: vom
            // Schlüsselaustausch bis zur Nutzlast. Die SCE-Hülle bleibt
            // aussen vor - python-omemo überlässt sie der Anwendung, und eine
            // Hülle, die ich selbst im Orakel bauen würde, wäre keine fremde
            // Prüfung, sondern dieselbe Annahme zweimal.
            var austausch = OmemoKeyExchange.Decode(
                                Convert.FromBase64String(antwort.GetProperty("key").GetString()!));

            var x3dh = X3DH.Accept(eigen,
                                   austausch.IdentityKey,
                                   austausch.EphemeralKey,
                                   austausch.SignedPreKeyId,
                                   austausch.PreKeyId == 0 ? null : austausch.PreKeyId);

            var ratchet = DoubleRatchet.InitiateAsReceiver(x3dh.SharedSecret, eigen.SignedPreKey);

            var schluesselUndHmac = ratchet.Decrypt(
                                        OmemoWireFormat.Decode(austausch.Message),
                                        x3dh.AssociatedData);

            var klartext = OmemoPayloadCipher.Decrypt(
                               Convert.FromBase64String(antwort.GetProperty("payload").GetString()!),
                               schluesselUndHmac);

            Assert.That(Encoding.UTF8.GetString(klartext), Is.EqualTo(geheim),
                        "Was die Referenzimplementierung geschrieben hat, konnten wir nicht lesen.");

        }

        #endregion

        #region TheReferenceCanReadWhatWeWrote()

        /// <summary>
        /// Die Gegenrichtung: <b>wir</b> verschlüsseln, die
        /// Referenzimplementierung liest.
        /// </summary>
        /// <remarks>
        /// <b>Das ist die Richtung, die darüber entscheidet, ob uns jemand
        /// lesen kann.</b> Die Hinrichtung prüft, ob wir fremde Nachrichten
        /// verstehen; erst diese hier prüft, ob unsere verstanden werden - und
        /// das ist die Frage, an der ein Client scheitert, ohne dass es
        /// jemandem auffällt: Wer nie eine Antwort bekommt, weiss nicht, ob
        /// niemand schreiben wollte oder niemand lesen konnte.
        ///
        /// Geprüft wird dabei zusätzlich unsere Kodierung des
        /// Schlüsselaustauschs: Die Bibliothek trennt aus unserem
        /// <c>&lt;key kex='true'/&gt;</c> beide Teile heraus - der
        /// Austausch und die eingepackte Nachricht. Gelingt das, stimmen
        /// unsere Feldnummern.
        /// </remarks>
        [Test]
        public void TheReferenceCanReadWhatWeWrote()
        {

            const String geheim = "Von uns geschrieben, von der Referenz gelesen";

            var zustand = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                // 1. Das Orakel gibt sein Bundle heraus - und merkt sich seine
                //    Schlüssel in einer Datei, sonst wäre der zweite Aufruf
                //    ein anderes Gerät.
                var (_, bundleAusgabe, _) = Rufe("bundle", new Dictionary<String, Object> {
                                                               ["state"] = WslPfad(zustand)
                                                           });

                var b = Antwort(bundleAusgabe);

                var bundle = new OmemoBundle(
                                 Convert.FromBase64String(b.GetProperty("identity_key").GetString()!),
                                 b.GetProperty("signed_pre_key_id").GetUInt32(),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key").GetString()!),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key_sig").GetString()!),
                                 [.. b.GetProperty("pre_keys").EnumerateArray()
                                      .Select(p => new OmemoPreKey(
                                                       p.GetProperty("id").GetUInt32(),
                                                       Convert.FromBase64String(p.GetProperty("key").GetString()!)))]);

                // Und schon hier fällt eine Prüfung an: Wir rechnen die
                // Signatur der Referenz nach.
                Assert.That(bundle.SignatureIsValid(), Is.True,
                            "Wir halten die Signatur der Referenzimplementierung für ungültig - " +
                            "dann prüfen wir über etwas anderes, als sie unterschreibt.");

                // 2. Wir verschlüsseln dagegen.
                var eigen     = OmemoIdentity.Create();
                var x3dh      = X3DH.Initiate(eigen, bundle);
                var ratchet   = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
                var nutzlast  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(geheim));
                var inhalt    = ratchet.Encrypt(nutzlast.KeyAndHmac, x3dh.AssociatedData);

                var austausch = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                     bundle.SignedPreKeyId,
                                                     eigen.PublicIdentityKey,
                                                     x3dh.EphemeralKey!,
                                                     OmemoWireFormat.Encode(inhalt));

                // 3. Das Orakel liest.
                var (code, ausgabe, fehler) = Rufe("decrypt",
                                                   new Dictionary<String, Object> {
                                                       ["state"]             = WslPfad(zustand),
                                                       ["key"]               = B64(austausch.Encode()),
                                                       ["payload"]           = B64(nutzlast.Ciphertext),
                                                       ["sender_jid"]        = "wir@example.org",
                                                       ["sender_device_id"]  = (Int32) eigen.DeviceId
                                                   },
                                                   pruefen: false);

                Assert.That(code, Is.EqualTo(0),
                            $"Die Referenzimplementierung konnte unsere Nachricht nicht lesen:\n{fehler}");

                Assert.That(Antwort(ausgabe).GetProperty("plaintext").GetString(), Is.EqualTo(geheim));

            }
            finally
            {
                try { File.Delete(zustand); } catch { /* egal */ }
            }

        }

        #endregion

    }

}
