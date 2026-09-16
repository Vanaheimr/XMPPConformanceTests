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

            // Two waits, because there are two connections and the room service
            // tells them separately - nothing orders one before the other.
            //
            // This round waited for Alice's event and then asserted on Bob's
            // state, which is a race and behaved like one: it cost one nightly,
            // in one of the two lanes running the same code. LeaveRoomAsync
            // returns when the departure has been *sent*; the room is forgotten
            // when the echo comes back, and that is the right design - dropping
            // it at once would file the echo in the roster.
            await WaitFor(() => gone.Count == 1,        "the departure, seen from the other side");
            await WaitFor(() => bob.Room(room) is null, "the departure, seen by the one who left");

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

        #region 8. A room hands out real addresses only when it is told to

        /// <summary>
        /// XEP-0045, section 10.2.1: <c>muc#roomconfig_whois</c>, and what
        /// changes for a <b>participant</b> when it does.
        /// </summary>
        /// <remarks>
        /// <b>The question that decides whether a room can be encrypted in at
        /// all</b>, and it is entirely the service's bookkeeping: who is told
        /// the real address behind a nickname. A moderator is told either way -
        /// round 9 depends on that - so the interesting side is Bob's, who is
        /// nobody in particular.
        ///
        /// Three things are asked, and the first is what makes the others mean
        /// anything: a default room must give Bob <b>nothing</b>. Without that,
        /// a service that hands real addresses to everybody always would pass
        /// the rest while the configuration did nothing at all.
        ///
        /// <b>The second was written the wrong way round first, and the far side
        /// corrected it.</b> The round expected that configuring a room mid-visit
        /// would make the addresses appear. Prosody accepts the configuration,
        /// announces it with status 172 - and does not send the occupants again.
        /// Nothing in section 10.2.1 says it must. So whoever was already in the
        /// room stays nameless, and what the client learns from the 172 is only
        /// that the <i>room</i> changed.
        ///
        /// That is not a fault to work around by re-joining: throwing away
        /// everybody's view of a room to fetch something the specification never
        /// promised is worse than saying what is true. The addresses arrive for
        /// whoever joins <b>after</b> the configuration - which is the order
        /// anybody setting up an encrypted room would use anyway, and is what
        /// the third part checks.
        /// </remarks>
        [Test]
        public async Task ARoomTellsWhoSomebodyIsOnlyWhenConfiguredTo()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);

            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await WaitFor(() => bob.Room(room)!.Occupants.Count == 2, "the other occupant, seen by Bob");

            Assert.Multiple(() =>
            {

                Assert.That(bob.Room(room)!.Occupants[User].RealJid, Is.Null,
                            $"{PeerName} tells an ordinary participant who everybody really is, in a " +
                            "room nobody configured. Then round 9 below proves nothing: it would " +
                            "pass whether the configuration worked or not.");

                Assert.That(bob.CannotEncryptInRoom(room), Does.Contain("semi-anonymous"),
                            "A default room was taken for one that can be encrypted in.");

            });

            // And now the owner changes what the room is.
            Assert.That(await alice.MakeRoomNonAnonymousAsync(room), Is.True,
                        $"{PeerName} would not set muc#roomconfig_whois - either it does not offer " +
                        "the field, or it refused the submit. Then no room on this service can " +
                        "carry an encrypted conversation.");

            // Status 172, as a message carrying nothing but status codes. A
            // client watching presences for what a room is never hears it.
            await WaitFor(() => bob.Room(room)!.IsNonAnonymous,
                          "status 172, saying the room is no longer anonymous");

            Assert.That(bob.Room(room)!.Occupants[User].RealJid, Is.Null,
                        $"{PeerName} sent the occupants again after the configuration. That is more " +
                        "than section 10.2.1 asks for - and if it holds, the note in OmemoRooms " +
                        "about people already present staying nameless is wrong for this service.");

            // Whoever comes in afterwards is named, and that is the order this
            // is meant to be used in.
            //
            // A second connection rather than Bob leaving and walking back in:
            // the two would race, because the unavailable presence of the
            // leaving arrives after the joining has already put the room back,
            // and takes it away again. Two resources of one account under
            // different nicknames is something a service allows and something
            // people do.
            var again = await ConnectAsync(User2);

            Assert.That((await again.JoinRoomAsync(room, "late")).Joined, Is.True);

            await WaitFor(() => again.Room(room) is not null &&
                                again.Room(room)!.Occupants.ContainsKey(User) &&
                                again.Room(room)!.Occupants[User].RealJid is not null,
                          "the real address of the other occupant, joining after the configuration");

            Assert.Multiple(() =>
            {

                Assert.That(again.Room(room)!.Occupants[User].RealJid!.Value.Bare,
                            Is.EqualTo(JID.Parse($"{User}@{PeerDomain}")),
                            "The address the service handed out is not the one behind that nickname.");

                Assert.That(again.CannotEncryptInRoom(room), Is.Null,
                            "The room is non-anonymous and this client still will not encrypt in it.");

            });

        }

        #endregion

        #region 9. What is said encrypted in a room stays in the room

        /// <summary>
        /// XEP-0384 through a room service nobody here wrote.
        /// </summary>
        /// <remarks>
        /// The round that needs everything before it: the room made
        /// non-anonymous (round 8), the service writing the real addresses in,
        /// PEP carrying the bundles - which is the same personal eventing the
        /// avatar lane had to switch on in D122 - and the room reflecting a
        /// stanza it cannot read.
        ///
        /// <b>What makes it worth running against a foreign service</b> is the
        /// last part. A room service handles a <c>groupchat</c> message with no
        /// <c>&lt;body/&gt;</c>, and there is nothing obliging it to pass one
        /// on: a service that decides an empty message is nothing to deliver
        /// would break every encrypted room without a single error anywhere.
        /// Our own tests cannot ask that question - there the room is played by
        /// the test, and it passes on whatever it is given.
        ///
        /// <b>What is deliberately not asserted is an empty Skipped list</b>,
        /// and the reason is a property of running against a server that
        /// remembers. These clients keep their OMEMO material in memory, so
        /// every run announces a new device id - and the device list in PEP is
        /// the account's, which outlives the run. After a handful of runs it
        /// names devices whose bundles nobody will ever publish again, and every
        /// one of them is reported skipped, for ever.
        ///
        /// That is the library behaving correctly: one unreachable device must
        /// not make a person unreachable. So what is asked here is the narrower
        /// and actually interesting question - whether <i>the device on the other
        /// end of this conversation</i> was left out.
        ///
        /// The plaintext check at the end is what stops this from passing for a
        /// client that gave up and sent in the clear.
        /// </remarks>
        [Test]
        public async Task WhatIsSaidEncryptedInARoomStaysInTheRoom()
        {

            var (alice, room) = await OpenARoomAsync();

            Assert.That(await alice.MakeRoomNonAnonymousAsync(room), Is.True,
                        $"{PeerName} would not make the room non-anonymous.");

            var bob = await ConnectAsync(User2);

            Assert.Multiple(() =>
            {
                Assert.That(alice.EnableOmemoAsync().GetAwaiter().GetResult(), Is.True,
                            $"Alice could not switch OMEMO on against {PeerName} - the bundles go " +
                            "over PEP, so this is the same personal eventing the avatars needed.");
                Assert.That(bob.EnableOmemoAsync().GetAwaiter().GetResult(), Is.True,
                            $"Bob could not switch OMEMO on against {PeerName}.");
            });

            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            await WaitFor(() => alice.Room(room)!.Occupants.Count == 2 &&
                                alice.Room(room)!.Occupants[User2].RealJid is not null,
                          "both occupants, with their real addresses");

            var heard = new ConcurrentQueue<String>();

            bob.OnEncryptedMessage += (t, s, message, omemo, ct) =>
            {
                // Only what really came out of the envelope, and attributed to
                // the person the envelope names - not to the nickname the room
                // wrote on the outside.
                if (omemo.EnvelopeFrom?.Bare == JID.Parse($"{User}@{PeerDomain}"))
                    heard.Enqueue(message.Body);

                return Task.CompletedTask;

            };

            const String secret = "The room is not the server's to read.";

            var sent = await alice.SendEncryptedRoomMessageAsync(room, secret);

            Assert.Multiple(() =>
            {

                Assert.That(sent.Refusal, Is.Null, $"Nothing was sent: {sent.Refusal}");

                Assert.That(sent.Recipients, Does.Contain(JID.Parse($"{User2}@{PeerDomain}")),
                            "Bob was not among the people this was encrypted to.");

                Assert.That(sent.Skipped.Any(s => s.Jid.Bare == JID.Parse($"{User2}@{PeerDomain}") &&
                                                  s.DeviceId == bob.Omemo!.Identity.DeviceId),
                            Is.False,
                            "The device actually on the other end of this conversation was left " +
                            "out: " +
                            String.Join(", ", sent.Skipped.Select(s => $"{s.Jid}/{s.DeviceId}: {s.Reason}")));

            });

            await WaitFor(() => heard.Contains(secret),
                          $"the encrypted line, through {PeerName}'s room service");

            Assert.That(heard, Does.Contain(secret));

        }

        #endregion

        #region 14. Whether one may ask anybody in is the room's to decide

        /// <summary>
        /// XEP-0045, section 7.8.1, from the side that is not the owner.
        /// </summary>
        /// <remarks>
        /// <b>Round 10 has the owner do the inviting</b>, which is the one
        /// person for whom it can never fail. What a room does to everybody else
        /// is a different question, and the two services answer it differently:
        /// ejabberd's default room carries <c>muc#roomconfig_allowinvites</c> at
        /// 0 and refuses, Prosody's lets anybody who is in the room ask. Both
        /// are within section 7.8.1, which leaves it to the room.
        ///
        /// So what is asserted is not which of the two happens - it is that
        /// <b>one of them happens visibly</b>. Before D129 ejabberd produced
        /// neither: the invitation came back as an ordinary message error,
        /// nothing could tell it apart from any other refused message, and
        /// <c>InviteToRoomAsync</c> had already answered true. The sender saw
        /// success and the room saw silence - the failure D127 was written to
        /// refuse, one lane over.
        ///
        /// The invitee is somebody already in the room, and that is not a
        /// shortcut: the room decides about the <i>inviter</i>, and with two
        /// accounts the other one is the only invitee whose treatment can be
        /// watched from here.
        /// </remarks>
        [Test]
        public async Task WhetherOneMayAskAnybodyInIsTheRoomsToDecide()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            MucInvitation?    asked    = null;
            MucInviteRefused? refused  = null;

            alice.OnRoomInvitation    += (t, s, i, ct) => { asked   = i; return Task.CompletedTask; };
            bob.  OnInvitationRefused += (t, s, r, ct) => { refused = r; return Task.CompletedTask; };

            Assert.That(await bob.InviteToRoomAsync(room, alice.BareJid, "you too"), Is.True,
                        "Bob is in the room, so there was something to send.");

            await WaitFor(() => asked is not null || refused is not null,
                          $"{PeerName} either passing the invitation on or saying that it will not");

            Assert.Multiple(() =>
            {

                Assert.That(asked is not null && refused is not null, Is.False,
                            "The room passed the invitation on and refused it at the same time.");

                if (refused is not null)
                {

                    Assert.That(refused.Room, Is.EqualTo(room.Bare),
                                "The refusal named a different room.");

                    Assert.That(refused.Who.Bare, Is.EqualTo(alice.BareJid.Bare),
                                "The refusal did not name the person who was never asked, so " +
                                "nothing can be said to anybody about what failed.");

                }

                else
                    Assert.That(asked!.Room, Is.EqualTo(room.Bare),
                                "The invitation that arrived was for a different room.");

            });

        }

        #endregion

        #region 15. A room that does not archive is one in which nothing can be answered

        /// <summary>
        /// XEP-0461, section 4 against a room with no archive - the case D116
        /// found by accident and nobody ever asked for.
        /// </summary>
        /// <remarks>
        /// <b>Every other round in this lane runs in a room that archives</b>,
        /// because both set-ups switch archiving on for every room they make.
        /// That was done in D116 for a reason - without it round 4 has nothing
        /// to measure - and the side effect is that the state a room is in when
        /// nobody has arranged anything was never entered here.
        ///
        /// What stands in the README about it ("a room without an archive is one
        /// in which nothing can be answered") comes from one observation of
        /// Prosody's behaviour, made while chasing something else. ejabberd was
        /// never asked at all.
        ///
        /// <b>The field has two names.</b> Prosody calls it
        /// <c>muc#roomconfig_enablearchiving</c>, ejabberd plain <c>mam</c>,
        /// with no <c>muc#roomconfig_</c> in front of it - so a client that
        /// wants to ask has to know both, and one that knows one of them will
        /// quietly configure nothing on the other service.
        ///
        /// What is asserted is the client's rule and not the service's choice.
        /// Whether a service names a message it does not archive is its own
        /// business; that a reply points at the room's name or at nothing, and
        /// never at the sender's own id, is XEP-0461 and ours.
        /// </remarks>
        [Test]
        public async Task ARoomWithoutAnArchiveCannotBeAnsweredIn()
        {

            var (alice, room) = await OpenARoomAsync();

            var form = await alice.FetchRoomConfigAsync(room);

            Assert.That(form, Is.Not.Null,
                        $"{PeerName} would not say how the room is configured, so nothing here " +
                        "can be turned off.");

            var offered = DataForm.Fields(form!).
                                   Select(field => field.Attribute("var")?.Value).
                                   Where (name  => name is not null).
                                   ToHashSet()!;

            var archiving = new[] { "muc#roomconfig_enablearchiving", "mam" }.
                                FirstOrDefault(offered.Contains);

            Assert.That(archiving, Is.Not.Null,
                        $"{PeerName} offers neither muc#roomconfig_enablearchiving nor mam, so " +
                        "there is no way from here to stand in a room without an archive.");

            Assert.That(await alice.ConfigureRoomAsync(room, new Dictionary<String, String> {
                                                                 [archiving!] = "0"
                                                             }), Is.True,
                        $"{PeerName} would not switch the room's archive off through {archiving}.");

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            var sentId = await alice.SendRoomMessageAsync(room, "Nothing to point at?");

            await WaitFor(() => heard.Any(m => m.Body == "Nothing to point at?"),
                          "the message from the unarchived room");

            var message = heard.First(m => m.Body == "Nothing to point at?");

            Assert.Multiple(() =>
            {

                Assert.That(message.ReplyableId, Is.EqualTo(message.StanzaId),
                            "The reference for a reply in a room is the room's name for the " +
                            "message, and nothing else is.");

                Assert.That(message.ReplyableId, Is.Not.EqualTo(sentId),
                            "The id of the stanza was used after all. Everybody present sees a " +
                            "different one.");

            });

            // The two halves are both real, and which one a service lands in is
            // reported rather than assumed - a run that measured the refusal
            // must not look like one that measured the answer.
            if (message.StanzaId is null)
            {

                TestContext.Out.WriteLine(
                    $"{PeerName} gives an unarchived room's messages no name of their own.");

                Assert.That(await bob.ReplyToAsync(message, "Then this cannot be sent"), Is.Null,
                            "The room named the message nothing and the client answered anyway - " +
                            "so the reference points at whatever the sender happened to call it, " +
                            "which is a different message for every reader.");

            }

            else
            {

                TestContext.Out.WriteLine(
                    $"{PeerName} names a message even in a room that does not archive.");

                Assert.That(await bob.ReplyToAsync(message, "Then this can be sent"), Is.Not.Null,
                            "The room named the message and the client would not answer it.");

            }

        }

        #endregion

        #region 16. Taking a room down is seen by everybody who was in it

        /// <summary>
        /// XEP-0045, section 10.9, from the side that did not do it.
        /// </summary>
        /// <remarks>
        /// <b>Not arrangeable from one side at all.</b> A destruction exists
        /// only as something a service does to everybody present, and the half
        /// that matters is the half this client did not send: the second person
        /// is told the room is gone, and told where to go instead.
        ///
        /// The alternative is the point of the round. A destruction without one
        /// leaves everybody nowhere; with one it is a move, and a client that
        /// drops the address has silently turned the second into the first. It
        /// travels through the service, so no test can arrange for it to be
        /// there - which is why it is asked for here and not in the unit rounds.
        /// </remarks>
        [Test]
        public async Task TakingARoomDownIsSeenByEverybodyWhoWasInIt()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            var elsewhere = JID.Parse($"moved{Guid.NewGuid():N}@{RoomDomain}");

            MucRoomDestroyed? gone = null;
            bob.OnRoomDestroyed += (t, s, d, ct) => { gone = d; return Task.CompletedTask; };

            Assert.That(await alice.DestroyRoomAsync(room, "moving on", elsewhere), Is.True,
                        $"{PeerName} would not let the room's own owner take it down.");

            await WaitFor(() => gone is not null, "the news that the room is gone");

            Assert.Multiple(() =>
            {

                Assert.That(gone!.Room, Is.EqualTo(room.Bare),
                            "Somebody was told a different room had gone.");

                Assert.That(gone.Alternate, Is.EqualTo(elsewhere),
                            "The address to move to did not survive the trip, so what arrived " +
                            "says the room vanished when it says the room moved.");

                Assert.That(gone.Reason, Is.EqualTo("moving on"));

                Assert.That(bob.Room(room), Is.Null,
                            "The room is gone and this client still holds it, so everything " +
                            "sent to it from now on goes nowhere.");

            });

        }

        #endregion

        #region 17. A room is not everybody's to take down

        /// <summary>
        /// XEP-0045, section 10.9: the owner, and nobody else.
        /// </summary>
        /// <remarks>
        /// The counterpart of round 16 and the reason it is worth having
        /// separately: a destruction that worked for the owner proves nothing
        /// about who else it works for, and this is the one place in the owner
        /// protocol where getting that wrong costs a whole room.
        ///
        /// Unlike an invitation this is an IQ, so the refusal is an answer and
        /// arrives by itself - there is nothing to surface and nothing that can
        /// be missed, which is exactly what makes D129's finding peculiar to
        /// invitations.
        /// </remarks>
        [Test]
        public async Task ARoomIsNotEverybodysToTakeDown()
        {

            var (alice, room) = await OpenARoomAsync();

            var bob = await ConnectAsync(User2);
            Assert.That((await bob.JoinRoomAsync(room, User2)).Joined, Is.True);

            Assert.That(await bob.DestroyRoomAsync(room, "not mine to take"), Is.False,
                        $"{PeerName} let somebody who is merely standing in the room destroy it.");

            // And the room is still there, which is the half that a refused IQ
            // does not prove on its own.
            Assert.That(alice.Room(room), Is.Not.Null);

            await alice.SendRoomMessageAsync(room, "still here");

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            await alice.SendRoomMessageAsync(room, "and still talking");

            await WaitFor(() => heard.Any(m => m.Body == "and still talking"),
                          "a message out of the room that was not destroyed");

        }

        #endregion

        #region 18. A room says who is on its lists

        /// <summary>
        /// XEP-0045, section 9.5: the affiliation list, read back from the
        /// service that keeps it.
        /// </summary>
        /// <remarks>
        /// <b>The only way to know an affiliation took.</b> Setting one is an IQ
        /// that answers <c>result</c>, and a <c>result</c> says the service
        /// accepted the request - not that anybody is on any list. Since D117
        /// this suite has been setting affiliations and reading them back out of
        /// the <i>presence</i> the room sends, which works only while the person
        /// is standing in the room. An affiliation outlives the visit; that is
        /// the whole of what distinguishes it from a role, and it was the one
        /// part never checked.
        ///
        /// Asked for twice, before and after, because a list that contained the
        /// answer already would prove nothing about the setting.
        /// </remarks>
        [Test]
        public async Task ARoomSaysWhoIsOnItsLists()
        {

            var (alice, room) = await OpenARoomAsync();

            var owners = await alice.RoomAffiliationsAsync(room, MucAffiliation.Owner);

            Assert.That(owners, Is.Not.Null,
                        $"{PeerName} would not tell the room's own owner who owns it.");

            Assert.That(owners!.Any(entry => entry.Jid.Bare == alice.BareJid.Bare), Is.True,
                        "The owner of the room is not on its list of owners: " +
                        String.Join(", ", owners.Select(entry => entry.Jid.ToString())));

            var before = await alice.RoomAffiliationsAsync(room, MucAffiliation.Member);

            Assert.That(before, Is.Not.Null,
                        $"{PeerName} would not say who its members are.");

            Assert.That(before!.Any(entry => entry.Jid.Bare == JID.Parse($"{User2}@{PeerDomain}").Bare),
                        Is.False,
                        "Somebody was on the member list before anybody put them there, so what " +
                        "the second half measures is not the setting.");

            Assert.That(await alice.SetRoomAffiliationAsync(room,
                                                            JID.Parse($"{User2}@{PeerDomain}"),
                                                            MucAffiliation.Member,
                                                            "asked along"), Is.True,
                        $"{PeerName} refused to make anybody a member.");

            var after = await alice.RoomAffiliationsAsync(room, MucAffiliation.Member);

            Assert.That(after, Is.Not.Null);

            Assert.That(after!.Any(entry => entry.Jid.Bare == JID.Parse($"{User2}@{PeerDomain}").Bare),
                        Is.True,
                        "The service answered result and put nobody on the list - which is why " +
                        "an affiliation cannot be checked by whether the request was accepted: " +
                        String.Join(", ", after.Select(entry => $"{entry.Jid} ({entry.Affiliation})")));

            Assert.That(after.All(entry => entry.Affiliation == MucAffiliation.Member), Is.True,
                        "The member list carries somebody who is not a member, so the affiliation " +
                        "on an item is not the one that was asked about.");

        }

        #endregion

    }

}
