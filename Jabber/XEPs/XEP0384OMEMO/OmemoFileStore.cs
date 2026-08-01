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

using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Ein OMEMO-Speicher im Arbeitsspeicher - für Tests und für Clients, die
/// nichts behalten wollen.
/// </summary>
/// <remarks>
/// <b>Er behält nichts über das Programmende hinaus, und das ist keine
/// Sparfassung, sondern eine Aussage:</b> Wer ihn benutzt, hat bei jedem Start
/// einen neuen Fingerabdruck. Für einen Test ist das richtig, für einen
/// Menschen wäre es die Zusicherung, dass jeder Vergleich wertlos ist.
/// </remarks>
public sealed class OmemoMemoryStore : IOmemoStore
{

    private readonly Dictionary<String, OmemoSessionState>  _sessions  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<String, OmemoDeviceRecord>  _devices   = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    private OmemoIdentityState? _identity;

    private static String Key(String bareJid, UInt32 deviceId)
        => $"{bareJid.ToLowerInvariant()}/{deviceId}";

    public OmemoIdentityState? LoadIdentity()
    {
        lock (_lock) return _identity;
    }

    public void SaveIdentity(OmemoIdentityState state)
    {
        lock (_lock) _identity = state;
    }

    public OmemoSessionState? LoadSession(String bareJid, UInt32 deviceId)
    {
        lock (_lock) return _sessions.GetValueOrDefault(Key(bareJid, deviceId));
    }

    public void SaveSession(String bareJid, UInt32 deviceId, OmemoSessionState state)
    {
        lock (_lock) _sessions[Key(bareJid, deviceId)] = state;
    }

    public IReadOnlyList<OmemoDeviceRecord> KnownDevices()
    {
        lock (_lock) return [.. _devices.Values];
    }

    public void SaveDevice(OmemoDeviceRecord record)
    {
        lock (_lock) _devices[Key(record.BareJid, record.DeviceId)] = record;
    }

}

/// <summary>
/// Ein OMEMO-Speicher in einer JSON-Datei.
/// </summary>
/// <remarks>
/// Eine Datei, bei jeder Änderung vollständig neu geschrieben - dasselbe
/// Verfahren wie beim <see cref="Server.FileAccountStore"/> und aus demselben
/// Grund ausreichend: Es geht um ein Gerät und seine Gesprächspartner, nicht
/// um einen Server.
///
/// Geschrieben wird über eine Nebendatei, die anschliessend an ihren Platz
/// verschoben wird. Bricht der Vorgang ab, steht die alte Fassung noch
/// vollständig da. Das ist hier <b>wichtiger als beim Kontenspeicher</b>: Eine
/// halb geschriebene Sitzungsdatei kostet nicht einen Anmeldeversuch, sondern
/// jede laufende Sitzung - und damit die Lesbarkeit alles Unterwegs.
///
/// <b>Die Datei ist nicht verschlüsselt.</b> Sie enthält den geheimen
/// IdentityKey, alle PreKeys und jeden Kettenschlüssel; wer sie liest, liest
/// die Gespräche mit. Eine Verschlüsselung mit einem Schlüssel, der daneben
/// läge, wäre keine - und einen, den ein Mensch eingibt, gibt es in dieser
/// Anwendung nicht. Deshalb steht es hier ausdrücklich, statt durch ein
/// beruhigendes Verfahren ersetzt zu werden: <b>Die Datei gehört an einen Ort,
/// an den nur dieser Benutzer kommt.</b>
/// </remarks>
public sealed class OmemoFileStore : IOmemoStore
{

    #region Data

    private readonly String _path;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions _options = new() {
        WriteIndented           = true,
        DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull
    };

    private Inhalt _inhalt = new();

    /// <summary>Die Gestalt der Datei.</summary>
    private sealed class Inhalt
    {
        public OmemoIdentityState?        Identity  { get; set; }
        public List<SitzungsEintrag>      Sessions  { get; set; } = [];
        public List<OmemoDeviceRecord>    Devices   { get; set; } = [];
    }

