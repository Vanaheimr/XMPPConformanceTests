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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Eine verschlüsselte Nutzlast samt dem, was jeder Empfänger dafür braucht.
/// </summary>
/// <param name="Ciphertext">Der Geheimtext, wie er ins <c>&lt;payload/&gt;</c> geht.</param>
/// <param name="KeyAndHmac">
/// Schlüssel und gekürzter HMAC hintereinander - 48 Byte. Genau das geht je
/// Empfänger durch den Double Ratchet (XEP-0384, Abschnitt 4.4, Schritt 6).
/// </param>
public sealed record OmemoPayload(Byte[] Ciphertext, Byte[] KeyAndHmac);

/// <summary>
/// XEP-0384, Abschnitt 4.4: die Nutzlast selbst - AES-256-CBC mit
/// HMAC-SHA-256.
/// </summary>
/// <remarks>
/// <b>Ein Schlüssel je Nachricht, und er geht nicht an die Empfänger.</b>
/// Verschlüsselt wird der Text genau einmal; durch den Ratchet geht je
/// Empfänger nur der 48-Byte-Wert. Bei zehn Geräten spart das nicht Rechenzeit,
/// sondern verhindert etwas: dass derselbe Klartext zehnmal in verschiedenen
/// Schlüsseln dasteht.
///
/// <b>Der HMAC steht nicht bei der Nachricht.</b> Er reist im verschlüsselten
/// Teil mit, und das ist der eigentliche Kniff des Verfahrens: Wer die Nutzlast
/// verändert, kann den HMAC nicht mitverändern, weil er ihn nicht kennt - und
/// wer ihn kennt, hat den Ratchet gebrochen. Ein danebenstehender HMAC wäre
/// dagegen von jedem neu zu rechnen, der den Schlüssel hat, und der Schlüssel
/// liegt bei jedem Empfänger.
///
/// Aus dem 32-Byte-Schlüssel werden mit HKDF 80 Byte: Chiffrierschlüssel,
/// Authentisierungsschlüssel und IV. Deshalb reist kein IV mit der Nachricht -
/// er ist aus dem Schlüssel abgeleitet und für jede Nachricht ein anderer,
/// weil es der Schlüssel auch ist.
/// </remarks>
public static class OmemoPayloadCipher
{

    #region Data

    /// <summary>Der Info-String der Ableitung (Abschnitt 4.4).</summary>
    public const String Info = "OMEMO Payload";

    /// <summary>Länge des Nachrichtenschlüssels in Byte.</summary>
    public const Int32 KeyLength = 32;

    /// <summary>
    /// Länge des gekürzten HMAC in Byte (Abschnitt 4.4: „Truncate the output
    /// of the HMAC to 16 bytes/128 bits by cutting off excess bytes from the
    /// end").
    /// </summary>
    public const Int32 HmacLength = 16;

    #endregion

    #region Material(key)

    /// <summary>
    /// Die 80 Byte Schlüsselmaterial aus dem Nachrichtenschlüssel:
    /// 32 Byte Chiffrierschlüssel, 32 Byte Authentisierungsschlüssel, 16 Byte IV.
    /// </summary>
    /// <remarks>
    /// Das Salz sind 32 Nullbytes und nicht etwa nichts. HKDF behandelt beides
    /// gleich (RFC 5869, Abschnitt 2.2 setzt ein fehlendes Salz auf genau
    /// diese Nullen) - aber die Spezifikation schreibt „256 zero-bits as HKDF
    /// salt", und wer hier abkürzt, muss beim nächsten Leser erklären, warum
    /// er etwas anderes tut als der Text.
    /// </remarks>
    public static (Byte[] Key, Byte[] AuthKey, Byte[] Iv) Material(Byte[] messageKey)
    {

        if (messageKey.Length != KeyLength)
            throw new ArgumentException($"Der Nachrichtenschlüssel hat {KeyLength} Byte, nicht {messageKey.Length}.",
                                        nameof(messageKey));

        var material = HKDF.DeriveKey(HashAlgorithmName.SHA256,
                                      ikm:   messageKey,
                                      salt:  new Byte[32],
                                      info:  Encoding.UTF8.GetBytes(Info),
                                      outputLength: 80);

        return (material[..32], material[32..64], material[64..]);

    }

    #endregion

    #region Encrypt(plaintext) / Decrypt(ciphertext, keyAndHmac)

    /// <summary>
    /// Verschlüsselt den Klartext mit einem frisch gezogenen Schlüssel.
    /// </summary>
    public static OmemoPayload Encrypt(Byte[] plaintext)
        => Encrypt(plaintext, RandomNumberGenerator.GetBytes(KeyLength));

    /// <summary>
    /// Verschlüsselt mit einem gegebenen Schlüssel - für Prüfvektoren und für
    /// Aufrufer, die den Schlüssel selbst verwalten.
    /// </summary>
    public static OmemoPayload Encrypt(Byte[] plaintext, Byte[] messageKey)
    {

        var (key, authKey, iv) = Material(messageKey);

        using var aes = Aes.Create();
        aes.Key = key;

        var ciphertext = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);

        // Encrypt-then-MAC: gerechnet wird über den Geheimtext, nicht über den
        // Klartext. Andersherum müsste der Empfänger entschlüsseln, bevor er
        // weiss, ob er darf.
        var hmac = HMACSHA256.HashData(authKey, ciphertext)[..HmacLength];

        return new OmemoPayload(ciphertext, [.. messageKey, .. hmac]);

    }

    /// <summary>
    /// Entschlüsselt die Nutzlast - oder wirft, wenn der HMAC nicht stimmt.
    /// </summary>
    /// <remarks>
    /// Verglichen wird in fester Zeit. Ein Vergleich, der beim ersten
    /// abweichenden Byte aufhört, verrät über seine Dauer, wie weit der
    /// Angreifer schon gekommen ist - und mit 16 Byte à 256 Möglichkeiten
    /// wären das 4096 Versuche statt 2¹²⁸.
    /// </remarks>
    public static Byte[] Decrypt(Byte[] ciphertext, Byte[] keyAndHmac)
    {

        if (keyAndHmac.Length != KeyLength + HmacLength)
            throw new ArgumentException(
                      $"Schlüssel und HMAC haben zusammen {KeyLength + HmacLength} Byte, " +
                      $"nicht {keyAndHmac.Length}.",
                      nameof(keyAndHmac));

        var (key, authKey, iv) = Material(keyAndHmac[..KeyLength]);

        var erwartet = HMACSHA256.HashData(authKey, ciphertext)[..HmacLength];

        if (!CryptographicOperations.FixedTimeEquals(erwartet, keyAndHmac[KeyLength..]))
            throw new CryptographicException(
                      "Der HMAC der Nutzlast stimmt nicht - sie wurde unterwegs verändert.");

        // Der Schlüssel muss an das Objekt, nicht bloss der IV an den Aufruf:
        // Ein frisch erzeugtes Aes hat einen zufälligen Schlüssel, und
        // DecryptCbc nimmt ihn stillschweigend. Das entschlüsselte dann mit
        // einem Schlüssel, den niemand kennt - und weil der HMAC vorher
        // stimmte, sah alles richtig aus, bis das Padding scheiterte.
        using var aes = Aes.Create();
        aes.Key = key;

        return aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);

    }

    #endregion

}
