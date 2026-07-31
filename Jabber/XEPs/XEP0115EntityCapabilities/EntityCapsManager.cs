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
    /// <remarks>
    /// Die eigenen Datenformulare gehen mit ein - sie stehen ja auch in der
    /// eigenen disco#info-Antwort. Blieben sie hier aussen vor, kündigte
    /// dieser Client einen Hash an, den seine eigene Antwort nicht ergibt, und
    /// jede Gegenstelle, die nach XEP-0115, Abschnitt 5.4 nachrechnet, hielte
    /// ihn für einen Fälscher.
    /// </remarks>
    public string CalculateVerificationString()
        => VerificationString(_disco.LocalIdentities, _disco.LocalFeatures, _disco.LocalForms);

    /// <summary>
    /// Der Verification String nach XEP-0115, Abschnitt 5.1, über beliebige
    /// Angaben.
    /// </summary>
    /// <remarks>
    /// Die Rechnung war bis dahin nur auf die eigenen Angaben anwendbar - und
    /// damit war der Hash ein Wert, den dieser Client zwar erzeugt, aber nie
    /// nachprüft. Genau das Nachprüfen ist der Zweck des Verfahrens: Der
    /// <c>ver</c>-Wert ist keine Kennung, die eine Entity sich aussucht,
    /// sondern der Hash über das, was sie auf disco#info antwortet.
    /// </remarks>
    public static string VerificationString(IEnumerable<DiscoIdentity>  Identities,
                                            IEnumerable<string>         Features,
                                            IEnumerable<DiscoForm>?     Forms   = null)
    {

        var sb = new StringBuilder();

        // Identitäten als category/type/xml:lang/name - jeder Schrägstrich steht
        // auch ohne Wert da (XEP-0115, Abschnitt 5.1). Sortiert wird über genau
        // die Zeichenkette, die auch ausgegeben wird: Weil '/' (0x2F) unter allen
        // Zeichen liegt, die in Kategorie, Typ und Sprache vorkommen, fällt das
        // mit der im XEP verlangten Sortierung über die vier Felder zusammen.
        foreach (var identity in Identities
                                     .Select(id => $"{id.Category}/{id.Type}/{id.Language ?? ""}/{id.Name ?? ""}")
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

        // XEP-0128-Datenformulare, sortiert nach ihrem FORM_TYPE. Formulare
        // ohne gültiges FORM_TYPE bleiben aussen vor - XEP-0115, Abschnitt 5.4
        // sagt ausdrücklich "ignore the form but continue processing", und das
        // ist der Unterschied ums Ganze: Sie machen die Antwort nicht ungültig,
        // sie zählen nur nicht mit.
        foreach (var form in (Forms ?? [])
                                 .Where  (f => f.FormType is not null)
                                 .OrderBy(f => f.FormType, StringComparer.Ordinal))
        {

            sb.Append(form.FormType).Append('<');

            foreach (var field in form.Fields
                                      .Where  (f => !f.IsFormType)
                                      .OrderBy(f => f.Var, StringComparer.Ordinal))
            {

                sb.Append(field.Var).Append('<');

                foreach (var value in field.Values.Order(StringComparer.Ordinal))
                    sb.Append(value).Append('<');

            }

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
    /// Bezeichnet dieser disco-Node diese Entity in ihrem <b>heutigen</b>
    /// Stand?
    /// </summary>
    /// <remarks>
    /// Zwei Formen zählen. <c>node#ver</c> ist die aus XEP-0115,
    /// Abschnitt 6.2: Wer unser <c>&lt;c/&gt;</c> in einer Presence gesehen
    /// hat, fragt genau so. Der blanke Node ohne <c>#ver</c> zählt ebenfalls -
    /// dort steht "SHOULD", nicht "MUST", und wer nur den Node nennt, fragt
    /// nach dieser Entity, ohne einen Stand festzunageln.
    ///
    /// Ein <b>anderes</b> <c>ver</c> zählt nicht, auch nicht ein früher einmal
    /// eigenes. Es fragt nach der Merkmalsliste von damals, und die gibt es
    /// hier nicht mehr. Wer darauf die heutige schickt, beantwortet eine andere
    /// Frage als die gestellte: Der Frager rechnet nach Abschnitt 5.4 den
    /// angekündigten Hash gegen die Antwort und bekommt einen anderen heraus.
    /// </remarks>
    public bool IsOwnNode(string node)

        => node == Node ||
           node == $"{Node}#{CalculateVerificationString()}";

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

        if (IllFormed(Info) is string mangel)
            return mangel;

        var errechnet = VerificationString(Info.Identities, Info.Features, Info.Forms);

        if (!String.Equals(errechnet, Ver, StringComparison.Ordinal))
            return $"Der Hash der Antwort ist {errechnet}, angekündigt war {Ver}.";

        return null;

    }

    /// <summary>
    /// Die Antwort ist in sich mehrdeutig (XEP-0115, Abschnitt 5.4) - oder
    /// null, wenn sie es nicht ist.
    /// </summary>
    /// <remarks>
    /// Diese drei Regeln sind keine Formstrenge. Der Verification String
    /// entsteht dadurch, dass eine Antwort in genau eine Zeichenkette
    /// überführt wird; wo Doppelungen stehen, gibt es mehr als eine solche
    /// Zeichenkette, und damit lässt sich zu einem gegebenen Hash eine zweite
    /// Antwort bauen. Das XEP verlangt deshalb, die ganze Antwort zu verwerfen,
    /// statt sich für eine Lesart zu entscheiden.
    /// </remarks>
    private static string? IllFormed(DiscoInfo Info)
    {

        if (Info.Identities.Count != Info.Identities.Distinct().Count())
            return "Die Antwort führt dieselbe Identität mehrfach auf.";

        if (Info.Features.Count != Info.Features.Distinct(StringComparer.Ordinal).Count())
            return "Die Antwort führt dasselbe Feature mehrfach auf.";

        // Ein FORM_TYPE mit mehreren verschiedenen Werten - welcher davon soll
        // das Formular einsortieren?
        foreach (var form in Info.Forms)
        {

            var werte = form.FormTypeField?.Values.Distinct(StringComparer.Ordinal).ToList();

            if (werte is not null && werte.Count > 1)
                return $"Ein Datenformular trägt {werte.Count} verschiedene FORM_TYPE-Werte.";

        }

        var typen = Info.Forms.Select(f => f.FormType)
                              .Where (t => t is not null)
                              .ToList();

        if (typen.Count != typen.Distinct(StringComparer.Ordinal).Count())
            return "Die Antwort enthält mehrere Datenformulare mit demselben FORM_TYPE.";

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
