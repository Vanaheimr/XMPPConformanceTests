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
        => Encode(null);

    /// <summary>
    /// Der Kopf als <c>OMEMOMessage.proto</c>, wahlweise mit Geheimtext.
    /// </summary>
    /// <remarks>
    /// <b>Genau diese Bytes prüft der HMAC</b> - „the HMAC is computed over
    /// <c>ad ‖ OMEMOMessage.proto</c> (after ciphertext is added to the
    /// proto)". Der Geheimtext steht darin also als Feld 4 <b>mit Kennung und
    /// Längenangabe</b> und nicht einfach hinten angehängt.
    ///
    /// Der Unterschied sind drei Byte, und er ist in D64 durchgerutscht: Dort
    /// wurde der Geheimtext roh angehängt. Beide Seiten taten dasselbe, alle
    /// Tests blieben grün - und gegen einen fremden Client hätte keine einzige
    /// Prüfsumme gestimmt. Dieselbe Familie von Fehlern wie der Info-String in
    /// D62, die Beigabe in D63 und die Wurzelkette in D64.
    /// </remarks>
    public Byte[] Encode(Byte[]? ciphertext)
    {

        var bytes = new List<Byte>();

        Protobuf.WriteUInt32 (bytes, 1, MessageNumber);
        Protobuf.WriteUInt32 (bytes, 2, PreviousChainLength);
        Protobuf.WriteBytes  (bytes, 3, DhPublicKey);

        if (ciphertext is not null)
            Protobuf.WriteBytes(bytes, 4, ciphertext);

        return [.. bytes];

    }

}

/// <summary>
/// Eine verschlüsselte Ratchet-Nachricht: Kopf, Geheimtext und der auf 16 Byte
/// gekürzte HMAC.
/// </summary>
/// <remarks>
/// Die drei Teile stehen getrennt, weil sie auf der Leitung getrennt stehen:
/// Kopf und Geheimtext bilden zusammen die <c>OMEMOMessage</c>, der HMAC
/// umschliesst sie als <c>OMEMOAuthenticatedMessage</c>.
/// </remarks>
public sealed record RatchetMessage(RatchetHeader Header, Byte[] Ciphertext, Byte[] Mac);

/// <summary>
/// Ein beiseitegelegter Nachrichtenschlüssel, wie er einen Neustart
/// überdauert.
/// </summary>
public sealed record SkippedMessageKey(String RatchetKey, UInt32 Number, Byte[] MessageKey);

