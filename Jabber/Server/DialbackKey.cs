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
    /// Der Dialback-Schlüssel aus XEP-0220, Abschnitt 2.1.1 (Verfahren aus
    /// XEP-0185).
    /// </summary>
    /// <remarks>
    /// <code>
    /// key = HMAC-SHA256(SHA256(Secret), { Target Domain, ' ', Sender Domain, ' ', Stream ID })
    /// </code>
    ///
    /// Zwei Dinge daran sind leicht falsch zu machen, und beide hätte man
    /// ohne den veröffentlichten Vektor nicht bemerkt:
    ///
    /// <list type="number">
    ///   <item>
    ///     <b><c>SHA256(Secret)</c> geht als Hex-Zeichenkette in den HMAC, nicht
    ///     als Rohbytes.</b> Die naheliegende Lesart - der Digest als 32 Bytes -
    ///     liefert ein anderes Ergebnis. Beide Fassungen sind in sich stimmig;
    ///     zwei Server, die sich für verschiedene entscheiden, kämen nie
    ///     zusammen, ohne dass einer von beiden einen Fehler machte.
    ///   </item>
    ///   <item>
    ///     <b>Die Reihenfolge ist Ziel- vor Absenderdomain</b>, also die
    ///     empfangende zuerst. Vertauscht ergäbe sie ebenfalls einen gültig
    ///     aussehenden Schlüssel.
    ///   </item>
    /// </list>
    ///
    /// Die Begriffe sind die des XEP und werden hier bewusst beibehalten:
    /// <b>Sender Domain</b> ist die Domain, für die der aufbauende Server
    /// sprechen will, <b>Target Domain</b> die des annehmenden. Aus Sicht des
    /// annehmenden Servers ist Target also die eigene.
    ///
    /// Die Domains gehen <b>unverändert</b> ein, ohne Normalisierung der
    /// Gross-/Kleinschreibung. Das ist Absicht: der prüfende Server reicht die
    /// Werte weiter, die der aufbauende in seine Adressierung geschrieben hat,
    /// und der autoritative rechnet aus genau denselben nach. Würde hier
    /// normalisiert, hinge das Ergebnis davon ab, ob beide Seiten dieselbe
    /// Normalisierung anwenden - eine zusätzliche Möglichkeit, sich zu
    /// verfehlen, ohne dass jemand etwas gewönne.
    /// </remarks>
    public static class DialbackKey
    {

        #region Properties

        /// <summary>Der Namensraum von XEP-0220.</summary>
        public const String Namespace = "jabber:server:dialback";

        #endregion

        #region Generate(secret, targetDomain, senderDomain, streamId)

        /// <summary>
        /// Erzeugt den Dialback-Schlüssel.
        /// </summary>
        /// <param name="secret">
        /// Das Geheimnis des aufbauenden Servers. Nur er kennt es; genau
        /// deshalb kann nur er einen Schlüssel erzeugen, den er später als
        /// autoritativer Server wiedererkennt.
        /// </param>
        /// <param name="targetDomain">Die Domain des annehmenden Servers.</param>
        /// <param name="senderDomain">Die Domain, für die gesprochen werden soll.</param>
        /// <param name="streamId">
        /// Die Stream-ID, die der annehmende Server in seinem Stream-Kopf
        /// vergeben hat. Sie bindet den Schlüssel an diese eine Verbindung -
        /// ohne sie liesse sich ein einmal mitgeschnittener Schlüssel
        /// beliebig wiederverwenden.
        /// </param>
        public static String Generate(String  secret,
                                      String  targetDomain,
                                      String  senderDomain,
                                      String  streamId)
        {

            var hmacKey  = Encoding.UTF8.GetBytes(
                               Convert.ToHexStringLower(
                                   SHA256.HashData(Encoding.UTF8.GetBytes(secret))));

            var message  = Encoding.UTF8.GetBytes($"{targetDomain} {senderDomain} {streamId}");

            return Convert.ToHexStringLower(HMACSHA256.HashData(hmacKey, message));

        }

        #endregion

        #region Verify(secret, targetDomain, senderDomain, streamId, presentedKey)

        /// <summary>
        /// Prüft einen vorgelegten Dialback-Schlüssel.
        /// </summary>
        /// <remarks>
        /// Verglichen wird über <see cref="CryptographicOperations.FixedTimeEquals"/>
        /// auf den entschlüsselten Bytes. Der Umweg über
        /// <see cref="Convert.FromHexString(String)"/> nimmt dabei die
        /// Gross-/Kleinschreibung des Hex mit, ohne dass ein
        /// zeichenweiser - und damit zeitlich verräterischer - Vergleich
        /// nötig wäre.
        /// </remarks>
        /// <returns>false auch dann, wenn der Schlüssel gar kein gültiges Hex ist.</returns>
        public static Boolean Verify(String  secret,
                                     String  targetDomain,
                                     String  senderDomain,
                                     String  streamId,
                                     String  presentedKey)
        {

            Byte[] presented;

            try
            {
                presented = Convert.FromHexString(presentedKey.Trim());
            }
            catch (FormatException)
            {
                return false;
            }

            var expected = Convert.FromHexString(
                               Generate(secret, targetDomain, senderDomain, streamId));

            return CryptographicOperations.FixedTimeEquals(expected, presented);

        }

        #endregion

        #region NewSecret()

        /// <summary>
        /// Erzeugt ein Geheimnis für einen Server, dem keines vorgegeben
        /// wurde.
        /// </summary>
        /// <remarks>
        /// Ein zufälliges Geheimnis je Prozess reicht für Dialback aus: es
        /// muss nur so lange gleich bleiben, wie ein Stream lebt, und darf
        /// ausser diesem Server niemand kennen. Wer mehrere Instanzen
        /// derselben Domain betreibt, muss es allerdings teilen - sonst
        /// könnte die Instanz, die die Verifikation beantwortet, den
        /// Schlüssel der Instanz, die ihn ausgestellt hat, nicht nachrechnen.
        /// </remarks>
        public static String NewSecret()
            => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        #endregion

    }

}
