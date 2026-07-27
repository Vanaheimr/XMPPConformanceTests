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
    /// Die Serverseite eines SCRAM-Austauschs (RFC 5802, RFC 7677) - ein
    /// Objekt je laufender Anmeldung.
    /// </summary>
    /// <remarks>
    /// Absichtlich unabhängig von <see cref="SCRAMAuthenticator"/>
    /// geschrieben, nicht als dessen Spiegelbild. Teilten sich beide Seiten
    /// den Code, prüften die Tests den Handshake mit derselben Logik, die ihn
    /// erzeugt: ein falsch zusammengesetzter <c>AuthMessage</c> zum Beispiel
    /// wäre auf beiden Seiten gleich falsch und fiele nirgends auf.
    ///
    /// Nicht implementiert ist Channel Binding (<c>-PLUS</c>). Der Server
    /// prüft aber, dass der Client denselben GS2-Header meldet, den er
    /// geschickt hat - sonst könnte ein Zwischenmann dem Client Channel
    /// Binding ausreden, ohne dass es auffiele (RFC 5802, Abschnitt 6).
    /// </remarks>
    internal sealed class SCRAMExchange
    {

        #region Data

        private readonly XMPPAccount _account;
        private readonly SCRAMMechanism _mechanism;
        private readonly String _gs2Header;
        private readonly String _clientFirstBare;
        private readonly String _combinedNonce;
        private readonly String _serverFirst;

        #endregion

        #region Properties

        /// <summary>Das Konto, um dessen Anmeldung es geht.</summary>
        public XMPPAccount Account => _account;

        /// <summary>
        /// Die server-first-message, fertig für <c>&lt;challenge/&gt;</c>.
        /// </summary>
        public String Challenge => Convert.ToBase64String(Encoding.UTF8.GetBytes(_serverFirst));

        #endregion

        #region Constructor(s)

        private SCRAMExchange(XMPPAccount     account,
                              SCRAMMechanism  mechanism,
                              String          gs2Header,
                              String          clientFirstBare,
                              String          combinedNonce,
                              String          serverFirst)
        {
            _account          = account;
            _mechanism        = mechanism;
            _gs2Header        = gs2Header;
            _clientFirstBare  = clientFirstBare;
            _combinedNonce    = combinedNonce;
            _serverFirst      = serverFirst;
        }

        #endregion


        #region Begin(clientFirstBase64, mechanism, lookup)

        /// <summary>
        /// Nimmt die client-first-message entgegen und bereitet die Antwort
        /// vor. Null bedeutet: unlesbar oder unbekanntes Konto.
        /// </summary>
        /// <param name="clientFirstBase64">Nutzlast des <c>&lt;auth/&gt;</c>.</param>
        /// <param name="mechanism">Der vom Client gewählte Mechanismus.</param>
        /// <param name="lookup">Sucht ein Konto zum Benutzernamen.</param>
        public static SCRAMExchange? Begin(String                       clientFirstBase64,
                                           SCRAMMechanism               mechanism,
                                           Func<String, XMPPAccount?>   lookup)
        {

            String clientFirst;

            try
            {
                clientFirst = Encoding.UTF8.GetString(Convert.FromBase64String(clientFirstBase64));
            }
            catch (FormatException)
            {
                return null;
            }

            // GS2-Header: "n,," ohne Channel Binding und ohne authzid, "y,,"
            // wenn der Client Channel Binding kann und meint der Server könne
            // es nicht. Beides endet nach dem zweiten Komma.
            var kopfEnde = NthComma(clientFirst, 2);

            if (kopfEnde < 0)
                return null;

            var gs2Header        = clientFirst[..(kopfEnde + 1)];
            var clientFirstBare  = clientFirst[(kopfEnde + 1)..];

            var benutzer  = Attribute(clientFirstBare, "n");
            var nonce     = Attribute(clientFirstBare, "r");

            if (benutzer is null || nonce is null || nonce.Length == 0)
                return null;

            var account = lookup(Unescape(benutzer));

            if (account is null)
                return null;

            var credentials    = account.Credentials;
            var combinedNonce  = nonce + Nonce();

            var serverFirst = $"r={combinedNonce}," +
                              $"s={Convert.ToBase64String(credentials.Salt)}," +
                              $"i={credentials.IterationCount}";

            return new SCRAMExchange(account,
                                     mechanism,
                                     gs2Header,
                                     clientFirstBare,
                                     combinedNonce,
                                     serverFirst);

        }

        #endregion

        #region Complete(clientFinalBase64)

        /// <summary>
        /// Prüft die client-final-message. Zurück kommt die
        /// server-final-message für das <c>&lt;success/&gt;</c>, oder null,
        /// wenn der Beweis nicht stimmt.
        /// </summary>
        /// <remarks>
        /// Der Server rechnet den <c>ClientKey</c> aus dem Beweis zurück und
        /// prüft, ob dessen Hash der aufbewahrte <c>StoredKey</c> ist. Er
        /// braucht dafür weder das Passwort noch den ClientKey selbst - genau
        /// deshalb muss er beides nicht aufbewahren.
        /// </remarks>
        public String? Complete(String clientFinalBase64)
        {

            String clientFinal;

            try
            {
                clientFinal = Encoding.UTF8.GetString(Convert.FromBase64String(clientFinalBase64));
            }
            catch (FormatException)
            {
                return null;
            }

            var proofBeginn = clientFinal.LastIndexOf(",p=", StringComparison.Ordinal);

            if (proofBeginn < 0)
                return null;

            var clientFinalOhneBeweis  = clientFinal[..proofBeginn];
            var binding                = Attribute(clientFinalOhneBeweis, "c");
            var nonce                  = Attribute(clientFinalOhneBeweis, "r");
            var beweisBase64           = clientFinal[(proofBeginn + 3)..];

            if (binding is null || nonce is null)
                return null;

            // Der Client muss die Nonce des Servers zurückspiegeln. Ohne diese
            // Prüfung liesse sich ein früherer Austausch wiedereinspielen.
            if (!String.Equals(nonce, _combinedNonce, StringComparison.Ordinal))
                return null;

            // Und er muss denselben GS2-Header melden, den er geschickt hat.
            if (!String.Equals(binding,
                               Convert.ToBase64String(Encoding.UTF8.GetBytes(_gs2Header)),
                               StringComparison.Ordinal))
                return null;

            Byte[] beweis;

            try
            {
                beweis = Convert.FromBase64String(beweisBase64);
            }
            catch (FormatException)
            {
                return null;
            }

            var keys = _account.Credentials.KeysOf(_mechanism);

            if (beweis.Length != keys.StoredKey.Length)
                return null;

            var authMessage = $"{_clientFirstBare},{_serverFirst},{clientFinalOhneBeweis}";
            var authBytes   = Encoding.UTF8.GetBytes(authMessage);

            // ClientSignature := HMAC(StoredKey, AuthMessage)
            // ClientKey       := ClientProof XOR ClientSignature
            var clientSignature = XMPPCredentials.Hmac(_mechanism, keys.StoredKey, authBytes);
            var clientKey       = XOR(beweis, clientSignature);

            if (!CryptographicOperations.FixedTimeEquals(XMPPCredentials.Hash(_mechanism, clientKey),
                                                         keys.StoredKey))
                return null;

            // ServerSignature := HMAC(ServerKey, AuthMessage)
            var serverSignature = XMPPCredentials.Hmac(_mechanism, keys.ServerKey, authBytes);

            return Convert.ToBase64String(
                       Encoding.UTF8.GetBytes($"v={Convert.ToBase64String(serverSignature)}"));

        }

        #endregion


        #region (private, static) Hilfsfunktionen

        /// <summary>
        /// Liest den Wert eines Attributs aus einer SCRAM-Nachricht.
        /// </summary>
        /// <remarks>
        /// Verankert am Anfang oder hinter einem Komma. Eine ungebundene Suche
        /// nach <c>i=</c> träfe sonst auch ein <c>i=</c> mitten in Nonce oder
        /// Salt - RFC 5802 erlaubt dort jedes druckbare Zeichen ausser dem
        /// Komma.
        /// </remarks>
        private static String? Attribute(String nachricht, String name)
        {

            for (var i = 0; i <= nachricht.Length - name.Length - 1; i++)
            {

                if (i > 0 && nachricht[i - 1] != ',')
                    continue;

                if (String.CompareOrdinal(nachricht, i, name, 0, name.Length) != 0)
                    continue;

                if (nachricht[i + name.Length] != '=')
                    continue;

                var wertBeginn  = i + name.Length + 1;
                var wertEnde    = nachricht.IndexOf(',', wertBeginn);

                return wertEnde < 0
                           ? nachricht[wertBeginn..]
                           : nachricht[wertBeginn..wertEnde];

            }

            return null;

        }

        /// <summary>Position des n-ten Kommas, oder -1.</summary>
        private static Int32 NthComma(String text, Int32 n)
        {

            var position = -1;

            for (var i = 0; i < n; i++)
            {

                position = text.IndexOf(',', position + 1);

                if (position < 0)
                    return -1;

            }

            return position;

        }

        /// <summary>
        /// RFC 5802: im Benutzernamen steht <c>=2C</c> für ein Komma und
        /// <c>=3D</c> für ein Gleichheitszeichen.
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge ist nicht beliebig: erst das Komma, dann das
        /// Gleichheitszeichen. Andersherum würde aus einem übertragenen
        /// <c>=3D2C</c> - also dem Text "=2C" - fälschlich ein Komma.
        /// </remarks>
        private static String Unescape(String benutzer)
            => benutzer.Replace("=2C", ",").Replace("=3D", "=");

        private static String Nonce()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

        private static Byte[] XOR(Byte[] a, Byte[] b)
        {

            var ergebnis = new Byte[a.Length];

            for (var i = 0; i < a.Length; i++)
                ergebnis[i] = (Byte) (a[i] ^ b[i]);

            return ergebnis;

        }

        #endregion

    }

}
