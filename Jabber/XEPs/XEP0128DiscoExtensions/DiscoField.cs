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
/// XEP-0004: Ein Feld eines Datenformulars.
/// </summary>
/// <param name="Var">Der Name des Feldes (<c>var</c>).</param>
/// <param name="Type">
/// Der Feldtyp, oder null, wenn das Formular keinen angibt. Er wird gebraucht,
/// weil XEP-0115, Abschnitt 5.4 ein <c>FORM_TYPE</c> nur dann gelten lässt,
/// wenn es <c>hidden</c> ist.
/// </param>
/// <param name="Values">Die Werte des Feldes, in der Reihenfolge des Formulars.</param>
public sealed record DiscoField(String                 Var,
                                String?                Type,
                                IReadOnlyList<String>  Values)
{

    /// <summary>Der Name des Feldes, das den Formulartyp trägt.</summary>
    public const String FormTypeVar = "FORM_TYPE";

    /// <summary>Der Feldtyp, den XEP-0115 für <see cref="FormTypeVar"/> verlangt.</summary>
    public const String HiddenType  = "hidden";

    /// <summary>Ist das ein gültiges FORM_TYPE-Feld (XEP-0115, Abschnitt 5.4)?</summary>
    public Boolean IsFormType

        => Var  == FormTypeVar &&
           Type == HiddenType;

}
