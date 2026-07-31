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
/// XEP-0030: Ergebnis einer disco#info-Abfrage (Identitäten + Features).
/// </summary>
public sealed class DiscoInfo
{
    public string From { get; init; } = "";
    public string? Node { get; init; }
    public List<DiscoIdentity> Identities { get; } = [];
    public List<string> Features { get; } = [];

    /// <summary>
    /// XEP-0128: Die Datenformulare der Antwort, ungefiltert und in der
    /// vorgefundenen Reihenfolge.
    /// </summary>
    /// <remarks>
    /// Sie gehören zur Antwort und nicht nur zur Zierde: XEP-0115,
    /// Abschnitt 5.1 lässt sie in den Verification String eingehen. Wer sie
    /// wegwirft, kann den Hash einer Entity, die welche führt, nicht
    /// nachrechnen - und muss ihr dann entweder blind glauben oder ihr
    /// grundlos misstrauen.
    /// </remarks>
    public List<DiscoForm> Forms { get; } = [];

    /// <summary>Trug die Antwort ein Datenformular (XEP-0128)?</summary>
    public bool HasExtendedInfo => Forms.Count > 0;

    /// <summary>Führt die Antwort dieses Merkmal auf?</summary>
    /// <remarks>
    /// Hier standen einmal fünf Abkürzungen daneben - <c>SupportsCarbons</c>,
    /// <c>SupportsReceipts</c> und drei weitere -, jede eine Zeile über dieser
    /// hier und jede mit einem eingebauten Namensraum. Aufgerufen hat sie
    /// niemand, und sie hätten auch nichts gekonnt, was diese Methode nicht
    /// kann: Der Namensraum steht ohnehin dort, wo die Erweiterung steht, und
    /// eine zweite Abschrift davon veraltet für sich allein.
    /// </remarks>
    public bool HasFeature(string feature) => Features.Contains(feature);
}
