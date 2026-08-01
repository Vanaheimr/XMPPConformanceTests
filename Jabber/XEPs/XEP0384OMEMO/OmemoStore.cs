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
/// Wie dieses Gerät zu einem fremden steht.
/// </summary>
public enum OmemoTrust
{

    /// <summary>Noch nicht entschieden - der Mensch hat den Fingerabdruck nie angesehen.</summary>
    Undecided,

    /// <summary>Bestätigt.</summary>
    Trusted,

    /// <summary>Ausdrücklich abgelehnt - an dieses Gerät geht nichts.</summary>
    Distrusted

}

/// <summary>
/// Was beim Wiedersehen eines Geräts herauskommt.
/// </summary>
public enum OmemoIdentityCheck
{

    /// <summary>Dieses Gerät war noch nie da.</summary>
    New,

    /// <summary>Bekannt, und der Schlüssel ist derselbe wie beim letzten Mal.</summary>
    Known,

    /// <summary>
    /// Bekannt, aber mit einem <b>anderen</b> Schlüssel als beim letzten Mal.
    /// </summary>
    Changed

}

/// <summary>
/// Was dieses Gerät über ein fremdes weiss.
/// </summary>
/// <param name="BareJid">Wem es gehört.</param>
/// <param name="DeviceId">Welches Gerät.</param>
/// <param name="IdentityKey">Sein IdentityKey in Ed25519-Form.</param>
/// <param name="Trust">Die Entscheidung des Menschen davor.</param>
/// <param name="FirstSeen">Wann es zum ersten Mal auftauchte.</param>
public sealed record OmemoDeviceRecord(String          BareJid,
                                       UInt32          DeviceId,
                                       Byte[]          IdentityKey,
                                       OmemoTrust      Trust,
                                       DateTimeOffset  FirstSeen)
{

    /// <summary>Der Fingerabdruck, den ein Mensch vergleicht.</summary>
    public String Fingerprint
        => Convert.ToHexString(IdentityKey).ToLowerInvariant();

}

/// <summary>
/// Das eigene Schlüsselmaterial, wie es abgelegt wird - <b>mit den geheimen
/// Teilen</b>.
/// </summary>
/// <param name="DeviceId">Die eigene Gerätekennung.</param>
/// <param name="IdentityPrivateKey">Der geheime IdentityKey.</param>
/// <param name="SignedPreKeyId">Die Kennung des aktuellen Signed PreKey.</param>
/// <param name="SignedPreKeyPrivateKey">Sein geheimer Teil.</param>
/// <param name="SignedPreKeySignature">Seine Signatur.</param>
/// <param name="PreviousSignedPreKeyId">Die Kennung des abgelösten, oder null.</param>
/// <param name="PreviousSignedPreKeyPrivateKey">Sein geheimer Teil, oder null.</param>
/// <param name="PreKeys">Die vorrätigen PreKeys mit ihren geheimen Teilen.</param>
public sealed record OmemoIdentityState(UInt32                                  DeviceId,
                                        Byte[]                                  IdentityPrivateKey,
                                        UInt32                                  SignedPreKeyId,
                                        Byte[]                                  SignedPreKeyPrivateKey,
                                        Byte[]                                  SignedPreKeySignature,
                                        UInt32?                                 PreviousSignedPreKeyId,
                                        Byte[]?                                 PreviousSignedPreKeyPrivateKey,
                                        IReadOnlyList<OmemoStoredPreKey>        PreKeys);

/// <summary>Ein PreKey mit seinem geheimen Teil.</summary>
public sealed record OmemoStoredPreKey(UInt32 Id, Byte[] PrivateKey);