    private sealed class SitzungsEintrag
    {
        public String        BareJid   { get; set; } = "";
        public UInt32        DeviceId  { get; set; }
        public OmemoSessionState? State     { get; set; }
    }

    #endregion

    #region Properties

    /// <summary>Die Datei, in der der Speicher liegt.</summary>
    public String Path => _path;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Legt einen Speicher an der angegebenen Datei an und liest sie, wenn es
    /// sie schon gibt.
    /// </summary>
    /// <remarks>
    /// Eine unlesbare Datei wirft, statt mit einem leeren Speicher
    /// weiterzumachen. <b>Der bequeme Weg wäre hier der gefährliche:</b> Ein
    /// Client, der nach einem Lesefehler mit neuen Schlüsseln startet, hat
    /// seinen Fingerabdruck gewechselt, ohne dass jemand gefragt wurde - und
    /// die alte Datei wäre beim ersten Ablegen überschrieben.
    /// </remarks>
    public OmemoFileStore(String path)
    {

        _path = System.IO.Path.GetFullPath(path);

        if (File.Exists(_path))
            _inhalt = JsonSerializer.Deserialize<Inhalt>(File.ReadAllText(_path), _options)
                          ?? throw new InvalidDataException(
                                 $"Der OMEMO-Speicher {_path} ist leer oder unlesbar. Er wird nicht " +
                                 "durch einen frischen ersetzt - das wäre ein stiller Wechsel des " +
                                 "eigenen Fingerabdrucks.");

    }

    #endregion

    #region IOmemoStore

    public OmemoIdentityState? LoadIdentity()
    {
        lock (_lock) return _inhalt.Identity;
    }

    public void SaveIdentity(OmemoIdentityState state)
    {

        lock (_lock)
        {
            _inhalt.Identity = state;
            Schreiben();
        }

    }

    public OmemoSessionState? LoadSession(String bareJid, UInt32 deviceId)
    {

        lock (_lock)
            return _inhalt.Sessions
                          .FirstOrDefault(s => s.DeviceId == deviceId &&
                                               String.Equals(s.BareJid, bareJid,
                                                             StringComparison.OrdinalIgnoreCase))
                         ?.State;

    }

    public void SaveSession(String bareJid, UInt32 deviceId, OmemoSessionState state)
    {

        lock (_lock)
        {

            _inhalt.Sessions.RemoveAll(s => s.DeviceId == deviceId &&
                                            String.Equals(s.BareJid, bareJid,
                                                          StringComparison.OrdinalIgnoreCase));

            _inhalt.Sessions.Add(new SitzungsEintrag {
                                     BareJid   = bareJid,
                                     DeviceId  = deviceId,
                                     State     = state
                                 });

            Schreiben();

        }

    }

    public IReadOnlyList<OmemoDeviceRecord> KnownDevices()
    {
        lock (_lock) return [.. _inhalt.Devices];
    }

    public void SaveDevice(OmemoDeviceRecord record)
    {

        lock (_lock)
        {

            _inhalt.Devices.RemoveAll(d => d.DeviceId == record.DeviceId &&
                                           String.Equals(d.BareJid, record.BareJid,
                                                         StringComparison.OrdinalIgnoreCase));

            _inhalt.Devices.Add(record);

            Schreiben();

        }

    }

    #endregion

    #region Schreiben()

    /// <summary>
    /// Schreibt über eine Nebendatei und verschiebt sie an ihren Platz.
    /// </summary>
    private void Schreiben()
    {

        var verzeichnis = System.IO.Path.GetDirectoryName(_path);

        if (!String.IsNullOrEmpty(verzeichnis))
            Directory.CreateDirectory(verzeichnis);

        var neben = _path + ".neu";

        File.WriteAllText(neben, JsonSerializer.Serialize(_inhalt, _options));
        File.Move(neben, _path, overwrite: true);

    }

    #endregion

}
