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

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0045 against a room service nobody here wrote.
    /// </summary>
    /// <remarks>
    /// The rooms in <c>RatatoskrTests</c> are driven by hand: every stanza a
    /// service would send is put in exactly, which is the only way to provoke a
    /// kick or an assigned nickname on demand - and which proves nothing about
    /// whether a real service sends what we think it sends. Both halves there
    /// are the same code, the finding of D62 to D65 all over again.
    ///
    /// Here the far side is a real one. What it is worth is visible in the
    /// things that cannot be arranged from this side at all:
    ///
    /// <list type="bullet">
    ///   <item>a room that <b>did not exist</b> a moment ago, and the lock that
    ///         comes with it;</item>
    ///   <item>a second person in the same room, seen from the first;</item>
    ///   <item>the name the service gives a message, which is what a reply in a
    ///         room has to point at (D114) and which until now had nowhere to
    ///         come from.</item>
    /// </list>
    ///
    /// What is peculiar to a counterpart - its domain, its endpoint, the domain
    /// its rooms live on - the derived classes lay down. A second service costs
    /// twenty lines.
    /// </remarks>
    public abstract class AForeignPeerRoomTests : AForeignPeerTests
    {

        #region What makes up the counterpart

        /// <summary>
        /// The domain its rooms live on.
        /// </summary>
        /// <remarks>
        /// A room service is a component: its own domain beside the host, not a
        /// corner of it. Which is why it has a name of its own here rather than
        /// being assembled from <see cref="PeerDomain"/> - the two are different
        /// things and a service may call its own anything it likes.
        /// </remarks>
        protected abstract String  RoomDomain    { get; }

        #endregion

        #region Helper functions

        /// <summary>
        /// Enters a room with the first client and unlocks it, so that anybody
        /// else can follow.
        /// </summary>
        protected async Task<(XMPPClient Client, JID Room)> OpenARoomAsync(String nick = User)
        {

            var client   = await ConnectAsync();
            var room     = JID.Parse($"r{Guid.NewGuid():N}@{RoomDomain}");

            var outcome  = await client.JoinRoomAsync(room, nick);

            Assert.That(outcome.Joined, Is.True,
                        $"{PeerName} did not let us into a room of our own: {outcome.Refusal}");

            Assert.That(await client.CreateInstantRoomAsync(room), Is.True,
                        "The room stayed locked, so nobody else can enter it.");

            return (client, room);

        }

        #endregion


        #region 1. Entering a room that does not exist creates it

        /// <summary>
        /// A room comes into being by somebody entering it - and belongs to
        /// them.
        /// </summary>
        /// <remarks>
        /// Status 201 is the service saying the room is new, and it is the one
        /// piece of a join that cannot be arranged from this side: a room either
        /// existed a moment ago or it did not.
        /// </remarks>
        [Test]
        public async Task EnteringARoomThatDoesNotExistCreatesIt()
        {

            var client   = await ConnectAsync();
            var room     = JID.Parse($"r{Guid.NewGuid():N}@{RoomDomain}");

            var outcome  = await client.JoinRoomAsync(room, User);

            Assert.That(outcome.Joined, Is.True,
                        $"{PeerName} refused a room of our own: {outcome.Refusal}");

            Assert.Multiple(() =>
            {

                Assert.That(outcome.Room!.WasCreated, Is.True,
                            "The service says nothing about the room being new, or we do not read " +
                            "status 201 - and a new room is locked until it is configured.");

                Assert.That(outcome.Room.Me!.Affiliation, Is.EqualTo(MucAffiliation.Owner),
                            "Whoever creates a room owns it.");

                Assert.That(outcome.Room.Me!.Role, Is.EqualTo(MucRole.Moderator));

                Assert.That(outcome.Room.Nick, Is.EqualTo(User));

            });

        }

        #endregion

        #region 2. Two people in one room see each other

        /// <summary>
        /// The second occupant, seen from the first.
        /// </summary>
        /// <remarks>
        /// <b>The check a single client cannot do.</b> Everything about rooms
        /// this library gets wrong on the sending side is invisible until
        /// somebody else is in the room to not see it.
        /// </remarks>
        [Test]
        public async Task TwoPeopleInOneRoomSeeEachOther()
        {

            var (alice, room) = await OpenARoomAsync();

            var arrived = new ConcurrentQueue<MucOccupant>();
            alice.OnOccupantJoined += (t, s, r, occupant, why, ct) =>
            {
                arrived.Enqueue(occupant);
                return Task.CompletedTask;
            };

            var bob      = await ConnectAsync(User2);
            var outcome  = await bob.JoinRoomAsync(room, User2);

            Assert.That(outcome.Joined, Is.True,
                        $"The second client could not enter: {outcome.Refusal}");

            await WaitFor(() => arrived.Count == 1, "the second occupant, seen from the first");

            Assert.Multiple(() =>
            {

                Assert.That(arrived.Single().Nick, Is.EqualTo(User2));

                Assert.That(alice.Room(room)!.Occupants.Count, Is.EqualTo(2));

                Assert.That(bob.Room(room)!.Occupants.Count, Is.EqualTo(2),
                            "The one who entered second does not see the one who was already there.");

            });

        }

        #endregion

        #region 3. What is said in a room reaches everybody

        /// <summary>
        /// A message to the room, and the service hands it to everybody in it.
        /// </summary>
        [Test]
        public async Task WhatIsSaidInARoomReachesEverybody()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            await alice.SendRoomMessageAsync(room, "Deployment on Friday?");

            await WaitFor(() => heard.Any(m => m.Body == "Deployment on Friday?"),
                          "the message from the room");

            var message = heard.First(m => m.Body == "Deployment on Friday?");

            Assert.Multiple(() =>
            {

                Assert.That(message.Type, Is.EqualTo(MessageType.GroupChat));

                Assert.That(message.From.Resourcepart, Is.EqualTo(User),
                            "In a room the sender is the nickname, and that is all anybody gets.");

                Assert.That(message.From.Bare, Is.EqualTo(room.Bare));

            });

        }

        #endregion

        #region 4. The room gives every message a name of its own

        /// <summary>
        /// XEP-0461 in a room, against a service that actually assigns the
        /// names.
        /// </summary>
        /// <remarks>
        /// <b>The rule from D114 that had nowhere to be checked.</b> In a room
        /// the <c>id</c> of the stanza must not be used for a reply (XEP-0461,
        /// section 4), because everybody present sees a different one - the
        /// sender's. What everybody does share is the name the room itself gave
        /// the message, in a <c>&lt;stanza-id/&gt;</c> of XEP-0359.
        ///
        /// Until there was a room there was nothing to produce one, so the rule
        /// was checked against a record this project had built itself. Here the
        /// number comes from somebody else.
        ///
        /// <b>And the first run of this test found something.</b> There was no
        /// <c>&lt;stanza-id/&gt;</c> anywhere - not a fault in the reading, but
        /// in the room: Prosody attaches one only when the room <b>archives</b>,
        /// and nothing had switched archiving on. So a room without an archive
        /// is a room in which nothing can be answered, and no error says so
        /// anywhere - <c>ReplyableId</c> is simply null, which is the honest
        /// answer D114 built for exactly this case. <c>muc_mam</c> is in the
        /// set-up since, with the reason beside it.
        /// </remarks>
        [Test]
        public async Task TheRoomGivesEveryMessageANameOfItsOwn()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            var sentId = await alice.SendRoomMessageAsync(room, "Coming along?");

            await WaitFor(() => heard.Any(m => m.Body == "Coming along?"),
                          "the message from the room");

            var message = heard.First(m => m.Body == "Coming along?");

            Assert.Multiple(() =>
            {

                Assert.That(message.StanzaId, Is.Not.Null,
                            $"{PeerName} assigned no name of its own to the message, so nothing in " +
                            "this room can be answered - which is what XEP-0461 section 4 says " +
                            "happens.");

                Assert.That(message.ReplyableId, Is.EqualTo(message.StanzaId),
                            "The reference for a reply in a room is the room's name for the " +
                            "message.");

                Assert.That(message.ReplyableId, Is.Not.EqualTo(sentId),
                            "The id of the stanza was used after all. Everybody present sees a " +
                            "different one.");

            });

        }

        #endregion

        #region 5. A nickname already taken is refused

        /// <summary>
        /// Two people cannot be called the same thing in one room.
        /// </summary>
        /// <remarks>
        /// The refusal a client has to be able to act on: a taken nickname is
        /// worth trying again with another, and a ban is not. Both arrive as an
        /// error presence and are told apart by the condition alone.
        /// </remarks>
        [Test]
        public async Task ANicknameAlreadyTakenIsRefused()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob      = await ConnectAsync(User2);
            var outcome  = await bob.JoinRoomAsync(room, User);

            Assert.Multiple(() =>
            {

                Assert.That(outcome.Joined, Is.False,
                            "The service let a second person into the room under a name that was " +
                            "taken.");

                Assert.That(outcome.TimedOut, Is.False,
                            "No answer at all - so this says nothing about the refusal.");

                Assert.That(outcome.Refusal!.Condition, Is.EqualTo("conflict"));

                Assert.That(bob.Room(room), Is.Null,
                            "The refused room stayed in the table, and every presence from that " +
                            "address is now kept out of the roster.");

            });

        }

        #endregion

        #region 6. Leaving is seen by the others

        /// <summary>
        /// Somebody going, seen from the one who stays.
        /// </summary>
        [Test]
        public async Task LeavingIsSeenByTheOthers()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await WaitFor(() => alice.Room(room)!.Occupants.Count == 2, "the second occupant");

            var gone = new ConcurrentQueue<MucOccupant>();
            alice.OnOccupantLeft += (t, s, r, occupant, why, ct) =>
            {
                gone.Enqueue(occupant);
                return Task.CompletedTask;
            };

            await bob.LeaveRoomAsync(room, "Later");

            await WaitFor(() => gone.Count == 1, "the departure, seen from the other side");

            Assert.Multiple(() =>
            {

                Assert.That(gone.Single().Nick, Is.EqualTo(User2));

                Assert.That(alice.Room(room)!.Occupants.Count, Is.EqualTo(1));

                Assert.That(bob.Room(room), Is.Null,
                            "The one who left is still holding the room - so the echo of their own " +
                            "departure was not recognised as their own.");

            });

        }

        #endregion

        #region 8. A kick is seen as a kick

        /// <summary>
        /// Status 307, produced by a service rather than put in by hand.
        /// </summary>
        /// <remarks>
        /// <b>The reason the moderating half was worth building at all.</b> The
        /// codes that tell a departure from a kick, a ban and a room shutting
        /// down have been read since D116 - and every one of them was checked
        /// against a stanza this project wrote itself. A client cannot kick
        /// itself, so until there was a second person in the room and a way to
        /// throw them out, 307 had nowhere to come from.
        /// </remarks>
        [Test]
        public async Task AKickIsSeenAsAKick()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await WaitFor(() => alice.Room(room)!.Occupants.Count == 2, "the second occupant");

            MucUserInfo? why = null;
            bob.OnRoomLeft += (t, s, r, info, ct) => { why = info; return Task.CompletedTask; };

            Assert.That(await alice.KickFromRoomAsync(room, User2, "Enough of that."), Is.True,
                        $"{PeerName} refused the kick, although the one asking created the room.");

            await WaitFor(() => why is not null, "the kick, seen by the one kicked");

            Assert.Multiple(() =>
            {

                Assert.That(why!.Has(MucStatus.Kicked), Is.True,
                            "The service reported no 307, or we do not read it - and then being " +
                            "thrown out is indistinguishable from leaving.");

                Assert.That(why.Reason, Is.EqualTo("Enough of that."),
                            "The reason did not survive the round trip.");

                Assert.That(bob.Room(room), Is.Null,
                            "The one kicked is still holding the room.");

            });

            await WaitFor(() => alice.Room(room)!.Occupants.Count == 1,
                          "the room, seen from the one who stayed");

        }

        #endregion

        #region 9. A ban needs a real address, and keeps somebody out

        /// <summary>
        /// Status 301, and the asymmetry that comes with it.
        /// </summary>
        /// <remarks>
        /// <b>A ban names a real address and a kick names a nickname</b>, and
        /// that is not a quirk of the syntax: an affiliation outlives the visit,
        /// so it has to name somebody who exists outside it. In a
        /// semi-anonymous room the real address is given to moderators only -
        /// so this test also asks the question one cannot ask from one side,
        /// which is whether the service gives it to a moderator at all.
        /// </remarks>
        [Test]
        public async Task ABanNeedsARealAddressAndKeepsSomebodyOut()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await WaitFor(() => alice.Room(room)!.Occupants.Count == 2, "the second occupant");

            var seen = alice.Room(room)!.Occupants[User2];

            Assert.That(seen.RealJid, Is.Not.Null,
                        $"{PeerName} does not give the moderator the real address of an occupant, " +
                        "so nobody in this room can be banned by anybody - a ban has nothing to " +
                        "name.");

            MucUserInfo? why = null;
            bob.OnRoomLeft += (t, s, r, info, ct) => { why = info; return Task.CompletedTask; };

            Assert.That(await alice.BanFromRoomAsync(room, seen.RealJid!.Value, "For good."), Is.True,
                        $"{PeerName} refused the ban.");

            await WaitFor(() => why is not null, "the ban, seen by the one banned");

            Assert.That(why!.Has(MucStatus.Banned), Is.True,
                        "The service reported no 301 - and then a ban reads like an ordinary " +
                        "departure, which invites the client to walk straight back in.");

            // And the part a status code alone does not prove.
            var again = await bob.JoinRoomAsync(room, User2);

            Assert.Multiple(() =>
            {

                Assert.That(again.Joined, Is.False, "The banned client got back in.");

                Assert.That(again.Refusal!.Condition, Is.EqualTo("forbidden"),
                            "A ban is refused with 'forbidden' (section 9.1) - the one condition " +
                            "that says trying another nickname will not help.");

            });

        }

        #endregion

        #region 10. An invitation reaches somebody who is not in the room

        /// <summary>
        /// The one thing a room says about a room the recipient has not
        /// entered.
        /// </summary>
        /// <remarks>
        /// Everything else from a room is recognised by asking whether this
        /// client entered it. For an invitation the answer is always no, which
        /// is why it has to be read before the question is asked - and why a
        /// client that gets the order wrong can never be invited anywhere.
        ///
        /// Mediated, too: the message goes to the room and the room passes it
        /// on with the inviter's address filled in. Whether a service does that
        /// is exactly what a test against a real one settles.
        /// </remarks>
        [Test]
        public async Task AnInvitationReachesSomebodyWhoIsNotInTheRoom()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);

            MucInvitation? invitation = null;
            bob.OnRoomInvitation += (t, s, i, ct) => { invitation = i; return Task.CompletedTask; };

            Assert.That(await alice.InviteToRoomAsync(room, bob.BareJid, "Come along"), Is.True);

            await WaitFor(() => invitation is not null, "the invitation");

            Assert.Multiple(() =>
            {

                Assert.That(invitation!.Room, Is.EqualTo(room.Bare));

                // The two services name the inviter differently, and both are
                // usable: ejabberd sends the real address the specification's
                // example shows, Prosody the occupant address, which says who
                // asked without saying who that is. What may NOT come back is
                // the bare room - a refusal addressed there reaches nobody.
                Assert.That(invitation.FromAnOccupantAddress
                                ? invitation.From.Resourcepart
                                : invitation.From.Bare.ToString(),

                            Is.EqualTo(invitation.FromAnOccupantAddress
                                           ? User
                                           : alice.BareJid.ToString()),

                            "The inviter is neither their address in the room nor their real one.");

                Assert.That(invitation.From.Bare == invitation.Room &&
                            invitation.From.Resourcepart is null, Is.False,
                            "The inviter came back as the bare address of the room, and a refusal " +
                            "addressed there reaches nobody.");

                Assert.That(bob.Room(room), Is.Null,
                            "Being invited is not being in the room.");

            });

            // And it is an invitation to something one can act on.
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True,
                        "The invitation named a room that could not be entered.");

        }

        #endregion

        #region 11. What was said in a room is there afterwards

        /// <summary>
        /// XEP-0313 against a room's archive: the reason a room has one.
        /// </summary>
        /// <remarks>
        /// <b>Somebody who was not there asks what was said.</b> That is the
        /// whole point of an archive and it cannot be arranged from one side: it
        /// needs a conversation that happened before the asker existed in the
        /// room, and an archive belonging to the room rather than to anybody's
        /// account - our own server never saw a word of it.
        ///
        /// It also closes the circle from D116. The room's archive is why a
        /// message gets a <c>&lt;stanza-id/&gt;</c> at all; without archiving
        /// there is no name for a reply to point at. Here the archive is asked
        /// for the thing that name belongs to.
        /// </remarks>
        [Test]
        public async Task WhatWasSaidInARoomIsThereAfterwards()
        {

            var (alice, room) = await OpenARoomAsync();

            await alice.SendRoomMessageAsync(room, "before anybody else arrived");

            // The service has to have written it down before it can be asked
            // about it, and it says so by handing it back to the sender.
            var echoed = new ConcurrentQueue<XMPPMessage>();
            alice.OnMessage += (t, s, m, ct) => { echoed.Enqueue(m); return Task.CompletedTask; };

            await alice.SendRoomMessageAsync(room, "and this too");

            await WaitFor(() => echoed.Any(m => m.Body == "and this too"), "the room's echo");

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            var page = await bob.RoomHistoryAsync(room, 20);

            Assert.That(page, Is.Not.Null,
                        $"{PeerName} refused the room's archive, or answered nothing at all.");

            Assert.Multiple(() =>
            {

                Assert.That(page!.Messages.Select(m => m.Message.Body),
                            Does.Contain("before anybody else arrived"),
                            "The room kept nothing, so anybody arriving late walks in blind.");

                Assert.That(page.Messages.Select(m => m.Message.Body),
                            Does.Contain("and this too"));

                Assert.That(page.Messages.All(m => m.ArchiveId.Length > 0), Is.True,
                            "An entry without a name of its own cannot be paged from.");

                Assert.That(page.Messages.Select(m => m.Timestamp),
                            Is.Ordered,
                            "An archive answers oldest first; out of order it is not a conversation.");

            });

        }

        #endregion

        #region 12. The last page is the end and not the beginning

        /// <summary>
        /// XEP-0059 against a real archive: what somebody opening a
        /// conversation wants to see.
        /// </summary>
        /// <remarks>
        /// An archive counts from the beginning, and the end is what anybody
        /// actually wants. An empty <c>&lt;before/&gt;</c> is how that is asked
        /// for - and leaving it out is a perfectly good query for the wrong
        /// thing, which looks like an old conversation nobody remembers.
        ///
        /// Asked against a service because paging is where archives differ most:
        /// how many they hand over at once, and whether they say the page was
        /// the last.
        /// </remarks>
        [Test]
        public async Task TheLastPageIsTheEndAndNotTheBeginning()
        {

            var (alice, room) = await OpenARoomAsync();

            var echoed = new ConcurrentQueue<XMPPMessage>();
            alice.OnMessage += (t, s, m, ct) => { echoed.Enqueue(m); return Task.CompletedTask; };

            for (var number = 1; number <= 5; number++)
                await alice.SendRoomMessageAsync(room, $"line {number}");

            await WaitFor(() => echoed.Count(m => m.Body!.StartsWith("line ",
                                                                     StringComparison.Ordinal)) == 5,
                          "all five lines coming back from the room");

            var last = await alice.QueryArchiveAsync(archive: room.Bare, max: 2, before: "");

            Assert.That(last, Is.Not.Null, $"{PeerName} refused the paged query.");

            Assert.Multiple(() =>
            {

                Assert.That(last!.Messages, Has.Count.EqualTo(2),
                            "The archive handed over more or fewer than the page that was asked for.");

                Assert.That(last.Messages.Select(m => m.Message.Body),
                            Is.EqualTo(new[] { "line 4", "line 5" }),
                            "The first page came back instead of the last, so opening a conversation " +
                            "shows its oldest corner.");

                Assert.That(last.Complete, Is.False,
                            "The archive says this page is everything, and three lines before it are " +
                            "not in it.");

                Assert.That(last.First, Is.Not.Null.And.Not.Empty,
                            "Without the first id of this page there is no way to ask for the one " +
                            "before it.");

            });

            // And the page before it, which is what scrolling up asks for.
            var earlier = await alice.QueryArchiveAsync(archive: room.Bare, max: 2, before: last.First);

            Assert.That(earlier!.Messages.Select(m => m.Message.Body),
                        Is.EqualTo(new[] { "line 2", "line 3" }),
                        "Paging backwards from the first id of a page did not give the one before it.");

        }

        #endregion

        #region 13. An archive of one's own answers

        /// <summary>
        /// The other archive: the one the server keeps for this account.
        /// </summary>
        /// <remarks>
        /// <b>What is asked here is that it answers, not what it kept</b>, and
        /// the first version of this test got that wrong. It said a message and
        /// then demanded to find it, which failed against both services and for
        /// two entirely different reasons: Prosody delivered it and archived
        /// nothing, ejabberd never delivered it at all.
        ///
        /// Neither is a fault in this client. What a server keeps for an account
        /// - for whom, for how long, whether at all - is its policy, and
        /// Prosody's default of keeping only what was exchanged with somebody in
        /// the roster is the careful one. A test that insists on a particular
        /// answer there is a test of the configuration somebody wrote for it,
        /// dressed up as conformance.
        ///
        /// What <b>is</b> ours is the question and the reading of the answer: a
        /// query the server refuses as malformed, or an answer that cannot be
        /// read, is this client's problem. So the assertion is that an answer
        /// came back at all - and an empty page is one. The two tests above say
        /// what happens to real entries, against an archive that definitely has
        /// some.
        /// </remarks>
        [Test]
        public async Task AnArchiveOfOnesOwnAnswers()
        {

            var alice = await ConnectAsync();

            var page = await alice.LastFromArchiveAsync(JID.Parse($"{User2}@{PeerDomain}"), 10);

            Assert.That(page, Is.Not.Null,
                        $"{PeerName} refused a query to this account's own archive, or said nothing " +
                        "at all - and an archive that kept nothing would still have answered.");

        }

        #endregion

        #region 7. The subject travels

        /// <summary>
        /// A subject set by one is the subject for everybody.
        /// </summary>
        [Test]
        public async Task TheSubjectTravels()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);

            var subjects = new ConcurrentQueue<String>();
            bob.OnRoomSubject += (t, s, r, subject, by, ct) =>
            {
                subjects.Enqueue(subject);
                return Task.CompletedTask;
            };

            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await alice.SetRoomSubjectAsync(room, "Deployment on Friday");

            await WaitFor(() => subjects.Contains("Deployment on Friday"),
                          "the subject of the room");

            Assert.That(bob.Room(room)!.Subject, Is.EqualTo("Deployment on Friday"));

        }

        #endregion

    }

}