/// <summary>
/// Eine abgelegte Sitzung: die Ratsche und die Beigabe aus X3DH.
/// </summary>
/// <param name="Ratchet">Der Zustand der beiden Ratschen.</param>
/// <param name="AssociatedData">
/// <c>Encode(IK_A) ‖ Encode(IK_B)</c> - beide IdentityKeys, der Anrufende
/// zuerst.
/// </param>
/// <remarks>
/// <b>Die Beigabe gehört zur Sitzung und nicht zur Ratsche</b>, deshalb steht
/// sie hier daneben und nicht im <see cref="RatchetState"/>: Die Ratsche
/// bekommt sie bei jedem Aufruf gereicht und besitzt sie nicht.
///
/// Abgelegt werden muss sie trotzdem. Sie entsteht einmal beim
/// Schlüsselaustausch und geht danach in jede Prüfsumme ein; ohne sie liesse
/// sich eine wiederhergestellte Sitzung zwar fortsetzen, aber keine einzige
/// Nachricht darin lesen - und der Grund stünde nirgends.
/// </remarks>
public sealed record OmemoSessionState(RatchetState Ratchet, Byte[] AssociatedData);

/// <summary>
/// Der Speicher, der einen Neustart überdauert.
/// </summary>
/// <remarks>
/// <b>Ohne ihn ist jede Wiederverbindung ein Vertrauensbruch.</b> Ein neuer
/// IdentityKey bedeutet einen neuen Fingerabdruck, und jeder Vergleich, den
/// irgendein Mensch je angestellt hat, ist damit wertlos. Ein Client, der bei
/// jedem Start neue Schlüssel erzeugt, sieht für seine Kontakte aus wie ein
/// Angreifer - jedes Mal.
///
/// <b>Und die laufenden Sitzungen müssen mit.</b> Eine neu begonnene Sitzung
/// hätte einen anderen Wurzelschlüssel; die Gegenstelle bekäme Nachrichten,
/// deren Prüfsumme nicht stimmt, und das sieht wiederum aus wie ein Angriff.
///
/// Was dieser Speicher enthält, ist ohne Ausnahme geheim: der IdentityKey, die
/// PreKeys, jeder Kettenschlüssel. <b>Wer ihn liest, liest die Gespräche
/// mit</b> - die vergangenen nur so weit, wie ihre Schlüssel noch da sind, die
/// künftigen ganz.
/// </remarks>
public interface IOmemoStore
{

    /// <summary>Das eigene Schlüsselmaterial, oder null beim ersten Start.</summary>
    OmemoIdentityState? LoadIdentity();

    /// <summary>Legt das eigene Schlüsselmaterial ab.</summary>
    void SaveIdentity(OmemoIdentityState state);

    /// <summary>Eine abgelegte Sitzung, oder null.</summary>
    OmemoSessionState? LoadSession(String bareJid, UInt32 deviceId);

    /// <summary>Legt eine Sitzung ab und ersetzt eine vorhandene.</summary>
    void SaveSession(String bareJid, UInt32 deviceId, OmemoSessionState state);

    /// <summary>Alle Geräte, von denen dieses hier weiss.</summary>
    IReadOnlyList<OmemoDeviceRecord> KnownDevices();

    /// <summary>Legt einen Gerätevermerk ab und ersetzt einen vorhandenen.</summary>
    void SaveDevice(OmemoDeviceRecord record);

}

/// <summary>
/// Gemeinsames Verhalten aller Speicher - alles, was nicht davon abhängt,
/// wohin geschrieben wird.
/// </summary>
public static class OmemoStoreExtensions
{

    #region Identity laden / ablegen

    /// <summary>
    /// Das eigene Schlüsselmaterial - aus dem Speicher, oder frisch erzeugt
    /// und abgelegt.
    /// </summary>
    /// <remarks>
    /// Beides in einem Aufruf, und das ist Absicht: Wer erst lädt und dann bei
    /// null selbst erzeugt, vergisst früher oder später das Ablegen - und
    /// merkt es nicht, weil beim nächsten Start wieder etwas erzeugt wird. Der
    /// Fehler sähe aus wie ein neuer Client, und das ist genau der Fall, den
    /// dieser Speicher verhindern soll.
    /// </remarks>
    public static OmemoIdentity LoadOrCreateIdentity(this IOmemoStore store)
    {

        if (store.LoadIdentity() is OmemoIdentityState abgelegt)
            return OmemoIdentity.Import(abgelegt);

        var neu = OmemoIdentity.Create();

        store.SaveIdentity(neu.Export());

        return neu;

    }

