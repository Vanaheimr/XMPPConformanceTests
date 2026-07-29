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
/// XEP-0128: Ein Datenformular an einer disco#info-Antwort - die erweiterten
/// Angaben einer Entity über sich selbst.
/// </summary>
/// <remarks>
/// Gespeichert wird, was im Formular stand, ungefiltert und in der
/// vorgefundenen Reihenfolge. Welche Formulare gelten und wie sie sortiert
/// werden, entscheidet XEP-0115, Abschnitt 5.1 und 5.4 - und das steht dort,
/// wo diese Regeln hingehören, im <see cref="EntityCapsManager"/>. Ein Parser,
/// der schon aussortiert, nimmt der Prüfung die Grundlage.
/// </remarks>
/// <param name="Fields">Die Felder des Formulars, FORM_TYPE eingeschlossen.</param>
public sealed record DiscoForm(IReadOnlyList<DiscoField> Fields)
{

    /// <summary>
    /// Das FORM_TYPE-Feld, sofern eines da ist und den verlangten Typ
    /// <c>hidden</c> trägt (XEP-0115, Abschnitt 5.4).
    /// </summary>
    public DiscoField? FormTypeField

        // "field" ist ab C# 14 in einem Property-Accessor ein Schlüsselwort.
        => Fields.FirstOrDefault(feld => feld.IsFormType);

    /// <summary>
    /// Der Formulartyp, oder null, wenn das Formular keinen gültigen trägt -
    /// ein solches Formular geht nach XEP-0115, Abschnitt 5.4 nicht in den
    /// Verification String ein.
    /// </summary>
    public String? FormType

        => FormTypeField?.Values.FirstOrDefault();

}
