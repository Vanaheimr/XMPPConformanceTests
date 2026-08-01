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
/// Das Ergebnis eines X3DH-Austauschs.
/// </summary>
/// <param name="SharedSecret">
/// Das gemeinsame Geheimnis, 32 Byte - der Anfang des Double Ratchet.
/// </param>
/// <param name="AssociatedData">
/// <c>AD = Encode(IK_A) ‖ Encode(IK_B)</c>, beide in Ed25519-Form. Geht in
/// jede Nachricht der Sitzung als mitgeprüfte Beigabe ein.
/// </param>
/// <param name="EphemeralKey">
/// Der öffentliche Teil des Einwegschlüssels des Anrufenden. Beim Annehmen
/// null - dort ist er bekannt und kommt von aussen.
/// </param>
/// <param name="UsedPreKeyId">
/// Der benutzte PreKey, oder null, wenn keiner vorrätig war.
/// </param>
public sealed record X3DHResult(Byte[]   SharedSecret,
                                Byte[]   AssociatedData,
                                Byte[]?  EphemeralKey,
                                UInt32?  UsedPreKeyId);

/// <summary>
/// X3DH nach XEP-0384, Abschnitt 4.2 - der Anfang einer Sitzung, ohne dass
/// beide gleichzeitig da sein müssen.
/// </summary>
/// <remarks>
/// <b>Warum vier Diffie-Hellman und nicht einer.</b> Jeder beantwortet eine
/// andere Frage, und erst zusammen ergeben sie das, was man von einem
/// Sitzungsanfang erwartet:
///
/// <list type="bullet">
/// <item><c>DH1 = DH(IK_A, SPK_B)</c> - beweist Bob, dass wirklich Alice
///       schreibt; ihr Identitätsschlüssel geht ein.</item>
/// <item><c>DH2 = DH(EK_A, IK_B)</c> - beweist Alice, dass wirklich Bob
///       liest.</item>
/// <item><c>DH3 = DH(EK_A, SPK_B)</c> - bringt die Frische: Alices
///       Einwegschlüssel gegen Bobs gewechselten. Wer beide
///       Identitätsschlüssel später stiehlt, kommt an diese Sitzung nicht
///       heran.</item>
/// <item><c>DH4 = DH(EK_A, OPK_B)</c> - sorgt dafür, dass zwei erste
///       Nachrichten an dasselbe Gerät verschiedene Sitzungen ergeben.
///       Entfällt, wenn Bobs Vorrat leer ist; dann fehlt genau diese
///       Eigenschaft und sonst nichts.</item>
/// </list>
///
/// <b>Die Reihenfolge ist Teil der Vorschrift</b>, nicht Geschmackssache:
/// Beide Seiten hängen die vier Werte hintereinander und leiten daraus ab.
/// Wer sie vertauscht, bekommt ein ebenso gutes Geheimnis - nur eben ein
/// anderes als die Gegenstelle. Der Fehler zeigt sich dann nicht hier,
/// sondern erst bei der ersten Nachricht, und sieht dort aus wie eine
/// Fälschung.
///
/// <b>Die 32 Byte 0xFF davor</b> sind kein Zierat. Sie trennen diese
/// Ableitung von jeder anderen, die dieselbe Kurve benutzt: Ohne sie liesse
/// sich ein Wert, der anderswo als Diffie-Hellman-Ergebnis entsteht, hier als
/// Sitzungsgeheimnis wiederverwenden.
/// </remarks>
public static class X3DH
{

    #region Data

    /// <summary>Der Info-String (XEP-0384, Abschnitt 4.2).</summary>
    public const String Info = "OMEMO X3DH";

    #endregion

    #region Initiate(own, theirBundle, preKeyId)

    /// <summary>
    /// Alice beginnt: aus dem Bundle der Gegenstelle wird ein gemeinsames
    /// Geheimnis, ohne dass die Gegenstelle etwas tun muss.
    /// </summary>
    /// <param name="own">Das eigene Schlüsselmaterial.</param>
    /// <param name="theirBundle">Das Bundle der Gegenstelle.</param>
    /// <param name="preKeyId">
    /// Welcher PreKey benutzt wird; ohne Angabe der erste des Bundles. Null
    /// bleibt es nur, wenn das Bundle gar keinen mitbringt.
    /// </param>
    /// <exception cref="CryptographicException">
    /// Wenn die Signatur über den Signed PreKey nicht stimmt. <b>Hier wird
    /// abgebrochen und nicht gewarnt:</b> Ein Bundle mit falscher Signatur ist
    /// entweder beschädigt oder untergeschoben, und in beiden Fällen ist eine
    /// Sitzung darauf schlimmer als keine - sie sähe aus wie eine
    /// verschlüsselte.
    /// </exception>
    public static X3DHResult Initiate(OmemoIdentity  own,
                                      OmemoBundle    theirBundle,
                                      UInt32?        preKeyId = null)
    {

        if (!theirBundle.SignatureIsValid())
            throw new CryptographicException(
                      "Die Signatur über den Signed PreKey stimmt nicht - das Bundle stammt nicht " +
                      "von dem IdentityKey, den es nennt.");

        var ephemeral  = Curve25519.GenerateKeyPair();

        var ihrIk      = theirBundle.IdentityKeyForAgreement();
        var ihrSpk     = theirBundle.SignedPreKey;

        var preKey     = preKeyId.HasValue
                             ? theirBundle.PreKeys.FirstOrDefault(p => p.Id == preKeyId.Value)
                             : theirBundle.PreKeys.FirstOrDefault();

        if (preKeyId.HasValue && preKey is null)
            throw new CryptographicException($"Das Bundle kennt keinen PreKey mit der Kennung {preKeyId}.");

        var dh1 = Curve25519.Agree(own.IdentityKey.PrivateKey,  ihrSpk);
        var dh2 = Curve25519.Agree(ephemeral.PrivateKey,        ihrIk);
        var dh3 = Curve25519.Agree(ephemeral.PrivateKey,        ihrSpk);
        var dh4 = preKey is not null
                      ? Curve25519.Agree(ephemeral.PrivateKey,  preKey.PublicKey)
                      : [];

        return new X3DHResult(
                   Derive(dh1, dh2, dh3, dh4),
                   AssociatedData(own.PublicIdentityKey, theirBundle.IdentityKey),
                   ephemeral.PublicKey,
                   preKey?.Id);

    }

