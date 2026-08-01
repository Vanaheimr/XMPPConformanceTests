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

using System.Xml.Linq;

using Microsoft.Extensions.Logging;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Was beim Verschlüsseln für ein einzelnes Gerät herauskam.
/// </summary>
/// <param name="Jid">Wem es gehört.</param>
/// <param name="DeviceId">Welches Gerät.</param>
/// <param name="Reason">Warum es übersprungen wurde.</param>
public sealed record OmemoSkippedDevice(String Jid, UInt32 DeviceId, String Reason);

/// <summary>
/// Eine entschlüsselte Nachricht.
/// </summary>
/// <param name="Content">Der Inhalt der SCE-Hülle.</param>
/// <param name="SenderDeviceId">Von welchem Gerät sie kam.</param>
/// <param name="Trust">Wie dieses Gerät eingestuft ist.</param>
/// <param name="IdentityCheck">Ob sein Schlüssel neu, bekannt oder ein anderer ist.</param>
/// <param name="EnvelopeFrom">
/// Der Absender <b>aus der verschlüsselten Hülle</b> - nicht der aus der
/// Stanza.
/// </param>
/// <remarks>
/// Die beiden Absender getrennt zu führen ist der Sinn der Beigabe aus
/// XEP-0420: Der äussere lässt sich von jedem ändern, der innere nicht. Sie
/// werden beim Entschlüsseln abgeglichen; hier steht der innere, damit ein
/// Aufrufer die Prüfung auch sehen kann und nicht nur darauf vertrauen muss.
/// </remarks>
public sealed record OmemoDecrypted(IReadOnlyList<XElement>  Content,
                                    UInt32                   SenderDeviceId,
                                    OmemoTrust               Trust,
                                    OmemoIdentityCheck       IdentityCheck,
                                    String?                  EnvelopeFrom);

/// <summary>
/// Führt zusammen, was die Etappen davor gebaut haben: Schlüsselmaterial,
/// X3DH, Ratschen, Drahtformat, PEP und Speicher.
/// </summary>
/// <remarks>
/// <b>Die schwierigste Frage hier ist nicht das Verschlüsseln, sondern was bei
/// einem Gerät geschieht, für das es nicht klappt.</b> Ein Kontakt hat vier
/// Geräte, eines davon hat kein abrufbares Bundle. Drei Antworten sind
/// möglich, und nur eine ist brauchbar:
///
/// <list type="bullet">
/// <item><b>Gar nicht senden.</b> Dann macht ein einziges kaputtes Gerät den
///       Menschen unerreichbar - und er erfährt nie, warum ihm niemand mehr
///       schreibt.</item>
/// <item><b>Unverschlüsselt senden.</b> Das ist die schlimmste: Der Absender
///       glaubt, verschlüsselt zu haben. Ein Angreifer, der ein Bundle
///       unerreichbar macht, bekommt damit den Klartext.</item>
/// <item><b>Verschlüsselt an alle übrigen, und sagen, wer fehlt.</b> Das tut
///       diese Klasse - <see cref="OmemoEncryptionResult.Skipped"/> nennt
///       jedes übersprungene Gerät samt Grund.</item>
/// </list>
///
/// <b>Die eigenen weiteren Geräte gehören dazu</b>, sonst sieht der eigene
/// Rechner nicht, was das eigene Telefon geschrieben hat. Das eigene <i>Gerät
/// selbst</i> nicht: Es müsste eine Sitzung mit sich selbst führen.
/// </remarks>
public sealed class OmemoManager
{

    #region Data

    private readonly IOmemoStore                                _store;
    private readonly String                                     _ownBareJid;
    private readonly Func<String, Task<OmemoDeviceList?>>       _fetchDeviceList;
    private readonly Func<String, UInt32, Task<OmemoBundle?>>   _fetchBundle;
    private readonly ILogger?                                   _logger;
    private readonly Lock                                       _lock = new();

    #endregion

    #region Properties

    /// <summary>Das eigene Schlüsselmaterial.</summary>
    public OmemoIdentity Identity { get; }

    /// <summary>Der eigene Fingerabdruck.</summary>
    public String Fingerprint => Identity.Fingerprint;

