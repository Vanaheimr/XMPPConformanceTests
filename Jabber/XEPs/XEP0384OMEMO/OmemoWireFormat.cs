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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Der Schlüsselaustausch am Anfang einer Sitzung
/// (<c>OMEMOKeyExchange.proto</c>).
/// </summary>
/// <param name="PreKeyId">Welcher PreKey des Empfängers benutzt wurde.</param>
/// <param name="SignedPreKeyId">Welcher Signed PreKey.</param>
/// <param name="IdentityKey">Der eigene IdentityKey, in Ed25519-Form.</param>
/// <param name="EphemeralKey">Der Einwegschlüssel aus X3DH.</param>
/// <param name="Message">Die eingepackte <c>OMEMOAuthenticatedMessage</c>.</param>
/// <remarks>
/// <b>Er reist mit jeder Nachricht mit, bis die Gegenstelle geantwortet
/// hat</b>, und nicht nur mit der ersten. Der Grund ist unangenehm einfach:
/// Die erste Nachricht kann verlorengehen. Käme der Austausch nur einmal,
/// stünde die zweite Nachricht vor einer Gegenstelle, die keine Sitzung
/// kennt - und wäre nicht zu lesen, ohne dass jemand erführe, warum.
/// </remarks>
public sealed record OmemoKeyExchange(UInt32  PreKeyId,
                                      UInt32  SignedPreKeyId,
                                      Byte[]  IdentityKey,
                                      Byte[]  EphemeralKey,
                                      Byte[]  Message)
{

    /// <summary>Die Kodierung nach dem Schema der Spezifikation.</summary>
    public Byte[] Encode()
    {

        var bytes = new List<Byte>();

        Protobuf.WriteUInt32 (bytes, 1, PreKeyId);
        Protobuf.WriteUInt32 (bytes, 2, SignedPreKeyId);
        Protobuf.WriteBytes  (bytes, 3, IdentityKey);
        Protobuf.WriteBytes  (bytes, 4, EphemeralKey);
        Protobuf.WriteBytes  (bytes, 5, Message);

        return [.. bytes];

    }

    /// <summary>Liest einen Schlüsselaustausch.</summary>
    public static OmemoKeyExchange Decode(Byte[] daten)
    {

        UInt32  pk = 0, spk = 0;
        Byte[]  ik = [], ek = [], msg = [];

        foreach (var (feld, _, zahl, inhalt) in Protobuf.Read(daten))
            switch (feld)
            {
                case 1: pk   = (UInt32) zahl;  break;
                case 2: spk  = (UInt32) zahl;  break;
                case 3: ik   = inhalt;         break;
                case 4: ek   = inhalt;         break;
                case 5: msg  = inhalt;         break;
            }

        if (ik.Length == 0 || ek.Length == 0 || msg.Length == 0)
            throw new FormatException("Dem Schlüsselaustausch fehlt ein Pflichtfeld.");

        return new OmemoKeyExchange(pk, spk, ik, ek, msg);

    }

}

/// <summary>
/// Die beglaubigte Nachricht (<c>OMEMOAuthenticatedMessage.proto</c>): der
/// gekürzte HMAC und die eingepackte <c>OMEMOMessage</c>.
/// </summary>
/// <remarks>
/// <b>Warum der HMAC nicht in der Nachricht selbst steht.</b> Er wird über die
/// kodierte Nachricht gerechnet - stünde er darin, prüfte er sich selbst mit.
/// Deshalb eine Hülle: innen die Nachricht, aussen ihre Beglaubigung.
/// </remarks>
public sealed record OmemoAuthenticatedMessage(Byte[] Mac, Byte[] Message)
{

    /// <summary>Die Kodierung nach dem Schema der Spezifikation.</summary>
    public Byte[] Encode()
    {

        var bytes = new List<Byte>();

        Protobuf.WriteBytes(bytes, 1, Mac);
        Protobuf.WriteBytes(bytes, 2, Message);

        return [.. bytes];

    }

    /// <summary>Liest eine beglaubigte Nachricht.</summary>
    public static OmemoAuthenticatedMessage Decode(Byte[] daten)
    {

        Byte[] mac = [], msg = [];

        foreach (var (feld, _, _, inhalt) in Protobuf.Read(daten))
            switch (feld)
            {
                case 1: mac  = inhalt;  break;
                case 2: msg  = inhalt;  break;
            }

        if (mac.Length != 16)
            throw new FormatException(
                      $"Der HMAC hat {mac.Length} statt 16 Byte. Abschnitt 4.3 kürzt ihn auf 16 - " +
                      "eine andere Länge ist keine Nachricht dieses Verfahrens.");

        if (msg.Length == 0)
            throw new FormatException("Die beglaubigte Nachricht ist leer.");

        return new OmemoAuthenticatedMessage(mac, msg);

    }

}

/// <summary>
/// Die Umwandlung zwischen einer <see cref="RatchetMessage"/> und ihrer
/// Gestalt auf der Leitung.
/// </summary>
public static class OmemoWireFormat
{

    #region RatchetMessage <-> OMEMOAuthenticatedMessage

    /// <summary>
    /// Packt eine Ratchet-Nachricht in eine <c>OMEMOAuthenticatedMessage</c>.
    /// </summary>
    public static Byte[] Encode(RatchetMessage message)
        => new OmemoAuthenticatedMessage(
               message.Mac,
               message.Header.Encode(message.Ciphertext)).Encode();

    /// <summary>
    /// Liest eine <c>OMEMOAuthenticatedMessage</c> zurück in eine
    /// Ratchet-Nachricht.
    /// </summary>
    /// <remarks>
    /// <b>Ein fehlendes Pflichtfeld ist ein Formatfehler und kein Vorgabewert.</b>
    /// Protocol Buffers kennt für <c>uint32</c> die Null und für <c>bytes</c>
    /// das leere Feld, und beides liesse sich hier stillschweigend einsetzen -
    /// die Nachricht sähe dann aus wie die erste einer Kette mit leerem
    /// Ratchet-Schlüssel. Sie liesse sich nicht entschlüsseln, und niemand
    /// wüsste, dass ein Feld fehlte.
    /// </remarks>
    public static RatchetMessage Decode(Byte[] daten)
    {

        var beglaubigt = OmemoAuthenticatedMessage.Decode(daten);

        UInt32  n = 0, pn = 0;
        Byte[]  dh = [], geheim = [];
        var     hatN = false;
        var     hatPn = false;

        foreach (var (feld, _, zahl, inhalt) in Protobuf.Read(beglaubigt.Message))
            switch (feld)
            {
                case 1: n       = (UInt32) zahl;  hatN  = true;  break;
                case 2: pn      = (UInt32) zahl;  hatPn = true;  break;
                case 3: dh      = inhalt;                        break;
                case 4: geheim  = inhalt;                        break;
            }

        if (!hatN || !hatPn || dh.Length != Curve25519.KeyLength || geheim.Length == 0)
            throw new FormatException(
                      "Der OMEMOMessage fehlt ein Pflichtfeld oder ihr Ratchet-Schlüssel hat die " +
                      "falsche Länge.");

        return new RatchetMessage(new RatchetHeader(dh, pn, n), geheim, beglaubigt.Mac);

    }

    #endregion

}
