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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Das eigene Schlüsselmaterial eines Geräts: IdentityKey, Signed PreKey und
/// die einmal verwendbaren PreKeys.
/// </summary>
/// <remarks>
/// <b>Die drei Schlüsselarten unterscheiden sich in ihrer Lebensdauer, und
/// genau daran hängt, was sie schützen.</b>
///
/// Der <b>IdentityKey</b> lebt so lange wie das Gerät; sein Fingerabdruck ist
/// das, was ein Mensch vergleicht. Er wird nie ausgetauscht - täte er es,
/// wären alle bisherigen Vergleiche wertlos.
///
/// Der <b>Signed PreKey</b> wird regelmässig gewechselt. Er ist der Grund,
/// warum ein gestohlener Schlüssel nicht rückwirkend alles öffnet: Wer den
/// heutigen hat, kommt an die Sitzungen von vorletzter Woche nicht heran, weil
/// es den Schlüssel von damals nicht mehr gibt. Deshalb wird der abgelöste
/// noch eine Weile aufgehoben und dann <b>wirklich</b> vergessen - ein
/// aufgehobener alter Schlüssel nimmt genau die Eigenschaft zurück, für die es
/// den Wechsel gibt.
///
/// Die <b>PreKeys</b> gelten einmal. Sie sorgen dafür, dass zwei erste
/// Nachrichten an dasselbe Gerät nicht denselben Sitzungsschlüssel ergeben.
/// Geht der Vorrat zur Neige, kann eine Sitzung auch ohne einen beginnen - das
/// ist ausdrücklich vorgesehen und kostet nur diese eine Eigenschaft.
/// </remarks>
public sealed class OmemoIdentity
{

    #region Data

    private readonly Dictionary<UInt32, Curve25519KeyPair> _preKeys = [];
    private readonly Lock _lock = new();

    /// <summary>Wie viele PreKeys ein frisches Gerät veröffentlicht.</summary>
    public const Int32 PreKeyCount = 100;

    #endregion

    #region Properties

    /// <summary>Der IdentityKey - so lange gültig wie dieses Gerät.</summary>
    public Curve25519KeyPair IdentityKey { get; }

    /// <summary>
    /// Die Gerätekennung (XEP-0384, Abschnitt 5.1): eine positive Zahl, unter
    /// der dieses Gerät in der Device-Liste steht.
    /// </summary>
    public UInt32 DeviceId { get; }

    /// <summary>Der aktuelle Signed PreKey.</summary>
    public Curve25519KeyPair SignedPreKey { get; private set; }

    /// <summary>Seine Kennung.</summary>
    public UInt32 SignedPreKeyId { get; private set; }

    /// <summary>Die Signatur des IdentityKey darüber.</summary>
    public Byte[] SignedPreKeySignature { get; private set; }

    /// <summary>Wie viele PreKeys noch vorrätig sind.</summary>
    public Int32 AvailablePreKeys
    {
        get { lock (_lock) return _preKeys.Count; }
    }

    /// <summary>
    /// Der IdentityKey in Ed25519-Form - so und nur so geht er über die
    /// Leitung (Abschnitt 5.3.2).
    /// </summary>
    public Byte[] PublicIdentityKey
        => Curve25519.MontgomeryToEdwards(IdentityKey.PublicKey);

    /// <summary>
    /// Der Fingerabdruck, den ein Mensch vergleicht: der öffentliche
    /// IdentityKey in Ed25519-Form, hexadezimal.
    /// </summary>
    /// <remarks>
    /// Über die Ed25519-Form und nicht über die Montgomery-Form, denn nur
    /// jene geht über die Leitung - die Gegenstelle könnte einen Vergleich
    /// über die andere gar nicht anstellen.
    /// </remarks>
    public String Fingerprint
        => Convert.ToHexString(PublicIdentityKey).ToLowerInvariant();

    #endregion

    #region Constructor(s)

    private OmemoIdentity(UInt32              deviceId,
                          Curve25519KeyPair   identityKey,
                          UInt32              signedPreKeyId,
                          Curve25519KeyPair   signedPreKey,
                          Byte[]              signature)
    {
        DeviceId               = deviceId;
        IdentityKey            = identityKey;
        SignedPreKeyId         = signedPreKeyId;
        SignedPreKey           = signedPreKey;
        SignedPreKeySignature  = signature;
    }

    #endregion

    #region Create(...)

    /// <summary>
    /// Legt ein frisches Gerät an: IdentityKey, Signed PreKey samt Signatur
    /// und <see cref="PreKeyCount"/> PreKeys.
    /// </summary>
    /// <param name="deviceId">
    /// Die Gerätekennung; ohne Angabe eine zufällige. Sie ist keine
    /// Geheimnis-, sondern eine Ordnungszahl - sie steht in jeder Device-Liste.
    /// </param>
    public static OmemoIdentity Create(UInt32? deviceId = null)
    {

        var identity      = Curve25519.GenerateKeyPair();
        var signedPreKey  = Curve25519.GenerateKeyPair();

        var eigen = new OmemoIdentity(deviceId ?? ZufaelligeKennung(),
                                      identity,
                                      1,
                                      signedPreKey,
                                      Curve25519.Sign(identity.PrivateKey, signedPreKey.PublicKey));

        eigen.ReplenishPreKeys();

        return eigen;

    }