    /// <summary>
    /// Wird an ein Gerät geschrieben, über das noch niemand entschieden hat?
    /// </summary>
    /// <remarks>
    /// <b>Blind Trust Before Verification.</b> Auf true - der Vorgabe - geht
    /// eine Nachricht auch an unbestätigte Geräte; auf false nur an
    /// ausdrücklich bestätigte.
    ///
    /// Die Vorgabe ist eine Abwägung und keine Bequemlichkeit: Ein Verfahren,
    /// das vor der ersten Nachricht einen Fingerabdruckvergleich verlangt,
    /// wird nicht benutzt - und unbenutzte Verschlüsselung schützt niemanden.
    /// Wer einmal verglichen hat, merkt jeden späteren Wechsel; das ist der
    /// Gewinn, und er bleibt auch bei blindem Anfangen erhalten.
    /// </remarks>
    public Boolean TrustNewDevicesBlindly { get; set; } = true;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Baut den Verwalter auf einem Speicher auf - und legt frisches
    /// Schlüsselmaterial an, wenn es noch keines gibt.
    /// </summary>
    public OmemoManager(IOmemoStore                               store,
                        String                                    ownBareJid,
                        Func<String, Task<OmemoDeviceList?>>      fetchDeviceList,
                        Func<String, UInt32, Task<OmemoBundle?>>  fetchBundle,
                        ILogger?                                  logger = null)
    {

        _store            = store;
        _ownBareJid       = ownBareJid;
        _fetchDeviceList  = fetchDeviceList;
        _fetchBundle      = fetchBundle;
        _logger           = logger;

        Identity          = store.LoadOrCreateIdentity();

    }

    #endregion

    #region EncryptAsync(recipients, content)

    /// <summary>
    /// Verschlüsselt einen Inhalt für alle Geräte der Empfänger und die
    /// eigenen weiteren.
    /// </summary>
    public async Task<OmemoEncryptionResult> EncryptAsync(IEnumerable<String>      recipients,
                                                          IReadOnlyList<XElement>  content)
    {

        // Die Hülle nach XEP-0420 - mit dem eigenen Absender darin, damit sich
        // die Nachricht nicht unter fremdem Namen weiterreichen lässt.
        var huelle = new SceEnvelope(content,
                                     From: _ownBareJid,
                                     Time: DateTimeOffset.UtcNow).ToXml();

        var nutzlast = OmemoPayloadCipher.Encrypt(
                           System.Text.Encoding.UTF8.GetBytes(huelle.ToString(SaveOptions.DisableFormatting)));

        var schluessel  = new Dictionary<String, IReadOnlyList<OmemoKey>>(StringComparer.OrdinalIgnoreCase);
        var uebersprungen = new List<OmemoSkippedDevice>();

        // Die eigenen weiteren Geräte gehören dazu - sonst sieht der eigene
        // Rechner nicht, was das eigene Telefon geschrieben hat.
        foreach (var jid in recipients.Append(_ownBareJid)
                                      .Select(JidUtilities.Bare)
                                      .Distinct(StringComparer.OrdinalIgnoreCase))
        {

            var liste = await _fetchDeviceList(jid);

            if (liste is null)
            {
                uebersprungen.Add(new OmemoSkippedDevice(jid, 0, "keine Geräteliste"));
                continue;
            }

            var fuerDiesenJid = new List<OmemoKey>();

            foreach (var geraet in liste.Devices)
            {

                // Das eigene Gerät müsste eine Sitzung mit sich selbst führen.
                if (geraet.Id == Identity.DeviceId &&
                    String.Equals(jid, _ownBareJid, StringComparison.OrdinalIgnoreCase))
                    continue;

                var (eintrag, grund) = await VerschluesselnFuerAsync(jid, geraet.Id, nutzlast.KeyAndHmac);

                if (eintrag is not null)
                    fuerDiesenJid.Add(eintrag);
                else
                    uebersprungen.Add(new OmemoSkippedDevice(jid, geraet.Id, grund!));

            }

            if (fuerDiesenJid.Count > 0)
                schluessel[jid] = fuerDiesenJid;

        }

        return new OmemoEncryptionResult(
                   new OmemoEncryptedElement(Identity.DeviceId, schluessel, nutzlast.Ciphertext),
                   uebersprungen);

    }

