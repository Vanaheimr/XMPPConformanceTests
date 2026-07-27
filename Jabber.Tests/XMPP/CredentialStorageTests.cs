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

using System.Reflection;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Wie der Server Passwörter aufbewahrt (RFC 5802, Abschnitt 3).
    ///
    /// Sie lagen im Klartext im Speicher der <c>XMPPServer</c>-Instanz. Das
    /// war nicht nur unschön: sobald Konten die Instanz überleben sollen -
    /// also mit S2 -, landete der Klartext auf der Platte.
    ///
    /// Aufbewahrt wird jetzt, was RFC 5802 dafür vorsieht: Salt,
    /// Iterationszahl und je Mechanismus <c>StoredKey</c> und
    /// <c>ServerKey</c>. Aus keinem davon lässt sich das Passwort
    /// zurückrechnen.
    /// </summary>
    [TestFixture]
    public class CredentialStorageTests
    {

        #region CorrectPassword_IsAccepted()

        /// <summary>
        /// Das Naheliegende zuerst: das richtige Passwort wird angenommen.
        /// </summary>
        [Test]
        public void CorrectPassword_IsAccepted()
        {

            var credentials = XMPPCredentials.FromPassword("geheim");

            Assert.That(credentials.Verify("geheim"), Is.True);

        }

        #endregion

        #region WrongPassword_IsRejected()

        /// <summary>
        /// Und die Gegenprobe, ohne die der vorige Test nichts aussagt.
        /// </summary>
        [Test]
        public void WrongPassword_IsRejected()
        {

            var credentials = XMPPCredentials.FromPassword("geheim");

            Assert.Multiple(() =>
            {
                Assert.That(credentials.Verify("Geheim"),  Is.False, "Gross- und Kleinschreibung.");
                Assert.That(credentials.Verify("geheim "), Is.False, "Angehängtes Leerzeichen.");
                Assert.That(credentials.Verify(""),        Is.False, "Leeres Passwort.");
                Assert.That(credentials.Verify("geheiM"),  Is.False, "Ein Zeichen anders.");
            });

        }

        #endregion

        #region SamePassword_GetsDifferentKeys()

        /// <summary>
        /// Zwei Konten mit demselben Passwort dürfen nicht dasselbe
        /// Gespeicherte tragen - sonst verriete ein Blick in die Kontenliste,
        /// wer dasselbe Passwort benutzt, und eine einmal berechnete
        /// Regenbogentabelle träfe alle auf einmal.
        /// </summary>
        [Test]
        public void SamePassword_GetsDifferentKeys()
        {

            var eine    = XMPPCredentials.FromPassword("geheim");
            var andere  = XMPPCredentials.FromPassword("geheim");

            Assert.Multiple(() =>
            {

                Assert.That(eine.Salt, Is.Not.EqualTo(andere.Salt),
                            "Jedes Konto braucht ein eigenes Salt.");

                Assert.That(eine.KeysOf(SCRAMMechanism.ScramSha256).StoredKey,
                            Is.Not.EqualTo(andere.KeysOf(SCRAMMechanism.ScramSha256).StoredKey),
                            "Gleiches Passwort, verschiedenes Salt, also verschiedene Schlüssel.");

                // Beide müssen trotzdem funktionieren.
                Assert.That(eine.Verify("geheim"),   Is.True);
                Assert.That(andere.Verify("geheim"), Is.True);

            });

        }

        #endregion

        #region Keys_DifferPerMechanism()

        /// <summary>
        /// SCRAM-SHA-1 und SCRAM-SHA-256 leiten aus demselben Passwort
        /// verschiedene Schlüssel ab; beide werden aufbewahrt, weil der Client
        /// sich den Mechanismus aussucht.
        /// </summary>
        [Test]
        public void Keys_DifferPerMechanism()
        {

            var credentials = XMPPCredentials.FromPassword("geheim");

            var sha1    = credentials.KeysOf(SCRAMMechanism.ScramSha1);
            var sha256  = credentials.KeysOf(SCRAMMechanism.ScramSha256);

            Assert.Multiple(() =>
            {
                Assert.That(sha1.StoredKey,   Has.Length.EqualTo(20));
                Assert.That(sha256.StoredKey, Has.Length.EqualTo(32));
                Assert.That(sha1.ServerKey,   Has.Length.EqualTo(20));
                Assert.That(sha256.ServerKey, Has.Length.EqualTo(32));

                Assert.That(sha1.StoredKey, Is.Not.EqualTo(sha1.ServerKey),
                            "StoredKey und ServerKey sind verschieden abgeleitet.");
            });

        }

        #endregion

        #region StoredKey_IsTheHashOfTheClientKey()

        /// <summary>
        /// Der Kern der Konstruktion, gegen RFC 5802, Abschnitt 3
        /// nachgerechnet: der aufbewahrte Schlüssel ist der <b>Hash</b> des
        /// ClientKey, nicht der ClientKey selbst.
        /// </summary>
        /// <remarks>
        /// Genau daran hängt der Sinn: wer den StoredKey erbeutet, kann damit
        /// eine Anmeldung prüfen, aber keine durchführen. Würde der ClientKey
        /// selbst gespeichert, wäre er ein Passwortersatz.
        ///
        /// Nachgerechnet wird hier unabhängig von der Ableitung, mit den
        /// Formeln aus dem RFC.
        /// </remarks>
        [Test]
        public void StoredKey_IsTheHashOfTheClientKey()
        {

            var salt         = new Byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var credentials  = XMPPCredentials.FromPassword("geheim", salt, 4096);

            // SaltedPassword := Hi(Normalize(password), salt, i)
            var saltedPassword = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                                     Encoding.UTF8.GetBytes("geheim"),
                                     salt,
                                     4096,
                                     System.Security.Cryptography.HashAlgorithmName.SHA256,
                                     32);

            var clientKey  = System.Security.Cryptography.HMACSHA256.HashData(saltedPassword, "Client Key"u8.ToArray());
            var storedKey  = System.Security.Cryptography.SHA256.HashData(clientKey);
            var serverKey  = System.Security.Cryptography.HMACSHA256.HashData(saltedPassword, "Server Key"u8.ToArray());

            var keys = credentials.KeysOf(SCRAMMechanism.ScramSha256);

            Assert.Multiple(() =>
            {
                Assert.That(keys.StoredKey, Is.EqualTo(storedKey));
                Assert.That(keys.ServerKey, Is.EqualTo(serverKey));

                Assert.That(keys.StoredKey, Is.Not.EqualTo(clientKey),
                            "Der ClientKey selbst darf nicht gespeichert sein.");
            });

        }

        #endregion

        #region GivenSalt_IsUsedUnchanged()

        /// <summary>
        /// Ein vorgegebenes Salt wird übernommen - der Weg, auf dem ein
        /// Kontenspeicher gespeicherte Zugangsdaten wieder einliest.
        /// </summary>
        [Test]
        public void GivenSalt_IsUsedUnchanged()
        {

            var salt         = new Byte[] { 42, 42, 42, 42, 42, 42, 42, 42 };
            var credentials  = XMPPCredentials.FromPassword("geheim", salt, 1024);

            Assert.Multiple(() =>
            {
                Assert.That(credentials.Salt,            Is.EqualTo(salt));
                Assert.That(credentials.IterationCount,  Is.EqualTo(1024));
                Assert.That(credentials.Verify("geheim"), Is.True);
            });

        }

        #endregion

        #region Salt_CannotBeChangedFromOutside()

        /// <summary>
        /// <c>Salt</c> gibt eine Kopie heraus. Gäbe es das innere Array
        /// heraus, könnte ein Aufrufer es überschreiben und damit jede weitere
        /// Prüfung dieses Kontos scheitern lassen.
        /// </summary>
        [Test]
        public void Salt_CannotBeChangedFromOutside()
        {

            var credentials = XMPPCredentials.FromPassword("geheim");

            var kopie = credentials.Salt;
            Array.Clear(kopie);

            Assert.Multiple(() =>
            {
                Assert.That(credentials.Salt,            Is.Not.EqualTo(kopie));
                Assert.That(credentials.Verify("geheim"), Is.True);
            });

        }

        #endregion

        #region Account_KeepsNoPlaintextPassword()

        /// <summary>
        /// Die eigentliche Zusage dieses Schritts: nach dem Anlegen eines
        /// Kontos liegt das Klartextpasswort in keinem Feld mehr.
        /// </summary>
        /// <remarks>
        /// Über Reflection und damit grob - aber die Zusage ist eine über den
        /// Zustand des Objekts, und nur so lässt sie sich prüfen, ohne sie auf
        /// die Felder einzuschränken, an die ich gerade denke. Absichtlich
        /// wird auch das <c>XMPPCredentials</c>-Objekt mit durchsucht.
        /// </remarks>
        [Test]
        public void Account_KeepsNoPlaintextPassword()
        {

            const String passwort = "ein-sehr-eigentuemliches-passwort";

            var account = new XMPPAccount("alice@localhost", passwort);

            var gefunden = new List<String>();

            SucheKlartext(account,       "XMPPAccount",     passwort, gefunden, 0);
            SucheKlartext(account.Credentials, "Credentials", passwort, gefunden, 0);

            Assert.That(gefunden, Is.Empty,
                        $"Das Klartextpasswort steckt noch in: {String.Join(", ", gefunden)}");

        }

        /// <summary>
        /// Durchsucht die Felder eines Objekts flach nach dem Klartext -
        /// Zeichenketten direkt, Byte-Arrays als UTF-8 gelesen.
        /// </summary>
        private static void SucheKlartext(Object        ziel,
                                          String        pfad,
                                          String        passwort,
                                          List<String>  gefunden,
                                          Int32         tiefe)
        {

            if (tiefe > 3)
                return;

            foreach (var feld in ziel.GetType().GetFields(BindingFlags.Instance |
                                                          BindingFlags.Public   |
                                                          BindingFlags.NonPublic))
            {

                var wert = feld.GetValue(ziel);

                switch (wert)
                {

                    case String text when text.Contains(passwort, StringComparison.Ordinal):
                        gefunden.Add($"{pfad}.{feld.Name}");
                        break;

                    case Byte[] bytes when Encoding.UTF8.GetString(bytes).Contains(passwort, StringComparison.Ordinal):
                        gefunden.Add($"{pfad}.{feld.Name}");
                        break;

                    case SCRAMKeys keys:
                        SucheKlartext(keys, $"{pfad}.{feld.Name}", passwort, gefunden, tiefe + 1);
                        break;

                    case System.Collections.IDictionary eintraege:
                        foreach (var eintrag in eintraege.Values)
                            if (eintrag is not null)
                                SucheKlartext(eintrag, $"{pfad}.{feld.Name}[]", passwort, gefunden, tiefe + 1);
                        break;

                }

            }

        }

        #endregion

    }

}
