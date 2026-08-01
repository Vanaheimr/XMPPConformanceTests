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
/// Der Kopf einer Ratchet-Nachricht (XEP-0384, <c>OMEMOMessage.proto</c>).
/// </summary>
/// <param name="DhPublicKey">Der aktuelle öffentliche Ratchet-Schlüssel des Absenders.</param>
/// <param name="PreviousChainLength">Wie lang die vorige Sendekette war (<c>pn</c>).</param>
/// <param name="MessageNumber">Die laufende Nummer in der aktuellen Kette (<c>n</c>).</param>
public sealed record RatchetHeader(Byte[] DhPublicKey,
                                   UInt32 PreviousChainLength,
                                   UInt32 MessageNumber)
{

    /// <summary>
    /// Der Kopf als <c>OMEMOMessage.proto</c> ohne Geheimtext - genau so geht
    /// er in die Beigabe der Verschlüsselung ein (Abschnitt 4.3).
    /// </summary>
    /// <remarks>
    /// Ohne den Geheimtext, und das steht ausdrücklich so da: „the
    /// OMEMOMessage.proto initializes without the ciphertext, which is
    /// optional". Anders ginge es auch nicht - die Beigabe wird gebraucht, um
    /// den Geheimtext überhaupt erst zu erzeugen.
    ///
    /// Die Feldnummern stammen aus dem Schema der Spezifikation:
    /// <c>n = 1</c>, <c>pn = 2</c>, <c>dh_pub = 3</c>, <c>ciphertext = 4</c>.
    /// Immer alle drei, immer in dieser Reihenfolge: Beide Seiten müssen aus
    /// demselben Kopf dieselben Bytes bilden, sonst scheitert die Prüfung an
    /// der Kodierung statt am Inhalt.
    /// </remarks>
    public Byte[] Encode()
    {

        var bytes = new List<Byte>();

        Protobuf.WriteUInt32 (bytes, 1, MessageNumber);
        Protobuf.WriteUInt32 (bytes, 2, PreviousChainLength);
        Protobuf.WriteBytes  (bytes, 3, DhPublicKey);

        return [.. bytes];

    }

}

/// <summary>Eine verschlüsselte Ratchet-Nachricht: Kopf und Geheimtext.</summary>
public sealed record RatchetMessage(RatchetHeader Header, Byte[] Ciphertext);

/// <summary>
/// Der Double Ratchet nach XEP-0384, Abschnitt 4.3.
/// </summary>
/// <remarks>
/// <b>Zwei Ratschen, und sie tun Verschiedenes.</b>
///
/// Die <i>symmetrische</i> Ratsche läuft mit jeder Nachricht: Aus dem
/// Kettenschlüssel entsteht ein Nachrichtenschlüssel und ein neuer
/// Kettenschlüssel, und der alte wird vergessen. Das gibt <b>Forward
/// Secrecy</b> - wer den heutigen Zustand stiehlt, kann die Nachrichten von
/// gestern nicht mehr lesen, weil es deren Schlüssel nicht mehr gibt.
///
/// Die <i>Diffie-Hellman</i>-Ratsche läuft, sobald die Gegenstelle einen neuen
/// öffentlichen Schlüssel mitschickt: Beide Seiten rechnen einen frischen
/// gemeinsamen Wert und beginnen neue Ketten. Das gibt <b>Break-in
/// Recovery</b> - wer den Zustand gestohlen hat, verliert ihn wieder, sobald
/// die beiden einmal in beide Richtungen geschrieben haben.
///
/// <b>Zusammen ergeben sie die Eigenschaft, um die es geht:</b> Ein
/// mitgelesener Zustand ist weder rückwärts noch dauerhaft vorwärts nützlich.
/// Genau deshalb sind Fehler hier still - eine Ratsche, die nicht weiterläuft,
/// verschlüsselt weiterhin einwandfrei. Sie tut es nur immer wieder mit
/// demselben Schlüssel.
/// </remarks>
public sealed class DoubleRatchet
{

    #region Data

    /// <summary>Der Info-String der Wurzelkette (Abschnitt 4.3).</summary>
    public const String RootChainInfo    = "OMEMO Root Chain";

    /// <summary>Der Info-String für das Material eines Nachrichtenschlüssels.</summary>
    public const String MessageKeyInfo   = "OMEMO Message Key Material";

