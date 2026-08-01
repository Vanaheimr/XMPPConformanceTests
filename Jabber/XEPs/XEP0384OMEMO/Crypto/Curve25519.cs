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

using System.Numerics;
using System.Security.Cryptography;

using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Math.EC.Rfc8032;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Ein Curve25519-Schlüsselpaar: 32 Byte geheim, 32 Byte öffentlich.
/// </summary>
/// <remarks>
/// Der öffentliche Teil ist die Montgomery-u-Koordinate, wie sie über die
/// Leitung geht (RFC 7748). Für die Signatur wird sie in die Edwards-Form
/// umgerechnet - siehe <see cref="Curve25519.Verify"/>.
/// </remarks>
public sealed class Curve25519KeyPair
{

    /// <summary>Der geheime Teil, bereits geklammert („clamped").</summary>
    public Byte[] PrivateKey { get; }

    /// <summary>Der öffentliche Teil, 32 Byte Montgomery-u.</summary>
    public Byte[] PublicKey { get; }

    internal Curve25519KeyPair(Byte[] privateKey, Byte[] publicKey)
    {
        PrivateKey  = privateKey;
        PublicKey   = publicKey;
    }

}

/// <summary>
/// Curve25519 für OMEMO: Schlüsselvereinbarung nach RFC 7748 und Signaturen
/// nach XEdDSA.
/// </summary>
/// <remarks>
/// <b>Warum XEdDSA und nicht einfach Ed25519?</b> OMEMO hat je Identität genau
/// <i>einen</i> Schlüssel, und der muss beides können: einen gemeinsamen
/// Geheimwert aushandeln (das kann nur die Montgomery-Form) und den Signed
/// PreKey unterschreiben (das kann nur die Edwards-Form). XEdDSA rechnet den
/// Schlüssel für die Signatur um, statt einen zweiten zu verlangen - denn ein
/// zweiter Schlüssel wäre ein zweiter Fingerabdruck, und der Mensch, der ihn
/// vergleichen soll, hat nur einen im Kopf.
///
/// Gerechnet wird mit BouncyCastle. Die eigentliche Kurvenarithmetik selbst zu
/// schreiben wäre der eine Ort, an dem ein Fehler nichts kostet, bis er alles
/// kostet: Eine falsche Multiplikation liefert kein falsches Ergebnis, sondern
/// ein plausibles.
/// </remarks>
public static class Curve25519
{

    #region Data

    /// <summary>Länge eines Schlüssels in Byte.</summary>
    public const Int32 KeyLength        = 32;

    /// <summary>Länge einer Signatur in Byte.</summary>
    public const Int32 SignatureLength  = 64;

    /// <summary>Der Primkörper: 2^255 - 19.</summary>
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;

    /// <summary>Die Gruppenordnung: 2^252 + 27742317777372353535851937790883648493.</summary>
    private static readonly BigInteger Q = BigInteger.Pow(2, 252) +
                                           BigInteger.Parse("27742317777372353535851937790883648493");

    #endregion

    #region Schlüssel

    /// <summary>
    /// Ein neues Schlüsselpaar aus dem Zufallsgenerator des Betriebssystems.
    /// </summary>
    public static Curve25519KeyPair GenerateKeyPair()
        => KeyPairFromPrivate(RandomNumberGenerator.GetBytes(KeyLength));