    #endregion

    #region RecordIdentity(store, bareJid, deviceId, identityKey)

    /// <summary>
    /// Vermerkt den IdentityKey eines fremden Geräts und meldet, ob er neu,
    /// bekannt oder <b>ein anderer als beim letzten Mal</b> ist.
    /// </summary>
    /// <remarks>
    /// <b>Ein geänderter Schlüssel wird nie stillschweigend übernommen.</b>
    /// Dafür gibt es genau zwei Erklärungen: Der Mensch hat sein Gerät neu
    /// aufgesetzt - oder jemand schiebt sich dazwischen. Von aussen sind die
    /// beiden nicht zu unterscheiden, und deshalb ist es keine Entscheidung,
    /// die ein Programm treffen kann.
    ///
    /// Der alte Vermerk bleibt in diesem Fall stehen, samt seiner
    /// Vertrauensentscheidung. Wer ihn überschriebe, machte aus einer
    /// bestätigten Identität eine unbestätigte, ohne dass es jemandem
    /// auffiele - und die Warnung wäre nach dem ersten Ansehen fort.
    /// </remarks>
    public static OmemoIdentityCheck RecordIdentity(this IOmemoStore  store,
                                                    String            bareJid,
                                                    UInt32            deviceId,
                                                    Byte[]            identityKey)
    {

        var bekannt = store.KnownDevices()
                           .FirstOrDefault(d => d.DeviceId == deviceId &&
                                                String.Equals(d.BareJid, bareJid,
                                                              StringComparison.OrdinalIgnoreCase));

        if (bekannt is null)
        {

            store.SaveDevice(new OmemoDeviceRecord(bareJid,
                                                   deviceId,
                                                   identityKey,
                                                   OmemoTrust.Undecided,
                                                   DateTimeOffset.UtcNow));

            return OmemoIdentityCheck.New;

        }

        return bekannt.IdentityKey.SequenceEqual(identityKey)
                   ? OmemoIdentityCheck.Known
                   : OmemoIdentityCheck.Changed;

    }

    #endregion

    #region TrustOf(store, bareJid, deviceId) / SetTrust(...)

    /// <summary>
    /// Wie dieses Gerät zu einem fremden steht - unentschieden, wenn es
    /// unbekannt ist.
    /// </summary>
    public static OmemoTrust TrustOf(this IOmemoStore store, String bareJid, UInt32 deviceId)
        => store.KnownDevices()
                .FirstOrDefault(d => d.DeviceId == deviceId &&
                                     String.Equals(d.BareJid, bareJid, StringComparison.OrdinalIgnoreCase))
               ?.Trust
           ?? OmemoTrust.Undecided;

    /// <summary>
    /// Entscheidet über ein Gerät.
    /// </summary>
    /// <returns>false, wenn das Gerät unbekannt ist - dann gibt es nichts zu entscheiden.</returns>
    /// <remarks>
    /// Über ein unbekanntes Gerät lässt sich nicht entscheiden, und das ist
    /// keine Förmlichkeit: Eine Vertrauensentscheidung gilt einem
    /// <i>Schlüssel</i>, nicht einer Nummer. Wer sie im Voraus für eine
    /// Gerätekennung träfe, hätte sie für den ersten Schlüssel getroffen, der
    /// unter dieser Nummer auftaucht - und das kann jeder sein.
    /// </remarks>
    public static Boolean SetTrust(this IOmemoStore  store,
                                   String            bareJid,
                                   UInt32            deviceId,
                                   OmemoTrust        trust)
    {

        var bekannt = store.KnownDevices()
                           .FirstOrDefault(d => d.DeviceId == deviceId &&
                                                String.Equals(d.BareJid, bareJid,
                                                              StringComparison.OrdinalIgnoreCase));

        if (bekannt is null)
            return false;

        store.SaveDevice(bekannt with { Trust = trust });

        return true;

    }

    #endregion

}
