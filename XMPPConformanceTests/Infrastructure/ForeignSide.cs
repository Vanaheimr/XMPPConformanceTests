/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
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

using System.ComponentModel;
using System.Diagnostics;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Runs a shell command where the foreign implementations live: through
    /// <c>wsl.exe</c> on a Windows host, directly on Linux, where they are
    /// neighbours on the same loopback.
    /// </summary>
    /// <remarks>
    /// Every far side in this repository is reached the same way - Prosody and
    /// ejabberd through their setups, python-omemo and the XEP-0454 encryptor
    /// as scripts - and the detour is the only platform-bound part of it.
    ///
    /// <para>
    /// This exists because <see cref="TestEnvironment"/> said, when it wrote
    /// the split a second time, that a third caller would mean the copies
    /// belong together. The third caller arrived with
    /// <see cref="AesGcmUrlOracleTests"/>. <c>OmemoOracleTests</c> still has one
    /// of its own and is deliberately left alone for now: it carries a Windows
    /// path across the border inside a JSON job, where an argument list cannot
    /// help it, and untangling that is a separate step from adding tests.
    /// </para>
    /// </remarks>
    public static class ForeignSide
    {

        /// <summary>
        /// True when the far side is on this machine rather than behind wsl.exe.
        /// </summary>
        public static Boolean IsHere
            => OperatingSystem.IsLinux();

        /// <summary>
        /// A local path as the far side sees it: unchanged on Linux, and
        /// <c>D:\x\y</c> as <c>/mnt/d/x/y</c> when it has to cross into WSL.
        /// </summary>
        public static String PathOver(String LocalPath)

            => IsHere
                   ? LocalPath
                   : "/mnt/" + Char.ToLowerInvariant(LocalPath[0]) +
                     LocalPath[2..].Replace('\\', '/');

        /// <summary>
        /// Runs a shell command over there and returns what it said.
        /// </summary>
        /// <remarks>
        /// A missing interpreter comes back as code -1 rather than as an
        /// exception: no wsl.exe on this Windows, no /bin/sh on this Linux, is
        /// the environment speaking, and it has to end in a skip. That is the
        /// lesson of d39656e, where exactly this threw out of a
        /// <c>[OneTimeSetUp]</c> and NUnit turned it into three failures rather
        /// than three skips.
        /// </remarks>
        public static (Int32 Code, String Output, String Error) Run(String ShellCommand)
        {

            var start = IsHere

                            ? new ProcessStartInfo("/bin/sh") {
                                  RedirectStandardOutput  = true,
                                  RedirectStandardError   = true,
                                  UseShellExecute         = false
                              }

                            : new ProcessStartInfo("wsl",
                                                   $"-d Debian -- bash -c \"{ShellCommand.Replace("\"", "\\\"")}\"") {
                                  RedirectStandardOutput  = true,
                                  RedirectStandardError   = true,
                                  UseShellExecute         = false
                              };

            // On Linux the command goes through ArgumentList and not through an
            // Arguments string: that one is re-parsed with backslash rules, and
            // a shell command is full of them.
            if (IsHere)
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(ShellCommand);
            }

            try
            {

                using var process = Process.Start(start)!;

                var output = process.StandardOutput.ReadToEnd();
                var error  = process.StandardError. ReadToEnd();

                process.WaitForExit(120_000);

                return (process.ExitCode, output, error);

            }
            catch (Win32Exception e)
            {
                return (-1, "", $"No shell to reach the far side with: {e.Message}");
            }

        }

    }

}