    #endregion

    #region Accept(own, theirIdentityKey, theirEphemeralKey, signedPreKeyId, preKeyId)

    /// <summary>
    /// Bob nimmt an: dieselben vier Werte, aus der anderen Richtung gerechnet.
    /// </summary>
    /// <param name="own">Das eigene Schlüsselmaterial.</param>
    /// <param name="theirIdentityKey">
    /// Der IdentityKey der Gegenstelle <b>in Ed25519-Form</b>, so wie er über
    /// die Leitung kam.
    /// </param>
    /// <param name="theirEphemeralKey">Ihr Einwegschlüssel, Montgomery-Form.</param>
    /// <param name="signedPreKeyId">
    /// Welchen Signed PreKey die Gegenstelle benutzt hat. Stimmt er nicht mit
    /// dem aktuellen überein, ist die Nachricht mit einem gewechselten
    /// Schlüssel unterwegs gewesen - dieser Stand kennt nur den aktuellen und
    /// weist sie ab.
    /// </param>
    /// <param name="preKeyId">
    /// Welchen PreKey sie benutzt hat, oder null. Er wird dabei
    /// <b>verbraucht</b>.
    /// </param>
    public static X3DHResult Accept(OmemoIdentity  own,
                                    Byte[]         theirIdentityKey,
                                    Byte[]         theirEphemeralKey,
                                    UInt32         signedPreKeyId,
                                    UInt32?        preKeyId)
    {

        // Der aktuelle oder der eine abgelöste - eine Nachricht, die vor dem
        // Wechsel abgeschickt wurde, nennt den alten und ist trotzdem zu
        // lesen. Alles darüber hinaus ist endgültig fort, und das ist Absicht.
        var signedPreKey = own.SignedPreKeyFor(signedPreKeyId)
                               ?? throw new CryptographicException(
                                      $"Die Nachricht nennt den Signed PreKey {signedPreKeyId}; dieses " +
                                      $"Gerät hat {own.SignedPreKeyId}" +
                                      (own.PreviousSignedPreKeyId is UInt32 alt ? $" und {alt}" : "") +
                                      ".");

        var preKey = preKeyId.HasValue ? own.TakePreKey(preKeyId.Value) : null;

        if (preKeyId.HasValue && preKey is null)
            throw new CryptographicException(
                      $"Der PreKey {preKeyId} ist unbekannt oder schon verbraucht. Eine zweite " +
                      "Sitzung auf denselben PreKey wäre wiederholbar.");

        var ihrIk = Curve25519.EdwardsToMontgomery(theirIdentityKey);

        // Dieselben vier Werte, jeweils von der anderen Seite: Wo Alice ihren
        // geheimen Teil und Bobs öffentlichen nimmt, nimmt Bob seinen geheimen
        // und ihren öffentlichen.
        var dh1 = Curve25519.Agree(signedPreKey.PrivateKey,       ihrIk);
        var dh2 = Curve25519.Agree(own.IdentityKey.PrivateKey,    theirEphemeralKey);
        var dh3 = Curve25519.Agree(signedPreKey.PrivateKey,       theirEphemeralKey);
        var dh4 = preKey is not null
                      ? Curve25519.Agree(preKey.PrivateKey,      theirEphemeralKey)
                      : [];

        return new X3DHResult(
                   Derive(dh1, dh2, dh3, dh4),
                   AssociatedData(theirIdentityKey, own.PublicIdentityKey),
                   null,
                   preKeyId);

    }

    #endregion

    #region Hilfsfunktionen

    /// <summary>
    /// Die Ableitung: 32 Byte 0xFF, dann die vier Diffie-Hellman-Werte, durch
    /// HKDF-SHA-256.
    /// </summary>
    internal static Byte[] Derive(Byte[] dh1, Byte[] dh2, Byte[] dh3, Byte[] dh4)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256,
                          ikm:           [.. Enumerable.Repeat((Byte) 0xFF, 32), .. dh1, .. dh2, .. dh3, .. dh4],
                          salt:          new Byte[32],
                          info:          Encoding.UTF8.GetBytes(Info),
                          outputLength:  32);

    /// <summary>
    /// <c>AD = Encode(IK_A) ‖ Encode(IK_B)</c> - immer der Anrufende zuerst.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist die Aussage: Sie hält fest, wer angefangen hat.
    /// Hingen die Schlüssel in beliebiger Reihenfolge da, rechneten beide
    /// Seiten verschiedene Beigaben aus - und jede Nachricht scheiterte an
    /// einer Prüfung, die nichts mit ihrem Inhalt zu tun hat.
    /// </remarks>
    internal static Byte[] AssociatedData(Byte[] initiatorIdentityKey, Byte[] responderIdentityKey)
        => [.. initiatorIdentityKey, .. responderIdentityKey];

    #endregion

}