    /// <summary>
    /// Verschlüsselt die 48 Byte für ein einzelnes Gerät - und baut die
    /// Sitzung auf, wenn es noch keine gibt.
    /// </summary>
    private async Task<(OmemoKey? Key, String? Reason)> VerschluesselnFuerAsync(String  jid,
                                                                                UInt32  deviceId,
                                                                                Byte[]  keyAndHmac)
    {

        var vertrauen = _store.TrustOf(jid, deviceId);

        if (vertrauen == OmemoTrust.Distrusted)
            return (null, "ausdrücklich abgelehnt");

        if (vertrauen == OmemoTrust.Undecided && !TrustNewDevicesBlindly)
            return (null, "nicht bestätigt");

        var abgelegt = _store.LoadSession(jid, deviceId);

        // Eine bestehende Sitzung.
        if (abgelegt is not null)
        {

            var ratchet   = DoubleRatchet.Import(abgelegt.Ratchet);
            var nachricht = ratchet.Encrypt(keyAndHmac, abgelegt.AssociatedData);

            _store.SaveSession(jid, deviceId,
                               new OmemoSessionState(ratchet.Export(), abgelegt.AssociatedData));

            return (new OmemoKey(deviceId, OmemoWireFormat.Encode(nachricht), false), null);

        }

        // Keine Sitzung - also eine beginnen.
        var bundle = await _fetchBundle(jid, deviceId);

        if (bundle is null)
            return (null, "kein abrufbares Bundle");

        // Der IdentityKey aus dem Bundle wird vermerkt, bevor irgendetwas
        // damit gerechnet wird: Ein Wechsel gehört gemeldet, nicht
        // stillschweigend benutzt.
        var pruefung = _store.RecordIdentity(jid, deviceId, bundle.IdentityKey);

        if (pruefung == OmemoIdentityCheck.Changed)
            return (null, "der IdentityKey hat sich geändert");

        if (!TrustNewDevicesBlindly && _store.TrustOf(jid, deviceId) != OmemoTrust.Trusted)
            return (null, "nicht bestätigt");

        var x3dh    = X3DH.Initiate(Identity, bundle);
        var neu     = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
        var inhalt  = neu.Encrypt(keyAndHmac, x3dh.AssociatedData);

        _store.SaveSession(jid, deviceId, new OmemoSessionState(neu.Export(), x3dh.AssociatedData));

        var austausch = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                             bundle.SignedPreKeyId,
                                             Identity.PublicIdentityKey,
                                             x3dh.EphemeralKey!,
                                             OmemoWireFormat.Encode(inhalt));