/// <summary>
/// Der vollständige Zustand einer Ratchet-Sitzung.
/// </summary>
/// <remarks>
/// <b>Vollständig heisst: was hier fehlt, ist nach einem Neustart verloren</b> -
/// und zwar so, dass die Gegenstelle es nicht erfährt. Fehlte der eigene
/// Ratchet-Schlüssel, liesse sich nichts mehr entschlüsseln, was noch
/// unterwegs ist; fehlten die beiseitegelegten Schlüssel, wären es die
/// überholten Nachrichten; fehlten die Zähler, stünde die Kette an falscher
/// Stelle.
///
/// Der geheime Teil des Ratchet-Schlüssels ist dabei ein Schlüssel wie jeder
/// andere: Wer die abgelegte Sitzung liest, liest das Gespräch mit.
/// </remarks>
public sealed record RatchetState(Byte[]?                            OwnRatchetPrivateKey,
                                  Byte[]?                            RemoteRatchetKey,
                                  Byte[]                             RootKey,
                                  Byte[]?                            SendChain,
                                  Byte[]?                            ReceiveChain,
                                  UInt32                             SendCount,
                                  UInt32                             ReceiveCount,
                                  UInt32                             PreviousSendCount,
                                  IReadOnlyList<SkippedMessageKey>   SkippedKeys);

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

    #region Export() / Import(state)

    /// <summary>
    /// Der Zustand dieser Sitzung, wie er abgelegt wird.
    /// </summary>
    public RatchetState Export()
    {

        lock (_lock)
            return new RatchetState(_eigenerRatchet?.PrivateKey,
                                    _fremderRatchet,
                                    _wurzel,
                                    _sendekette,
                                    _empfangskette,
                                    SendCount,
                                    ReceiveCount,
                                    PreviousSendCount,
                                    [.. _uebersprungen.Select(e => new SkippedMessageKey(e.Key.Dh,
                                                                                          e.Key.N,
                                                                                          e.Value))]);

    }

    /// <summary>
    /// Stellt eine abgelegte Sitzung wieder her.
    /// </summary>
    /// <remarks>
    /// <b>Wiederhergestellt und nicht neu begonnen</b> - der Unterschied ist
    /// die ganze Etappe. Eine neu begonnene Sitzung hätte einen anderen
    /// Wurzelschlüssel, und die Gegenstelle könnte nichts mehr lesen, was von
    /// hier kommt. Sie sähe dabei keinen Fehler, sondern nur Nachrichten, die
    /// ihre Prüfsumme nicht bestehen - also etwas, das wie ein Angriff
    /// aussieht.
    /// </remarks>
    public static DoubleRatchet Import(RatchetState state)
    {

        var ratchet = new DoubleRatchet(state.RootKey)
        {
            _eigenerRatchet    = state.OwnRatchetPrivateKey is not null
                                     ? Curve25519.KeyPairFromPrivate(state.OwnRatchetPrivateKey)
                                     : null,
            _fremderRatchet    = state.RemoteRatchetKey,
            _sendekette        = state.SendChain,
            _empfangskette     = state.ReceiveChain,
            SendCount          = state.SendCount,
            ReceiveCount       = state.ReceiveCount,
            PreviousSendCount  = state.PreviousSendCount
        };

        foreach (var k in state.SkippedKeys)
            ratchet._uebersprungen[(k.RatchetKey, k.Number)] = k.MessageKey;

        return ratchet;

    }

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

            var (geheim, mac) = Seal(nachrichtenschluessel, plaintext, associatedData, kopf);

            return new RatchetMessage(kopf, geheim, mac);

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

            // 1. Ein beiseitegelegter Schlüssel?
            var schluessel = (message.Header.DhPublicKey, message.Header.MessageNumber);
            var kennung    = (Convert.ToHexString(schluessel.DhPublicKey), schluessel.MessageNumber);

            if (_uebersprungen.TryGetValue(kennung, out var beiseite))
            {

                var klartext = Open(beiseite, message, associatedData);

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

            return Open(mk, message, associatedData);

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
    private static (Byte[] Ciphertext, Byte[] Mac) Seal(Byte[]         messageKey,
                                                        Byte[]         plaintext,
                                                        Byte[]         associatedData,
                                                        RatchetHeader  header)
    {

        var (key, authKey, iv) = Material(messageKey);

        using var aes = Aes.Create();
        aes.Key = key;

        var geheim = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);

        return (geheim, Mac(authKey, associatedData, header, geheim));

    }

    /// <summary>Prüft den HMAC und entschlüsselt.</summary>
    private static Byte[] Open(Byte[] messageKey, RatchetMessage message, Byte[] associatedData)
    {

        var (key, authKey, iv) = Material(messageKey);

        if (!CryptographicOperations.FixedTimeEquals(
                 Mac(authKey, associatedData, message.Header, message.Ciphertext),
                 message.Mac))
            throw new CryptographicException(
                      "Der HMAC der Ratchet-Nachricht stimmt nicht - sie wurde verändert, gehört " +
                      "zu einer anderen Sitzung oder steht an einer anderen Stelle der Kette.");

        using var aes = Aes.Create();
        aes.Key = key;

        return aes.DecryptCbc(message.Ciphertext, iv, PaddingMode.PKCS7);

    }

    /// <summary>
    /// Der gekürzte HMAC über <c>ad ‖ OMEMOMessage.proto</c> - mit Geheimtext
    /// <b>im</b> Protobuf und nicht dahinter.
    /// </summary>
    /// <remarks>
    /// Die Beigabe enthält damit alles, was diese Nachricht ausmacht: die
    /// beiden IdentityKeys aus X3DH, die Stelle in der Kette und den
    /// Geheimtext selbst. Ohne den Kopf liesse sich eine gültige Nachricht an
    /// eine andere Stelle der Kette verschieben, ohne die IdentityKeys in eine
    /// fremde Sitzung.
    /// </remarks>
    internal static Byte[] Mac(Byte[] authKey, Byte[] associatedData, RatchetHeader header, Byte[] ciphertext)
    {

        Byte[] gepruefet = [.. associatedData, .. header.Encode(ciphertext)];

        return HMACSHA256.HashData(authKey, gepruefet)[..16];

    }

    #endregion

}