    /// <summary>
    /// Das Schlüsselpaar zu einem gegebenen geheimen Teil - für gespeicherte
    /// Schlüssel und für Prüfvektoren.
    /// </summary>
    /// <remarks>
    /// Der geheime Teil wird <b>geklammert</b> abgelegt (RFC 7748, Abschnitt
    /// 5): die untersten drei Bits gelöscht, das oberste gelöscht, das
    /// zweitoberste gesetzt. Das gehört hierhin und nicht erst in die
    /// Vereinbarung: XEdDSA rechnet mit demselben Skalar weiter, und ein
    /// ungeklammerter ergäbe eine Signatur, die zum eigenen öffentlichen
    /// Schlüssel nicht passt.
    /// </remarks>
    public static Curve25519KeyPair KeyPairFromPrivate(Byte[] privateKey)
    {

        if (privateKey.Length != KeyLength)
            throw new ArgumentException($"Ein Curve25519-Schlüssel hat {KeyLength} Byte, nicht {privateKey.Length}.",
                                        nameof(privateKey));

        var geklammert = (Byte[]) privateKey.Clone();

        geklammert[0]  &= 248;
        geklammert[31] &= 127;
        geklammert[31] |= 64;

        var oeffentlich = new Byte[KeyLength];
        X25519.ScalarMultBase(geklammert, 0, oeffentlich, 0);

        return new Curve25519KeyPair(geklammert, oeffentlich);

    }

    #endregion

    #region Agree(ownPrivateKey, otherPublicKey)

    /// <summary>
    /// Der gemeinsame Geheimwert nach RFC 7748 - 32 Byte.
    /// </summary>
    /// <remarks>
    /// Ein Ergebnis aus lauter Nullen wird abgewiesen. Es entsteht, wenn die
    /// Gegenseite einen Punkt kleiner Ordnung schickt, und ist dann kein
    /// Geheimnis, sondern eine Zahl, die der Angreifer vorher kennt. RFC 7748,
    /// Abschnitt 6.1 stellt die Prüfung frei; frei ist sie nur dort, wo der
    /// öffentliche Schlüssel aus einer vertrauenswürdigen Quelle stammt - ein
    /// OMEMO-Bundle kommt vom Server.
    /// </remarks>
    public static Byte[] Agree(Byte[] ownPrivateKey, Byte[] otherPublicKey)
    {

        if (ownPrivateKey.Length  != KeyLength ||
            otherPublicKey.Length != KeyLength)
            throw new ArgumentException($"Curve25519-Schlüssel haben {KeyLength} Byte.");

        var gemeinsam = new Byte[KeyLength];

        if (!X25519.CalculateAgreement(ownPrivateKey, 0, otherPublicKey, 0, gemeinsam, 0))
            throw new CryptographicException(
                      "Die Schlüsselvereinbarung ergab lauter Nullen - die Gegenstelle hat einen " +
                      "Punkt kleiner Ordnung geschickt.");

        return gemeinsam;

    }

    #endregion

    #region Sign(privateKey, message) / Verify(publicKey, message, signature)