        return (new OmemoKey(deviceId, austausch.Encode(), true), null);

    }

    #endregion

    #region DecryptAsync(element, senderBareJid)

    /// <summary>
    /// Entschlüsselt eine Nachricht, die an dieses Gerät gerichtet ist.
    /// </summary>
    /// <returns>
    /// null, wenn nichts für dieses Gerät dabei war oder es sich nicht lesen
    /// lässt.
    /// </returns>
    /// <remarks>
    /// <b>Ein Fehlschlag wirft nicht, sondern ergibt null.</b> Eine
    /// unlesbare Nachricht ist für den Empfänger dasselbe wie keine, und ein
    /// Absturz liesse sich von jedem auslösen, der Unsinn schickt. Der Grund
    /// steht im Protokoll - dort, wo jemand nachsieht, der sucht.
    /// </remarks>
    public async Task<OmemoDecrypted?> DecryptAsync(OmemoEncryptedElement  element,
                                                    String                 senderBareJid)
    {

        var jid     = JidUtilities.Bare(senderBareJid);
        var eintrag = element.KeyFor(_ownBareJid, Identity.DeviceId);

        if (eintrag is null)
        {
            _logger?.LogDebug("OMEMO: Die Nachricht von {Jid} war nicht für dieses Gerät bestimmt", jid);
            return null;
        }

        if (element.Payload is null)
        {
            // Eine Nachricht ohne Nutzlast baut nur die Sitzung auf. Sie wird
            // trotzdem verarbeitet - genau dafür gibt es sie.
            _ = await SitzungAufbauenAsync(jid, element.SenderDeviceId, eintrag);
            return null;
        }

        try
        {

            var (klartext, pruefung) = await EntschluesselnAsync(jid, element.SenderDeviceId, eintrag);

            if (klartext is null)
                return null;

            var roh = OmemoPayloadCipher.Decrypt(element.Payload, klartext);

            if (!SceEnvelope.TryRead(XElement.Parse(System.Text.Encoding.UTF8.GetString(roh)),
                                     out var huelle,
                                     senderBareJid))
            {
                _logger?.LogWarning("OMEMO: Die Hülle von {Jid} nennt einen anderen Absender", jid);
                return null;
            }

            return new OmemoDecrypted(huelle!.Content,
                                      element.SenderDeviceId,
                                      _store.TrustOf(jid, element.SenderDeviceId),
                                      pruefung,
                                      huelle.From);

        }
        catch (Exception e)
        {
            _logger?.LogWarning("OMEMO: Die Nachricht von {Jid}/{Device} liess sich nicht lesen: {Reason}",
                                jid, element.SenderDeviceId, e.Message);
            return null;
        }

    }

    /// <summary>
    /// Holt die 48 Byte aus dem Eintrag - über eine bestehende Sitzung oder
    /// über einen Schlüsselaustausch.
    /// </summary>
    private async Task<(Byte[]? KeyAndHmac, OmemoIdentityCheck Check)> EntschluesselnAsync(
        String jid, UInt32 deviceId, OmemoKey eintrag)
    {

        if (eintrag.IsKeyExchange)
            return await SitzungAufbauenAsync(jid, deviceId, eintrag);

        var abgelegt = _store.LoadSession(jid, deviceId);

        if (abgelegt is null)
        {
            _logger?.LogWarning("OMEMO: Keine Sitzung mit {Jid}/{Device}, und die Nachricht bringt " +
                                "keinen Schlüsselaustausch mit", jid, deviceId);
            return (null, OmemoIdentityCheck.New);
        }

        var ratchet   = DoubleRatchet.Import(abgelegt.Ratchet);
        var klartext  = ratchet.Decrypt(OmemoWireFormat.Decode(eintrag.Data), abgelegt.AssociatedData);

        _store.SaveSession(jid, deviceId, new OmemoSessionState(ratchet.Export(), abgelegt.AssociatedData));

        return (klartext, OmemoIdentityCheck.Known);

    }

    /// <summary>
    /// Nimmt einen Schlüsselaustausch an und legt die Sitzung an.
    /// </summary>
    private async Task<(Byte[]? KeyAndHmac, OmemoIdentityCheck Check)> SitzungAufbauenAsync(
        String jid, UInt32 deviceId, OmemoKey eintrag)
    {

        await Task.CompletedTask;

        var austausch = OmemoKeyExchange.Decode(eintrag.Data);

        // Erst den IdentityKey vermerken, dann rechnen. Ein Wechsel wird
        // gemeldet und die Nachricht nicht angenommen - von aussen ist ein neu
        // aufgesetztes Gerät nicht von einem Angreifer zu unterscheiden, und
        // das ist keine Entscheidung, die ein Programm treffen kann.
        var pruefung = _store.RecordIdentity(jid, deviceId, austausch.IdentityKey);

        if (pruefung == OmemoIdentityCheck.Changed)
        {
            _logger?.LogWarning("OMEMO: {Jid}/{Device} meldet sich mit einem anderen IdentityKey",
                                jid, deviceId);
            return (null, pruefung);
        }

        var x3dh = X3DH.Accept(Identity,
                               austausch.IdentityKey,
                               austausch.EphemeralKey,
                               austausch.SignedPreKeyId,
                               austausch.PreKeyId == 0 ? null : austausch.PreKeyId);

        var ratchet   = DoubleRatchet.InitiateAsReceiver(x3dh.SharedSecret, Identity.SignedPreKey);
        var klartext  = ratchet.Decrypt(OmemoWireFormat.Decode(austausch.Message), x3dh.AssociatedData);

        lock (_lock)
        {

            _store.SaveSession(jid, deviceId, new OmemoSessionState(ratchet.Export(), x3dh.AssociatedData));

            // Der verbrauchte PreKey ist fort - das gehört sofort abgelegt,
            // sonst wäre er nach einem Neustart wieder da und die Nachricht
            // ein zweites Mal annehmbar.
            _store.SaveIdentity(Identity.Export());

        }

        return (klartext, pruefung);

    }

    #endregion

    #region Fingerabdrücke und Vertrauen

    /// <summary>Alle bekannten Geräte samt Fingerabdruck und Einstufung.</summary>
    public IReadOnlyList<OmemoDeviceRecord> KnownDevices()
        => _store.KnownDevices();

    /// <summary>Entscheidet über ein Gerät.</summary>
    public Boolean SetTrust(String bareJid, UInt32 deviceId, OmemoTrust trust)
        => _store.SetTrust(JidUtilities.Bare(bareJid), deviceId, trust);

    #endregion

}

/// <summary>
/// Das Ergebnis des Verschlüsselns: die Stanza und wer nicht mitlesen kann.
/// </summary>
/// <param name="Element">Das <c>&lt;encrypted/&gt;</c>-Element.</param>
/// <param name="Skipped">
/// Die übersprungenen Geräte samt Grund - <b>leer heisst: alle sind dabei</b>.
/// </param>
/// <remarks>
/// Die Liste ist der Grund, warum diese Methode kein blosses
/// <c>XElement</c> zurückgibt. Ein Absender, der nicht erfährt, dass drei von
/// vier Geräten seines Gegenübers nicht mitlesen, hält sein Gespräch für
/// geführt - und wundert sich über die ausbleibende Antwort.
/// </remarks>
public sealed record OmemoEncryptionResult(OmemoEncryptedElement                Element,
                                           IReadOnlyList<OmemoSkippedDevice>    Skipped);
