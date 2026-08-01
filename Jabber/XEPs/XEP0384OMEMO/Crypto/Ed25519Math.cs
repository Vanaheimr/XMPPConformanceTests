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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Punktarithmetik auf Ed25519 - genau so viel davon, wie XEdDSA braucht:
/// <c>kB</c> für einen frei gewählten Skalar.
/// </summary>
/// <remarks>
/// <b>Warum das hier steht, obwohl BouncyCastle Ed25519 kann.</b> BouncyCastle
/// gibt öffentlich nur <c>Sign</c> und <c>Verify</c> heraus; sein
/// <c>ScalarMultBase</c> ist intern. Beide öffentlichen Wege leiten den Skalar
/// aus einem Seed ab (SHA-512, geklammert) - XEdDSA muss aber mit einem
/// <i>gegebenen</i> Skalar rechnen: dem Identitätsschlüssel und dem Nonce.
///
/// <b>Zwei Auswege, die verworfen wurden</b>, und der Grund gehört
/// aufgeschrieben, weil beide zunächst verlockend aussehen:
///
/// <list type="number">
/// <item><b>Den Nonce aus einem Seed über BouncyCastles
/// <c>GeneratePublicKey</c> erzeugen.</b> Dann wäre <c>r</c> geklammert:
/// ein Vielfaches von 8 in einem festen Fenster, also rund vier Bit
/// vorhersagbar. Genau darauf zielt der Angriff auf verzerrte Nonces (Hidden
/// Number Problem) - wenige hundert Signaturen genügen, und der
/// Identitätsschlüssel fällt. <b>Ein verzerrter Nonce ist kein kleiner
/// Schönheitsfehler, sondern der übliche Weg, wie solche Schlüssel gestohlen
/// werden.</b></item>
/// <item><b><c>R</c> über <c>X25519.ScalarMultBase</c> rechnen und die
/// u-Koordinate umrechnen.</b> Scheitert an derselben Klammerung - und
/// zusätzlich daran, dass die u-Koordinate das Vorzeichen von x nicht kennt,
/// das die Signatur aber festlegt.</item>
/// </list>
///
/// Bleibt: selbst rechnen. Die Formeln stehen in RFC 8032, Abschnitt 5.1.4
/// und sind für diese Kurve <b>vollständig</b> - sie haben keine Sonderfälle,
/// über die man stolpern könnte. Geprüft wird gegen die veröffentlichten
/// Vektoren aus RFC 8032, Abschnitt 7.1: Aus dem Seed dieselbe Skalarbildung
/// wie Ed25519, und <c>sB</c> muss den dort abgedruckten öffentlichen
/// Schlüssel ergeben. Das ist eine Prüfung gegen fremde Zahlen und nicht gegen
/// die eigene Rechnung.
///
/// <b>Was diese Rechnung nicht ist: gehärtet gegen Zeitmessung.</b>
/// <see cref="BigInteger"/> rechnet in variabler Zeit, und die Schleife unten
/// verzweigt über die Bits des Skalars. Wer die Laufzeit dieses Prozesses fein
/// genug messen kann, erfährt etwas über den Schlüssel - das setzt Zugriff auf
/// denselben Rechner voraus. Für einen Client, der auf dem Gerät seines
/// Benutzers läuft, ist das die richtige Reihenfolge der Sorgen; für einen
/// Server, der fremde Anfragen beantwortet, wäre es die falsche. Es steht hier,
/// damit niemand es später für erledigt hält.
/// </remarks>
internal static class Ed25519Math
{

    #region Data

    /// <summary>Der Primkörper: 2^255 - 19.</summary>
    internal static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;

    /// <summary>Der Kurvenparameter d = -121665/121666 mod p.</summary>
    private static readonly BigInteger D =
        BigInteger.Parse("37095705934669439343138083508754565189542113879843219016388785533085940283555");

    /// <summary>Die x-Koordinate des Basispunkts.</summary>
    private static readonly BigInteger Bx =
        BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202");

    /// <summary>Die y-Koordinate des Basispunkts: 4/5 mod p.</summary>
    private static readonly BigInteger By =
        BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960");

    #endregion

    #region ScalarMultBaseEncoded(scalar)

    /// <summary>
    /// <c>kB</c>, kodiert wie in RFC 8032, Abschnitt 5.1.2: 32 Byte
    /// y-Koordinate, kleinstwertiges Byte zuerst, im obersten Bit das
    /// niedrigste Bit von x.
    /// </summary>
    internal static Byte[] ScalarMultBaseEncoded(BigInteger scalar)
        => Encode(ScalarMult(scalar));

    /// <summary>
    /// Doppeln-und-Addieren über die Bits des Skalars, von oben nach unten.
    /// </summary>
    private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) ScalarMult(BigInteger scalar)
    {

        // Der neutrale Punkt (0 : 1 : 1 : 0).
        var ergebnis  = (X: BigInteger.Zero, Y: BigInteger.One, Z: BigInteger.One, T: BigInteger.Zero);
        var basis     = (X: Bx, Y: By, Z: BigInteger.One, T: Bx * By % P);

        var k = ((scalar % Order) + Order) % Order;

        for (var bit = 254; bit >= 0; bit--)
        {

            ergebnis = Add(ergebnis, ergebnis);

            if (!((k >> bit) & BigInteger.One).IsZero)
                ergebnis = Add(ergebnis, basis);

        }

        return ergebnis;

    }

    /// <summary>Die Gruppenordnung.</summary>
    internal static readonly BigInteger Order =
        BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");

    #endregion

    #region Add / Encode

    /// <summary>
    /// Die vollständige Additionsformel für a = -1 in erweiterten Koordinaten
    /// (RFC 8032, Abschnitt 5.1.4).
    /// </summary>
    /// <remarks>
    /// Vollständig heisst: Sie gilt auch, wenn beide Summanden derselbe Punkt
    /// sind, und auch für den neutralen Punkt. Deshalb steht hier keine
    /// gesonderte Verdopplung - eine zweite Formel wäre ein zweiter Ort für
    /// denselben Fehler, und die Ersparnis fiele bei 255 Durchläufen nicht auf.
    /// </remarks>
    private static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Add(
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p1,
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p2)
    {

        var a = (p1.Y - p1.X) * (p2.Y - p2.X) % P;
        var b = (p1.Y + p1.X) * (p2.Y + p2.X) % P;
        var c = p1.T * 2 * D % P * p2.T % P;
        var d = p1.Z * 2 * p2.Z % P;

        var e = b - a;
        var f = d - c;
        var g = d + c;
        var h = b + a;

        return (Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));

    }

    /// <summary>
    /// Kodiert einen Punkt: y als 32 Byte, kleinstwertiges zuerst, mit dem
    /// niedrigsten Bit von x im obersten Bit.
    /// </summary>
    private static Byte[] Encode((BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) punkt)
    {

        var invZ  = BigInteger.ModPow(punkt.Z, P - 2, P);
        var x     = Mod(punkt.X * invZ);
        var y     = Mod(punkt.Y * invZ);

        var bytes = new Byte[32];
        var roh   = y.ToByteArray(isUnsigned: true, isBigEndian: false);

        Array.Copy(roh, bytes, Math.Min(roh.Length, 32));

        if (!x.IsEven)
            bytes[31] |= 0x80;

        return bytes;

    }

    /// <summary>Ein nicht-negativer Rest modulo p.</summary>
    private static BigInteger Mod(BigInteger value)
    {
        var rest = value % P;
        return rest.Sign < 0 ? rest + P : rest;
    }

    #endregion

}