    /// <summary>
    /// Unterschreibt eine Nachricht mit dem Montgomery-Schlüssel (XEdDSA).
    /// </summary>
    /// <remarks>
    /// Der Ablauf stammt aus Signals XEdDSA-Papier, Abschnitt 2.4:
    /// <list type="number">
    /// <item>Aus dem Skalar <c>k</c> das Edwards-Paar bestimmen und das
    ///       Vorzeichen so wählen, dass der öffentliche Punkt es nicht trägt.</item>
    /// <item><c>r</c> aus <c>hash₁(a ‖ M ‖ Z)</c>, mit 64 zufälligen Byte
    ///       <c>Z</c>.</item>
    /// <item><c>R = rB</c>, <c>h = SHA-512(R ‖ A ‖ M)</c>,
    ///       <c>s = r + h·a</c>.</item>
    /// </list>
    ///
    /// Das <c>Z</c> ist nicht Zierde: Ohne den Zufallsanteil wäre <c>r</c>
    /// allein vom Schlüssel und der Nachricht bestimmt, und zwei Signaturen
    /// über dieselbe Nachricht wären Byte für Byte gleich. Das ist bei Ed25519
    /// Absicht und hier eine Preisgabe - der Signed PreKey wird über seine
    /// Lebenszeit mehrfach unterschrieben.
    ///
    /// Das <c>hash₁</c> ist SHA-512 mit dem Präfix <c>0xFE</c> gefolgt von 31
    /// Byte <c>0xFF</c>. Der Präfix trennt diese Hash-Verwendung von der in
    /// Ed25519 selbst - ohne ihn liessen sich die beiden Verfahren gegenseitig
    /// als Orakel benutzen.
    /// </remarks>
    public static Byte[] Sign(Byte[] privateKey, Byte[] message)
    {

        var (a, aPunkt) = EdwardsKeyPair(privateKey);

        var z = RandomNumberGenerator.GetBytes(64);

        // hash₁(a ‖ M ‖ Z)
        var praefix = new Byte[32];
        praefix[0] = 0xFE;
        for (var i = 1; i < 32; i++)
            praefix[i] = 0xFF;

        var r = ReduceMod(SHA512.HashData([.. praefix, .. ToLittleEndian(a), .. message, .. z]), Q);

        var gross_r = Ed25519Math.ScalarMultBaseEncoded(r);

        var h = ReduceMod(SHA512.HashData([.. gross_r, .. aPunkt, .. message]), Q);
        var s = (r + h * a) % Q;

        Byte[] signatur = [.. gross_r, .. ToLittleEndian(s)];

        // Die eigene Signatur prüfen, bevor sie das Haus verlässt - mit dem
        // fremden Prüfer aus BouncyCastle und nicht mit der Rechnung von
        // oben. Das kostet eine Verifikation und macht aus jedem Rechenfehler
        // hier eine Ausnahme statt einer Unterschrift, die niemand
        // nachvollziehen kann. Ein Signed PreKey mit falscher Signatur fällt
        // sonst erst bei der Gegenstelle auf, und dort sieht er aus wie ein
        // Angriff.
        if (!Verify(KeyPairFromPrivate(privateKey).PublicKey, message, signatur))
            throw new CryptographicException(
                      "Die erzeugte XEdDSA-Signatur prüft sich selbst nicht - " +
                      "hier stimmt die Rechnung nicht.");

        return signatur;

    }

    /// <summary>
    /// Prüft eine XEdDSA-Signatur gegen den Montgomery-Schlüssel.
    /// </summary>
    /// <remarks>
    /// Geprüft wird mit dem gewöhnlichen Ed25519-Verfahren aus BouncyCastle,
    /// nachdem der öffentliche Schlüssel umgerechnet wurde. Das ist keine
    /// Bequemlichkeit, sondern die Aussage von XEdDSA: Eine XEdDSA-Signatur
    /// <b>ist</b> eine Ed25519-Signatur zum umgerechneten Schlüssel.
    /// </remarks>
    public static Boolean Verify(Byte[] publicKey, Byte[] message, Byte[] signature)
    {

        if (publicKey.Length != KeyLength || signature.Length != SignatureLength)
            return false;

        try
        {
            return Ed25519.Verify(signature, 0,
                                  MontgomeryToEdwards(publicKey), 0,
                                  message, 0, message.Length);
        }
        catch (Exception)
        {
            // Ein unbrauchbarer Schlüssel ist keine gültige Signatur, und
            // keine Ausnahme des Aufrufers: Beides heisst „nicht von diesem
            // Absender", und der Unterschied ginge nur an den Angreifer.
            return false;
        }

    }

    #endregion

    #region MontgomeryToEdwards(publicKey)

