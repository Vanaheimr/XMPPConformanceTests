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

        #region Decoy(user, secret)

        /// <summary>
        /// Erfundene Zugangsdaten für ein Konto, das es nicht gibt - damit ein
        /// unbekannter Benutzername genauso aussieht wie ein bekannter
        /// (RFC 6120, Abschnitt 13.11).
        /// </summary>
        /// <remarks>
        /// „Not reveal whether or not an account exists at a server when an
        /// entity attempts to authenticate" - bei SCRAM genügt dafür der
        /// gleiche Fehler nicht. Wer ein unbekanntes Konto sofort abweist,
        /// beantwortet die erste Nachricht mit einem Fehlschlag und die eines
        /// bestehenden Kontos mit einer Aufforderung; die Auskunft steckt dann
        /// im <b>Ablauf</b> und nicht im Fehlerwort.
        ///
        /// <b>Gleichbleibend, nicht zufällig.</b> Ein Salt, das sich bei jedem
        /// Versuch ändert, wäre selbst die Auskunft: das eines bestehenden
        /// Kontos steht fest. Es entsteht deshalb aus dem Benutzernamen und
        /// einem Serverschlüssel - für jeden Namen ein anderes, für denselben
        /// Namen immer dasselbe, und keines davon vorherzusagen, ohne den
        /// Schlüssel zu kennen. Genau deshalb ist die Iterationszahl auch die
        /// gewöhnliche: eine abweichende wäre wieder ein Erkennungszeichen.
        ///
        /// Die Schlüssel entstehen auf demselben Weg und passen zu keinem
        /// Passwort. Der Austausch läuft damit zu Ende und scheitert dort, wo
        /// er auch bei einem falschen Passwort scheitert - am Beweis.
        ///
        /// <b>Was das nicht leistet:</b> Über einen Neustart hinweg ändern sich
        /// die erfundenen Salts, die echten nicht. Wer denselben Namen davor
        /// und danach probiert, sieht den Unterschied. Ein dauerhafter
        /// Serverschlüssel gehörte in den Kontenspeicher und ist nicht Teil
        /// dieses Schritts.
        /// </remarks>
        /// <param name="user">Der Benutzername aus der client-first-message.</param>
        /// <param name="secret">Der Serverschlüssel, aus dem abgeleitet wird.</param>
        public static XMPPCredentials Decoy(String user, Byte[] secret)
        {

            var keys = new Dictionary<SCRAMMechanism, SCRAMKeys>();

            foreach (var mechanism in Enum.GetValues<SCRAMMechanism>())
            {

                var laenge = KeyLengthOf(mechanism);

                keys[mechanism] = new SCRAMKeys(
                                      StoredKey: Abgeleitet(secret, $"stored:{mechanism}:{user}", laenge),
                                      ServerKey: Abgeleitet(secret, $"server:{mechanism}:{user}", laenge));

            }

            return new XMPPCredentials(Abgeleitet(secret, $"salt:{user}", SaltLength),
                                       DefaultIterationCount,
                                       keys);

        }

        private static Byte[] Abgeleitet(Byte[] secret, String zweck, Int32 length)
            => HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(zweck))[..length];

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

            var mechanism = SCRAMMechanism.ScramSha256;

            SCRAMKeys candidate;

            try
            {
                candidate = DeriveKeys(password, _salt, IterationCount, mechanism);
            }
            catch (AuthenticationException)
            {
                // Ein Passwort, das sich nicht nach SASLprep vorbereiten lässt,
                // kann auf keinen gespeicherten Schlüssel führen. Das ist ein
                // Fehlversuch und kein Serverfehler - über die Leitung kommt,
                // was der Gegenüber schickt, und das darf hier nichts umwerfen.
                return false;
            }

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
        /// SASLprep (RFC 4013) - dieselbe Vorbereitung wie auf der Client-Seite.
        /// </summary>
        /// <remarks>
        /// Dass hier <see cref="SaslPrep"/> steht und nicht eine eigene
        /// Rechnung, ist der Punkt: Server und Client müssen aus derselben
        /// Eingabe denselben Schlüssel gewinnen. Zwei Fassungen desselben
        /// Verfahrens wären zwei Gelegenheiten auseinanderzulaufen, und
        /// auffallen würde es erst bei einem Passwort ausserhalb von ASCII.
        /// </remarks>
        internal static String Normalize(String input)
            => SaslPrep.Prepare(input);

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
