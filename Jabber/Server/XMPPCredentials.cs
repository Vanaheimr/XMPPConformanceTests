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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Die Zugangsdaten eines Kontos in der Form, in der ein Server sie
    /// aufbewahren darf: Salt, Iterationszahl und je Mechanismus die beiden
    /// abgeleiteten Schlüssel aus RFC 5802, Abschnitt 3.
    /// </summary>
    /// <remarks>
    /// Das Passwort selbst kommt hier nicht vor und ist aus dem Gespeicherten
    /// nicht zurückzurechnen. Auch die PLAIN-Anmeldung braucht es nicht
    /// aufzubewahren: sie leitet aus dem angebotenen Klartext mit demselben
    /// Salt neu ab und vergleicht die Ergebnisse.
    ///
    /// Die Serverseite ist absichtlich unabhängig von
    /// <see cref="SCRAMAuthenticator"/> geschrieben, wie im Projekt üblich.
    /// Benutzten beide Seiten dieselben Hilfsfunktionen, prüften die Tests den
    /// Handshake mit derselben Logik, die ihn erzeugt, und ein gemeinsamer
    /// Denkfehler bliebe unentdeckt.
    /// </remarks>
    public sealed class XMPPCredentials
    {

        #region Data

        private readonly Byte[] _salt;
        private readonly Dictionary<SCRAMMechanism, SCRAMKeys> _keys;

        #endregion

        #region Konstanten

        /// <summary>
        /// Iterationszahl für neue Konten.
        /// </summary>
        /// <remarks>
        /// RFC 7677, Abschnitt 4 nennt 4096 als Untergrenze für
        /// SCRAM-SHA-256. Das ist nach heutigen Massstäben wenig - ein echter
        /// Betrieb sollte deutlich höher gehen. Der Wert steht hier, weil jedes
        /// angelegte Testkonto ihn zweimal durchläuft und die Suite sonst
        /// spürbar langsamer würde; er ist je Konto überschreibbar.
        /// </remarks>
        public const Int32 DefaultIterationCount = 4096;

        /// <summary>Länge des erzeugten Salts in Bytes.</summary>
        public const Int32 SaltLength = 16;

        #endregion

        #region Properties

        /// <summary>Das Salt dieses Kontos.</summary>
        public Byte[] Salt => [.. _salt];

        /// <summary>Die Iterationszahl, mit der abgeleitet wurde.</summary>
        public Int32 IterationCount { get; }

        /// <summary>Für welche Mechanismen Schlüssel vorliegen.</summary>
        public IEnumerable<SCRAMMechanism> Mechanisms => _keys.Keys;

        #endregion

        #region Constructor(s)

        private XMPPCredentials(Byte[]                                 salt,
                                Int32                                  iterationCount,
                                Dictionary<SCRAMMechanism, SCRAMKeys>  keys)
        {
            _salt           = salt;
            _keys           = keys;
            IterationCount  = iterationCount;
        }

        #endregion


        #region FromPassword(password, salt = null, iterationCount = DefaultIterationCount)

        /// <summary>
        /// Leitet die Zugangsdaten aus einem Klartextpasswort ab. Danach wird
        /// das Passwort nicht mehr gebraucht.
        /// </summary>
        /// <param name="password">Das Klartextpasswort.</param>
        /// <param name="salt">Vorgegebenes Salt; null erzeugt ein zufälliges.</param>
        /// <param name="iterationCount">Iterationszahl für PBKDF2.</param>
        public static XMPPCredentials FromPassword(String   password,
                                                   Byte[]?  salt             = null,
                                                   Int32    iterationCount   = DefaultIterationCount)
        {

            ArgumentOutOfRangeException.ThrowIfLessThan(iterationCount, 1);

            salt ??= RandomNumberGenerator.GetBytes(SaltLength);

            var keys = new Dictionary<SCRAMMechanism, SCRAMKeys>();

            foreach (var mechanism in Enum.GetValues<SCRAMMechanism>())
                keys[mechanism] = DeriveKeys(password, salt, iterationCount, mechanism);

            return new XMPPCredentials([.. salt], iterationCount, keys);

        }

        #endregion

        #region FromStored(salt, iterationCount, keys)

        /// <summary>
        /// Setzt Zugangsdaten aus dem Gespeicherten wieder zusammen - der Weg
        /// zurück für einen <see cref="IXMPPAccountStore"/>.
        /// </summary>
        /// <remarks>
        /// Ohne Ableitung: die Schlüssel liegen ja bereits vor, und das
        /// Passwort, aus dem sie stammen, gibt es nicht mehr.
        /// </remarks>
        public static XMPPCredentials FromStored(Byte[]                                          salt,
                                                 Int32                                           iterationCount,
                                                 IReadOnlyDictionary<SCRAMMechanism, SCRAMKeys>  keys)
        {

            ArgumentOutOfRangeException.ThrowIfLessThan(iterationCount, 1);

            if (keys.Count == 0)
                throw new ArgumentException("Ohne Schlüssel lässt sich keine Anmeldung prüfen.", nameof(keys));

            return new XMPPCredentials([.. salt],
                                       iterationCount,
                                       keys.ToDictionary(k => k.Key, k => k.Value));

        }

        #endregion

        #region KeysOf(mechanism)

        /// <summary>
        /// Die Schlüssel für einen Mechanismus.
        /// </summary>
        public SCRAMKeys KeysOf(SCRAMMechanism mechanism)
            => _keys[mechanism];

        #endregion

        #region Verify(password)

        /// <summary>
        /// Prüft ein Klartextpasswort, wie es SASL PLAIN liefert.
        /// </summary>
        /// <remarks>
        /// Abgeleitet wird mit dem gespeicherten Salt und der gespeicherten
        /// Iterationszahl; verglichen wird der <c>StoredKey</c>. Der Vergleich
        /// läuft über <see cref="CryptographicOperations.FixedTimeEquals"/> -
        /// ein Vergleich, der beim ersten abweichenden Byte abbricht, verriete
        /// über die Laufzeit, wie weit ein Rateversuch gekommen ist.
        /// </remarks>
        public Boolean Verify(String password)
        {

            var mechanism  = SCRAMMechanism.ScramSha256;
            var candidate  = DeriveKeys(password, _salt, IterationCount, mechanism);

            return CryptographicOperations.FixedTimeEquals(candidate.StoredKey,
                                                           _keys[mechanism].StoredKey);

        }

        #endregion


        #region (private, static) Ableitung nach RFC 5802, Abschnitt 3

        private static SCRAMKeys DeriveKeys(String          password,
                                            Byte[]          salt,
                                            Int32           iterationCount,
                                            SCRAMMechanism  mechanism)
        {

            // SaltedPassword := Hi(Normalize(password), salt, i)
            var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(
                                     Encoding.UTF8.GetBytes(Normalize(password)),
                                     salt,
                                     iterationCount,
                                     HashOf(mechanism),
                                     KeyLengthOf(mechanism)
                                 );

            // ClientKey  := HMAC(SaltedPassword, "Client Key")
            // StoredKey  := H(ClientKey)
            // ServerKey  := HMAC(SaltedPassword, "Server Key")
            var clientKey = Hmac(mechanism, saltedPassword, "Client Key"u8.ToArray());

            return new SCRAMKeys(StoredKey: Hash(mechanism, clientKey),
                                 ServerKey: Hmac(mechanism, saltedPassword, "Server Key"u8.ToArray()));

        }

        /// <summary>
        /// SASLprep (RFC 4013) in der Kurzfassung, die für ASCII genügt.
        /// </summary>
        /// <remarks>
        /// Vollständig verlangte es StringPrep (RFC 3454) samt Abbildungs- und
        /// Verbotstabellen. Für ein Passwort ausserhalb von ASCII kann diese
        /// Fassung ein anderes Ergebnis liefern als eine vollständige - dann
        /// scheitert die Anmeldung, statt jemanden fälschlich einzulassen.
        /// </remarks>
        internal static String Normalize(String input)
            => input.Normalize(NormalizationForm.FormKC);

        internal static HashAlgorithmName HashOf(SCRAMMechanism mechanism)
            => mechanism == SCRAMMechanism.ScramSha256
                   ? HashAlgorithmName.SHA256
                   : HashAlgorithmName.SHA1;

        internal static Int32 KeyLengthOf(SCRAMMechanism mechanism)
            => mechanism == SCRAMMechanism.ScramSha256 ? 32 : 20;

        internal static Byte[] Hmac(SCRAMMechanism mechanism, Byte[] key, Byte[] data)
            => mechanism == SCRAMMechanism.ScramSha256
                   ? HMACSHA256.HashData(key, data)
                   : HMACSHA1.HashData(key, data);

        internal static Byte[] Hash(SCRAMMechanism mechanism, Byte[] data)
            => mechanism == SCRAMMechanism.ScramSha256
                   ? SHA256.HashData(data)
                   : SHA1.HashData(data);

        #endregion

    }

}