    /// <summary>
    /// Wie viele übersprungene Nachrichtenschlüssel eine Sitzung aufhebt.
    /// </summary>
    /// <remarks>
    /// Die Spezifikation empfiehlt tausend. Die Zahl ist ein Ausgleich
    /// zwischen zwei Übeln: Zu wenige, und eine Nachricht, die einen Tag lang
    /// unterwegs war, lässt sich nicht mehr lesen. Zu viele - oder gar keine
    /// Grenze -, und ein Angreifer schickt eine einzige Nachricht mit
    /// <c>n = 4000000000</c> und der Empfänger rechnet vier Milliarden
    /// Schlüssel aus, bevor er merkt, dass sie nicht stimmt.
    /// </remarks>
    public const Int32 MaxSkip = 1000;

    private readonly Dictionary<(String Dh, UInt32 N), Byte[]> _uebersprungen = [];
    private readonly Lock _lock = new();

    private Curve25519KeyPair?  _eigenerRatchet;
    private Byte[]?             _fremderRatchet;
    private Byte[]              _wurzel;
    private Byte[]?             _sendekette;
    private Byte[]?             _empfangskette;

    #endregion

    #region Properties

    /// <summary>Die laufende Nummer der nächsten gesendeten Nachricht.</summary>
    public UInt32 SendCount { get; private set; }

    /// <summary>Wie viele Nachrichten in der aktuellen Kette empfangen wurden.</summary>
    public UInt32 ReceiveCount { get; private set; }

    /// <summary>Die Länge der vorigen Sendekette.</summary>
    public UInt32 PreviousSendCount { get; private set; }

    /// <summary>Wie viele übersprungene Schlüssel gerade aufgehoben sind.</summary>
    public Int32 SkippedKeys
    {
        get { lock (_lock) return _uebersprungen.Count; }
    }

    /// <summary>Kann diese Sitzung schon senden?</summary>
    /// <remarks>
    /// Der Angerufene kann es erst, nachdem er die erste Nachricht bekommen
    /// hat: Vorher kennt er den Ratchet-Schlüssel der Gegenstelle nicht und
    /// hat nichts, woraus er eine Sendekette bilden könnte.
    /// </remarks>
    public Boolean CanSend => _sendekette is not null;

    #endregion

    #region Constructor(s)

    private DoubleRatchet(Byte[] wurzel)
    {
        _wurzel = wurzel;
    }

    #endregion

    #region InitiateAsSender / InitiateAsReceiver

    /// <summary>
    /// Die anrufende Seite: Sie kennt den Ratchet-Schlüssel der Gegenstelle
    /// aus deren Bundle und kann sofort senden.
    /// </summary>
    /// <param name="sharedSecret">Das Ergebnis von X3DH.</param>
    /// <param name="theirRatchetKey">
    /// Der Signed PreKey der Gegenstelle - er ist zugleich ihr erster
    /// Ratchet-Schlüssel.
    /// </param>
    public static DoubleRatchet InitiateAsSender(Byte[] sharedSecret, Byte[] theirRatchetKey)
    {

        var ratchet = new DoubleRatchet(sharedSecret)
        {
            _eigenerRatchet  = Curve25519.GenerateKeyPair(),
            _fremderRatchet  = theirRatchetKey
        };

        (ratchet._wurzel, ratchet._sendekette) =
            ratchet.AdvanceRootChain(Curve25519.Agree(ratchet._eigenerRatchet.PrivateKey, theirRatchetKey));

        return ratchet;

    }

    /// <summary>
    /// Die angerufene Seite: Sie hat nur das gemeinsame Geheimnis und ihren
    /// eigenen Signed PreKey.
    /// </summary>
    /// <remarks>
    /// Hier wird noch nichts abgeleitet. Die Wurzel <b>ist</b> das gemeinsame
    /// Geheimnis, und die Ketten entstehen erst, wenn die erste Nachricht
    /// eintrifft - sie bringt den Ratchet-Schlüssel der Gegenstelle mit.
    /// Wer hier schon eine Sendekette bildete, hätte eine, die die
    /// Gegenstelle nicht kennt.
    /// </remarks>
    public static DoubleRatchet InitiateAsReceiver(Byte[] sharedSecret, Curve25519KeyPair ownRatchetKey)
        => new(sharedSecret)
           {
               _eigenerRatchet = ownRatchetKey
           };

    #endregion

    #region Encrypt(plaintext, associatedData)

