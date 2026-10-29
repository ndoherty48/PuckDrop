# PuckDrop

A game day prediction game for a friend group following EIHL ice hockey: admins set questions per
game, friends pick before puck drop, and a season leaderboard tracks who called it best.

## Language

**Cancelled poll**:
A game day poll that will never be scored because its game did not go ahead. It counts for nothing
on the leaderboard, and carries a reason shown to every player. A rearranged game is a new poll,
not the cancelled one moved.
_Avoid_: Voided poll, abandoned, postponed poll, deleted poll

**Game day played**:
A scored game day where at least one of a player's picks was graded, and they weren't voided. A
cancelled poll is never one, and a void takes the day back off.
_Avoid_: Appearance, attendance, games (a hockey game is not a game day played)

**Head-to-head**:
Two players' record over the game days they both played: on each, whoever scored more takes it, and
equal scores are a draw. Between three or more players, a mini-league of those records, ranked on
days taken minus days lost. Only meaningful between players level on everything before it in the tie-break.
_Avoid_: H2H record against the whole field, aggregate points

**Tie-break**:
How players level on season total are separated: most game days played, then head-to-head.
Players still level after both share a rank, shown as "=3". A point adjustment moves the total,
never the tie-break; a void takes that game day out of both.
_Avoid_: Countback, accuracy (a stat shown alongside, not a tie-break)

**Void**:
An admin's sanction on one player's picks for one game day, taking that day's points and answered
count off their season total. Always per player, never a whole poll.
_Avoid_: Cancel (for a single player's picks)