    /// <summary>
    /// Eine Kennung aus dem Bereich, den Abschnitt 5.3.2 zulässt: 1 bis 2³¹-1.
    /// </summary>
    /// <remarks>
    /// Aus dem kryptographischen Zufallsgenerator und nicht aus einem Zähler:
    /// Eine fortlaufende Nummer verriete, das wievielte Gerät dieses Kontos
    /// hier gerade angelegt wird, und die Device-Liste ist öffentlich.
    /// </remarks>
    private static UInt32 ZufaelligeKennung()
        => (UInt32) RandomNumberGenerator.GetInt32(1, Int32.MaxValue);

    #endregion

    #region PreKeys

    /// <summary>
    /// Füllt den Vorrat wieder auf <see cref="PreKeyCount"/> auf.
    /// </summary>
    /// <remarks>
    /// Die Kennungen laufen weiter und werden nicht wiederverwendet. Eine
    /// wiederverwendete Kennung wäre keine Ordnungszahl mehr, sondern eine
    /// Verwechslung: Eine Nachricht, die unterwegs liegenblieb und den alten
    /// PreKey nennt, fände beim Ankommen einen neuen unter derselben Nummer
    /// und ergäbe eine Sitzung, die es nie gab.
    /// </remarks>
    public IReadOnlyList<OmemoPreKey> ReplenishPreKeys()
    {

        lock (_lock)
        {

            var naechste = _preKeys.Count == 0 ? 1u : _preKeys.Keys.Max() + 1;

            while (_preKeys.Count < PreKeyCount)
                _preKeys[naechste++] = Curve25519.GenerateKeyPair();

            return PublicPreKeys();

        }

    }

    /// <summary>Die öffentlichen Teile aller vorrätigen PreKeys.</summary>
    private IReadOnlyList<OmemoPreKey> PublicPreKeys()
        => [.. _preKeys.OrderBy(e => e.Key)
                       .Select(e => new OmemoPreKey(e.Key, e.Value.PublicKey))];

    /// <summary>
    /// Nimmt einen PreKey heraus - und zwar endgültig.
    /// </summary>
    /// <returns>
    /// Der PreKey, oder null, wenn es ihn nicht (mehr) gibt.
    /// </returns>
    /// <remarks>
    /// <b>Entnehmen und Löschen sind ein Schritt, und das ist der Kern der
    /// Sache.</b> Ein PreKey, der zweimal gilt, ergibt zweimal denselben
    /// Sitzungsschlüssel - und damit ist die Sitzung wiederholbar: Wer eine
    /// alte erste Nachricht noch einmal einspielt, bekommt eine Antwort, als
    /// sei sie neu. Deshalb gibt es hier kein „nachsehen" und kein
    /// „verbrauchen" getrennt; wer den Schlüssel in die Hand bekommt, hat ihn
    /// damit auch schon aus dem Vorrat genommen.
    /// </remarks>
    public Curve25519KeyPair? TakePreKey(UInt32 id)
    {

        lock (_lock)
        {

            if (!_preKeys.Remove(id, out var paar))
                return null;

            return paar;

        }

    }

    #endregion

    #region RotateSignedPreKey()

    /// <summary>
    /// Wechselt den Signed PreKey und unterschreibt den neuen.
    /// </summary>
    /// <remarks>
    /// Der abgelöste wird hier <b>nicht</b> aufgehoben. Das gehört in die
    /// Etappe, die Sitzungen speichert: Solange noch eine Nachricht unterwegs
    /// sein kann, die den alten nennt, muss er greifbar bleiben - und danach
    /// muss er verschwinden, sonst nimmt er die Eigenschaft zurück, für die es
    /// den Wechsel gibt. Beides an einem Ort zu regeln, an dem es keinen
    /// Speicher gibt, wäre eine Zusage, die niemand hält.
    /// </remarks>
    public void RotateSignedPreKey()
    {

        var neu = Curve25519.GenerateKeyPair();

        lock (_lock)
        {
            SignedPreKeyId++;
            SignedPreKey           = neu;
            SignedPreKeySignature  = Curve25519.Sign(IdentityKey.PrivateKey, neu.PublicKey);
        }

    }

    #endregion

    #region Bundle()

    /// <summary>
    /// Das eigene Bundle, wie es veröffentlicht wird.
    /// </summary>
    public OmemoBundle Bundle()
    {
        lock (_lock)
            return new OmemoBundle(PublicIdentityKey,
                                   SignedPreKeyId,
                                   SignedPreKey.PublicKey,
                                   SignedPreKeySignature,
                                   PublicPreKeys());
    }

    #endregion

}