    /// <summary>
    /// Verschlüsselt eine Nachricht und schiebt die symmetrische Ratsche
    /// einen Schritt weiter.
    /// </summary>
    /// <param name="plaintext">
    /// Bei OMEMO die 48 Byte aus Schlüssel und HMAC der Nutzlast.
    /// </param>
    /// <param name="associatedData">
    /// Die Beigabe aus X3DH - beide IdentityKeys. Der Kopf dieser Nachricht
    /// wird hier angehängt, nicht vom Aufrufer.
    /// </param>
    public RatchetMessage Encrypt(Byte[] plaintext, Byte[] associatedData)
    {

        lock (_lock)
        {

            if (_sendekette is null)
                throw new InvalidOperationException(
                          "Diese Sitzung kann noch nicht senden - der Ratchet-Schlüssel der " +
                          "Gegenstelle ist unbekannt, solange von ihr nichts kam.");

            var (nachrichtenschluessel, naechste) = AdvanceChain(_sendekette);
            _sendekette = naechste;

            var kopf = new RatchetHeader(_eigenerRatchet!.PublicKey, PreviousSendCount, SendCount);

            SendCount++;

            return new RatchetMessage(kopf,
                                      Seal(nachrichtenschluessel, plaintext,
                                           [.. associatedData, .. kopf.Encode()]));

        }

    }

    #endregion

    #region Decrypt(message, associatedData)

    /// <summary>
    /// Entschlüsselt eine Nachricht - auch wenn sie zu früh, zu spät oder gar
    /// nicht mehr in der aktuellen Kette ankommt.
    /// </summary>
    /// <remarks>
    /// <b>Die Reihenfolge der drei Fälle ist die ganze Schwierigkeit.</b>
    /// Zuerst wird nachgesehen, ob es sich um eine Nachricht handelt, deren
    /// Schlüssel schon beiseitegelegt wurde - sie kam verspätet, und die
    /// Ketten sind längst weiter. Dann, ob die Gegenstelle einen neuen
    /// Ratchet-Schlüssel mitbringt; in dem Fall wird die alte Empfangskette
    /// bis zu ihrem Ende ausgerechnet und beiseitegelegt, bevor die neue
    /// beginnt. Erst dann wird in der aktuellen Kette vorgespult.
    ///
    /// Wer die Reihenfolge vertauscht, verliert Nachrichten, die noch
    /// unterwegs sind - und zwar unwiederbringlich, denn ihre Schlüssel sind
    /// dann bereits vergessen.
    /// </remarks>
    public Byte[] Decrypt(RatchetMessage message, Byte[] associatedData)
    {

        lock (_lock)
        {

            var beigabe = (Byte[]) [.. associatedData, .. message.Header.Encode()];

            // 1. Ein beiseitegelegter Schlüssel?
            var schluessel = (message.Header.DhPublicKey, message.Header.MessageNumber);
            var kennung    = (Convert.ToHexString(schluessel.DhPublicKey), schluessel.MessageNumber);

            if (_uebersprungen.TryGetValue(kennung, out var beiseite))
            {

                var klartext = Open(beiseite, message.Ciphertext, beigabe);

                // Erst nach der erfolgreichen Prüfung entfernen. Wirft das
                // Entschlüsseln, war es nicht die erwartete Nachricht - und
                // ein Angreifer hätte sonst mit einer gefälschten den
                // Schlüssel der echten gelöscht.
                _uebersprungen.Remove(kennung);

                return klartext;

            }

            // 2. Ein neuer Ratchet-Schlüssel der Gegenstelle?
            if (_fremderRatchet is null ||
                !message.Header.DhPublicKey.SequenceEqual(_fremderRatchet))
            {
                SkipTo(message.Header.PreviousChainLength);
                TurnDhRatchet(message.Header.DhPublicKey);
            }

            // 3. In der aktuellen Kette vorspulen.
            SkipTo(message.Header.MessageNumber);

            var (mk, naechste) = AdvanceChain(_empfangskette!);
            _empfangskette = naechste;

            ReceiveCount++;

            return Open(mk, message.Ciphertext, beigabe);

        }

    }

    #endregion

    #region Die beiden Ratschen

