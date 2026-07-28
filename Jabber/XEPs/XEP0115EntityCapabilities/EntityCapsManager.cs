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
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0115: Entity Capabilities - kürzt wiederholte disco#info-Abfragen
/// durch einen Hash der eigenen bzw. fremden Feature-Liste ab.
/// </summary>
public sealed class EntityCapsManager
{

    /// <summary>Der Namespace von XEP-0115.</summary>
    public const string Namespace = "http://jabber.org/protocol/caps";

    /// <summary>
    /// Der einzige Hash-Algorithmus, den dieser Client nachrechnen kann
    /// (XEP-0115, Abschnitt 5.1).
    /// </summary>
    public const string Sha1Algorithm = "sha-1";

    /// <summary>Der Namespace der Datenformulare (XEP-0004).</summary>
    private const string DataFormNamespace = "jabber:x:data";

    private readonly DiscoManager _disco;
    private readonly Dictionary<string, DiscoInfo> _cache = new();
    private readonly object _lock = new();

    public string Node { get; set; } = "https://github.com/xmpp-console";

    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    /// <summary>
    /// Eine disco#info-Antwort wurde nicht in den Cache übernommen, weil sie
    /// den angekündigten Verification String nicht belegt. Der zweite
    /// Parameter nennt den Grund.
    /// </summary>
    /// <remarks>
    /// Die Antwort selbst wird trotzdem über <see cref="OnCapsDiscovered"/>
    /// gemeldet: Sie ist das, was diese Entity über sich sagt, und genau das
    /// hätte auch eine gewöhnliche disco#info-Abfrage ergeben. Verweigert wird
    /// nur das Bündeln - sie unter <c>node#ver</c> abzulegen und damit
    /// jedem anderen zuzuschreiben, der dasselbe Paar ankündigt.
    /// </remarks>
    public event Action<string, string>? OnCapsRejected;

    public EntityCapsManager(DiscoManager disco)
    {
        _disco = disco;
    }

    /// <summary>
    /// Berechnet den Verification String (SHA-1 Hash der Features)
    /// </summary>
    public string CalculateVerificationString()
        => VerificationString(_disco.LocalIdentities, _disco.LocalFeatures);