    /// <summary>
    /// Rechnet die Montgomery-u-Koordinate in die Edwards-y-Koordinate um:
    /// <c>y = (u - 1) / (u + 1) mod p</c>.
    /// </summary>
    /// <remarks>
    /// Das Vorzeichenbit bleibt gelöscht - genau das stellt
    /// <see cref="EdwardsKeyPair"/> beim Unterschreiben sicher. Die beiden
    /// Kurven sind dieselbe Kurve in anderer Schreibweise, und diese Formel
    /// ist der Übersetzer; sie steht in RFC 7748, Abschnitt 4.1.
    ///
    /// Geprüft wird sie an einem Punkt, den beide Seiten kennen: Der
    /// X25519-Basispunkt <c>u = 9</c> muss den Ed25519-Basispunkt ergeben.
    /// </remarks>
    internal static Byte[] MontgomeryToEdwards(Byte[] publicKey)
    {

        var roh = (Byte[]) publicKey.Clone();

        // RFC 7748, Abschnitt 5: Das oberste Bit der u-Koordinate wird beim
        // Lesen verworfen. Wer es stehen liesse, rechnete mit einer Zahl, die
        // die Gegenstelle gar nicht gemeint hat.
        roh[31] &= 127;

        var u = new BigInteger(roh, isUnsigned: true, isBigEndian: false);

        var zaehler = ((u - 1)                                  % P + P) % P;
        var nenner  = BigInteger.ModPow((u + 1) % P, P - 2, P);   // Inverses über den kleinen Satz von Fermat

        if (nenner.IsZero)
            throw new CryptographicException("Der öffentliche Schlüssel lässt sich nicht umrechnen (u = -1).");

        return ToLittleEndian(zaehler * nenner % P);

    }

    #endregion

    #region Hilfsfunktionen

    /// <summary>
    /// Das Edwards-Paar zum Montgomery-Skalar: der Skalar mit passendem
    /// Vorzeichen und der zugehörige öffentliche Punkt ohne Vorzeichenbit.
    /// </summary>
    /// <remarks>
    /// XEdDSA, Abschnitt 2.4: <c>E = kB</c>; trägt <c>E</c> das Vorzeichenbit,
    /// wird mit <c>-k</c> weitergerechnet. Danach passt der Skalar zu einem
    /// öffentlichen Punkt ohne Vorzeichen - und genau den bekommt der Prüfer
    /// aus der u-Koordinate, die kein Vorzeichen kennt.
    /// </remarks>
    private static (BigInteger Scalar, Byte[] PublicPoint) EdwardsKeyPair(Byte[] privateKey)
    {

        var k = KeyPairFromPrivate(privateKey).PrivateKey;

        var e = Ed25519Math.ScalarMultBaseEncoded(
                    new BigInteger(k, isUnsigned: true, isBigEndian: false));

        var vorzeichen  = (e[31] & 0x80) != 0;

        var punkt       = (Byte[]) e.Clone();
        punkt[31] &= 0x7F;

        // Erst reduzieren, dann negieren. Ein geklammerter Skalar ist knapp
        // 2^255 gross und damit weit über der Gruppenordnung von etwa 2^252 -
        // ein blosses (Q - k) % Q wäre negativ, und C# behält bei % das
        // Vorzeichen. Die Rechnung ging dann nicht falsch aus, sondern gar
        // nicht: Die Kodierung wirft.
        //
        // Getroffen hat es genau die Hälfte aller Schlüssel, nämlich die mit
        // gesetztem Vorzeichenbit. Ein Test mit einem einzigen erzeugten
        // Schlüssel wäre in jedem zweiten Lauf grün gewesen.
        var skalar      = new BigInteger(k, isUnsigned: true, isBigEndian: false) % Q;

        return (vorzeichen ? (Q - skalar) % Q : skalar, punkt);

    }

    /// <summary>Ein Hash-Ergebnis als Zahl modulo <paramref name="modulus"/>.</summary>
    private static BigInteger ReduceMod(Byte[] hash, BigInteger modulus)
        => new BigInteger(hash, isUnsigned: true, isBigEndian: false) % modulus;

    /// <summary>Eine Zahl als 32 Byte, kleinstwertiges zuerst.</summary>
    private static Byte[] ToLittleEndian(BigInteger value)
    {

        var bytes  = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var feld   = new Byte[32];

        Array.Copy(bytes, feld, Math.Min(bytes.Length, 32));

        return feld;

    }

    #endregion

}