    /// <summary>
    /// Die Wurzelkette: aus altem Wurzelschlüssel und einem
    /// Diffie-Hellman-Ergebnis werden neuer Wurzelschlüssel und
    /// Kettenschlüssel.
    /// </summary>
    /// <remarks>
    /// Der Wurzelschlüssel ist das <b>Salz</b> und der Diffie-Hellman-Wert das
    /// Eingabematerial - so herum steht es in Abschnitt 4.3, und die
    /// Vertauschung wäre nicht zu bemerken: Beide Seiten kämen weiterhin
    /// überein, nur eine fremde Gegenstelle nicht.
    /// </remarks>
    private (Byte[] Root, Byte[] Chain) AdvanceRootChain(Byte[] dhOutput)
        => DeriveRootChain(_wurzel, dhOutput);

    /// <summary>
    /// Dieselbe Ableitung ohne Zustand - damit ein Test sie gegen die
    /// Vorschrift halten kann.
    /// </summary>
    /// <remarks>
    /// <b>Nicht der Bequemlichkeit halber herausgezogen.</b> Als das hier noch
    /// eine private Methode war, überlebten vier Mutationen: Salz und
    /// Eingabematerial vertauscht, Info-String weg, beide Hälften aus
    /// derselben. Keine davon fiel auf, <b>weil beide Seiten dieselbe Funktion
    /// benutzen und weiterhin übereinkamen</b> - und zwei davon sind nicht
    /// bloss Interop-Fragen, sondern Aufhebungen der Sicherheit: Wären
    /// Wurzel- und Kettenschlüssel dieselben Bytes, liesse sich aus einer
    /// mitgelesenen Nachricht die Wurzel und damit die ganze Sitzung
    /// aufrollen.
    ///
    /// Prüfbar wird das erst, wenn die Rechnung einzeln zu greifen ist.
    /// </remarks>
    internal static (Byte[] Root, Byte[] Chain) DeriveRootChain(Byte[] rootKey, Byte[] dhOutput)
    {

        var material = HKDF.DeriveKey(HashAlgorithmName.SHA256,
                                      ikm:           dhOutput,
                                      salt:          rootKey,
                                      info:          Encoding.UTF8.GetBytes(RootChainInfo),
                                      outputLength:  64);

        return (material[..32], material[32..]);

    }

    /// <summary>
    /// Die symmetrische Kette: <c>HMAC(ck, 0x01)</c> ist der
    /// Nachrichtenschlüssel, <c>HMAC(ck, 0x02)</c> der nächste
    /// Kettenschlüssel.
    /// </summary>
    /// <remarks>
    /// Zwei verschiedene Konstanten, und das ist der Kern: Wären sie gleich,
    /// wäre der Nachrichtenschlüssel zugleich der nächste Kettenschlüssel -
    /// und wer eine einzige Nachricht mitliest, könnte die ganze weitere
    /// Kette ausrechnen. Aus Forward Secrecy würde damit ihr Gegenteil.
    /// </remarks>
    internal static (Byte[] MessageKey, Byte[] NextChain) AdvanceChain(Byte[] chainKey)
        => (HMACSHA256.HashData(chainKey, new Byte[] { 0x01 }),
            HMACSHA256.HashData(chainKey, new Byte[] { 0x02 }));

    /// <summary>
    /// Die Diffie-Hellman-Ratsche: neuer fremder Schlüssel, zwei Schritte der
    /// Wurzelkette, ein frisches eigenes Schlüsselpaar.
    /// </summary>
    /// <remarks>
    /// Zwei Schritte, weil zwei Ketten entstehen: erst die Empfangskette aus
    /// dem alten eigenen Schlüssel gegen den neuen fremden, dann die
    /// Sendekette aus dem neuen eigenen gegen denselben fremden. Der
    /// Zwischenstand der Wurzel geht dabei in beide ein - deshalb kommen
    /// beide Seiten überein, obwohl jede ihre Ketten in umgekehrter
    /// Reihenfolge bildet.
    /// </remarks>
    private void TurnDhRatchet(Byte[] theirRatchetKey)
    {

        PreviousSendCount  = SendCount;
        SendCount          = 0;
        ReceiveCount       = 0;
        _fremderRatchet    = theirRatchetKey;

        (_wurzel, _empfangskette) = AdvanceRootChain(
                                        Curve25519.Agree(_eigenerRatchet!.PrivateKey, theirRatchetKey));

        _eigenerRatchet = Curve25519.GenerateKeyPair();

        (_wurzel, _sendekette) = AdvanceRootChain(
                                     Curve25519.Agree(_eigenerRatchet.PrivateKey, theirRatchetKey));

    }