    /// <summary>
    /// Der Verification String nach XEP-0115, Abschnitt 5.1, über beliebige
    /// Identitäten und Features.
    /// </summary>
    /// <remarks>
    /// Die Rechnung war bis dahin nur auf die eigenen Angaben anwendbar - und
    /// damit war der Hash ein Wert, den dieser Client zwar erzeugt, aber nie
    /// nachprüft. Genau das Nachprüfen ist der Zweck des Verfahrens: Der
    /// <c>ver</c>-Wert ist keine Kennung, die eine Entity sich aussucht,
    /// sondern der Hash über das, was sie auf disco#info antwortet.
    /// </remarks>
    public static string VerificationString(IEnumerable<DiscoIdentity>  Identities,
                                            IEnumerable<string>         Features)
    {

        var sb = new StringBuilder();

        // Identitäten als category/type/xml:lang/name - jeder Schrägstrich steht
        // auch ohne Wert da (XEP-0115, Abschnitt 5.1). Sortiert wird über genau
        // die Zeichenkette, die auch ausgegeben wird, damit name mitsortiert;
        // der xml:lang-Platz bleibt leer, weil DiscoIdentity kein xml:lang trägt.
        foreach (var identity in Identities
                                     .Select(id => $"{id.Category}/{id.Type}//{id.Name ?? ""}")
                                     .Order(StringComparer.Ordinal))
        {
            sb.Append(identity).Append('<');
        }

        // Features sortiert - XEP-0115, Abschnitt 5.1 verlangt Oktett-Reihenfolge,
        // nicht den kulturabhängigen Standardvergleich ('B' 0x42 vor 'a' 0x61).
        foreach (var feature in Features.Order(StringComparer.Ordinal))
        {
            sb.Append($"{feature}<");
        }

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash);

    }

    /// <summary>
    /// Erzeugt das <c>&lt;c/&gt;</c> Element für Presence
    /// </summary>
    public string GetCapsElement()
    {
        var ver = CalculateVerificationString();
        return $"<c xmlns='{Namespace}' hash='sha-1' node='{Node}' ver='{ver}'/>";
    }

    /// <summary>
    /// Verarbeitet ein caps-Element aus Presence.
    /// </summary>
    /// <remarks>
    /// XEP-0115, Abschnitt 5.4: Die Antwort wird erst dann unter
    /// <c>node#ver</c> abgelegt, wenn ihr Hash den angekündigten Wert
    /// tatsächlich ergibt.
    ///
    /// Ohne diese Prüfung war der Cache vergiftbar, und zwar von jedem, dessen
    /// Presence hier ankommt. Die Bewegung ist kurz: Der Angreifer kündigt in
    /// seiner Presence das <c>node#ver</c>-Paar eines verbreiteten Clients an,
    /// antwortet auf die folgende disco#info-Abfrage aber mit einer Liste
    /// seiner Wahl. Unter diesem Paar liegt fortan seine Liste - und
    /// ausgeliefert wird sie an jeden weiteren Kontakt, der dasselbe Paar
    /// ankündigt, ohne dass der je gefragt würde. Der Angreifer bestimmt damit,
    /// was dieser Client über Dritte glaubt: welche Verschlüsselung sie können,
    /// ob sie Empfangsbestätigungen verstehen, was sich ihnen schicken lässt.
    ///
    /// Der <c>ver</c>-Wert ist genau dagegen gebaut - er ist der Hash über die
    /// Antwort, nicht eine frei gewählte Kennung. Man muss ihn nur nachrechnen.
    /// </remarks>
    /// <param name="hash">
    /// Der Algorithmus aus dem <c>hash</c>-Attribut. Fehlt er oder ist er ein
    /// anderer als <c>sha-1</c>, ist der <c>ver</c>-Wert nicht nachrechenbar
    /// (bei der Altform aus XEP-0115 vor Version 1.4 ist er eine
    /// Versionsnummer) - abgefragt wird dann noch, abgelegt nicht mehr.
    /// </param>
    public async Task ProcessCapsAsync(string             from,
                                       string             node,
                                       string             ver,
                                       string?            hash   = null,
                                       CancellationToken  ct     = default)
    {
        var cacheKey = $"{node}#{ver}";

        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                OnCapsDiscovered?.Invoke(from, cached);
                return;
            }
        }

        // Noch nicht im Cache - disco#info abfragen
        var info = await _disco.QueryInfoAsync(from, cacheKey, ct: ct);

        if (info is null)
            return;

        if (VerificationFailure(info, ver, hash) is string grund)
            OnCapsRejected?.Invoke(from, grund);

        else
            lock (_lock)
            {
                _cache[cacheKey] = info;
            }

        OnCapsDiscovered?.Invoke(from, info);
    }

    /// <summary>
    /// Der Grund, aus dem diese Antwort den angekündigten Verification String
    /// nicht belegt - oder null, wenn sie ihn belegt.
    /// </summary>
    private static string? VerificationFailure(DiscoInfo Info, string Ver, string? Hash)
    {

        if (Hash is null)
            return "Das caps-Element trägt kein hash-Attribut (Altform vor XEP-0115 1.4); " +
                   "der ver-Wert ist damit kein Hash und nicht nachrechenbar.";

        if (Hash != Sha1Algorithm)
            return $"Unbekannter Hash-Algorithmus '{Hash}'; nachrechnen lässt sich nur {Sha1Algorithm}.";

        // XEP-0128-Datenformulare gehen nach XEP-0115, Abschnitt 5.1 in den
        // Verification String ein. Diese Rechnung kennt sie noch nicht, also
        // ergäbe sie für eine solche Antwort zwangsläufig einen anderen Wert.
        // Das ist ein Grund, sie nicht abzulegen - aber keiner, sie einer
        // Fälschung gleichzusetzen.
        if (Info.HasExtendedInfo)
            return "Die Antwort enthält ein Datenformular (XEP-0128); der Verification String " +
                   "darüber wird noch nicht berechnet.";

        var errechnet = VerificationString(Info.Identities, Info.Features);

        if (!String.Equals(errechnet, Ver, StringComparison.Ordinal))
            return $"Der Hash der Antwort ist {errechnet}, angekündigt war {Ver}.";

        return null;

    }

    /// <summary>
    /// Prüft ob ein JID ein Feature unterstützt (aus Cache)
    /// </summary>
    public DiscoInfo? GetCachedInfo(string verString)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(verString, out var info) ? info : null;
        }
    }

    /// <summary>
    /// Extrahiert Caps aus einer Presence.
    ///
    /// Gesucht wird unter den direkten Kindelementen im Caps-Namespace. Das
    /// frühere Muster fand ein <c>&lt;c/&gt;</c> irgendwo in der Stanza und
    /// verlangte ein unpräfigiertes Element.
    /// </summary>
    public static (string Node, string Ver, string? Hash)? ParseCaps(XElement presence)
    {

        var caps = presence.Elements()
                           .FirstOrDefault(child => child.Name.NamespaceName == Namespace &&
                                                    child.Name.LocalName     == "c");

        if (caps is null)
            return null;

        var node = caps.Attr("node");
        var ver  = caps.Attr("ver");

        if (node is null || ver is null)
            return null;

        return (node, ver, caps.Attr("hash"));

    }
}
