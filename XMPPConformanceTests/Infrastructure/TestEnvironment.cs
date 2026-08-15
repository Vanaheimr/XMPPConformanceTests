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
using System.Net;
using System.Net.Sockets;

using NUnit.Framework;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// One-time probing of what the environment can actually do, with
    /// <c>Assert.Ignore</c> as the answer when it cannot.
    /// </summary>
    /// <remarks>
    /// The DNS and NTS conformance suites carry the same class for the same
    /// reason, and the reason is worth stating rather than inheriting: a test
    /// that decides whether to run by asking <i>which platform am I on</i> is
    /// answering a different question from the one it means. It happens to
    /// agree today, and a coincidence that holds is still a coincidence.
    /// </remarks>
    public static class TestEnvironment
    {

        #region (private) Inbound probe

        /// <summary>
        /// Whether the far side can open a connection <b>to</b> this server, by
        /// the name it will actually use.
        /// </summary>
        /// <remarks>
        /// This is not NTS's <c>RequireWslInboundTcp</c> with the names changed,
        /// and copying that one would have been wrong here. It probes the
        /// <i>Windows host address</i>, because its tools dial the host. Ours do
        /// not: <c>tools/prosody/setup.sh</c> and <c>tools/ejabberd/setup.sh</c>
        /// give our side the domain <c>localhost</c> for the incoming run,
        /// because a name the peer can resolve without an /etc/hosts entry and
        /// therefore without root was the only one available. So the path that
        /// has to be measured is the peer's <c>localhost</c>, not ours.
        ///
        /// The difference is not academic, and measuring it turned up **two
        /// independent obstacles** on the development machine (August 2026)
        /// where the tests had always named one.
        ///
        /// The first is the name. Driven from WSL against a listener on
        /// 0.0.0.0, <c>localhost</c> came back <i>Connection refused</i> within
        /// milliseconds - the shape of a refusal, not of a dropped packet.
        /// Under WSL's default NAT networking <c>localhost</c> inside the VM is
        /// the VM's own loopback, so the connection never leaves it. No
        /// firewall is involved in that at all.
        ///
        /// The second is per-program, and it is the one that makes copying
        /// NTS's probe actively wrong. Against the host's address the same
        /// command connected when the listener belonged to <c>python.exe</c>
        /// and did not when it belonged to <c>testhost.exe</c>. The reason is
        /// two <b>Block</b> rules for this repository's testhost.exe on the
        /// Public profile - which is the profile the WSL vEthernet interface
        /// lands in - left behind by a firewall prompt somebody once dismissed.
        /// Block beats Allow in Windows Firewall, and the dozens of Allow rules
        /// beside them change nothing:
        ///
        ///     Get-NetFirewallApplicationFilter |
        ///         Where-Object { $_.Program -match 'testhost\.exe$' } |
        ///         Get-NetFirewallRule | Where-Object Action -eq Block
        ///
        /// So "the Hyper-V firewall discards every connection from WSL to the
        /// host", which is what these tests said about themselves until now,
        /// was the wrong shape twice over: it is not the subnet, it is the
        /// program - and for the path the peer actually takes, it is not the
        /// firewall at all.
        ///
        /// Both legs are probed, because the pair is what makes the diagnosis
        /// actionable: <c>localhost</c> alone can only say "no".
        /// </remarks>
        private static readonly Lazy<(Boolean Ok, String Diagnosis)> inboundFromThePeer = new(Probe);

        private static (Boolean Ok, String Diagnosis) Probe()
        {

            try
            {

                using var listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();

                var port     = ((IPEndPoint) listener.LocalEndpoint).Port;
                var accepted = listener.AcceptTcpClientAsync();

                // Bash's /dev/tcp needs no netcat, and the timeout keeps a
                // dropped connection - which is a real possibility on a machine
                // whose firewall does block - from stalling the probe.
                var (code, _, error) = OnThePeerSide(
                                           $"timeout 5 bash -c 'exec 3<>/dev/tcp/localhost/{port}'"
                                       );

                if (code == -1)
                    return (false, error);

                if (accepted.Wait(TimeSpan.FromSeconds(5)))
                    return (true, "");

                // It did not arrive. Ask the second question, so the message can
                // name what is missing instead of only what failed.
                return (false, DiagnoseFrom(port));

            }
            catch (Exception e)
            {
                return (false, $"The probe itself failed: {e.Message}");
            }

        }

        /// <summary>
        /// Called once the peer's <c>localhost</c> did not reach us: is this
        /// host reachable from the peer's side at all?
        /// </summary>
        private static String DiagnoseFrom(Int32 unusedPort)
        {

            if (OperatingSystem.IsLinux())
                return "The peer's own loopback did not reach this process, although both are on " +
                       "the same host. Something is listening in between, or the far side has no " +
                       "shell to drive the probe with.";

            var hostAddress = HostAddressAsThePeerSeesIt();

            if (hostAddress is null)
                return "The peer resolves 'localhost' to its own loopback, and this host's address " +
                       "could not be determined from over there either.";

            using var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();

            var port     = ((IPEndPoint) listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();

            OnThePeerSide($"timeout 5 bash -c 'exec 3<>/dev/tcp/{hostAddress}/{port}'");

            return accepted.Wait(TimeSpan.FromSeconds(5))

                       // Reachable by address, not by name: the name is the problem.
                       ? $"The far side dials the domain 'localhost', and under WSL's default NAT " +
                         $"networking that is WSL's own loopback - the connection never leaves the VM. " +
                         $"This host IS reachable from there ({hostAddress} answered), so no firewall " +
                         $"rule is what is missing: 'networkingMode=mirrored' in %USERPROFILE%\\.wslconfig " +
                         $"makes localhost shared between Windows and WSL, and only then does an inbound " +
                         $"firewall rule become the next question."

                       // Neither name nor address. Two things are in the way, and
                       // the per-program one is the surprise: Windows Firewall
                       // filters by executable, a dismissed prompt leaves a Block
                       // rule behind, and Block beats every Allow beside it. That
                       // is what was found on the development machine - two Block
                       // rules for this repository's testhost.exe on the Public
                       // profile, which is where the WSL interface lands.
                       : $"Neither 'localhost' nor this host's address ({hostAddress}) reached this " +
                         $"process from the peer's side, and there are two separate reasons this can " +
                         $"happen. (1) 'localhost' inside WSL is WSL's own loopback unless " +
                         $"'networkingMode=mirrored' stands in %USERPROFILE%\\.wslconfig. (2) Windows " +
                         $"Firewall filters per executable, so look for a Block rule on THIS test host " +
                         $"before blaming the subnet: Get-NetFirewallApplicationFilter | " +
                         $"Where-Object {{ $_.Program -match 'testhost\\.exe$' }} | Get-NetFirewallRule | " +
                         $"Where-Object Action -eq Block. A dismissed firewall prompt leaves one behind, " +
                         $"and Block wins against any number of Allow rules.";

        }

        private static String? HostAddressAsThePeerSeesIt()
        {

            var (code, output, _) = OnThePeerSide("ip -4 route show default");

            if (code != 0)
                return null;

            // "default via 172.23.32.1 dev eth0 proto kernel"
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "via")
                    return parts[i + 1];

            return null;

        }

        /// <summary>
        /// Runs a shell command where the far side lives: through wsl.exe on a
        /// Windows host, directly on Linux, where the peer is a neighbour on the
        /// same loopback.
        /// </summary>
        /// <remarks>
        /// The same split <see cref="OmemoOracleTests"/> makes for the oracle,
        /// and it is written twice on purpose rather than shared prematurely -
        /// that one has to carry Windows paths across the border as well, which
        /// this does not. Should a third caller appear, the two belong together.
        ///
        /// A missing interpreter comes back as code -1 and not as an exception:
        /// no wsl.exe on this Windows, no /bin/sh on this Linux, is the
        /// environment speaking, and it has to end in a skip. That is the lesson
        /// of d39656e, where exactly this threw out of a [OneTimeSetUp] and
        /// NUnit turned it into three failures.
        /// </remarks>
        private static (Int32 Code, String Output, String Error) OnThePeerSide(String shellCommand)
        {

            var start = OperatingSystem.IsLinux()

                            ? new ProcessStartInfo("/bin/sh") {
                                  RedirectStandardOutput  = true,
                                  RedirectStandardError   = true,
                                  UseShellExecute         = false
                              }

                            : new ProcessStartInfo("wsl",
                                                   $"-d Debian -- bash -c \"{shellCommand.Replace("\"", "\\\"")}\"") {
                                  RedirectStandardOutput  = true,
                                  RedirectStandardError   = true,
                                  UseShellExecute         = false
                              };

            if (OperatingSystem.IsLinux())
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(shellCommand);
            }

            try
            {

                using var process = Process.Start(start)!;

                var output = process.StandardOutput.ReadToEnd();
                var error  = process.StandardError. ReadToEnd();

                process.WaitForExit(30_000);

                return (process.ExitCode, output, error);

            }
            catch (Win32Exception e)
            {
                return (-1, "", $"No shell to reach the far side with: {e.Message}");
            }

        }

        #endregion


        /// <summary>
        /// Skips unless the far side can open a connection to this server.
        /// </summary>
        public static void RequireInboundFromThePeer()
        {

            var (ok, diagnosis) = inboundFromThePeer.Value;

            if (!ok)
                Assert.Ignore($"The far side cannot dial in to this server - skipping. {diagnosis}");

        }

    }

}