    /// <summary>
    /// Spult die Empfangskette bis zur genannten Nummer vor und legt jeden
    /// dabei entstehenden Schlüssel beiseite.
    /// </summary>
    /// <remarks>
    /// Die Obergrenze ist keine Bequemlichkeit, sondern eine Abwehr: Ohne sie
    /// genügt <b>eine einzige</b> Nachricht mit einer sehr grossen Nummer, und
    /// der Empfänger rechnet Milliarden Schlüssel aus, bevor er merkt, dass
    /// sie nicht stimmt. Die Prüfung steht deshalb <b>vor</b> der Schleife und
    /// nicht darin.
    /// </remarks>
    private void SkipTo(UInt32 until)
    {

        if (_empfangskette is null)
            return;

        if (until < ReceiveCount)
            return;

        if (until - ReceiveCount > MaxSkip)
            throw new CryptographicException(
                      $"Die Nachricht überspringt {until - ReceiveCount} Schlüssel; erlaubt sind " +
                      $"{MaxSkip}. Eine einzelne Nachricht darf keine unbegrenzte Rechnung auslösen.");

        while (ReceiveCount < until)
        {

            var (mk, naechste) = AdvanceChain(_empfangskette);

            _uebersprungen[(Convert.ToHexString(_fremderRatchet!), ReceiveCount)] = mk;

            _empfangskette = naechste;
            ReceiveCount++;

        }

    }

    #endregion

    #region Die Verschlüsselung einer einzelnen Nachricht

    /// <summary>
    /// AES-256-CBC mit HMAC-SHA-256, aus dem Nachrichtenschlüssel abgeleitet.
    /// </summary>
    internal static (Byte[] Key, Byte[] AuthKey, Byte[] Iv) Material(Byte[] messageKey)
    {

        var material = HKDF.DeriveKey(HashAlgorithmName.SHA256,
                                      ikm:           messageKey,
                                      salt:          new Byte[32],
                                      info:          Encoding.UTF8.GetBytes(MessageKeyInfo),
                                      outputLength:  80);

        return (material[..32], material[32..64], material[64..]);

    }

    /// <summary>
    /// Verschlüsselt und hängt den auf 16 Byte gekürzten HMAC an.
    /// </summary>
    /// <remarks>
    /// Der HMAC läuft über <b>Beigabe und Geheimtext</b>. Die Beigabe enthält
    /// die beiden IdentityKeys und den Kopf dieser Nachricht - damit ist
    /// mitgeprüft, wer mit wem spricht und an welcher Stelle der Kette diese
    /// Nachricht steht. Ohne sie liesse sich eine gültige Nachricht in eine
    /// andere Sitzung oder an eine andere Stelle der Kette verschieben.
    /// </remarks>
    private static Byte[] Seal(Byte[] messageKey, Byte[] plaintext, Byte[] associatedData)
    {

        var (key, authKey, iv) = Material(messageKey);

        using var aes = Aes.Create();
        aes.Key = key;

        var geheim = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);

        Byte[] gepruefet = [.. associatedData, .. geheim];

        return [.. geheim, .. HMACSHA256.HashData(authKey, gepruefet)[..16]];

    }

    /// <summary>Prüft den HMAC und entschlüsselt.</summary>
    private static Byte[] Open(Byte[] messageKey, Byte[] sealed_, Byte[] associatedData)
    {

        if (sealed_.Length < 16)
            throw new CryptographicException("Die Nachricht ist kürzer als ihr eigener HMAC.");

        var geheim  = sealed_[..^16];
        var mac     = sealed_[^16..];

        var (key, authKey, iv) = Material(messageKey);

        Byte[] gepruefet = [.. associatedData, .. geheim];

        if (!CryptographicOperations.FixedTimeEquals(
                 HMACSHA256.HashData(authKey, gepruefet)[..16],
                 mac))
            throw new CryptographicException(
                      "Der HMAC der Ratchet-Nachricht stimmt nicht - sie wurde verändert, gehört " +
                      "zu einer anderen Sitzung oder steht an einer anderen Stelle der Kette.");

        using var aes = Aes.Create();
        aes.Key = key;

        return aes.DecryptCbc(geheim, iv, PaddingMode.PKCS7);

    }

    #endregion

}
